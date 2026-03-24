using Microsoft.AspNetCore.Mvc;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Dtos;
using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BuildsController : ControllerBase
{
    private readonly IGitHubPort _githubPort;
    private readonly ILogger<BuildsController> _logger;

    public BuildsController(IGitHubPort githubPort, ILogger<BuildsController> logger)
    {
        _githubPort = githubPort;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<BuildStatusDto>>> GetBuilds([FromQuery] string? platform = null)
    {
        try
        {
            var runs = await _githubPort.GetWorkflowRunsAsync();
            var filtered = string.IsNullOrEmpty(platform) ? runs : runs.Where(r => r.Platform.ToString() == platform).ToList();
            var dtos = filtered.Select(r => new BuildStatusDto(r.WorkflowName, r.RunId, r.Status, r.Platform, r.DurationSeconds, r.TriggeredBy, r.Url, r.StartedAt)).OrderByDescending(d => d.StartedAt).ToList();
            return Ok(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving builds");
            return StatusCode(500, new { error = "Failed to retrieve builds" });
        }
    }

    [HttpGet("{runId}")]
    public async Task<ActionResult<BuildStatusDto>> GetBuildDetails(string runId)
    {
        try
        {
            var runs = await _githubPort.GetWorkflowRunsAsync();
            var run = runs.FirstOrDefault(r => r.RunId == runId);
            if (run == null) return NotFound(new { error = "Build not found" });
            return Ok(new BuildStatusDto(run.WorkflowName, run.RunId, run.Status, run.Platform, run.DurationSeconds, run.TriggeredBy, run.Url, run.StartedAt));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving build details for {RunId}", runId);
            return StatusCode(500, new { error = "Failed to retrieve build details" });
        }
    }

    [HttpGet("stats/summary")]
    public async Task<ActionResult<object>> GetBuildStats()
    {
        try
        {
            var runs = await _githubPort.GetWorkflowRunsAsync();
            var last24h = runs.Where(r => r.StartedAt > DateTime.Now.AddHours(-24)).ToList();
            return Ok(new
            {
                TotalRuns = runs.Count, Last24hRuns = last24h.Count,
                SuccessCount = last24h.Count(r => r.Status == BuildResult.Success),
                FailureCount = last24h.Count(r => r.Status == BuildResult.Failure),
                SuccessRate = last24h.Any() ? (last24h.Count(r => r.Status == BuildResult.Success) * 100) / last24h.Count : 0,
                AverageDuration = last24h.Any() ? last24h.Average(r => r.DurationSeconds) : 0,
                ByPlatform = last24h.GroupBy(r => r.Platform.ToString()).ToDictionary(g => g.Key, g => new { Total = g.Count(), Success = g.Count(r => r.Status == BuildResult.Success), Failure = g.Count(r => r.Status == BuildResult.Failure) })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving build stats");
            return StatusCode(500, new { error = "Failed to retrieve build stats" });
        }
    }
}
