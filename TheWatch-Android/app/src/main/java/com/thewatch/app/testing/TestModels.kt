package com.thewatch.app.testing

import java.time.Instant

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: TestModels mirrors the C# record types from TestOrchestratorService.cs:
//   TestStep(Id, Order, ScreenName, Action, Target, Value)
//   TestStepResult(StepId, Order, ScreenName, Action, Passed, Screenshot, ErrorMessage, DurationMs, CompletedAt)
//   TestRun(Id, SuiteId, SuiteName, TargetDevice, Status, StartedAt, ...)
//
// The SignalR hub dispatches "ExecuteTestStep" with (runId: String, step: TestStep).
// The device executes the step and reports back via "ReportTestStepResult".
//
// Actions defined in seeded suites:
//   Navigate  — open a screen by route path (e.g., "/login", "/settings", "/home")
//   Tap       — tap a UI element identified by target (e.g., "sos_button", "login_button")
//   TypeText  — type text into a field identified by target (e.g., "email_field")
//   Assert    — check UI state: target visible/has value (e.g., "sos_button" visible)
//   TriggerSOS — trigger SOS via various mechanisms (phrase, clearword, quicktap, dispatch)
//   WaitFor   — poll/wait for a condition with timeout in ms (e.g., "countdown_timer" "3000")
//
// Example JSON from hub:
// {
//   "Id": "step_001",
//   "Order": 1,
//   "ScreenName": "LoginScreen",
//   "Action": "Navigate",
//   "Target": "/login",
//   "Value": null
// }

/**
 * Represents a single test step dispatched from the MAUI dashboard
 * via the TestOrchestratorService.
 *
 * Maps 1:1 to the C# TestStep record:
 * ```csharp
 * record TestStep(string Id, int Order, string ScreenName, string Action, string Target, string? Value);
 * ```
 */
data class TestStep(
    val id: String,
    val order: Int,
    val screenName: String,
    val action: String,
    val target: String,
    val value: String? = null
)

/**
 * Supported test step action types.
 * Matches the actions used in TestOrchestratorService.SeedSuites().
 */
enum class TestAction {
    Navigate,
    Tap,
    TypeText,
    Assert,
    TriggerSOS,
    WaitFor;

    companion object {
        fun fromString(action: String): TestAction? = entries.find {
            it.name.equals(action, ignoreCase = true)
        }
    }
}

/**
 * Result of executing a single test step on-device.
 * Sent back to the dashboard via "ReportTestStepResult" SignalR message.
 */
data class TestStepResult(
    val stepId: String,
    val order: Int,
    val screenName: String,
    val action: String,
    val passed: Boolean,
    val screenshot: String? = null,
    val errorMessage: String? = null,
    val durationMs: Long,
    val completedAt: Instant = Instant.now()
)

/**
 * Status of the overall test run on this device.
 */
enum class TestRunStatus {
    Idle,
    Running,
    Passed,
    Failed,
    Cancelled
}

/**
 * Local representation of an active test run being executed on-device.
 * Tracks progress as steps complete sequentially.
 */
data class TestRunState(
    val runId: String,
    val suiteName: String = "",
    val status: TestRunStatus = TestRunStatus.Running,
    val totalSteps: Int = 0,
    val completedSteps: Int = 0,
    val passedSteps: Int = 0,
    val failedSteps: Int = 0,
    val currentStep: TestStep? = null,
    val results: MutableList<TestStepResult> = mutableListOf(),
    val startedAt: Instant = Instant.now()
)

/**
 * SignalR protocol contract for the test runner.
 * Defines inbound/outbound message names matching DashboardHub.cs.
 */
object TestRunnerProtocol {
    // ── Inbound (from dashboard to device) ──────────────────
    /** Hub dispatches next step to execute. Payload: (runId, TestStep) */
    const val EXECUTE_TEST_STEP = "ExecuteTestStep"

    /** Hub requests the device cancel the current run. Payload: (runId) */
    const val CANCEL_TEST_RUN = "CancelTestRun"

    // ── Outbound (from device to dashboard) ──────────────────
    /** Device reports result of a completed step. Payload: (runId, stepId, passed, screenshot?, errorMessage?) */
    const val REPORT_STEP_RESULT = "ReportTestStepResult"

    /** Device reports its current run status. Payload: (TestRunState) */
    const val REPORT_RUN_STATUS = "ReportTestRunStatus"

    // ── Device Group ─────────────────────────────────────────
    /** Group name pattern the dashboard uses to target a specific device */
    fun deviceGroup(deviceId: String) = "device_$deviceId"
}
