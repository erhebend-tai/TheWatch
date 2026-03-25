// =============================================================================
// FirebaseAuthenticationHandler — ASP.NET Core auth handler backed by IAuthPort.
// =============================================================================
// Reads Bearer token from Authorization header, validates via IAuthPort.ValidateTokenAsync(),
// and converts WatchUserClaims → ClaimsPrincipal so [Authorize] works across all controllers.
//
// Works with any IAuthPort implementation:
//   - FirebaseAuthAdapter (production) → real Firebase ID token verification
//   - MockAuthAdapter (development)    → accepts any non-empty token
//
// Example — protecting an endpoint:
//   [Authorize] on the controller/action
//   Access claims via: User.FindFirst("uid")?.Value
//
// Example — accessing WatchUserClaims directly:
//   var watchClaims = HttpContext.Items["WatchUserClaims"] as WatchUserClaims;
//
// WAL: [WAL-AUTH-HANDLER] prefix for all log messages.
// =============================================================================

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Dashboard.Api.Auth;

public class FirebaseAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IAuthPort _authPort;

    public const string SchemeName = "Firebase";

    public FirebaseAuthenticationHandler(
        IAuthPort authPort,
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
        _authPort = authPort;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Extract Bearer token from Authorization header
        var authHeader = Request.Headers.Authorization.ToString();
        string? token = null;

        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = authHeader["Bearer ".Length..].Trim();
        }

        // SignalR sends token via query string during WebSocket negotiate
        if (string.IsNullOrEmpty(token) && Request.Query.TryGetValue("access_token", out var queryToken))
        {
            var path = Request.Path;
            if (path.StartsWithSegments("/hubs"))
            {
                token = queryToken.ToString();
            }
        }

        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.NoResult();
        }

        // Validate via IAuthPort (Firebase in prod, Mock in dev)
        var result = await _authPort.ValidateTokenAsync(token, Context.RequestAborted);
        if (!result.Success || result.Data is null)
        {
            Logger.LogWarning("[WAL-AUTH-HANDLER] Token validation failed: {Error}", result.ErrorMessage);
            return AuthenticateResult.Fail(result.ErrorMessage ?? "Token validation failed");
        }

        var watchClaims = result.Data;

        // Store WatchUserClaims on HttpContext for direct access in controllers
        Context.Items["WatchUserClaims"] = watchClaims;

        // Build ClaimsPrincipal from WatchUserClaims
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, watchClaims.Uid),
            new("uid", watchClaims.Uid),
            new(ClaimTypes.Email, watchClaims.Email),
            new("provider", watchClaims.Provider),
        };

        if (!string.IsNullOrEmpty(watchClaims.DisplayName))
            claims.Add(new Claim(ClaimTypes.Name, watchClaims.DisplayName));

        if (!string.IsNullOrEmpty(watchClaims.PhotoUrl))
            claims.Add(new Claim("photo_url", watchClaims.PhotoUrl));

        foreach (var role in watchClaims.Roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        foreach (var custom in watchClaims.CustomClaims)
            claims.Add(new Claim(custom.Key, custom.Value));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        Logger.LogDebug("[WAL-AUTH-HANDLER] Authenticated {Uid} ({Email}) via {Provider}",
            watchClaims.Uid, watchClaims.Email, watchClaims.Provider);

        return AuthenticateResult.Success(ticket);
    }
}
