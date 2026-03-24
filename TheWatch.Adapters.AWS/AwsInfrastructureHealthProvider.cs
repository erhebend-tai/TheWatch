using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.AWS;

/// <summary>
/// AWS implementation of IInfrastructureHealthProvider.
/// Currently a stub with NotConfigured status.
/// TODO: Implement real AWS health checks.
/// </summary>
public class AwsInfrastructureHealthProvider : IInfrastructureHealthProvider
{
    public string ProviderId => "AWS";
    public string ProviderName => "Amazon Web Services (AWS)";
    public bool IsConfigured => false; // TODO: Set to true when configured

    public Task<IReadOnlyList<InfrastructureServiceHealth>> CheckHealthAsync(CancellationToken ct = default)
    {
        // TODO: Implement real AWS health checks
        // 1. Check AWS Lambda function status
        // 2. Check AWS Alexa skill health
        // 3. Check EC2 instance status
        // 4. Check RDS database status
        // 5. Return aggregated health status

        var services = new List<InfrastructureServiceHealth>
        {
            new InfrastructureServiceHealth(
                ServiceId: "aws-lambda",
                ServiceName: "AWS Lambda",
                Provider: "AWS",
                Category: "Compute",
                State: HealthState.NotConfigured,
                StatusMessage: "AWS adapter not yet configured",
                LastChecked: DateTime.UtcNow
            )
        };

        return Task.FromResult<IReadOnlyList<InfrastructureServiceHealth>>(services);
    }
}
