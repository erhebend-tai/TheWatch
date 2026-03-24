using Microsoft.AspNetCore.Mvc;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Dtos;
using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AgentsController : ControllerBase
{
    private readonly IGitHubPort _githubPort;
    private readonly IFirestorePort _firestorePort;
    private readonly ILogger<AgentsController> _logger;

    public AgentsController(IGitHubPort githubPort, IFirestorePort firestorePort, ILogger<AgentsController> logger)
    {
        _githubPort = githubPort;
        _firestorePort = firestorePort;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<object>>> GetAgents()
    {
        try
        {
            var branches = await _githubPort.GetBranchesAsync();
            var agentGroups = branches
                .GroupBy(b => b.Agent)
                .Select(g => new { Agent = g.Key, ActiveBranches = g.Count(b => b.IsActive), TotalBranches = g.Count(), LastActivity = g.Max(b => b.LastCommitDate) })
                .OrderByDescending(a => a.LastActivity).ToList();
            return Ok(agentGroups);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving agents");
            return StatusCode(500, new { error = "Failed to retrieve agents" });
        }
    }

    [HttpGet("{agent}")]
    public async Task<ActionResult<object>> GetAgentDetails(string agent)
    {
        try
        {
            var branches = await _githubPort.GetBranchesAsync();
            var activity = await _firestorePort.GetRecentActivityAsync(limit: 20);
            var agentBranches = branches.Where(b => b.Agent == agent).ToList();
            var agentActivity = activity.Where(a => a.AgentType.ToString() == agent).ToList();

            return Ok(new
            {
                Agent = agent, Branches = agentBranches, RecentActivity = agentActivity,
                TotalCommits = agentActivity.Count(a => a.Action == "commit"),
                TotalPrs = agentActivity.Count(a => a.Action == "pr"),
                TotalMerges = agentActivity.Count(a => a.Action == "merge")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving agent details for {Agent}", agent);
            return StatusCode(500, new { error = "Failed to retrieve agent details" });
        }
    }

    [HttpGet("activity/recent")]
    public async Task<ActionResult<List<AgentActivityDto>>> GetRecentActivity([FromQuery] int limit = 50)
    {
        try
        {
            var activity = await _firestorePort.GetRecentActivityAsync(limit);
            return Ok(activity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving recent activity");
            return StatusCode(500, new { error = "Failed to retrieve recent activity" });
        }
    }

    [HttpPost("activity")]
    public async Task<IActionResult> LogActivity([FromBody] AgentActivityDto activity)
    {
        try
        {
            var model = new AgentActivity
            {
                AgentType = activity.AgentType,
                Action = activity.Action,
                Description = activity.Description,
                Timestamp = activity.Timestamp,
                BranchName = activity.BranchName,
                Platform = activity.Platform
            };
            await _firestorePort.LogAgentActivityAsync(model);
            _logger.LogInformation("Logged agent activity for {Agent}", activity.AgentType);
            return Accepted();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging agent activity");
            return StatusCode(500, new { error = "Failed to log agent activity" });
        }
    }
}
