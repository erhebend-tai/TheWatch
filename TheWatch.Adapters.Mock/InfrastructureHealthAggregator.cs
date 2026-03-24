using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Mock;

/// <summary>
/// Aggregates health status from all registered IInfrastructureHealthProviders.
/// Implements the IInfrastructureHealthPort to provide a unified health view to consumers.
/// </summary>
public class InfrastructureHealthAggregator : IInfrastructureHealthPort
{
    private readonly IEnumerable<IInfrastructureHealthProvider> _providers;

    public InfrastructureHealthAggregator(IEnumerable<IInfrastructureHealthProvider> providers)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
    }

    public async Task<IReadOnlyList<InfrastructureServiceHealth>> GetAllHealthAsync(CancellationToken ct = default)
    {
        var allHealth = new List<InfrastructureServiceHealth>();

        foreach (var provider in _providers)
        {
            var health = await provider.CheckHealthAsync(ct);
            allHealth.AddRange(health);
        }

        return allHealth.AsReadOnly();
    }

    public async Task<IReadOnlyList<InfrastructureServiceHealth>> GetProviderHealthAsync(string providerId, CancellationToken ct = default)
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderId == providerId);
        if (provider == null)
        {
            return new List<InfrastructureServiceHealth>().AsReadOnly();
        }

        var health = await provider.CheckHealthAsync(ct);
        return health;
    }

    public IReadOnlyList<ProviderInfo> GetRegisteredProviders()
    {
        return _providers
            .Select(p => new ProviderInfo(
                ProviderId: p.ProviderId,
                ProviderName: p.ProviderName,
                IsConfigured: p.IsConfigured,
                ServiceCount: 0 // Will be populated after health check if needed
            ))
            .ToList()
            .AsReadOnly();
    }

    public async Task<HealthState> GetOverallHealthAsync(CancellationToken ct = default)
    {
        var allHealth = await GetAllHealthAsync(ct);

        // If any service is unhealthy, return Unhealthy
        if (allHealth.Any(h => h.State == HealthState.Unhealthy))
        {
            return HealthState.Unhealthy;
        }

        // If any service is degraded, return Degraded
        if (allHealth.Any(h => h.State == HealthState.Degraded))
        {
            return HealthState.Degraded;
        }

        // Otherwise, return Healthy
        return HealthState.Healthy;
    }
}
