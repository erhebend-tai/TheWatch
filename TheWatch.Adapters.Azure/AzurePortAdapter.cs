using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Dtos;

namespace TheWatch.Adapters.Azure;

/// <summary>
/// Azure implementation of IAzurePort.
/// Currently a stub.
/// TODO: Implement real Azure resource integration.
/// </summary>
public class AzurePortAdapter : IAzurePort
{
    public Task<List<HealthStatusDto>> GetResourceHealthAsync(CancellationToken ct = default)
    {
        // TODO: Implement Azure Resource Health API integration
        // 1. Query Azure Resource Manager for resource status
        // 2. Filter for App Service, SQL Database, etc.
        // 3. Return health status for each resource
        throw new NotImplementedException("Azure adapter not yet configured");
    }

    public Task<Dictionary<string, object>> GetServiceBusMetricsAsync(CancellationToken ct = default)
    {
        // TODO: Implement Azure Service Bus metrics collection
        // 1. Connect to Azure Service Bus
        // 2. Query queue/topic metrics
        // 3. Return metrics including message count, processing rate, etc.
        throw new NotImplementedException("Azure adapter not yet configured");
    }

    public Task<Dictionary<string, object>> GetNotificationHubStatusAsync(CancellationToken ct = default)
    {
        // TODO: Implement Azure Notification Hub status retrieval
        // 1. Connect to Azure Notification Hub
        // 2. Query registration count, send success/failure stats
        // 3. Return status metrics
        throw new NotImplementedException("Azure adapter not yet configured");
    }
}
