// =============================================================================
// FirebaseAuthAdapter — IAuthPort implementation using Firebase Admin SDK.
// =============================================================================
// Validates Firebase ID tokens server-side using Google's public key verification.
// Supports custom claims for role-based access control.
//
// Requires: FirebaseAdmin NuGet (2.4.0) in TheWatch.Data.csproj
// Configuration: AIProviders:Firebase section in appsettings.json
//   - If running with GOOGLE_APPLICATION_CREDENTIALS env var set, uses default creds
//   - Otherwise, uses the credential file at DatabaseSettings:FirestoreCredentialPath
//
// WAL: [WAL-AUTH-FIREBASE] prefix for all log messages.
// =============================================================================

using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Adapters.Firebase;

public class FirebaseAuthAdapter : IAuthPort
{
    private readonly FirebaseAuth _auth;
    private readonly ILogger<FirebaseAuthAdapter> _logger;

    public string ProviderName => "firebase";

    public FirebaseAuthAdapter(FirebaseApp firebaseApp, ILogger<FirebaseAuthAdapter> logger)
    {
        _auth = FirebaseAuth.GetAuth(firebaseApp);
        _logger = logger;
        _logger.LogInformation("[WAL-AUTH-FIREBASE] Initialized Firebase Auth adapter");
    }

    public async Task<StorageResult<WatchUserClaims>> ValidateTokenAsync(
        string idToken, CancellationToken ct = default)
    {
        try
        {
            // Verify the ID token — checks signature, expiry, audience, issuer
            var decoded = await _auth.VerifyIdTokenAsync(idToken, ct);

            var claims = new WatchUserClaims
            {
                Uid = decoded.Uid,
                Email = decoded.Claims.TryGetValue("email", out var email) ? email?.ToString() ?? "" : "",
                DisplayName = decoded.Claims.TryGetValue("name", out var name) ? name?.ToString() : null,
                PhotoUrl = decoded.Claims.TryGetValue("picture", out var photo) ? photo?.ToString() : null,
                Provider = "firebase",
                TokenIssuedAt = DateTimeOffset.FromUnixTimeSeconds(decoded.IssuedAtTimeSeconds).UtcDateTime,
                TokenExpiresAt = DateTimeOffset.FromUnixTimeSeconds(decoded.ExpirationTimeSeconds).UtcDateTime,
                Roles = ExtractRoles(decoded.Claims),
                CustomClaims = ExtractCustomClaims(decoded.Claims)
            };

            _logger.LogDebug("[WAL-AUTH-FIREBASE] Validated token for user {Uid} ({Email})",
                claims.Uid, claims.Email);

            return StorageResult<WatchUserClaims>.Ok(claims);
        }
        catch (FirebaseAuthException ex)
        {
            _logger.LogWarning(ex, "[WAL-AUTH-FIREBASE] Token validation failed: {Code}", ex.AuthErrorCode);
            return StorageResult<WatchUserClaims>.Fail($"Firebase token invalid: {ex.AuthErrorCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-AUTH-FIREBASE] Unexpected error validating token");
            return StorageResult<WatchUserClaims>.Fail($"Auth validation error: {ex.Message}");
        }
    }

    public async Task<bool> RevokeTokensAsync(string uid, CancellationToken ct = default)
    {
        try
        {
            await _auth.RevokeRefreshTokensAsync(uid, ct);
            _logger.LogInformation("[WAL-AUTH-FIREBASE] Revoked tokens for user {Uid}", uid);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-AUTH-FIREBASE] Failed to revoke tokens for {Uid}", uid);
            return false;
        }
    }

    public async Task<bool> SetCustomClaimsAsync(string uid, Dictionary<string, object> claims, CancellationToken ct = default)
    {
        try
        {
            await _auth.SetCustomUserClaimsAsync(uid, claims, ct);
            _logger.LogInformation("[WAL-AUTH-FIREBASE] Set custom claims for user {Uid}: {Claims}",
                uid, string.Join(", ", claims.Keys));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-AUTH-FIREBASE] Failed to set custom claims for {Uid}", uid);
            return false;
        }
    }

    /// <summary>Extract "roles" custom claim if present.</summary>
    private static List<string> ExtractRoles(IReadOnlyDictionary<string, object> claims)
    {
        if (claims.TryGetValue("roles", out var rolesObj) && rolesObj is IEnumerable<object> rolesList)
            return rolesList.Select(r => r.ToString() ?? "").Where(r => r.Length > 0).ToList();
        return new List<string>();
    }

    /// <summary>Extract custom claims (skip standard JWT/Firebase fields).</summary>
    private static Dictionary<string, string> ExtractCustomClaims(IReadOnlyDictionary<string, object> claims)
    {
        var standardKeys = new HashSet<string>
        {
            "iss", "aud", "auth_time", "user_id", "sub", "iat", "exp",
            "email", "email_verified", "name", "picture", "firebase", "roles"
        };

        return claims
            .Where(c => !standardKeys.Contains(c.Key) && c.Value is not null)
            .ToDictionary(c => c.Key, c => c.Value.ToString() ?? "");
    }
}
