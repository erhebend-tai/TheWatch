// =============================================================================
// MockAuthPort — Mock adapter for IAuthPort in TheWatch.Adapters.Mock
// =============================================================================
// Delegates to MockAuthAdapter in TheWatch.Data for the actual implementation.
// This adapter exists in the Adapters.Mock project for DI registration convenience.
// =============================================================================

using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Domain.Models;

namespace TheWatch.Adapters.Mock;

public class MockAuthPort : IAuthPort
{
    public string ProviderName => "Mock";

    public Task<StorageResult<WatchUserClaims>> ValidateTokenAsync(string idToken, CancellationToken ct = default)
    {
        var claims = new WatchUserClaims
        {
            Uid = "mock-uid-001",
            Email = "mock@thewatch.dev",
            DisplayName = "Mock User",
            Provider = "mock",
            EmailVerified = true,
            MfaEnabled = false,
            Roles = new List<string> { "user" },
            TokenIssuedAt = DateTime.UtcNow,
            TokenExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        return Task.FromResult(StorageResult<WatchUserClaims>.Ok(claims));
    }

    public Task<bool> RevokeTokensAsync(string uid, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<bool> SetCustomClaimsAsync(string uid, Dictionary<string, object> claims, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<StorageResult<AccountStatus>> GetAccountStatusAsync(string uid, CancellationToken ct = default) =>
        Task.FromResult(StorageResult<AccountStatus>.Ok(new AccountStatus
        {
            Uid = uid, EmailVerified = true, PhoneVerified = true,
            MfaEnabled = false, MfaMethods = new(), CreatedAt = DateTime.UtcNow.AddDays(-30)
        }));

    public Task<bool> SendEmailVerificationAsync(string uid, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<bool> ConfirmEmailVerificationAsync(string uid, string code, CancellationToken ct = default) =>
        Task.FromResult(code == "123456");

    public Task<StorageResult<MfaEnrollmentChallenge>> EnrollMfaAsync(
        string uid, string method, string? phoneNumber = null, CancellationToken ct = default) =>
        Task.FromResult(StorageResult<MfaEnrollmentChallenge>.Ok(new MfaEnrollmentChallenge
        {
            Method = method,
            ChallengeUri = "otpauth://totp/TheWatch:mock@thewatch.dev?secret=JBSWY3DPEHPK3PXP&issuer=TheWatch",
            SessionId = Guid.NewGuid().ToString("N"),
            BackupCodes = Enumerable.Range(0, 10).Select(i => $"1000000{i}").ToList()
        }));

    public Task<bool> ConfirmMfaEnrollmentAsync(string uid, string sessionId, string code, CancellationToken ct = default) =>
        Task.FromResult(code.Length == 6);

    public Task<bool> VerifyMfaCodeAsync(string uid, string code, string method, CancellationToken ct = default) =>
        Task.FromResult(code.Length == 6 || code.StartsWith("1000000"));

    public Task<bool> DisableMfaAsync(string uid, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<bool> SendPasswordResetAsync(string email, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<bool> DeleteAccountAsync(string uid, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<bool> DisableAccountAsync(string uid, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<bool> EnableAccountAsync(string uid, CancellationToken ct = default) =>
        Task.FromResult(true);
}
