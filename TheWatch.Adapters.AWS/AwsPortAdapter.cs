using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Dtos;

namespace TheWatch.Adapters.AWS;

/// <summary>
/// AWS implementation of IAwsPort.
/// Currently a stub.
/// TODO: Implement real AWS resource integration.
/// </summary>
public class AwsPortAdapter : IAwsPort
{
    public Task<Dictionary<string, object>> GetAlexaSkillStatusAsync(CancellationToken ct = default)
    {
        // TODO: Implement AWS Alexa skill status retrieval
        // 1. Use AWS Alexa Skills Kit API
        // 2. Query skill metadata and statistics
        // 3. Return skill status and metrics
        throw new NotImplementedException("AWS adapter not yet configured");
    }

    public Task<List<HealthStatusDto>> GetLambdaHealthAsync(CancellationToken ct = default)
    {
        // TODO: Implement AWS Lambda health checks
        // 1. Query Lambda function status via AWS SDK
        // 2. Check function configuration and availability
        // 3. Monitor recent invocations for errors
        // 4. Return health status for each Lambda function
        throw new NotImplementedException("AWS adapter not yet configured");
    }
}
