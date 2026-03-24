using Microsoft.AspNetCore.Mvc;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Dtos;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MilestonesController : ControllerBase
{
    private readonly IGitHubPort _githubPort;
    private readonly ILogger<MilestonesController> _logger;

    public MilestonesController(IGitHubPort githubPort, ILogger<MilestonesController> logger)
    {
        _githubPort = githubPort;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<MilestoneDto>>> GetMilestones()
    {
        try
        {
            var milestones = await _githubPort.GetMilestonesAsync();
            var dtos = milestones.Select(m => new MilestoneDto(m.Id, m.Name, m.Description, m.DueDate, m.TotalIssues, m.ClosedIssues, m.PercentComplete)).ToList();
            return Ok(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving milestones");
            return StatusCode(500, new { error = "Failed to retrieve milestones" });
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<MilestoneProgressDto>> GetMilestoneProgress(string id)
    {
        try
        {
            var issues = await _githubPort.GetIssuesByMilestoneAsync(id);
            var milestones = await _githubPort.GetMilestonesAsync();
            var milestone = milestones.FirstOrDefault(m => m.Id == id);
            if (milestone == null) return NotFound(new { error = "Milestone not found" });

            var issuesByStatus = issues.GroupBy(i => i.Status.ToString()).ToDictionary(g => g.Key, g => g.Count());
            var issuesByPlatform = issues.GroupBy(i => i.Platform.ToString()).ToDictionary(g => g.Key, g => g.Count());
            var issuesByAgent = issues.Where(i => !string.IsNullOrEmpty(i.AssignedAgent)).GroupBy(i => i.AssignedAgent).ToDictionary(g => g.Key ?? "Unassigned", g => g.Count());

            return Ok(new MilestoneProgressDto(id, milestone.Name, issuesByStatus, issuesByPlatform, issuesByAgent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving milestone progress for {MilestoneId}", id);
            return StatusCode(500, new { error = "Failed to retrieve milestone progress" });
        }
    }

    [HttpGet("{id}/issues")]
    public async Task<ActionResult<List<MilestoneDto>>> GetIssuesByMilestone(string id)
    {
        try
        {
            var issues = await _githubPort.GetIssuesByMilestoneAsync(id);
            var dtos = issues.Select(i => new MilestoneDto(i.Id, i.Title, i.Description, DateTime.Now, 1, 0, 0)).ToList();
            return Ok(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving issues for milestone {MilestoneId}", id);
            return StatusCode(500, new { error = "Failed to retrieve issues" });
        }
    }
}
