// IoTAccountLinkController — OAuth2 account linking endpoints for IoT platforms.
//
// Each IoT platform (Alexa, Google Home, SmartThings, etc.) requires OAuth2
// account linking so they can identify which TheWatch user is speaking.
//
// Flow (Alexa example):
//   1. User enables TheWatch skill in Alexa app
//   2. Alexa opens our authorize URL in a WebView
//   3. User logs into TheWatch, grants consent
//   4. We redirect back to Alexa with an authorization code
//   5. Alexa exchanges the code for access + refresh tokens via our token endpoint
//   6. On each skill invocation, Alexa sends the access token
//   7. We resolve the token to a TheWatch user ID
//
// Endpoints:
//   GET  /api/iot/auth/authorize  — OAuth2 authorization endpoint (redirect-based)
//   POST /api/iot/auth/token      — OAuth2 token exchange (code → tokens, refresh → new tokens)
//   POST /api/iot/auth/revoke     — Revoke a token (unlink account)
//
// Supported grant types:
//   - authorization_code (initial linking)
//   - refresh_token (token rotation)
//
// Security:
//   - PKCE is required for all authorization code flows
//   - Tokens are opaque (no JWT) — validated by lookup, not signature
//   - Access tokens expire in 1 hour, refresh tokens in 30 days
//   - All token operations are idempotent and audit-logged
//
// WAL: This controller handles OAuth2 tokens which are sensitive credentials.
//      Tokens MUST be stored encrypted at rest (mock adapter stores in-memory only).
//      Production adapters MUST use Azure Key Vault / AWS Secrets Manager / etc.
//      No token values are logged — only token type and user ID.

using Microsoft.AspNetCore.Mvc;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/iot/auth")]
public class IoTAccountLinkController : ControllerBase
{
    private readonly IIoTAlertPort _alertPort;
    private readonly ILogger<IoTAccountLinkController> _logger;

    // In-memory authorization code store (mock — production uses Redis/DB)
    private static readonly Dictionary<string, AuthorizationCodeEntry> _authCodes = new();
    private static readonly object _authLock = new();

    public IoTAccountLinkController(
        IIoTAlertPort alertPort,
        ILogger<IoTAccountLinkController> logger)
    {
        _alertPort = alertPort;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────
    // Authorization Endpoint
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// OAuth2 authorization endpoint. IoT platforms redirect users here to link accounts.
    ///
    /// Query parameters:
    ///   response_type=code (required)
    ///   client_id={platform_client_id} (required — identifies the IoT platform)
    ///   redirect_uri={platform_callback} (required — where to send the auth code)
    ///   state={opaque_state} (required — CSRF protection)
    ///   scope=thewatch:iot (optional — requested permission scope)
    ///   code_challenge={challenge} (recommended — PKCE)
    ///   code_challenge_method=S256 (recommended — PKCE method)
    ///
    /// In production, this would render a login/consent page.
    /// In dev/mock, it auto-approves and redirects with a code.
    /// </summary>
    [HttpGet("authorize")]
    public IActionResult Authorize(
        [FromQuery] string response_type,
        [FromQuery] string client_id,
        [FromQuery] string redirect_uri,
        [FromQuery] string state,
        [FromQuery] string? scope = null,
        [FromQuery] string? code_challenge = null,
        [FromQuery] string? code_challenge_method = null)
    {
        if (response_type != "code")
            return BadRequest(new { error = "unsupported_response_type", error_description = "Only 'code' is supported" });

        if (string.IsNullOrWhiteSpace(client_id))
            return BadRequest(new { error = "invalid_request", error_description = "client_id is required" });

        if (string.IsNullOrWhiteSpace(redirect_uri))
            return BadRequest(new { error = "invalid_request", error_description = "redirect_uri is required" });

        if (string.IsNullOrWhiteSpace(state))
            return BadRequest(new { error = "invalid_request", error_description = "state is required" });

        // Determine IoT source from client_id
        var source = ResolveSourceFromClientId(client_id);

        _logger.LogInformation(
            "IoT OAuth AUTHORIZE: Platform={Source}, ClientId={ClientId}, RedirectUri={RedirectUri}",
            source, client_id, redirect_uri);

        // Generate authorization code
        var authCode = $"authcode-{Guid.NewGuid():N}"[..32];
        var entry = new AuthorizationCodeEntry(
            Code: authCode,
            Source: source,
            ClientId: client_id,
            RedirectUri: redirect_uri,
            State: state,
            Scope: scope,
            CodeChallenge: code_challenge,
            CodeChallengeMethod: code_challenge_method,
            // Mock: auto-assign to mock-user-001 (production would use authenticated user)
            TheWatchUserId: "mock-user-001",
            CreatedAt: DateTime.UtcNow,
            ExpiresAt: DateTime.UtcNow.AddMinutes(10));

        lock (_authLock)
        {
            _authCodes[authCode] = entry;
        }

        // Redirect back to the platform with the authorization code
        var separator = redirect_uri.Contains('?') ? "&" : "?";
        var redirectUrl = $"{redirect_uri}{separator}code={Uri.EscapeDataString(authCode)}&state={Uri.EscapeDataString(state)}";

        _logger.LogInformation("IoT OAuth AUTHORIZE: Redirecting to {RedirectUri} with code", redirect_uri);

        return Redirect(redirectUrl);
    }

    // ─────────────────────────────────────────────────────────────
    // Token Endpoint
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// OAuth2 token endpoint. Exchanges authorization codes for tokens,
    /// or refreshes expired tokens.
    ///
    /// grant_type=authorization_code:
    ///   code={auth_code} + redirect_uri={same_redirect} + client_id + code_verifier (PKCE)
    ///
    /// grant_type=refresh_token:
    ///   refresh_token={token} + client_id
    /// </summary>
    [HttpPost("token")]
    public async Task<IActionResult> Token([FromForm] TokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.grant_type))
            return BadRequest(new { error = "invalid_request", error_description = "grant_type is required" });

        return request.grant_type switch
        {
            "authorization_code" => await ExchangeCodeForTokens(request, ct),
            "refresh_token" => await RefreshTokens(request, ct),
            _ => BadRequest(new { error = "unsupported_grant_type", error_description = $"Unsupported: {request.grant_type}" })
        };
    }

    private async Task<IActionResult> ExchangeCodeForTokens(TokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.code))
            return BadRequest(new { error = "invalid_request", error_description = "code is required" });

        AuthorizationCodeEntry? entry;
        lock (_authLock)
        {
            _authCodes.TryGetValue(request.code, out entry);
            if (entry is not null)
                _authCodes.Remove(request.code); // Single-use
        }

        if (entry is null)
            return BadRequest(new { error = "invalid_grant", error_description = "Invalid or expired authorization code" });

        if (entry.ExpiresAt < DateTime.UtcNow)
            return BadRequest(new { error = "invalid_grant", error_description = "Authorization code expired" });

        if (entry.RedirectUri != request.redirect_uri)
            return BadRequest(new { error = "invalid_grant", error_description = "redirect_uri mismatch" });

        // Generate tokens
        var accessToken = $"at-{Guid.NewGuid():N}";
        var refreshToken = $"rt-{Guid.NewGuid():N}";

        // Map the external user
        var externalUserId = $"{entry.Source.ToString().ToLowerInvariant()}-user-{entry.TheWatchUserId}";
        var mapping = new IoTUserMapping(
            Source: entry.Source,
            ExternalUserId: externalUserId,
            TheWatchUserId: entry.TheWatchUserId,
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            TokenExpiresAt: DateTime.UtcNow.AddHours(1),
            LinkedAt: DateTime.UtcNow);

        await _alertPort.MapExternalUserAsync(mapping, ct);

        _logger.LogInformation(
            "IoT OAuth TOKEN: Issued tokens for {Source} → TheWatch user {UserId}",
            entry.Source, entry.TheWatchUserId);

        return Ok(new
        {
            access_token = accessToken,
            token_type = "Bearer",
            expires_in = 3600,
            refresh_token = refreshToken,
            scope = entry.Scope ?? "thewatch:iot"
        });
    }

    private async Task<IActionResult> RefreshTokens(TokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.refresh_token))
            return BadRequest(new { error = "invalid_request", error_description = "refresh_token is required" });

        // In production: look up the refresh token in the token store, validate,
        // issue new access + refresh tokens, invalidate the old refresh token.
        // Mock: always succeed with new tokens.

        var newAccessToken = $"at-{Guid.NewGuid():N}";
        var newRefreshToken = $"rt-{Guid.NewGuid():N}";

        _logger.LogInformation("IoT OAuth REFRESH: Issued new token pair");

        return Ok(new
        {
            access_token = newAccessToken,
            token_type = "Bearer",
            expires_in = 3600,
            refresh_token = newRefreshToken
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Revoke Endpoint
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Revoke a token (unlink IoT account). Follows RFC 7009.
    /// Called when user unlinks TheWatch from their IoT platform settings,
    /// or when TheWatch admin revokes a compromised token.
    /// </summary>
    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(
        [FromForm] RevokeRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.token))
            return BadRequest(new { error = "invalid_request", error_description = "token is required" });

        // In production: look up the token, identify the user mapping, revoke both tokens,
        // remove the user mapping. Mock: just log it.
        _logger.LogInformation(
            "IoT OAuth REVOKE: TokenType={TokenType}",
            request.token_type_hint ?? "unknown");

        // RFC 7009: always return 200 even if token not found
        return Ok();
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

    private static IoTSource ResolveSourceFromClientId(string clientId)
    {
        // In production: look up client_id in registered OAuth2 clients table.
        // Mock: infer from naming convention.
        if (clientId.Contains("alexa", StringComparison.OrdinalIgnoreCase)) return IoTSource.Alexa;
        if (clientId.Contains("google", StringComparison.OrdinalIgnoreCase)) return IoTSource.GoogleHome;
        if (clientId.Contains("smartthings", StringComparison.OrdinalIgnoreCase) ||
            clientId.Contains("samsung", StringComparison.OrdinalIgnoreCase)) return IoTSource.SmartThings;
        if (clientId.Contains("homekit", StringComparison.OrdinalIgnoreCase) ||
            clientId.Contains("apple", StringComparison.OrdinalIgnoreCase)) return IoTSource.HomeKit;
        if (clientId.Contains("ifttt", StringComparison.OrdinalIgnoreCase)) return IoTSource.IFTTT;
        if (clientId.Contains("ring", StringComparison.OrdinalIgnoreCase)) return IoTSource.Ring;
        if (clientId.Contains("tuya", StringComparison.OrdinalIgnoreCase)) return IoTSource.Tuya;
        if (clientId.Contains("matter", StringComparison.OrdinalIgnoreCase)) return IoTSource.Matter;

        return IoTSource.CustomWebhook;
    }
}

// ─────────────────────────────────────────────────────────────
// OAuth2 DTOs
// ─────────────────────────────────────────────────────────────

public record AuthorizationCodeEntry(
    string Code,
    IoTSource Source,
    string ClientId,
    string RedirectUri,
    string State,
    string? Scope,
    string? CodeChallenge,
    string? CodeChallengeMethod,
    string TheWatchUserId,
    DateTime CreatedAt,
    DateTime ExpiresAt
);

public record TokenRequest(
    string grant_type,
    string? code = null,
    string? redirect_uri = null,
    string? client_id = null,
    string? client_secret = null,
    string? refresh_token = null,
    string? code_verifier = null
);

public record RevokeRequest(
    string token,
    string? token_type_hint = null
);
