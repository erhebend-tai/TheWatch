// MockAuthAdapter — always-authenticated IAuthPort for dev/test.
// Returns a valid WatchUserClaims for any token, with configurable user identity.
// Useful for development when Firebase isn't available.
//
// Example: services.AddSingleton<IAuthPort, MockAuthAdapter>();

using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Data.Adapters.Mock;

public class MockAuthAdapter : IAuthPort
{
    public string ProviderName => "mock";

    public Task<StorageResult<WatchUserClaims>> ValidateTokenAsync(
        string idToken, CancellationToken ct = default)
    {
        // Accept any non-empty token
        if (string.IsNullOrWhiteSpace(idToken))
            return Task.FromResult(StorageResult<WatchUserClaims>.Fail("Empty token"));

        var claims = new WatchUserClaims
        {
            Uid = "mock-user-001",
            Email = "dev@thewatch.local",
            DisplayName = "TheWatch Developer",
            PhotoUrl = null,
            Provider = "mock",
            Roles = new List<string> { "admin", "operator" },
            CustomClaims = new Dictionary<string, string> { { "env", "development" } },
            TokenIssuedAt = DateTime.UtcNow.AddMinutes(-5),
            TokenExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        return Task.FromResult(StorageResult<WatchUserClaims>.Ok(claims));
    }

    public Task<bool> RevokeTokensAsync(string uid, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<bool> SetCustomClaimsAsync(string uid, Dictionary<string, object> claims, CancellationToken ct = default) =>
        Task.FromResult(true);
}
