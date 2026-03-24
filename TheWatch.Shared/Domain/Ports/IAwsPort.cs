// IAwsPort — domain port for AWS Alexa skill and Lambda health.
// NO database SDK imports allowed in this file.
// Example:
//   var alexaStatus = await aws.GetAlexaSkillStatusAsync();
//   var lambdaHealth = await aws.GetLambdaHealthAsync();
using TheWatch.Shared.Dtos;

namespace TheWatch.Shared.Domain.Ports;

public interface IAwsPort
{
    Task<Dictionary<string, object>> GetAlexaSkillStatusAsync(CancellationToken ct = default);
    Task<List<HealthStatusDto>> GetLambdaHealthAsync(CancellationToken ct = default);
}
