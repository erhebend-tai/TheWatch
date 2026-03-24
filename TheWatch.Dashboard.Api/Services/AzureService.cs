using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Dtos;

namespace TheWatch.Dashboard.Api.Services;

/// <summary>
/// Azure service providing mock data for resource health, Service Bus, and Notification Hubs.
/// Implements IAzurePort — the domain port defined in TheWatch.Shared.
/// In production, this would use Azure.ResourceManager to call Azure APIs.
/// </summary>
public class AzureService : IAzurePort
{
    private readonly ILogger<AzureService> _logger;

    public AzureService(ILogger<AzureService> logger) => _logger = logger;

    public Task<List<HealthStatusDto>> GetResourceHealthAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<HealthStatusDto>
        {
            new("Azure App Service (Backend API)", true, "Running in Standard tier, 2 instances, CPU: 45%, Memory: 62%", DateTime.Now),
            new("Azure SQL Database", true, "Standard tier, 98.5% uptime, DTU usage: 35%", DateTime.Now),
            new("Azure Cosmos DB (Firestore backup)", true, "Provisioned 400 RU/s, 2.1 GB stored, no throttling", DateTime.Now.AddMinutes(-5)),
            new("Azure Service Bus", true, "Standard tier, 0 dead-letter messages, throughput nominal", DateTime.Now.AddMinutes(-10)),
        });

    public Task<Dictionary<string, object>> GetServiceBusMetricsAsync(CancellationToken ct = default) =>
        Task.FromResult(new Dictionary<string, object>
        {
            { "ActiveMessages", 145 }, { "DeadLetterMessages", 0 }, { "IncomingMessages", 12450 },
            { "OutgoingMessages", 12410 }, { "FailedAuthentications", 2 }, { "ThrottledRequests", 0 },
            { "LastUpdated", DateTime.Now }
        });

    public Task<Dictionary<string, object>> GetNotificationHubStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new Dictionary<string, object>
        {
            { "Status", "Active" }, { "ConnectedDevices", 1243 }, { "NotificationsSent24h", 54320 },
            { "DeliveryRate", "99.7%" }, { "AverageLatency", "245ms" }, { "LastUpdated", DateTime.Now.AddMinutes(-2) }
        });
}
