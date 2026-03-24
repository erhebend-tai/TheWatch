using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Mock;

/// <summary>
/// Mock implementation of IInfrastructureHealthProvider for testing and development.
/// Always returns a healthy status for a set of mock services.
/// </summary>
public class MockInfrastructureHealthProvider : IInfrastructureHealthProvider
{
    public string ProviderId => "Mock";
    public string ProviderName => "Mock Provider";
    public bool IsConfigured => true;

    public Task<IReadOnlyList<InfrastructureServiceHealth>> CheckHealthAsync(CancellationToken ct = default)
    {
        var services = new[]
        {
            new InfrastructureServiceHealth(
                ServiceId: "mock-database",
                ServiceName: "Mock Database",
                Provider: "Mock",
                Category: "Database",
                State: HealthState.Healthy,
                StatusMessage: "Mock database is running",
                LastChecked: DateTime.UtcNow
            ),
            new InfrastructureServiceHealth(
                ServiceId: "mock-messaging",
                ServiceName: "Mock Message Queue",
                Provider: "Mock",
                Category: "Messaging",
                State: HealthState.Healthy,
                StatusMessage: "Mock message queue is operational",
                LastChecked: DateTime.UtcNow
            ),
            new InfrastructureServiceHealth(
                ServiceId: "mock-notification",
                ServiceName: "Mock Notification Service",
                Provider: "Mock",
                Category: "Notification",
                State: HealthState.Healthy,
                StatusMessage: "Mock notification service is ready",
                LastChecked: DateTime.UtcNow
            ),
            new InfrastructureServiceHealth(
                ServiceId: "mock-storage",
                ServiceName: "Mock Storage",
                Provider: "Mock",
                Category: "Storage",
                State: HealthState.Healthy,
                StatusMessage: "Mock storage is available",
                LastChecked: DateTime.UtcNow
            ),
            new InfrastructureServiceHealth(
                ServiceId: "mock-auth",
                ServiceName: "Mock Auth Service",
                Provider: "Mock",
                Category: "Auth",
                State: HealthState.Healthy,
                StatusMessage: "Mock auth is configured",
                LastChecked: DateTime.UtcNow
            ),
            new InfrastructureServiceHealth(
                ServiceId: "mock-cdn",
                ServiceName: "Mock CDN",
                Provider: "Mock",
                Category: "CDN",
                State: HealthState.Healthy,
                StatusMessage: "Mock CDN is serving requests",
                LastChecked: DateTime.UtcNow
            )
        };

        return Task.FromResult<IReadOnlyList<InfrastructureServiceHealth>>(services);
    }
}
