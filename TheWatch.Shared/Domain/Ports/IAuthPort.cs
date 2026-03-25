// IAuthPort — domain port for authentication and identity verification.
// NO SDK imports allowed in this file. Adapters implement this per-provider:
//   - FirebaseAuthAdapter     → Firebase Auth ID token validation
//   - AzureADAuthAdapter      → Azure AD / Entra ID (future)
//   - MockAuthAdapter         → always-authenticated for dev/test
//
// The auth flow for Blazor SSR:
//   1. Client signs in via provider JS SDK (Firebase popup, Microsoft redirect, etc.)
//   2. Client receives an ID token (JWT)
//   3. Server validates the token via this port
//   4. Server creates a session cookie with validated claims
//   5. AuthenticationStateProvider reads from the cookie
//
// Example:
//   var result = await authPort.ValidateTokenAsync(idToken, ct);
//   if (result.Success) {
//       var claims = result.Data!; // WatchUserClaims with UID, email, roles
//   }

namespace TheWatch.Shared.Domain.Ports;

/// <summary>
/// Validated user identity returned by IAuthPort after token verification.
/// </summary>
public record WatchUserClaims
{
    public string Uid { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? PhotoUrl { get; init; }
    public string Provider { get; init; } = string.Empty; // "firebase", "azure-ad", "mock"
    public List<string> Roles { get; init; } = new();
    public Dictionary<string, string> CustomClaims { get; init; } = new();
    public DateTime TokenIssuedAt { get; init; }
    public DateTime TokenExpiresAt { get; init; }
}

public interface IAuthPort
{
    /// <summary>Which auth provider this adapter uses.</summary>
    string ProviderName { get; }

    /// <summary>
    /// Validate an ID token (JWT) and extract user claims.
    /// Returns Fail if token is expired, malformed, or from wrong issuer.
    /// </summary>
    Task<TheWatch.Shared.Domain.Models.StorageResult<WatchUserClaims>> ValidateTokenAsync(
        string idToken, CancellationToken ct = default);

    /// <summary>
    /// Revoke all refresh tokens for a user (force sign-out everywhere).
    /// Not all providers support this — returns false if unsupported.
    /// </summary>
    Task<bool> RevokeTokensAsync(string uid, CancellationToken ct = default);

    /// <summary>
    /// Set custom claims on a user (e.g., roles, permissions).
    /// These will appear in future ID tokens after refresh.
    /// </summary>
    Task<bool> SetCustomClaimsAsync(string uid, Dictionary<string, object> claims, CancellationToken ct = default);
}
