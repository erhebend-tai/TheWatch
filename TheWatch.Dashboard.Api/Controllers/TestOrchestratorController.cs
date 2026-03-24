using Microsoft.AspNetCore.Mvc;
using TheWatch.Dashboard.Api.Services;

namespace TheWatch.Dashboard.Api.Controllers;

/// <summary>
/// REST API for the test orchestrator.
/// The MAUI dashboard calls these to manage test suites and runs,
/// while connected mobile devices receive step instructions via SignalR.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TestOrchestratorController : ControllerBase
{
    private readonly ITestOrchestratorService _orchestrator;
    private readonly ILogger<TestOrchestratorController> _logger;

    public TestOrchestratorController(ITestOrchestratorService orchestrator, ILogger<TestOrchestratorController> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    /// <summary>
    /// List all available test suites.
    /// </summary>
    [HttpGet("suites")]
    public async Task<ActionResult<IReadOnlyList<TestSuite>>> GetSuites()
        => Ok(await _orchestrator.GetSuitesAsync());

    /// <summary>
    /// Get a specific test suite.
    /// </summary>
    [HttpGet("suites/{suiteId}")]
    public async Task<ActionResult<TestSuite>> GetSuite(string suiteId)
    {
        var suite = await _orchestrator.GetSuiteAsync(suiteId);
        return suite != null ? Ok(suite) : NotFound();
    }

    /// <summary>
    /// Start a new test run on a target device.
    /// </summary>
    [HttpPost("runs")]
    public async Task<ActionResult<TestRun>> StartRun([FromBody] StartTestRunRequest request)
    {
        try
        {
            var run = await _orchestrator.StartRunAsync(request.SuiteId, request.TargetDevice);
            return CreatedAtAction(nameof(GetRun), new { runId = run.Id }, run);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Record a step result from a device.
    /// </summary>
    [HttpPost("runs/{runId}/steps/{stepId}")]
    public async Task<ActionResult<TestStepResult>> RecordStep(
        string runId, string stepId, [FromBody] StepResultRequest request)
    {
        try
        {
            var result = await _orchestrator.RecordStepResultAsync(
                runId, stepId, request.Passed, request.Screenshot, request.ErrorMessage);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get the status of a test run.
    /// </summary>
    [HttpGet("runs/{runId}")]
    public async Task<ActionResult<TestRun>> GetRun(string runId)
    {
        var run = await _orchestrator.GetRunAsync(runId);
        return run != null ? Ok(run) : NotFound();
    }

    /// <summary>
    /// List test runs, optionally filtered.
    /// </summary>
    [HttpGet("runs")]
    public async Task<ActionResult<IReadOnlyList<TestRun>>> GetRuns(
        [FromQuery] string? suiteId = null,
        [FromQuery] string? device = null,
        [FromQuery] int limit = 50)
        => Ok(await _orchestrator.GetRunsAsync(suiteId, device, limit));

    /// <summary>
    /// Cancel a running test.
    /// </summary>
    [HttpPost("runs/{runId}/cancel")]
    public async Task<ActionResult> CancelRun(string runId)
    {
        var cancelled = await _orchestrator.CancelRunAsync(runId);
        return cancelled ? Ok(new { status = "Cancelled" }) : NotFound();
    }
}

// ── Request DTOs ─────────────────────────────────────────────

public record StartTestRunRequest(string SuiteId, string TargetDevice);
public record StepResultRequest(bool Passed, string? Screenshot = null, string? ErrorMessage = null);
