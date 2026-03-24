// MockAwsAdapter — returns healthy Lambda/Alexa status for dev/test.
// Example:
//   services.AddSingleton<IAwsPort, MockAwsAdapter>();
//   var health = await aws.GetLambdaHealthAsync();
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Dtos;

namespace TheWatch.Data.Adapters.Mock;

public class MockAwsAdapter : IAwsPort
{
    public Task<Dictionary<string, object>> GetAlexaSkillStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new Dictionary<string, object>
        {
            ["skillId"] = "amzn1.ask.skill.thewatch-safety",
            ["status"] = "LIVE",
            ["version"] = "1.2.0",
            ["intentCount"] = 8,
            ["intentCoverage"] = new[]
            {
                "TriggerSOSIntent", "CheckInIntent", "StatusQueryIntent",
                "NearbyRespondersIntent", "CancelAlertIntent", "EmergencyContactsIntent",
                "LocationUpdateIntent", "HelpIntent"
            },
            ["lastPublished"] = DateTime.UtcNow.AddDays(-3).ToString("O"),
        });

    public Task<List<HealthStatusDto>> GetLambdaHealthAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<HealthStatusDto>
        {
            new("Lambda: thewatch-sos-handler", true, "us-east-1, 0.82s avg", DateTime.UtcNow),
            new("Lambda: thewatch-notification-dispatcher", true, "us-east-1, 0.45s avg", DateTime.UtcNow),
            new("Lambda: thewatch-geofence-processor", true, "us-east-1, 1.2s avg", DateTime.UtcNow),
            new("Lambda: thewatch-audit-archiver", true, "us-east-1, 2.1s avg", DateTime.UtcNow),
        });
}
