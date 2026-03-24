using Microsoft.AspNetCore.Mvc;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Dtos;
using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WorkItemsController : ControllerBase
{
    private readonly IGitHubPort _githubPort;
    private readonly ILogger<WorkItemsController> _logger;

    public WorkItemsController(IGitHubPort githubPort, ILogger<WorkItemsController> logger)
    {
        _githubPort = githubPort;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<WorkItemDto>>> GetWorkItems(
        [FromQuery] string? milestone = null, [FromQuery] string? agent = null,
        [FromQuery] string? platform = null, [FromQuery] string? status = null)
    {
        try
        {
            var allIssues = new List<WorkItem>();
            if (!string.IsNullOrEmpty(milestone))
            {
                allIssues.AddRange(await _githubPort.GetIssuesByMilestoneAsync(milestone));
            }
            else
            {
                var milestones = await _githubPort.GetMilestonesAsync();
                foreach (var m in milestones)
                    allIssues.AddRange(await _githubPort.GetIssuesByMilestoneAsync(m.Id));
            }

            var filtered = allIssues
                .Where(i => string.IsNullOrEmpty(agent) || i.AssignedAgent == agent)
                .Where(i => string.IsNullOrEmpty(platform) || i.Platform.ToString() == platform)
                .Where(i => string.IsNullOrEmpty(status) || i.Status.ToString() == status)
                .Select(i => new WorkItemDto(i.Id, i.Title, i.Description, i.Milestone, i.Platform, i.AssignedAgent, i.Status, i.Priority, i.Type, i.BranchName, i.PrUrl, i.CreatedAt, i.UpdatedAt))
                .ToList();
            return Ok(filtered);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving work items");
            return StatusCode(500, new { error = "Failed to retrieve work items" });
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<WorkItemDto>> GetWorkItem(string id)
    {
        try
        {
            var milestones = await _githubPort.GetMilestonesAsync();
            foreach (var milestone in milestones)
            {
                var issues = await _githubPort.GetIssuesByMilestoneAsync(milestone.Id);
                var issue = issues.FirstOrDefault(i => i.Id == id);
                if (issue != null)
                    return Ok(new WorkItemDto(issue.Id, issue.Title, issue.Description, issue.Milestone, issue.Platform, issue.AssignedAgent, issue.Status, issue.Priority, issue.Type, issue.BranchName, issue.PrUrl, issue.CreatedAt, issue.UpdatedAt));
            }
            return NotFound(new { error = "Work item not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving work item {WorkItemId}", id);
            return StatusCode(500, new { error = "Failed to retrieve work item" });
        }
    }

    [HttpPost]
    public async Task<ActionResult<WorkItemDto>> CreateWorkItem([FromBody] CreateWorkItemRequest request)
    {
        try
        {
            var workItem = new WorkItem
            {
                Id = $"GH-{Guid.NewGuid().GetHashCode() % 10000}",
                Title = request.Title, Description = request.Description, Milestone = request.Milestone,
                Platform = request.Platform, AssignedAgent = request.AssignedAgent,
                Status = WorkItemStatus.Backlog, Priority = request.Priority, Type = request.Type,
                CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now
            };
            var dto = new WorkItemDto(workItem.Id, workItem.Title, workItem.Description, workItem.Milestone, workItem.Platform, workItem.AssignedAgent, workItem.Status, workItem.Priority, workItem.Type, workItem.BranchName, workItem.PrUrl, workItem.CreatedAt, workItem.UpdatedAt);
            return CreatedAtAction(nameof(GetWorkItem), new { id = workItem.Id }, dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating work item");
            return StatusCode(500, new { error = "Failed to create work item" });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateWorkItem(string id, [FromBody] UpdateWorkItemRequest request)
    {
        try
        {
            _logger.LogInformation("Updating work item {WorkItemId}", id);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating work item {WorkItemId}", id);
            return StatusCode(500, new { error = "Failed to update work item" });
        }
    }
}

public record CreateWorkItemRequest(string Title, string Description, string Milestone, Platform Platform, string? AssignedAgent, WorkItemPriority Priority, WorkItemType Type);
public record UpdateWorkItemRequest(string? Title = null, string? Description = null, string? AssignedAgent = null, WorkItemStatus? Status = null);
