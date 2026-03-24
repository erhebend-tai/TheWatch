// SwarmInventoryController — REST endpoints for swarm file inventory CRUD.
// Backed by Firestore via ISwarmInventoryPort.
//
// GET    /api/swarm-inventory/files              → all files
// GET    /api/swarm-inventory/files/{id}         → single file
// GET    /api/swarm-inventory/files/project/{p}  → files by project
// PUT    /api/swarm-inventory/files              → upsert file
// PUT    /api/swarm-inventory/files/batch        → upsert batch
// DELETE /api/swarm-inventory/files/{id}         → delete file
// GET    /api/swarm-inventory/supervisors        → all supervisors
// PUT    /api/swarm-inventory/supervisors        → upsert supervisor
// PUT    /api/swarm-inventory/files/{id}/goals   → update goals
// POST   /api/swarm-inventory/seed              → seed if empty

using Microsoft.AspNetCore.Mvc;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/swarm-inventory")]
public class SwarmInventoryController : ControllerBase
{
    private readonly ISwarmInventoryPort _inventory;
    private readonly ILogger<SwarmInventoryController> _logger;

    public SwarmInventoryController(ISwarmInventoryPort inventory, ILogger<SwarmInventoryController> logger)
    {
        _inventory = inventory;
        _logger = logger;
    }

    // ── Files ────────────────────────────────────────────────────

    [HttpGet("files")]
    public async Task<ActionResult<List<SwarmFileRecord>>> GetAllFiles(CancellationToken ct)
    {
        var files = await _inventory.GetAllFilesAsync(ct);
        return Ok(files);
    }

    [HttpGet("files/{id}")]
    public async Task<ActionResult<SwarmFileRecord>> GetFile(string id, CancellationToken ct)
    {
        var file = await _inventory.GetFileAsync(id, ct);
        return file is not null ? Ok(file) : NotFound();
    }

    [HttpGet("files/project/{project}")]
    public async Task<ActionResult<List<SwarmFileRecord>>> GetFilesByProject(string project, CancellationToken ct)
    {
        var files = await _inventory.GetFilesByProjectAsync(project, ct);
        return Ok(files);
    }

    [HttpPut("files")]
    public async Task<ActionResult> UpsertFile([FromBody] SwarmFileRecord file, CancellationToken ct)
    {
        await _inventory.UpsertFileAsync(file, ct);
        return Ok(new { file.Id, Status = "Upserted" });
    }

    [HttpPut("files/batch")]
    public async Task<ActionResult> UpsertFilesBatch([FromBody] List<SwarmFileRecord> files, CancellationToken ct)
    {
        await _inventory.UpsertFilesAsync(files, ct);
        return Ok(new { Count = files.Count, Status = "BatchUpserted" });
    }

    [HttpDelete("files/{id}")]
    public async Task<ActionResult> DeleteFile(string id, CancellationToken ct)
    {
        await _inventory.DeleteFileAsync(id, ct);
        return Ok(new { Id = id, Status = "Deleted" });
    }

    // ── Supervisors ──────────────────────────────────────────────

    [HttpGet("supervisors")]
    public async Task<ActionResult<List<SwarmSupervisorRecord>>> GetSupervisors(CancellationToken ct)
    {
        var supervisors = await _inventory.GetSupervisorsAsync(ct);
        return Ok(supervisors);
    }

    [HttpPut("supervisors")]
    public async Task<ActionResult> UpsertSupervisor([FromBody] SwarmSupervisorRecord supervisor, CancellationToken ct)
    {
        await _inventory.UpsertSupervisorAsync(supervisor, ct);
        return Ok(new { supervisor.Id, Status = "Upserted" });
    }

    // ── Goals ────────────────────────────────────────────────────

    [HttpPut("files/{fileId}/goals")]
    public async Task<ActionResult> UpdateGoals(string fileId, [FromBody] List<SwarmGoalRecord> goals, CancellationToken ct)
    {
        await _inventory.UpdateGoalsAsync(fileId, goals, ct);
        return Ok(new { FileId = fileId, GoalCount = goals.Count, Status = "Updated" });
    }

    // ── Seeding ──────────────────────────────────────────────────

    [HttpPost("seed")]
    public async Task<ActionResult> Seed(
        [FromBody] SwarmSeedRequest request,
        CancellationToken ct)
    {
        _logger.LogInformation("Seed requested: {FileCount} files, {SupervisorCount} supervisors",
            request.Files.Count, request.Supervisors.Count);

        await _inventory.SeedIfEmptyAsync(request.Files, request.Supervisors, ct);
        return Ok(new { Status = "Seeded" });
    }
}

public record SwarmSeedRequest(List<SwarmFileRecord> Files, List<SwarmSupervisorRecord> Supervisors);
