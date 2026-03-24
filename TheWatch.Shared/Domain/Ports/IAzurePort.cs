// IAzurePort — domain port for Azure resource health, Service Bus, and Notification Hub.
// NO database SDK imports allowed in this file.
// Example:
//   var health = await azure.GetResourceHealthAsync();
//   var metrics = await azure.GetServiceBusMetricsAsync();
using TheWatch.Shared.Dtos;

namespace TheWatch.Shared.Domain.Ports;

public interface IAzurePort
{
    Task<List<HealthStatusDto>> GetResourceHealthAsync(CancellationToken ct = default);
    Task<Dictionary<string, object>> GetServiceBusMetricsAsync(CancellationToken ct = default);
    Task<Dictionary<string, object>> GetNotificationHubStatusAsync(CancellationToken ct = default);
}
