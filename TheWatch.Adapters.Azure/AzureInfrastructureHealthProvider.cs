using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Azure;

/// <summary>
/// Azure implementation of IInfrastructureHealthProvider.
/// Currently a stub with NotConfigured status.
/// TODO: Implement real Azure Resource Health API integration.
/// </summary>
public class AzureInfrastructureHealthProvider : IInfrastructureHealthProvider
{
    public string ProviderId => "Azure";
    public string ProviderName => "Azure Cloud Provider";
    public bool IsConfigured => false; // TODO: Set to true when configured

    public Task<IReadOnlyList<InfrastructureServiceHealth>> CheckHealthAsync(CancellationToken ct = default)
    {
        // TODO: Implement real Azure health checks
        // 1. Check Azure App Service status via Azure Resource Health API
        // 2. Check Azure Service Bus connectivity
        // 3. Check Azure Notification Hub status
        // 4. Check Azure SQL Database status
        // 5. Return aggregated health status

        var services = new List<InfrastructureServiceHealth>
        {
            new InfrastructureServiceHealth(
                ServiceId: "azure-app-service",
                ServiceName: "Azure App Service",
                Provider: "Azure",
                Category: "Compute",
                State: HealthState.NotConfigured,
                StatusMessage: "Azure adapter not yet configured",
                LastChecked: DateTime.UtcNow
            )
        };

        return Task.FromResult<IReadOnlyList<InfrastructureServiceHealth>>(services);
    }
}
