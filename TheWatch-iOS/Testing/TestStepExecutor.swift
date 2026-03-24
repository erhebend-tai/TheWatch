import Foundation
import SwiftUI

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: TestStepExecutor is a protocol with one implementation per action type.
// Each executor receives the TestStep, performs the action against the live app,
// and returns a TestStepResult indicating pass/fail.
//
// Uses Swift concurrency (async/await) for all execution.
//
// Executor routing:
//   Navigate    → NavigateExecutor: uses AppRouter to open screens
//   Tap         → TapExecutor: finds UI element by accessibility identifier and taps
//   TypeText    → TypeTextExecutor: finds input field and sets text
//   Assert      → AssertExecutor: checks UI state (visibility, text content)
//   TriggerSOS  → TriggerSOSExecutor: invokes SOS pipeline
//   WaitFor     → WaitForExecutor: polls condition with timeout
//
// Route mapping from TestOrchestratorService step definitions:
//   "/login"        → .login
//   "/signup"       → .signup
//   "/home"         → .home
//   "/profile"      → .profile
//   "/settings"     → .settings
//   "/volunteering" → .volunteering
//   "/contacts"     → .contacts
//   "/evacuation"   → .evacuation
//   "/health"       → .health
//   "/permissions"  → .permissions
//   "/history"      → .history
//   "/eula"         → .eula
//
// Example:
// ```swift
// let executor = registry.executor(for: step.action)
// let result = try await executor.execute(step: step, context: context)
// ```

/// Contract for executing a single test step action on-device.
protocol TestStepExecutorProtocol: Sendable {
    /// The action type this executor handles.
    var actionType: TestAction { get }

    /// Execute the test step and return a result.
    func execute(step: TestStep, context: TestExecutionContext) async -> TestStepResult
}

/// Shared context passed to all executors during a test run.
/// Provides access to the navigation router, SOS pipeline, and UI state queries.
struct TestExecutionContext: Sendable {
    /// Navigate to a route path. Returns true on success.
    let navigateTo: @Sendable (String) async -> Bool

    /// Tap a UI element by accessibility identifier. Returns true if found and tapped.
    let tapElement: @Sendable (String) async -> Bool

    /// Type text into a field by accessibility identifier. Returns true if field found.
    let typeText: @Sendable (String, String) async -> Bool

    /// Query whether a UI element is visible.
    let isElementVisible: @Sendable (String) async -> Bool

    /// Get the text/value of a UI element.
    let getElementValue: @Sendable (String) async -> String?

    /// Trigger SOS via a specific mechanism.
    let triggerSOS: @Sendable (String, String) async -> Bool

    /// Cancel an active SOS.
    let cancelSOS: @Sendable () async -> Bool

    /// Get the current screen route.
    let getCurrentRoute: @Sendable () async -> String?

    /// Capture a screenshot and return file path or base64 data.
    let captureScreenshot: @Sendable () async -> String?
}

// ── Executor Implementations ─────────────────────────────────────

/// Navigates to a screen by route path using AppRouter.
struct NavigateExecutor: TestStepExecutorProtocol {
    let actionType = TestAction.navigate
    private let logger = WatchLogger.shared

    /// Maps dashboard route strings to AppRouter.Destination-compatible route keys
    static let routeMap: [String: String] = [
        "/login": "login",
        "/signup": "signup",
        "/forgot_password": "forgotPassword",
        "/reset_password": "resetPassword",
        "/eula": "eula",
        "/home": "home",
        "/profile": "profile",
        "/permissions": "permissions",
        "/history": "history",
        "/volunteering": "volunteering",
        "/contacts": "contacts",
        "/settings": "settings",
        "/evacuation": "evacuation",
        "/health": "health",
        "/notifications": "notifications"
    ]

    func execute(step: TestStep, context: TestExecutionContext) async -> TestStepResult {
        let start = ContinuousClock.now

        guard let route = Self.routeMap[step.target] else {
            let elapsed = start.duration(to: .now)
            return step.toResult(
                passed: false,
                durationMs: elapsed.milliseconds,
                errorMessage: "Unknown route: '\(step.target)'. Known: \(Self.routeMap.keys.sorted().joined(separator: ", "))"
            )
        }

        logger.information(
            source: "NavigateExecutor",
            template: "Navigating to {Route} (mapped from {Target})",
            properties: ["Route": route, "Target": step.target]
        )

        let success = await context.navigateTo(route)

        // Brief settle time for navigation animation
        try? await Task.sleep(for: .milliseconds(300))

        let elapsed = start.duration(to: .now)
        let screenshot = await context.captureScreenshot()

        return step.toResult(
            passed: success,
            durationMs: elapsed.milliseconds,
            errorMessage: success ? nil : "Navigation to '\(step.target)' failed",
            screenshot: screenshot
        )
    }
}

/// Taps a UI element identified by accessibility identifier.
struct TapExecutor: TestStepExecutorProtocol {
    let actionType = TestAction.tap
    private let logger = WatchLogger.shared

    func execute(step: TestStep, context: TestExecutionContext) async -> TestStepResult {
        let start = ContinuousClock.now

        logger.information(
            source: "TapExecutor",
            template: "Tapping element {Target} on {Screen}",
            properties: ["Target": step.target, "Screen": step.screenName]
        )

        // Verify element exists before tapping
        let visible = await context.isElementVisible(step.target)
        guard visible else {
            let elapsed = start.duration(to: .now)
            return step.toResult(
                passed: false,
                durationMs: elapsed.milliseconds,
                errorMessage: "Element '\(step.target)' not found or not visible on \(step.screenName)"
            )
        }

        let success = await context.tapElement(step.target)
        try? await Task.sleep(for: .milliseconds(200))

        let elapsed = start.duration(to: .now)
        let screenshot = await context.captureScreenshot()

        return step.toResult(
            passed: success,
            durationMs: elapsed.milliseconds,
            errorMessage: success ? nil : "Tap on '\(step.target)' did not register",
            screenshot: screenshot
        )
    }
}

/// Types text into a field identified by accessibility identifier.
struct TypeTextExecutor: TestStepExecutorProtocol {
    let actionType = TestAction.typeText
    private let logger = WatchLogger.shared

    func execute(step: TestStep, context: TestExecutionContext) async -> TestStepResult {
        let start = ContinuousClock.now

        guard let text = step.value else {
            let elapsed = start.duration(to: .now)
            return step.toResult(
                passed: false,
                durationMs: elapsed.milliseconds,
                errorMessage: "TypeText requires a value but none was provided for '\(step.target)'"
            )
        }

        let logText = step.target.localizedCaseInsensitiveContains("password") ? "***" : text
        logger.information(
            source: "TypeTextExecutor",
            template: "Typing into {Target}: '{Text}' on {Screen}",
            properties: ["Target": step.target, "Text": logText, "Screen": step.screenName]
        )

        let success = await context.typeText(step.target, text)
        let elapsed = start.duration(to: .now)
        let screenshot = await context.captureScreenshot()

        return step.toResult(
            passed: success,
            durationMs: elapsed.milliseconds,
            errorMessage: success ? nil : "Failed to type into '\(step.target)'",
            screenshot: screenshot
        )
    }
}

/// Asserts UI state: element visibility or value match.
///
/// Value determines the assertion type:
///   "visible"      → assert element is visible
///   "hidden"       → assert element is NOT visible
///   "true"/"false" → assert element value matches boolean string
///   any other      → assert element text/value equals the string
struct AssertExecutor: TestStepExecutorProtocol {
    let actionType = TestAction.assert
    private let logger = WatchLogger.shared

    func execute(step: TestStep, context: TestExecutionContext) async -> TestStepResult {
        let start = ContinuousClock.now
        let expected = step.value ?? "visible"

        logger.information(
            source: "AssertExecutor",
            template: "Asserting {Target} == '{Expected}' on {Screen}",
            properties: ["Target": step.target, "Expected": expected, "Screen": step.screenName]
        )

        let (passed, errorMessage): (Bool, String?)

        switch expected.lowercased() {
        case "visible":
            let visible = await context.isElementVisible(step.target)
            passed = visible
            errorMessage = visible ? nil : "Element '\(step.target)' expected to be visible but was not found"

        case "hidden", "not_visible":
            let visible = await context.isElementVisible(step.target)
            passed = !visible
            errorMessage = !visible ? nil : "Element '\(step.target)' expected to be hidden but was visible"

        default:
            let actual = await context.getElementValue(step.target)
            let matches = actual?.lowercased() == expected.lowercased()
            passed = matches
            errorMessage = matches ? nil : "Element '\(step.target)' expected '\(expected)' but got '\(actual ?? "<not found>")'"
        }

        let elapsed = start.duration(to: .now)
        let screenshot = passed ? nil : await context.captureScreenshot()

        return step.toResult(
            passed: passed,
            durationMs: elapsed.milliseconds,
            errorMessage: errorMessage,
            screenshot: screenshot
        )
    }
}

/// Triggers SOS via various mechanisms.
///
/// Target is the trigger mechanism:
///   "phrase"                → simulate spoken phrase SOS trigger
///   "clearword"             → simulate spoken clear word to cancel SOS
///   "quicktap"              → simulate rapid tap sequence (value = tap count)
///   "dispatch_notification" → simulate receiving an SOS dispatch notification
///   "checkin_notification"  → simulate receiving a check-in notification
struct TriggerSOSExecutor: TestStepExecutorProtocol {
    let actionType = TestAction.triggerSOS
    private let logger = WatchLogger.shared

    func execute(step: TestStep, context: TestExecutionContext) async -> TestStepResult {
        let start = ContinuousClock.now
        let mechanism = step.target
        let payload = step.value ?? ""

        logger.information(
            source: "TriggerSOSExecutor",
            template: "Triggering SOS via {Mechanism} with payload '{Payload}'",
            properties: ["Mechanism": mechanism, "Payload": payload]
        )

        let success: Bool
        switch mechanism {
        case "phrase", "clearword", "quicktap", "dispatch_notification", "checkin_notification":
            success = await context.triggerSOS(mechanism, payload)
        default:
            logger.warning(
                source: "TriggerSOSExecutor",
                template: "Unknown SOS trigger mechanism: {Mechanism}",
                properties: ["Mechanism": mechanism]
            )
            success = false
        }

        // SOS triggers need time to propagate
        try? await Task.sleep(for: .milliseconds(500))

        let elapsed = start.duration(to: .now)
        let screenshot = await context.captureScreenshot()

        return step.toResult(
            passed: success,
            durationMs: elapsed.milliseconds,
            errorMessage: success ? nil : "SOS trigger via '\(mechanism)' failed",
            screenshot: screenshot
        )
    }
}

/// Waits for a condition to become true, polling with timeout.
///
/// Target is the element or condition to wait for.
/// Value is the timeout in milliseconds (default 5000).
struct WaitForExecutor: TestStepExecutorProtocol {
    let actionType = TestAction.waitFor
    private let logger = WatchLogger.shared

    private static let pollIntervalMs: UInt64 = 250
    private static let defaultTimeoutMs: Int64 = 5000

    func execute(step: TestStep, context: TestExecutionContext) async -> TestStepResult {
        let start = ContinuousClock.now
        let timeoutMs = Int64(step.value ?? "") ?? Self.defaultTimeoutMs

        logger.information(
            source: "WaitForExecutor",
            template: "Waiting for {Target} with timeout {TimeoutMs}ms on {Screen}",
            properties: ["Target": step.target, "TimeoutMs": "\(timeoutMs)", "Screen": step.screenName]
        )

        let deadline = ContinuousClock.now + .milliseconds(timeoutMs)
        var conditionMet = false

        while ContinuousClock.now < deadline {
            conditionMet = await context.isElementVisible(step.target)
            if conditionMet { break }
            try? await Task.sleep(for: .milliseconds(Self.pollIntervalMs))
        }

        if !conditionMet {
            let elapsedMs = start.duration(to: .now).milliseconds
            logger.warning(
                source: "WaitForExecutor",
                template: "Timeout waiting for {Target} after {ElapsedMs}ms",
                properties: ["Target": step.target, "ElapsedMs": "\(elapsedMs)"]
            )
        }

        let elapsed = start.duration(to: .now)
        let screenshot = conditionMet ? nil : await context.captureScreenshot()

        return step.toResult(
            passed: conditionMet,
            durationMs: elapsed.milliseconds,
            errorMessage: conditionMet ? nil : "Timeout after \(timeoutMs)ms waiting for '\(step.target)'",
            screenshot: screenshot
        )
    }
}

// ── Executor Registry ────────────────────────────────────────────

/// Registry of all test step executors, keyed by TestAction.
final class TestStepExecutorRegistry: @unchecked Sendable {
    static let shared = TestStepExecutorRegistry()

    private let executors: [TestAction: TestStepExecutorProtocol]

    init() {
        executors = [
            .navigate: NavigateExecutor(),
            .tap: TapExecutor(),
            .typeText: TypeTextExecutor(),
            .assert: AssertExecutor(),
            .triggerSOS: TriggerSOSExecutor(),
            .waitFor: WaitForExecutor()
        ]
    }

    /// Get the executor for a given action string. Returns nil if unknown.
    func executor(for action: String) -> TestStepExecutorProtocol? {
        guard let actionType = TestAction(from: action) else { return nil }
        return executors[actionType]
    }

    /// All registered action types.
    var supportedActions: Set<TestAction> {
        Set(executors.keys)
    }
}

// ── Duration Helper ──────────────────────────────────────────────

extension Duration {
    /// Convert Duration to milliseconds as Int64.
    var milliseconds: Int64 {
        let (seconds, attoseconds) = self.components
        return Int64(seconds) * 1000 + Int64(attoseconds) / 1_000_000_000_000_000
    }
}
