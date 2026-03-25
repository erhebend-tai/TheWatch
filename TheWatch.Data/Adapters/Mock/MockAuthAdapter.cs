// MockAuthAdapter — always-authenticated IAuthPort for dev/test.
// Returns valid WatchUserClaims for any token, with configurable user identity.
// Simulates email verification, MFA enrollment, and account management.
//
// Example: services.AddSingleton<IAuthPort, MockAuthAdapter>();

using System.Collections.Concurrent;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Adapters.Mock;

public class MockAuthAdapter : IAuthPort
{
    public string ProviderName => "mock";

    // In-memory state for simulating verification flows
    private readonly ConcurrentDictionary<string, MockUserState> _users = new();

    private MockUserState GetOrCreateUser(string uid) =>
        _users.GetOrAdd(uid, _ => new MockUserState { Uid = uid });

    // ── Token Validation ─────────────────────────────────────────

    public Task<StorageResult<WatchUserClaims>> ValidateTokenAsync(
        string idToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
            return Task.FromResult(StorageResult<WatchUserClaims>.Fail("Empty token"));

        var user = GetOrCreateUser("mock-user-001");

        var claims = new WatchUserClaims
        {
            Uid = user.Uid,
            Email = "dev@thewatch.local",
            DisplayName = "TheWatch Developer",
            PhotoUrl = null,
            PhoneNumber = "+15551234567",
            Provider = "mock",
            EmailVerified = user.EmailVerified,
            MfaEnabled = user.MfaEnabled,
            Roles = new List<string> { "admin", "operator" },
            CustomClaims = new Dictionary<string, string> { { "env", "development" } },
            TokenIssuedAt = DateTime.UtcNow.AddMinutes(-5),
            TokenExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        return Task.FromResult(StorageResult<WatchUserClaims>.Ok(claims));
    }

    public Task<bool> RevokeTokensAsync(string uid, CancellationToken ct = default) =>
        Task.FromResult(true);

    // ── Claims & Roles ───────────────────────────────────────────

    public Task<bool> SetCustomClaimsAsync(string uid, Dictionary<string, object> claims, CancellationToken ct = default) =>
        Task.FromResult(true);

    // ── Account Status ───────────────────────────────────────────

    public Task<StorageResult<AccountStatus>> GetAccountStatusAsync(
        string uid, CancellationToken ct = default)
    {
        var user = GetOrCreateUser(uid);
        return Task.FromResult(StorageResult<AccountStatus>.Ok(new AccountStatus
        {
            Uid = uid,
            EmailVerified = user.EmailVerified,
            PhoneVerified = true,
            MfaEnabled = user.MfaEnabled,
            MfaMethods = user.MfaMethods.ToList(),
            LastSignIn = DateTime.UtcNow.AddMinutes(-30),
            CreatedAt = DateTime.UtcNow.AddDays(-30)
        }));
    }

    // ── Email Verification ───────────────────────────────────────

    public Task<bool> SendEmailVerificationAsync(string uid, CancellationToken ct = default)
    {
        var user = GetOrCreateUser(uid);
        user.PendingEmailCode = "123456";
        return Task.FromResult(true);
    }

    public Task<bool> ConfirmEmailVerificationAsync(string uid, string code, CancellationToken ct = default)
    {
        var user = GetOrCreateUser(uid);
        if (user.PendingEmailCode == code || code == "123456") // Mock always accepts 123456
        {
            user.EmailVerified = true;
            user.PendingEmailCode = null;
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    // ── Multi-Factor Authentication ──────────────────────────────

    public Task<StorageResult<MfaEnrollmentChallenge>> EnrollMfaAsync(
        string uid, string method, string? phoneNumber = null, CancellationToken ct = default)
    {
        var user = GetOrCreateUser(uid);
        var sessionId = Guid.NewGuid().ToString("N");
        user.PendingMfaSession = sessionId;
        user.PendingMfaMethod = method;

        var challenge = new MfaEnrollmentChallenge
        {
            Method = method,
            ChallengeUri = method == "totp"
                ? $"otpauth://totp/TheWatch:dev@thewatch.local?secret=JBSWY3DPEHPK3PXP&issuer=TheWatch&digits=6&period=30"
                : phoneNumber != null ? $"***{phoneNumber[^4..]}" : "***1234",
            SessionId = sessionId,
            BackupCodes = method == "totp"
                ? Enumerable.Range(0, 10).Select(i => $"1000000{i}").ToList()
                : new List<string>()
        };

        return Task.FromResult(StorageResult<MfaEnrollmentChallenge>.Ok(challenge));
    }

    public Task<bool> ConfirmMfaEnrollmentAsync(
        string uid, string sessionId, string code, CancellationToken ct = default)
    {
        var user = GetOrCreateUser(uid);
        if (user.PendingMfaSession != sessionId)
            return Task.FromResult(false);

        // Mock accepts any 6-digit code
        if (code.Length == 6)
        {
            user.MfaEnabled = true;
            if (!string.IsNullOrEmpty(user.PendingMfaMethod) && !user.MfaMethods.Contains(user.PendingMfaMethod))
                user.MfaMethods.Add(user.PendingMfaMethod);
            user.PendingMfaSession = null;
            user.PendingMfaMethod = null;
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<bool> VerifyMfaCodeAsync(
        string uid, string code, string method, CancellationToken ct = default)
    {
        // Mock: accept any 6-digit code or known backup codes
        if (code.Length == 6 || code.StartsWith("1000000"))
            return Task.FromResult(true);
        return Task.FromResult(false);
    }

    public Task<bool> DisableMfaAsync(string uid, CancellationToken ct = default)
    {
        var user = GetOrCreateUser(uid);
        user.MfaEnabled = false;
        user.MfaMethods.Clear();
        return Task.FromResult(true);
    }

    // ── Account Management ───────────────────────────────────────

    public Task<bool> SendPasswordResetAsync(string email, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<bool> DeleteAccountAsync(string uid, CancellationToken ct = default)
    {
        _users.TryRemove(uid, out _);
        return Task.FromResult(true);
    }

    public Task<bool> DisableAccountAsync(string uid, CancellationToken ct = default)
    {
        var user = GetOrCreateUser(uid);
        user.Disabled = true;
        return Task.FromResult(true);
    }

    public Task<bool> EnableAccountAsync(string uid, CancellationToken ct = default)
    {
        var user = GetOrCreateUser(uid);
        user.Disabled = false;
        return Task.FromResult(true);
    }

    // ── Internal state ───────────────────────────────────────────

    private class MockUserState
    {
        public string Uid { get; set; } = string.Empty;
        public bool EmailVerified { get; set; } = true; // Default verified in dev
        public bool MfaEnabled { get; set; }
        public bool Disabled { get; set; }
        public List<string> MfaMethods { get; } = new();
        public string? PendingEmailCode { get; set; }
        public string? PendingMfaSession { get; set; }
        public string? PendingMfaMethod { get; set; }
    }
}
