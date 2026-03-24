// SwarmController — REST endpoints for the swarm dashboard.
//
// GET  /api/swarm/health       → current swarm health snapshot
// POST /api/swarm/dispatch     → dispatch a task to an agent via RabbitMQ
// POST /api/swarm/refresh      → trigger inventory refresh via Hangfire

using Microsoft.AspNetCore.Mvc;
using TheWatch.Dashboard.Api.Services;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SwarmController : ControllerBase
{
    private readonly ISwarmCoordinationService _swarm;
    private readonly ILogger<SwarmController> _logger;

    public SwarmController(ISwarmCoordinationService swarm, ILogger<SwarmController> logger)
    {
        _swarm = swarm;
        _logger = logger;
    }

    /// <summary>Get current swarm health snapshot.</summary>
    [HttpGet("health")]
    public ActionResult<SwarmHealthSnapshot> GetHealth()
    {
        return Ok(_swarm.GetHealthSnapshot());
    }

    /// <summary>Dispatch a task to an agent via RabbitMQ.</summary>
    [HttpPost("dispatch")]
    public async Task<ActionResult> DispatchTask(
        [FromBody] SwarmAgentTask task,
        CancellationToken ct)
    {
        _logger.LogInformation("API: Dispatching swarm task {TaskId} to {Agent}",
            task.TaskId, task.AgentName);

        await _swarm.DispatchAgentTaskAsync(task, ct);
        return Accepted(new { task.TaskId, Status = "Queued" });
    }

    /// <summary>Trigger a full inventory refresh via Hangfire.</summary>
    [HttpPost("refresh")]
    public async Task<ActionResult> RefreshInventory(CancellationToken ct)
    {
        _logger.LogInformation("API: Inventory refresh requested");

        await _swarm.RequestInventoryRefreshAsync(ct);
        return Accepted(new { Status = "RefreshQueued" });
    }
}
