using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Dtos;

namespace TheWatch.Dashboard.Api.Services;

/// <summary>
/// AWS service providing mock data for Alexa skill and Lambda function health.
/// Implements IAwsPort — the domain port defined in TheWatch.Shared.
/// In production, this would use AWSSDK.Lambda and AWSSDK.AlexaForBusiness.
/// </summary>
public class AwsService : IAwsPort
{
    private readonly ILogger<AwsService> _logger;

    public AwsService(ILogger<AwsService> logger) => _logger = logger;

    public Task<Dictionary<string, object>> GetAlexaSkillStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new Dictionary<string, object>
        {
            { "SkillId", "amzn1.ask.skill.thewatch-v1" }, { "Status", "Published" }, { "Version", "1.2.1" },
            { "EnablementStatus", "Enabled" }, { "DailyActiveUsers", 342 }, { "MonthlyActiveUsers", 1245 },
            { "AverageSessionDuration", "3m 45s" }, { "LastUpdated", DateTime.Now.AddDays(-7) },
            { "IntentCoverage", new Dictionary<string, int> { { "EmergencyAlert", 156 }, { "HealthQuery", 289 }, { "DeviceStatus", 201 }, { "Help", 127 } } }
        });

    public Task<List<HealthStatusDto>> GetLambdaHealthAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<HealthStatusDto>
        {
            new("Lambda: Alexa Intent Handler", true, "0.82s avg duration, 128 MB memory, 99.8% success rate", DateTime.Now),
            new("Lambda: Alert Processing", true, "1.24s avg duration, 512 MB memory, processing 245 events/min", DateTime.Now.AddSeconds(-30)),
            new("Lambda: Sensor Data Sync", true, "2.15s avg duration, 256 MB memory, 4,230 invocations/hour", DateTime.Now.AddMinutes(-2)),
            new("Lambda: Notification Dispatcher", true, "0.45s avg duration, 64 MB memory, 987 messages queued", DateTime.Now.AddMinutes(-1)),
        });
}
