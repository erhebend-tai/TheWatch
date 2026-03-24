// BuildOutputController — REST endpoints for build output persistence and querying.
// Receives build results from CI/CD, Claude Code, or manual submissions and persists
// to the configured store (SQLite default, switchable to SQL Server/PostgreSQL/CosmosDB/Firestore).
//
// Endpoints:
//   POST   /api/buildoutput                     — Submit a raw build result (auto-parses diagnostics)
//   POST   /api/buildoutput/raw                 — Submit pre-parsed build output
//   GET    /api/buildoutput/recent              — Recent builds (with optional project filter)
//   GET    /api/buildoutput/{id}                — Single build with full stdout/stderr + diagnostics
//   GET    /api/buildoutput/failed              — Failed builds since a date
//   GET    /api/buildoutput/diagnostics         — Errors/warnings across builds
//   GET    /api/buildoutput/stats               — Aggregated statistics
//   DELETE /api/buildoutput/purge               — Delete old records (retention)

using Microsoft.AspNetCore.Mvc;
using TheWatch.Dashboard.Api.Services;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BuildOutputController : ControllerBase
{
    private readonly IBuildOutputPort _buildPort;
    private readonly ILogger<BuildOutputController> _logger;

    public BuildOutputController(IBuildOutputPort buildPort, ILogger<BuildOutputController> logger)
    {
        _buildPort = buildPort;
        _logger = logger;
    }

    /// <summary>
    /// Submit raw build output — auto-parses stdout/stderr for errors and warnings.
    /// Used by CI/CD pipelines, Claude Code hooks, and local build wrappers.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SubmitBuild([FromBody] BuildSubmission submission, CancellationToken ct)
    {
        var combinedOutput = $"{submission.Stdout}\n{submission.Stderr}";
        var diagnostics = BuildOutputParserService.ParseOutput(combinedOutput);
        var result = BuildOutputParserService.DetermineResult(submission.ExitCode, diagnostics);

        var buildOutput = new BuildOutput
        {
            ProjectName = submission.ProjectName,
            Configuration = submission.Configuration ?? "Debug",
            TargetFramework = submission.TargetFramework,
            Command = submission.Command,
            TriggerSource = submission.TriggerSource,
            Branch = submission.Branch,
            CommitSha = submission.CommitSha,
            ExitCode = submission.ExitCode,
            Succeeded = submission.ExitCode == 0,
            Result = result,
            Stdout = submission.Stdout,
            Stderr = submission.Stderr,
            Diagnostics = diagnostics,
            ErrorCount = diagnostics.Count(d => d.Severity >= BuildOutputSeverity.Error),
            WarningCount = diagnostics.Count(d => d.Severity == BuildOutputSeverity.Warning),
            StartedAt = submission.StartedAt ?? DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            DurationMs = submission.DurationMs,
            MachineName = Environment.MachineName,
            OsVersion = Environment.OSVersion.ToString(),
            DotNetVersion = Environment.Version.ToString(),
            Store = _buildPort.Store
        };

        var saveResult = await _buildPort.SaveAsync(buildOutput, ct);
        if (!saveResult.Success)
            return StatusCode(500, new { error = saveResult.ErrorMessage });

        _logger.LogInformation(
            "Build output saved: {Id} | {Project} | {Result} | {Errors} errors, {Warnings} warnings | Store={Store}",
            buildOutput.Id, buildOutput.ProjectName, result, buildOutput.ErrorCount, buildOutput.WarningCount, _buildPort.Store);

        return Accepted(new
        {
            buildOutput.Id,
            buildOutput.ProjectName,
            Result = result.ToString(),
            buildOutput.ErrorCount,
            buildOutput.WarningCount,
            buildOutput.DurationMs,
            Store = _buildPort.Store.ToString(),
            DiagnosticsSample = diagnostics.Take(5).Select(d => new { d.Severity, d.Code, d.Message, d.FilePath, d.Line })
        });
    }

    /// <summary>Submit a pre-parsed BuildOutput (from systems that parse their own output).</summary>
    [HttpPost("raw")]
    public async Task<IActionResult> SubmitRaw([FromBody] BuildOutput output, CancellationToken ct)
    {
        output.Store = _buildPort.Store;
        var result = await _buildPort.SaveAsync(output, ct);
        return result.Success ? Accepted(new { output.Id, output.ProjectName }) : StatusCode(500, new { error = result.ErrorMessage });
    }

    [HttpGet("recent")]
    public async Task<IActionResult> GetRecent([FromQuery] int limit = 50, [FromQuery] string? project = null, CancellationToken ct = default)
    {
        var result = await _buildPort.GetRecentAsync(limit, project, ct);
        return Ok(result.Data ?? new());
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var result = await _buildPort.GetByIdAsync(id, ct);
        return result.Success ? Ok(result.Data) : NotFound(new { error = result.ErrorMessage });
    }

    [HttpGet("failed")]
    public async Task<IActionResult> GetFailed([FromQuery] int hoursBack = 24, [FromQuery] string? project = null, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddHours(-hoursBack);
        var result = await _buildPort.GetFailedAsync(since, project, ct);
        return Ok(result.Data ?? new());
    }

    [HttpGet("diagnostics")]
    public async Task<IActionResult> GetDiagnostics(
        [FromQuery] int hoursBack = 24,
        [FromQuery] BuildOutputSeverity? severity = null,
        [FromQuery] string? project = null,
        CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddHours(-hoursBack);
        var result = await _buildPort.GetDiagnosticsAsync(since, severity, project, ct);
        return Ok(result.Data ?? new());
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats([FromQuery] int hoursBack = 24, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddHours(-hoursBack);
        var result = await _buildPort.GetStatsAsync(since, ct);
        return result.Success ? Ok(result.Data) : StatusCode(500, new { error = result.ErrorMessage });
    }

    [HttpDelete("purge")]
    public async Task<IActionResult> Purge([FromQuery] int daysOlderThan = 30, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-daysOlderThan);
        var result = await _buildPort.PurgeAsync(cutoff, ct);
        _logger.LogInformation("Purged {Count} build records older than {Cutoff}", result.Data, cutoff);
        return Ok(new { purged = result.Data, olderThan = cutoff });
    }
}

public record BuildSubmission(
    string ProjectName,
    string? Configuration = null,
    string? TargetFramework = null,
    string? Command = null,
    string? TriggerSource = null,
    string? Branch = null,
    string? CommitSha = null,
    int ExitCode = 0,
    string? Stdout = null,
    string? Stderr = null,
    long DurationMs = 0,
    DateTime? StartedAt = null
);
