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
/// <summary>
/// Validated user identity returned by IAuthPort after token verification.
/// </summary>
public record WatchUserClaims
{
    public string Uid { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? PhotoUrl { get; init; }
    public string? PhoneNumber { get; init; }
    public string Provider { get; init; } = string.Empty; // "firebase", "azure-ad", "mock"
    public bool EmailVerified { get; init; }
    public bool MfaEnabled { get; init; }
    public List<string> Roles { get; init; } = new();
    public Dictionary<string, string> CustomClaims { get; init; } = new();
    public DateTime TokenIssuedAt { get; init; }
    public DateTime TokenExpiresAt { get; init; }
}

/// <summary>
/// Result of a 2FA enrollment initiation. The secret/URI is used client-side
/// to display a QR code (TOTP) or trigger SMS delivery. Provider-agnostic.
/// </summary>
public record MfaEnrollmentChallenge
{
    /// <summary>The MFA method: "totp", "sms", "email".</summary>
    public string Method { get; init; } = string.Empty;
    /// <summary>For TOTP: otpauth:// URI for QR code. For SMS/email: masked destination.</summary>
    public string ChallengeUri { get; init; } = string.Empty;
    /// <summary>Provider-specific session/verification ID needed to complete enrollment.</summary>
    public string SessionId { get; init; } = string.Empty;
    /// <summary>TOTP only: backup codes generated during enrollment.</summary>
    public List<string> BackupCodes { get; init; } = new();
}

/// <summary>
/// User account status for email verification, MFA enrollment, etc.
/// </summary>
public record AccountStatus
{
    public string Uid { get; init; } = string.Empty;
    public bool EmailVerified { get; init; }
    public bool PhoneVerified { get; init; }
    public bool MfaEnabled { get; init; }
    public List<string> MfaMethods { get; init; } = new(); // "totp", "sms", "email"
    public DateTime? LastSignIn { get; init; }
    public DateTime CreatedAt { get; init; }
}

public interface IAuthPort
{
    /// <summary>Which auth provider this adapter uses.</summary>
    string ProviderName { get; }

    // ── Token Validation ─────────────────────────────────────────

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

    // ── Claims & Roles ───────────────────────────────────────────

    /// <summary>
    /// Set custom claims on a user (e.g., roles, permissions).
    /// These will appear in future ID tokens after refresh.
    /// </summary>
    Task<bool> SetCustomClaimsAsync(string uid, Dictionary<string, object> claims, CancellationToken ct = default);

    // ── Account Status ───────────────────────────────────────────

    /// <summary>
    /// Get account status: email verification, MFA enrollment, sign-in history.
    /// </summary>
    Task<TheWatch.Shared.Domain.Models.StorageResult<AccountStatus>> GetAccountStatusAsync(
        string uid, CancellationToken ct = default);

    // ── Email Verification ───────────────────────────────────────

    /// <summary>
    /// Send an email verification link/code to the user's email.
    /// Implementation varies: Firebase sends a link, Azure AD uses a code, etc.
    /// </summary>
    Task<bool> SendEmailVerificationAsync(string uid, CancellationToken ct = default);

    /// <summary>
    /// Confirm email verification using a code or token from the verification link.
    /// Some providers (Firebase) handle this client-side; this is for server-side confirmation.
    /// Returns false if the provider handles verification client-side only.
    /// </summary>
    Task<bool> ConfirmEmailVerificationAsync(string uid, string code, CancellationToken ct = default);

    // ── Multi-Factor Authentication ──────────────────────────────

    /// <summary>
    /// Start MFA enrollment for a user. Returns a challenge (TOTP secret URI,
    /// SMS destination, etc.) that the client uses to complete enrollment.
    /// </summary>
    /// <param name="uid">User ID.</param>
    /// <param name="method">MFA method: "totp", "sms", or "email".</param>
    /// <param name="phoneNumber">Required for "sms" method. Ignored otherwise.</param>
    Task<TheWatch.Shared.Domain.Models.StorageResult<MfaEnrollmentChallenge>> EnrollMfaAsync(
        string uid, string method, string? phoneNumber = null, CancellationToken ct = default);

    /// <summary>
    /// Complete MFA enrollment by verifying the user can produce a valid code.
    /// </summary>
    /// <param name="uid">User ID.</param>
    /// <param name="sessionId">Session ID from the enrollment challenge.</param>
    /// <param name="code">TOTP code, SMS code, or email code from the user.</param>
    Task<bool> ConfirmMfaEnrollmentAsync(
        string uid, string sessionId, string code, CancellationToken ct = default);

    /// <summary>
    /// Verify an MFA code during sign-in (second factor after password).
    /// </summary>
    /// <param name="uid">User ID.</param>
    /// <param name="code">The code from the user's authenticator, SMS, or email.</param>
    /// <param name="method">MFA method being verified: "totp", "sms", "email", or "backup".</param>
    Task<bool> VerifyMfaCodeAsync(
        string uid, string code, string method, CancellationToken ct = default);

    /// <summary>
    /// Disable MFA for a user. Requires re-authentication in most providers.
    /// </summary>
    Task<bool> DisableMfaAsync(string uid, CancellationToken ct = default);

    // ── Account Management ───────────────────────────────────────

    /// <summary>
    /// Send a password reset email/link to the user.
    /// </summary>
    Task<bool> SendPasswordResetAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Delete a user account and all associated data from the auth provider.
    /// Required for GDPR Article 17 (right to erasure).
    /// </summary>
    Task<bool> DeleteAccountAsync(string uid, CancellationToken ct = default);

    /// <summary>
    /// Disable a user account without deleting it. Prevents sign-in.
    /// </summary>
    Task<bool> DisableAccountAsync(string uid, CancellationToken ct = default);

    /// <summary>
    /// Re-enable a previously disabled user account.
    /// </summary>
    Task<bool> EnableAccountAsync(string uid, CancellationToken ct = default);
}
