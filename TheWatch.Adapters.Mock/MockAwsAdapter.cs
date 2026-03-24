using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Dtos;

namespace TheWatch.Adapters.Mock;

/// <summary>
/// Mock implementation of IAwsPort for testing and development.
/// Returns healthy status for AWS Alexa and Lambda services.
/// </summary>
public class MockAwsAdapter : IAwsPort
{
    public Task<Dictionary<string, object>> GetAlexaSkillStatusAsync(CancellationToken ct = default)
    {
        var status = new Dictionary<string, object>
        {
            { "SkillId", "amzn1.ask.skill.mock-skill-id" },
            { "SkillStatus", "PUBLISHED" },
            { "LastPublished", DateTime.UtcNow.AddDays(-7) },
            { "Enabled", true },
            { "UserCount", 1542 },
            { "InvocationsLastDay", 8734 },
            { "AverageSessionDurationSeconds", 145 },
            { "CrashRate", 0.02 },
            { "Health", "Healthy" }
        };

        return Task.FromResult(status);
    }

    public Task<List<HealthStatusDto>> GetLambdaHealthAsync(CancellationToken ct = default)
    {
        var health = new List<HealthStatusDto>
        {
            new HealthStatusDto(
                Provider: "AWS",
                IsHealthy: true,
                Message: "Lambda function: ProcessWorkItems is healthy",
                LastChecked: DateTime.UtcNow
            ),
            new HealthStatusDto(
                Provider: "AWS",
                IsHealthy: true,
                Message: "Lambda function: SyncGitHubData is healthy",
                LastChecked: DateTime.UtcNow
            ),
            new HealthStatusDto(
                Provider: "AWS",
                IsHealthy: true,
                Message: "Lambda function: AuditLogging is healthy",
                LastChecked: DateTime.UtcNow
            ),
            new HealthStatusDto(
                Provider: "AWS",
                IsHealthy: true,
                Message: "Lambda function: SpatialIndexing is healthy",
                LastChecked: DateTime.UtcNow
            )
        };

        return Task.FromResult(health);
    }
}
