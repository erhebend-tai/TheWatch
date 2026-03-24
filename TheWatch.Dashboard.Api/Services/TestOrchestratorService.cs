using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using TheWatch.Dashboard.Api.Hubs;

namespace TheWatch.Dashboard.Api.Services;

/// <summary>
/// In-memory test orchestrator for development.
///
/// Pre-seeded with test suites covering the core mobile flows:
/// authentication, SOS triggers, phrase detection, location tracking,
/// notifications, and volunteering.
///
/// In production, suites would be persisted to Firestore/SQL,
/// and step results would include screenshot blob references.
/// </summary>
public class TestOrchestratorService : ITestOrchestratorService
{
    private readonly ConcurrentDictionary<string, TestSuite> _suites = new();
    private readonly ConcurrentDictionary<string, TestRun> _runs = new();
    private readonly IHubContext<DashboardHub> _hub;
    private readonly ILogger<TestOrchestratorService> _logger;

    public TestOrchestratorService(IHubContext<DashboardHub> hub, ILogger<TestOrchestratorService> logger)
    {
        _hub = hub;
        _logger = logger;
        SeedSuites();
    }

    public Task<IReadOnlyList<TestSuite>> GetSuitesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TestSuite>>(_suites.Values.OrderBy(s => s.Name).ToList());

    public Task<TestSuite?> GetSuiteAsync(string suiteId, CancellationToken ct = default)
        => Task.FromResult(_suites.GetValueOrDefault(suiteId));

    public async Task<TestRun> StartRunAsync(string suiteId, string targetDevice, CancellationToken ct = default)
    {
        if (!_suites.TryGetValue(suiteId, out var suite))
            throw new ArgumentException($"Suite not found: {suiteId}");

        var run = new TestRun(
            Id: $"run_{Guid.NewGuid():N}",
            SuiteId: suiteId,
            SuiteName: suite.Name,
            TargetDevice: targetDevice,
            Status: "Running",
            StartedAt: DateTime.UtcNow,
            CompletedAt: null,
            Results: new List<TestStepResult>(),
            TotalSteps: suite.Steps.Count,
            CompletedSteps: 0,
            PassedSteps: 0,
            FailedSteps: 0
        );

        _runs[run.Id] = run;

        _logger.LogInformation(
            "Test run started: {RunId} — suite '{Suite}' on {Device} ({Steps} steps)",
            run.Id, suite.Name, targetDevice, suite.Steps.Count);

        // Notify connected dashboards
        await _hub.Clients.All.SendAsync("TestRunStarted", run, ct);

        // Dispatch first step to device
        if (suite.Steps.Any())
        {
            await _hub.Clients.Group($"device_{targetDevice}")
                .SendAsync("ExecuteTestStep", run.Id, suite.Steps[0], ct);
        }

        return run;
    }

    public async Task<TestStepResult> RecordStepResultAsync(
        string runId, string stepId, bool passed,
        string? screenshot = null, string? errorMessage = null,
        CancellationToken ct = default)
    {
        if (!_runs.TryGetValue(runId, out var run))
            throw new ArgumentException($"Run not found: {runId}");

        if (!_suites.TryGetValue(run.SuiteId, out var suite))
            throw new InvalidOperationException($"Suite not found: {run.SuiteId}");

        var step = suite.Steps.FirstOrDefault(s => s.Id == stepId)
            ?? throw new ArgumentException($"Step not found: {stepId}");

        var result = new TestStepResult(
            StepId: stepId,
            Order: step.Order,
            ScreenName: step.ScreenName,
            Action: step.Action,
            Passed: passed,
            Screenshot: screenshot,
            ErrorMessage: errorMessage,
            DurationMs: (long)(DateTime.UtcNow - run.StartedAt).TotalMilliseconds,
            CompletedAt: DateTime.UtcNow
        );

        var updatedResults = new List<TestStepResult>(run.Results) { result };
        var completedSteps = updatedResults.Count;
        var passedSteps = updatedResults.Count(r => r.Passed);
        var failedSteps = updatedResults.Count(r => !r.Passed);

        // Determine if run is complete
        var isComplete = completedSteps >= run.TotalSteps || (!passed && errorMessage?.Contains("FATAL") == true);
        var status = isComplete
            ? (failedSteps > 0 ? "Failed" : "Passed")
            : "Running";

        var updated = run with
        {
            Results = updatedResults,
            CompletedSteps = completedSteps,
            PassedSteps = passedSteps,
            FailedSteps = failedSteps,
            Status = status,
            CompletedAt = isComplete ? DateTime.UtcNow : null
        };

        _runs[runId] = updated;

        _logger.LogInformation(
            "Test step {StepId} ({Screen}.{Action}): {Result} — run {RunId} [{Completed}/{Total}]",
            stepId, step.ScreenName, step.Action, passed ? "PASS" : "FAIL",
            runId, completedSteps, run.TotalSteps);

        // Broadcast result
        await _hub.Clients.All.SendAsync("TestStepCompleted", runId, result, ct);

        // Dispatch next step if not complete
        if (!isComplete)
        {
            var nextStep = suite.Steps.FirstOrDefault(s => s.Order == step.Order + 1);
            if (nextStep != null)
            {
                await _hub.Clients.Group($"device_{run.TargetDevice}")
                    .SendAsync("ExecuteTestStep", runId, nextStep, ct);
            }
        }
        else
        {
            await _hub.Clients.All.SendAsync("TestRunCompleted", updated, ct);
        }

        return result;
    }

    public Task<TestRun?> GetRunAsync(string runId, CancellationToken ct = default)
        => Task.FromResult(_runs.GetValueOrDefault(runId));

    public Task<IReadOnlyList<TestRun>> GetRunsAsync(string? suiteId = null, string? device = null, int limit = 50, CancellationToken ct = default)
    {
        var query = _runs.Values.AsEnumerable();
        if (suiteId != null) query = query.Where(r => r.SuiteId == suiteId);
        if (device != null) query = query.Where(r => r.TargetDevice == device);
        return Task.FromResult<IReadOnlyList<TestRun>>(
            query.OrderByDescending(r => r.StartedAt).Take(limit).ToList());
    }

    public async Task<bool> CancelRunAsync(string runId, CancellationToken ct = default)
    {
        if (!_runs.TryGetValue(runId, out var run) || run.Status != "Running")
            return false;

        _runs[runId] = run with { Status = "Cancelled", CompletedAt = DateTime.UtcNow };
        await _hub.Clients.All.SendAsync("TestRunCompleted", _runs[runId], ct);
        _logger.LogWarning("Test run cancelled: {RunId}", runId);
        return true;
    }

    // ── Seeded test suites ───────────────────────────────────

    private void SeedSuites()
    {
        AddSuite("suite_auth", "Authentication Flow", "Login, sign up, forgot password, biometric", "Both", new()
        {
            Step(1, "LoginScreen", "Navigate", "/login"),
            Step(2, "LoginScreen", "Assert", "email_field", "visible"),
            Step(3, "LoginScreen", "TypeText", "email_field", "test@thewatch.app"),
            Step(4, "LoginScreen", "TypeText", "password_field", "Test1234!"),
            Step(5, "LoginScreen", "Tap", "login_button"),
            Step(6, "HomeScreen", "Assert", "sos_button", "visible"),
            Step(7, "HomeScreen", "Navigate", "/profile"),
            Step(8, "ProfileScreen", "Assert", "user_name", "Test User"),
        });

        AddSuite("suite_sos", "SOS Trigger Pipeline", "Manual SOS, countdown, cancel, and full dispatch", "Both", new()
        {
            Step(1, "HomeScreen", "Assert", "sos_button", "visible"),
            Step(2, "HomeScreen", "Tap", "sos_button"),
            Step(3, "SOSCountdown", "Assert", "countdown_ring", "visible"),
            Step(4, "SOSCountdown", "WaitFor", "countdown_timer", "3000"),
            Step(5, "SOSActive", "Assert", "alert_status", "ACTIVE"),
            Step(6, "SOSActive", "Assert", "location_sharing", "true"),
            Step(7, "SOSActive", "Tap", "cancel_sos_button"),
            Step(8, "HomeScreen", "Assert", "sos_button", "visible"),
        });

        AddSuite("suite_phrase", "Phrase Detection", "Trigger SOS via spoken phrase, test clear word", "Both", new()
        {
            Step(1, "SettingsScreen", "Navigate", "/settings"),
            Step(2, "SettingsScreen", "Assert", "phrase_detection_toggle", "visible"),
            Step(3, "SettingsScreen", "Tap", "phrase_detection_toggle"),
            Step(4, "HomeScreen", "Navigate", "/home"),
            Step(5, "HomeScreen", "TriggerSOS", "phrase", "help me now"),
            Step(6, "SOSCountdown", "Assert", "countdown_ring", "visible"),
            Step(7, "SOSCountdown", "TriggerSOS", "clearword", "all clear"),
            Step(8, "HomeScreen", "Assert", "sos_button", "visible"),
        });

        AddSuite("suite_notifications", "Notification Actions", "Receive SOS dispatch, accept, check-in response", "Both", new()
        {
            Step(1, "HomeScreen", "Assert", "sos_button", "visible"),
            Step(2, "NotificationSimulator", "TriggerSOS", "dispatch_notification", "SOS_DISPATCH"),
            Step(3, "NotificationAction", "Tap", "accept_button"),
            Step(4, "ResponseScreen", "Assert", "response_status", "ACCEPTED"),
            Step(5, "NotificationSimulator", "TriggerSOS", "checkin_notification", "CHECK_IN"),
            Step(6, "NotificationAction", "Tap", "im_ok_button"),
            Step(7, "HomeScreen", "Assert", "sos_button", "visible"),
        });

        AddSuite("suite_location", "Location Tracking Modes", "Normal → Emergency → Passive mode transitions", "Both", new()
        {
            Step(1, "HomeScreen", "Assert", "location_indicator", "visible"),
            Step(2, "HomeScreen", "Assert", "location_mode", "Normal"),
            Step(3, "HomeScreen", "Tap", "sos_button"),
            Step(4, "SOSCountdown", "WaitFor", "countdown_timer", "3000"),
            Step(5, "SOSActive", "Assert", "location_mode", "Emergency"),
            Step(6, "SOSActive", "Tap", "cancel_sos_button"),
            Step(7, "HomeScreen", "WaitFor", "location_deescalate", "5000"),
            Step(8, "HomeScreen", "Assert", "location_mode", "Normal"),
        });

        AddSuite("suite_volunteer", "Volunteer Enrollment", "Enroll as responder, set skills, configure radius", "Both", new()
        {
            Step(1, "VolunteerScreen", "Navigate", "/volunteering"),
            Step(2, "VolunteerScreen", "Assert", "enrollment_toggle", "visible"),
            Step(3, "VolunteerScreen", "Tap", "enrollment_toggle"),
            Step(4, "VolunteerScreen", "Assert", "skills_section", "visible"),
            Step(5, "VolunteerScreen", "Tap", "skill_cpr"),
            Step(6, "VolunteerScreen", "Tap", "skill_first_aid"),
            Step(7, "VolunteerScreen", "Assert", "response_radius_slider", "visible"),
            Step(8, "VolunteerScreen", "Assert", "enrollment_status", "Active"),
        });

        AddSuite("suite_quicktap", "Quick-Tap Detection", "4x tap in 5 seconds triggers SOS", "Both", new()
        {
            Step(1, "SettingsScreen", "Navigate", "/settings"),
            Step(2, "SettingsScreen", "Tap", "quicktap_toggle"),
            Step(3, "HomeScreen", "Navigate", "/home"),
            Step(4, "HomeScreen", "TriggerSOS", "quicktap", "4"),
            Step(5, "SOSCountdown", "Assert", "countdown_ring", "visible"),
            Step(6, "SOSCountdown", "Assert", "trigger_source", "QuickTap"),
        });

        _logger.LogInformation("Seeded {Count} test suites", _suites.Count);
    }

    private void AddSuite(string id, string name, string desc, string platform, List<TestStep> steps)
    {
        _suites[id] = new TestSuite(id, name, desc, platform, steps, DateTime.UtcNow);
    }

    private static TestStep Step(int order, string screen, string action, string target, string? value = null)
        => new($"step_{order:D3}", order, screen, action, target, value);
}
