using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Oracle;

/// <summary>
/// Oracle Cloud implementation of IInfrastructureHealthProvider.
/// Currently a stub with NotConfigured status.
/// TODO: Implement real Oracle Cloud health checks.
/// </summary>
public class OracleInfrastructureHealthProvider : IInfrastructureHealthProvider
{
    public string ProviderId => "Oracle";
    public string ProviderName => "Oracle Cloud Infrastructure";
    public bool IsConfigured => false; // TODO: Set to true when configured

    public Task<IReadOnlyList<InfrastructureServiceHealth>> CheckHealthAsync(CancellationToken ct = default)
    {
        // TODO: Implement real Oracle Cloud health checks
        // 1. Check Oracle Cloud database instances
        // 2. Check compute instances
        // 3. Check Oracle Cloud storage
        // 4. Check networking and security groups
        // 5. Return aggregated health status

        var services = new List<InfrastructureServiceHealth>
        {
            new InfrastructureServiceHealth(
                ServiceId: "oracle-database",
                ServiceName: "Oracle Cloud Database",
                Provider: "Oracle",
                Category: "Database",
                State: HealthState.NotConfigured,
                StatusMessage: "Oracle adapter not yet configured",
                LastChecked: DateTime.UtcNow
            )
        };

        return Task.FromResult<IReadOnlyList<InfrastructureServiceHealth>>(services);
    }
}
