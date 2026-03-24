using Microsoft.AspNetCore.Mvc;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Dtos;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly IAzurePort _azurePort;
    private readonly IAwsPort _awsPort;
    private readonly IGitHubPort _githubPort;
    private readonly ILogger<HealthController> _logger;

    public HealthController(IAzurePort azurePort, IAwsPort awsPort, IGitHubPort githubPort, ILogger<HealthController> logger)
    {
        _azurePort = azurePort;
        _awsPort = awsPort;
        _githubPort = githubPort;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<object>> GetAggregatedHealth()
    {
        try
        {
            var azureHealth = await _azurePort.GetResourceHealthAsync();
            var lambdaHealth = await _awsPort.GetLambdaHealthAsync();
            var allHealthStatuses = new List<HealthStatusDto>();
            allHealthStatuses.AddRange(azureHealth);
            allHealthStatuses.AddRange(lambdaHealth);

            return Ok(new
            {
                OverallStatus = allHealthStatuses.All(h => h.IsHealthy) ? "Healthy" : "Degraded",
                HealthyServices = allHealthStatuses.Count(h => h.IsHealthy),
                UnhealthyServices = allHealthStatuses.Count(h => !h.IsHealthy),
                Services = allHealthStatuses.OrderByDescending(h => h.LastChecked).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving aggregated health");
            return StatusCode(500, new { error = "Failed to retrieve aggregated health" });
        }
    }

    [HttpGet("azure")]
    public async Task<ActionResult<List<HealthStatusDto>>> GetAzureHealth()
    {
        try { return Ok(await _azurePort.GetResourceHealthAsync()); }
        catch (Exception ex) { _logger.LogError(ex, "Error retrieving Azure health"); return StatusCode(500, new { error = "Failed to retrieve Azure health" }); }
    }

    [HttpGet("aws")]
    public async Task<ActionResult<List<HealthStatusDto>>> GetAwsHealth()
    {
        try { return Ok(await _awsPort.GetLambdaHealthAsync()); }
        catch (Exception ex) { _logger.LogError(ex, "Error retrieving AWS health"); return StatusCode(500, new { error = "Failed to retrieve AWS health" }); }
    }

    [HttpGet("github")]
    public async Task<ActionResult<object>> GetGitHubHealth()
    {
        try
        {
            var milestones = await _githubPort.GetMilestonesAsync();
            var branches = await _githubPort.GetBranchesAsync();
            var runs = await _githubPort.GetWorkflowRunsAsync();
            return Ok(new { Status = "Connected", Milestones = milestones.Count, ActiveBranches = branches.Count(b => b.IsActive), RecentBuilds = runs.Count, LastCheck = DateTime.Now });
        }
        catch (Exception ex) { _logger.LogError(ex, "Error retrieving GitHub health"); return StatusCode(500, new { error = "Failed to retrieve GitHub health" }); }
    }
}
