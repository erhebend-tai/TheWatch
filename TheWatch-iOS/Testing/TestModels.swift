import Foundation

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: TestModels mirrors the C# record types from TestOrchestratorService.cs:
//   TestStep(Id, Order, ScreenName, Action, Target, Value)
//   TestStepResult(StepId, Order, ScreenName, Action, Passed, Screenshot, ErrorMessage, DurationMs, CompletedAt)
//
// The SignalR hub dispatches "ExecuteTestStep" with (runId: String, step: TestStep).
// The device executes the step and reports back via "ReportTestStepResult".
//
// Actions defined in seeded suites:
//   Navigate   — open a screen by route path (e.g., "/login", "/settings")
//   Tap        — tap a UI element identified by target (e.g., "sos_button")
//   TypeText   — type text into a field (e.g., "email_field" → "test@thewatch.app")
//   Assert     — check UI state: visible/value match
//   TriggerSOS — trigger SOS via phrase/clearword/quicktap/dispatch
//   WaitFor    — poll/wait for a condition with timeout in ms
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

/// Single test step dispatched from the MAUI dashboard via TestOrchestratorService.
/// Maps 1:1 to the C# TestStep record.
struct TestStep: Codable, Identifiable, Sendable {
    let id: String
    let order: Int
    let screenName: String
    let action: String
    let target: String
    let value: String?

    enum CodingKeys: String, CodingKey {
        case id = "Id"
        case order = "Order"
        case screenName = "ScreenName"
        case action = "Action"
        case target = "Target"
        case value = "Value"
    }
}

/// Supported test step action types.
/// Matches the actions used in TestOrchestratorService.SeedSuites().
enum TestAction: String, CaseIterable, Sendable {
    case navigate = "Navigate"
    case tap = "Tap"
    case typeText = "TypeText"
    case assert = "Assert"
    case triggerSOS = "TriggerSOS"
    case waitFor = "WaitFor"

    init?(from string: String) {
        self.init(rawValue: string)
        if self == nil {
            // Case-insensitive fallback
            if let match = Self.allCases.first(where: {
                $0.rawValue.lowercased() == string.lowercased()
            }) {
                self = match
            } else {
                return nil
            }
        }
    }
}

/// Result of executing a single test step on-device.
/// Sent back to the dashboard via "ReportTestStepResult" SignalR message.
struct TestStepResult: Codable, Sendable {
    let stepId: String
    let order: Int
    let screenName: String
    let action: String
    let passed: Bool
    let screenshot: String?
    let errorMessage: String?
    let durationMs: Int64
    let completedAt: Date

    init(
        stepId: String,
        order: Int,
        screenName: String,
        action: String,
        passed: Bool,
        screenshot: String? = nil,
        errorMessage: String? = nil,
        durationMs: Int64,
        completedAt: Date = Date()
    ) {
        self.stepId = stepId
        self.order = order
        self.screenName = screenName
        self.action = action
        self.passed = passed
        self.screenshot = screenshot
        self.errorMessage = errorMessage
        self.durationMs = durationMs
        self.completedAt = completedAt
    }
}

/// Status of the overall test run on this device.
enum TestRunStatus: String, Sendable {
    case idle = "Idle"
    case running = "Running"
    case passed = "Passed"
    case failed = "Failed"
    case cancelled = "Cancelled"
}

/// Local representation of an active test run being executed on-device.
@Observable
final class TestRunState: @unchecked Sendable {
    let runId: String
    var suiteName: String
    var status: TestRunStatus
    var totalSteps: Int
    var completedSteps: Int
    var passedSteps: Int
    var failedSteps: Int
    var currentStep: TestStep?
    var results: [TestStepResult]
    let startedAt: Date

    init(
        runId: String,
        suiteName: String = "",
        status: TestRunStatus = .running,
        totalSteps: Int = 0,
        completedSteps: Int = 0,
        passedSteps: Int = 0,
        failedSteps: Int = 0,
        currentStep: TestStep? = nil,
        results: [TestStepResult] = [],
        startedAt: Date = Date()
    ) {
        self.runId = runId
        self.suiteName = suiteName
        self.status = status
        self.totalSteps = totalSteps
        self.completedSteps = completedSteps
        self.passedSteps = passedSteps
        self.failedSteps = failedSteps
        self.currentStep = currentStep
        self.results = results
        self.startedAt = startedAt
    }
}

/// SignalR protocol constants for the test runner.
/// Defines inbound/outbound message names matching DashboardHub.cs.
enum TestRunnerProtocol {
    // ── Inbound (from dashboard to device) ──────────────────
    static let executeTestStep = "ExecuteTestStep"
    static let cancelTestRun = "CancelTestRun"

    // ── Outbound (from device to dashboard) ──────────────────
    static let reportStepResult = "ReportTestStepResult"
    static let reportRunStatus = "ReportTestRunStatus"

    // ── Device Group ─────────────────────────────────────────
    static func deviceGroup(_ deviceId: String) -> String {
        "device_\(deviceId)"
    }
}

/// Convenience extension to build a TestStepResult from a TestStep.
extension TestStep {
    func toResult(
        passed: Bool,
        durationMs: Int64,
        errorMessage: String? = nil,
        screenshot: String? = nil
    ) -> TestStepResult {
        TestStepResult(
            stepId: id,
            order: order,
            screenName: screenName,
            action: action,
            passed: passed,
            screenshot: screenshot,
            errorMessage: errorMessage,
            durationMs: durationMs
        )
    }
}
