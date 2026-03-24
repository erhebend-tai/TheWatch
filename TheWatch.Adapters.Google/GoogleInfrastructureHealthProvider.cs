using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Google;

/// <summary>
/// Google Cloud implementation of IInfrastructureHealthProvider.
/// Currently a stub with NotConfigured status.
/// TODO: Implement real Google Cloud health checks.
/// </summary>
public class GoogleInfrastructureHealthProvider : IInfrastructureHealthProvider
{
    public string ProviderId => "Google";
    public string ProviderName => "Google Cloud Platform";
    public bool IsConfigured => false; // TODO: Set to true when configured

    public Task<IReadOnlyList<InfrastructureServiceHealth>> CheckHealthAsync(CancellationToken ct = default)
    {
        // TODO: Implement real Google Cloud health checks
        // 1. Check Firestore database status
        // 2. Check Cloud Functions status
        // 3. Check Cloud Run services
        // 4. Check Firebase authentication status
        // 5. Return aggregated health status

        var services = new List<InfrastructureServiceHealth>
        {
            new InfrastructureServiceHealth(
                ServiceId: "google-firestore",
                ServiceName: "Google Firestore",
                Provider: "Google",
                Category: "Database",
                State: HealthState.NotConfigured,
                StatusMessage: "Google adapter not yet configured",
                LastChecked: DateTime.UtcNow
            )
        };

        return Task.FromResult<IReadOnlyList<InfrastructureServiceHealth>>(services);
    }
}
