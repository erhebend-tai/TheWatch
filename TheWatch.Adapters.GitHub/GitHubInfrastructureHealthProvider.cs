using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.GitHub;

/// <summary>
/// GitHub implementation of IInfrastructureHealthProvider.
/// Currently a stub with NotConfigured status.
/// TODO: Implement real GitHub status checks.
/// </summary>
public class GitHubInfrastructureHealthProvider : IInfrastructureHealthProvider
{
    public string ProviderId => "GitHub";
    public string ProviderName => "GitHub";
    public bool IsConfigured => false; // TODO: Set to true when configured

    public Task<IReadOnlyList<InfrastructureServiceHealth>> CheckHealthAsync(CancellationToken ct = default)
    {
        // TODO: Implement real GitHub health checks
        // 1. Query GitHub API status
        // 2. Check repository access
        // 3. Check workflow status
        // 4. Verify authentication token validity
        // 5. Return aggregated health status

        var services = new List<InfrastructureServiceHealth>
        {
            new InfrastructureServiceHealth(
                ServiceId: "github-api",
                ServiceName: "GitHub API",
                Provider: "GitHub",
                Category: "Compute",
                State: HealthState.NotConfigured,
                StatusMessage: "GitHub adapter not yet configured",
                LastChecked: DateTime.UtcNow
            )
        };

        return Task.FromResult<IReadOnlyList<InfrastructureServiceHealth>>(services);
    }
}
