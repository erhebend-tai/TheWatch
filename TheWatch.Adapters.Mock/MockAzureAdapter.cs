using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Dtos;

namespace TheWatch.Adapters.Mock;

/// <summary>
/// Mock implementation of IAzurePort for testing and development.
/// Returns healthy status for Azure services.
/// </summary>
public class MockAzureAdapter : IAzurePort
{
    public Task<List<HealthStatusDto>> GetResourceHealthAsync(CancellationToken ct = default)
    {
        var health = new List<HealthStatusDto>
        {
            new HealthStatusDto(
                Provider: "Azure",
                IsHealthy: true,
                Message: "Azure App Service is running normally",
                LastChecked: DateTime.UtcNow
            ),
            new HealthStatusDto(
                Provider: "Azure",
                IsHealthy: true,
                Message: "Azure SQL Database is operational",
                LastChecked: DateTime.UtcNow
            ),
            new HealthStatusDto(
                Provider: "Azure",
                IsHealthy: true,
                Message: "Azure KeyVault is accessible",
                LastChecked: DateTime.UtcNow
            )
        };

        return Task.FromResult(health);
    }

    public Task<Dictionary<string, object>> GetServiceBusMetricsAsync(CancellationToken ct = default)
    {
        var metrics = new Dictionary<string, object>
        {
            { "QueuesActive", 8 },
            { "MessagesInQueue", 124 },
            { "MessagesProcessedPerSecond", 42.5 },
            { "AverageProcessingTimeMs", 235 },
            { "DeadLetterMessageCount", 3 },
            { "ConnectionStatus", "Connected" },
            { "LastRefreshed", DateTime.UtcNow }
        };

        return Task.FromResult(metrics);
    }

    public Task<Dictionary<string, object>> GetNotificationHubStatusAsync(CancellationToken ct = default)
    {
        var status = new Dictionary<string, object>
        {
            { "RegistrationCount", 2847 },
            { "SendSuccessCount", 18942 },
            { "SendFailureCount", 12 },
            { "AverageLantencyMs", 145 },
            { "QuotaUsagePercent", 34 },
            { "Status", "Healthy" },
            { "LastRefreshed", DateTime.UtcNow }
        };

        return Task.FromResult(status);
    }
}
