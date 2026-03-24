// =============================================================================
// BuildController — REST API for build orchestration
// =============================================================================
// Provides HTTP endpoints for:
//   - Triggering builds (manual, agent merge, PR validation)
//   - Querying build history and current status
//   - Managing agent branches and merge plans
//   - Querying LSIF index (symbol search, port-adapter map)
//
// The CLI Dashboard polls these endpoints and also receives SignalR push updates.
// Claude Code agents can call these to check build health before committing.
//
// WAL: All mutation endpoints return the BuildRun immediately (queued state).
//      Actual execution is async. Poll GET /api/build/runs/{id} for status.
// =============================================================================

using Microsoft.AspNetCore.Mvc;
using TheWatch.BuildServer.Lsif;
using TheWatch.BuildServer.Models;
using TheWatch.BuildServer.Services;

namespace TheWatch.BuildServer.Controllers;

[ApiController]
[Route("api/build")]
public class BuildController : ControllerBase
{
    private readonly BuildOrchestrator _orchestrator;
    private readonly LsifIndexer _indexer;
    private readonly ILogger<BuildController> _logger;

    public BuildController(BuildOrchestrator orchestrator, LsifIndexer indexer, ILogger<BuildController> logger)
    {
        _orchestrator = orchestrator;
        _indexer = indexer;
        _logger = logger;
    }

    // ── Build Runs ───────────────────────────────────────────────────────────

    /// <summary>POST /api/build/runs — Queue a new build</summary>
    [HttpPost("runs")]
    public ActionResult<BuildRun> QueueBuild([FromBody] QueueBuildRequest? request = null)
    {
        var run = _orchestrator.QueueBuild(
            request?.Trigger ?? BuildTrigger.Manual,
            request?.TriggerSource);
        return Accepted(run);
    }

    /// <summary>GET /api/build/runs — List build history</summary>
    [HttpGet("runs")]
    public ActionResult<IEnumerable<BuildRun>> GetBuildHistory([FromQuery] int limit = 20)
    {
        return Ok(_orchestrator.BuildHistory.TakeLast(limit));
    }

    /// <summary>GET /api/build/runs/{id} — Get specific build run</summary>
    [HttpGet("runs/{id}")]
    public ActionResult<BuildRun> GetBuildRun(string id)
    {
        if (_orchestrator.CurrentBuild?.Id == id)
            return Ok(_orchestrator.CurrentBuild);

        var run = _orchestrator.BuildHistory.FirstOrDefault(r => r.Id == id);
        return run is not null ? Ok(run) : NotFound();
    }

    /// <summary>GET /api/build/status — Current build orchestrator state</summary>
    [HttpGet("status")]
    public ActionResult<BuildStatusResponse> GetStatus()
    {
        return Ok(new BuildStatusResponse(
            _orchestrator.CurrentBuild?.Status.ToString() ?? "Idle",
            _orchestrator.CurrentBuild,
            _orchestrator.LastCompletedBuild,
            _orchestrator.QueueDepth));
    }

    // ── Agent Branches ───────────────────────────────────────────────────────

    /// <summary>GET /api/build/agents — List tracked agent branches</summary>
    [HttpGet("agents")]
    public ActionResult<IEnumerable<AgentBranch>> GetAgentBranches()
    {
        return Ok(_orchestrator.AgentBranches);
    }

    /// <summary>POST /api/build/agents — Register a new agent branch</summary>
    [HttpPost("agents")]
    public ActionResult<AgentBranch> RegisterAgent([FromBody] RegisterAgentRequest request)
    {
        var branch = _orchestrator.RegisterAgentBranch(request.AgentName, request.BranchName, request.Scope);
        return Created($"/api/build/agents/{request.BranchName}", branch);
    }

    /// <summary>PUT /api/build/agents/{branch}/status — Update agent branch status</summary>
    [HttpPut("agents/{branch}/status")]
    public IActionResult UpdateAgentStatus(string branch, [FromBody] UpdateAgentStatusRequest request)
    {
        _orchestrator.UpdateAgentBranch(branch, request.Status, request.FilesModified);
        return NoContent();
    }

    // ── Merge Planning ───────────────────────────────────────────────────────

    /// <summary>POST /api/build/merge/plan — Generate a merge plan</summary>
    [HttpPost("merge/plan")]
    public ActionResult<MergePlan> GenerateMergePlan()
    {
        var plan = _orchestrator.GenerateMergePlan();
        return Ok(plan);
    }

    /// <summary>GET /api/build/merge/plan — Get active merge plan</summary>
    [HttpGet("merge/plan")]
    public ActionResult<MergePlan> GetMergePlan()
    {
        return _orchestrator.ActiveMergePlan is not null
            ? Ok(_orchestrator.ActiveMergePlan)
            : NotFound("No active merge plan");
    }

    /// <summary>POST /api/build/merge/{branch} — Merge a branch and validate</summary>
    [HttpPost("merge/{branch}")]
    public async Task<ActionResult<BuildRun>> MergeAndValidate(string branch, CancellationToken ct)
    {
        try
        {
            var run = await _orchestrator.MergeAndValidateAsync(branch, ct);
            return Accepted(run);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // ── LSIF Index Queries ───────────────────────────────────────────────────

    /// <summary>GET /api/build/index/symbols — Search symbols in LSIF index</summary>
    [HttpGet("index/symbols")]
    public ActionResult<IEnumerable<SymbolInfo>> SearchSymbols(
        [FromQuery] string query,
        [FromQuery] string? kind = null,
        [FromQuery] string? project = null,
        [FromQuery] int limit = 50)
    {
        var index = _indexer.CurrentIndex;
        var results = index.Symbols.AsEnumerable();

        if (!string.IsNullOrEmpty(query))
            results = results.Where(s =>
                s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.FullyQualifiedName.Contains(query, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(kind) && Enum.TryParse<SymbolKind>(kind, true, out var symbolKind))
            results = results.Where(s => s.Kind == symbolKind);

        if (!string.IsNullOrEmpty(project))
            results = results.Where(s => s.ProjectName.Contains(project, StringComparison.OrdinalIgnoreCase));

        return Ok(results.Take(limit));
    }

    /// <summary>GET /api/build/index/ports — Port→adapter mapping</summary>
    [HttpGet("index/ports")]
    public ActionResult<IEnumerable<PortAdapterLink>> GetPortAdapterMap([FromQuery] string? port = null)
    {
        var links = _indexer.CurrentIndex.PortAdapterLinks.AsEnumerable();

        if (!string.IsNullOrEmpty(port))
            links = links.Where(l => l.PortInterfaceName.Contains(port, StringComparison.OrdinalIgnoreCase));

        return Ok(links);
    }

    /// <summary>GET /api/build/index/stats — LSIF index statistics</summary>
    [HttpGet("index/stats")]
    public ActionResult<object> GetIndexStats()
    {
        var index = _indexer.CurrentIndex;
        return Ok(new
        {
            index.GeneratedAt,
            index.Version,
            index.SolutionPath,
            index.TotalFiles,
            index.TotalSymbols,
            index.TotalReferences,
            index.TotalPortAdapterLinks,
            ProjectBreakdown = index.Documents
                .GroupBy(d => d.ProjectName)
                .Select(g => new { Project = g.Key, Files = g.Count() })
                .OrderBy(p => p.Project)
        });
    }

    /// <summary>POST /api/build/index/reindex — Trigger full or incremental re-index</summary>
    [HttpPost("index/reindex")]
    public async Task<ActionResult<LsifIndexSummary>> TriggerReindex(
        [FromQuery] string? project = null, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        LsifIndex index;
        if (project is not null)
            index = await _indexer.IncrementalIndexAsync(project, ct);
        else
            index = await _indexer.BuildFullIndexAsync(ct);

        sw.Stop();

        return Ok(new LsifIndexSummary(
            index.TotalFiles, index.TotalSymbols, index.TotalReferences,
            index.TotalPortAdapterLinks, sw.Elapsed));
    }
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

public record QueueBuildRequest(
    BuildTrigger Trigger = BuildTrigger.Manual,
    string? TriggerSource = null);

public record RegisterAgentRequest(
    string AgentName,
    string BranchName,
    string Scope);

public record UpdateAgentStatusRequest(
    AgentBranchStatus Status,
    List<string>? FilesModified = null);
