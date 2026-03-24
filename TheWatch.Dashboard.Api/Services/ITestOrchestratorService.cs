using TheWatch.Shared.Domain.Models;

namespace TheWatch.Dashboard.Api.Services;

/// <summary>
/// Test orchestration service for driving screen-by-screen mobile test scenarios.
///
/// The MAUI dashboard acts as a test orchestrator: it defines test suites
/// (sequences of screens, actions, and assertions), dispatches them to
/// connected Android/iOS devices via SignalR, and collects results.
///
/// This is NOT a port — it's an application-level orchestrator that lives
/// in Dashboard.Api and composes existing ports + SignalR.
/// </summary>
public interface ITestOrchestratorService
{
    /// <summary>
    /// Get all available test suites.
    /// </summary>
    Task<IReadOnlyList<TestSuite>> GetSuitesAsync(CancellationToken ct = default);

    /// <summary>
    /// Get a specific test suite by ID.
    /// </summary>
    Task<TestSuite?> GetSuiteAsync(string suiteId, CancellationToken ct = default);

    /// <summary>
    /// Start a test run: dispatches test steps to connected devices.
    /// </summary>
    Task<TestRun> StartRunAsync(string suiteId, string targetDevice, CancellationToken ct = default);

    /// <summary>
    /// Record a test step result from a device.
    /// </summary>
    Task<TestStepResult> RecordStepResultAsync(string runId, string stepId, bool passed, string? screenshot = null, string? errorMessage = null, CancellationToken ct = default);

    /// <summary>
    /// Get the current status of a test run.
    /// </summary>
    Task<TestRun?> GetRunAsync(string runId, CancellationToken ct = default);

    /// <summary>
    /// Get all test runs, optionally filtered by suite or device.
    /// </summary>
    Task<IReadOnlyList<TestRun>> GetRunsAsync(string? suiteId = null, string? device = null, int limit = 50, CancellationToken ct = default);

    /// <summary>
    /// Cancel a running test.
    /// </summary>
    Task<bool> CancelRunAsync(string runId, CancellationToken ct = default);
}

// ── Domain models ────────────────────────────────────────────

public record TestSuite(
    string Id,
    string Name,
    string Description,
    string Platform, // "Android", "iOS", "Both"
    List<TestStep> Steps,
    DateTime CreatedAt
);

public record TestStep(
    string Id,
    int Order,
    string ScreenName,
    string Action, // "Navigate", "Tap", "TypeText", "Assert", "TriggerSOS", "WaitFor"
    string Target, // Element identifier or navigation route
    string? Value, // Input value for TypeText, expected value for Assert
    int TimeoutMs = 5000
);

public record TestRun(
    string Id,
    string SuiteId,
    string SuiteName,
    string TargetDevice,
    string Status, // "Running", "Passed", "Failed", "Cancelled"
    DateTime StartedAt,
    DateTime? CompletedAt,
    List<TestStepResult> Results,
    int TotalSteps,
    int CompletedSteps,
    int PassedSteps,
    int FailedSteps
);

public record TestStepResult(
    string StepId,
    int Order,
    string ScreenName,
    string Action,
    bool Passed,
    string? Screenshot, // Base64 or blob URL
    string? ErrorMessage,
    long DurationMs,
    DateTime CompletedAt
);
