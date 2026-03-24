// MockAzureAdapter — returns healthy status for all Azure resources.
// Example:
//   services.AddSingleton<IAzurePort, MockAzureAdapter>();
//   var health = await azure.GetResourceHealthAsync();
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Dtos;

namespace TheWatch.Data.Adapters.Mock;

public class MockAzureAdapter : IAzurePort
{
    public Task<List<HealthStatusDto>> GetResourceHealthAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<HealthStatusDto>
        {
            new("Azure App Service", true, "Running, Central US, CPU: 45%", DateTime.UtcNow),
            new("Azure SQL Database", true, "Standard tier, DTU: 35%", DateTime.UtcNow),
            new("Azure Cosmos DB", true, "400 RU/s, no throttling", DateTime.UtcNow),
            new("Azure Service Bus", true, "Standard tier, 0 dead-letter", DateTime.UtcNow),
            new("Azure Notification Hub", true, "Active, 1250 registrations", DateTime.UtcNow),
        });

    public Task<Dictionary<string, object>> GetServiceBusMetricsAsync(CancellationToken ct = default) =>
        Task.FromResult(new Dictionary<string, object>
        {
            ["activeMessageCount"] = 42,
            ["deadLetterMessageCount"] = 0,
            ["scheduledMessageCount"] = 3,
            ["transferMessageCount"] = 0,
            ["topicCount"] = 4,
            ["subscriptionCount"] = 12,
        });

    public Task<Dictionary<string, object>> GetNotificationHubStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new Dictionary<string, object>
        {
            ["registrationCount"] = 1250,
            ["apnsRegistrations"] = 580,
            ["fcmRegistrations"] = 670,
            ["scheduledNotifications"] = 5,
            ["lastNotificationSentAt"] = DateTime.UtcNow.AddMinutes(-2).ToString("O"),
        });
}
