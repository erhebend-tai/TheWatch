// IInfrastructureHealthPort — unified health view across ALL cloud providers.
// The dashboard queries THIS instead of individual provider ports for the overview.
// Each cloud adapter registers as an IInfrastructureHealthProvider (below).
// The aggregator (InfrastructureHealthAggregator) collects from all registered providers.
// NO database SDK imports allowed in this file.

namespace TheWatch.Shared.Domain.Ports;

/// <summary>
/// Represents a single infrastructure service's health status.
/// Provider-agnostic — the dashboard renders these uniformly.
/// </summary>
public record InfrastructureServiceHealth(
    string ServiceId,
    string ServiceName,
    string Provider,        // "Azure", "AWS", "Google", "Oracle", "Cloudflare", "Mock"
    string Category,        // "Compute", "Database", "Messaging", "Notification", "CDN", "Auth", "Storage"
    HealthState State,
    string? StatusMessage,
    DateTime LastChecked,
    Dictionary<string, string>? Metadata = null
);

public enum HealthState
{
    Healthy,
    Degraded,
    Unhealthy,
    Unknown,
    NotConfigured
}

/// <summary>
/// Each cloud adapter project implements this to report its services' health.
/// Multiple providers can be registered simultaneously.
/// </summary>
public interface IInfrastructureHealthProvider
{
    /// <summary>Unique provider identifier (e.g., "Azure", "AWS", "Google").</summary>
    string ProviderId { get; }

    /// <summary>Human-readable name.</summary>
    string ProviderName { get; }

    /// <summary>Whether this provider is currently configured and active.</summary>
    bool IsConfigured { get; }

    /// <summary>Check health of all services this provider manages.</summary>
    Task<IReadOnlyList<InfrastructureServiceHealth>> CheckHealthAsync(CancellationToken ct = default);
}

/// <summary>
/// Aggregates health from all registered IInfrastructureHealthProviders.
/// Injected as a single dependency into the dashboard.
/// </summary>
public interface IInfrastructureHealthPort
{
    /// <summary>Get health for ALL configured providers.</summary>
    Task<IReadOnlyList<InfrastructureServiceHealth>> GetAllHealthAsync(CancellationToken ct = default);

    /// <summary>Get health for a specific provider.</summary>
    Task<IReadOnlyList<InfrastructureServiceHealth>> GetProviderHealthAsync(string providerId, CancellationToken ct = default);

    /// <summary>Get the list of registered providers and their configuration state.</summary>
    IReadOnlyList<ProviderInfo> GetRegisteredProviders();

    /// <summary>Quick aggregate: is everything healthy?</summary>
    Task<HealthState> GetOverallHealthAsync(CancellationToken ct = default);
}

public record ProviderInfo(
    string ProviderId,
    string ProviderName,
    bool IsConfigured,
    int ServiceCount
);
