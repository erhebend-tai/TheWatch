import Foundation

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: Mock implementations of HubMessageRouterProtocol and HubEventSenderProtocol
// for use when Agent 4's SignalR client isn't merged yet. Also includes
// MockTestExecutionContext that simulates UI interactions.
//
// When Agent 4 merges:
//   1. Replace MockHubMessageRouter with real router on hub connection
//   2. Replace MockHubEventSender with real sender via hub.send()
//
// When XCTest UI integration is ready:
//   Replace MockTestExecutionContext with real context using XCUIApplication.
//
// Example:
// ```swift
// let router = MockHubMessageRouter()
// let sender = MockHubEventSender()
// let context = MockTestExecutionContext.create()
// TestRunnerService.shared.initialize(router: router, sender: sender, context: context)
//
// // Simulate receiving a step (for local testing)
// await router.simulateStep(
//     runId: "run_123",
//     step: TestStep(id: "step_001", order: 1, screenName: "LoginScreen", action: "Navigate", target: "/login", value: nil)
// )
// ```

/// Mock HubMessageRouterProtocol that stores handlers and allows manual step injection.
final class MockHubMessageRouter: HubMessageRouterProtocol, @unchecked Sendable {
    private let logger = WatchLogger.shared
    private var stepHandler: (@Sendable (String, TestStep) async -> Void)?
    private var cancelHandler: (@Sendable (String) async -> Void)?

    func onTestStepReceived(_ handler: @escaping @Sendable (String, TestStep) async -> Void) {
        stepHandler = handler
        logger.debug(
            source: "MockHubMessageRouter",
            template: "Test step handler registered"
        )
    }

    func onTestRunCancelled(_ handler: @escaping @Sendable (String) async -> Void) {
        cancelHandler = handler
        logger.debug(
            source: "MockHubMessageRouter",
            template: "Test cancel handler registered"
        )
    }

    /// Simulate receiving an "ExecuteTestStep" message from the dashboard.
    func simulateStep(runId: String, step: TestStep) async {
        logger.information(
            source: "MockHubMessageRouter",
            template: "Simulating step dispatch: {StepId} for run {RunId}",
            properties: ["StepId": step.id, "RunId": runId]
        )
        await stepHandler?(runId, step)
    }

    /// Simulate receiving a "CancelTestRun" message from the dashboard.
    func simulateCancel(runId: String) async {
        logger.information(
            source: "MockHubMessageRouter",
            template: "Simulating run cancellation: {RunId}",
            properties: ["RunId": runId]
        )
        await cancelHandler?(runId)
    }
}

/// Mock HubEventSenderProtocol that logs results instead of sending over SignalR.
final class MockHubEventSender: HubEventSenderProtocol, @unchecked Sendable {
    private let logger = WatchLogger.shared

    struct ReportedResult: Sendable {
        let runId: String
        let stepId: String
        let passed: Bool
        let screenshot: String?
        let errorMessage: String?
    }

    /// All results reported during the session, for inspection/debugging.
    private(set) var reportedResults: [ReportedResult] = []

    func reportStepResult(
        runId: String,
        stepId: String,
        passed: Bool,
        screenshot: String?,
        errorMessage: String?
    ) async {
        let result = ReportedResult(
            runId: runId,
            stepId: stepId,
            passed: passed,
            screenshot: screenshot,
            errorMessage: errorMessage
        )
        reportedResults.append(result)

        logger.information(
            source: "MockHubEventSender",
            template: "Reported step result: {StepId} {Result} for run {RunId}",
            properties: [
                "StepId": stepId,
                "Result": passed ? "PASS" : "FAIL",
                "RunId": runId,
                "Error": errorMessage ?? "none"
            ]
        )
    }

    func reportRunStatus(_ runState: TestRunState) async {
        logger.information(
            source: "MockHubEventSender",
            template: "Reported run status: {RunId} {Status} [{Completed}/{Total}]",
            properties: [
                "RunId": runState.runId,
                "Status": runState.status.rawValue,
                "Completed": "\(runState.completedSteps)",
                "Total": "\(runState.totalSteps)"
            ]
        )
    }
}

/// Factory for creating a mock TestExecutionContext that simulates all UI interactions.
/// All operations succeed with synthetic delays mimicking real UI response times.
enum MockTestExecutionContext {

    /// Known element values for mock assertions
    private static let mockElementValues: [String: String] = [
        "user_name": "Test User",
        "alert_status": "ACTIVE",
        "location_sharing": "true",
        "location_mode": "Normal",
        "enrollment_status": "Active",
        "trigger_source": "QuickTap",
        "response_status": "ACCEPTED"
    ]

    /// Actor to hold mutable state for the mock context safely
    private actor MockState {
        var visitedScreens: Set<String> = []
        var locationMode = "Normal"
        var sosActive = false

        func navigate(to route: String) {
            visitedScreens.insert(route)
        }

        func tap(_ tag: String) {
            switch tag {
            case "sos_button":
                sosActive = true
                locationMode = "Emergency"
            case "cancel_sos_button":
                sosActive = false
                locationMode = "Normal"
            default:
                break
            }
        }

        func triggerSOS(mechanism: String) {
            switch mechanism {
            case "phrase", "quicktap":
                sosActive = true
                locationMode = "Emergency"
            case "clearword":
                sosActive = false
                locationMode = "Normal"
            default:
                break
            }
        }

        func cancelSOS() {
            sosActive = false
            locationMode = "Normal"
        }

        func isVisible(_ tag: String) -> Bool {
            switch tag {
            case "countdown_ring", "alert_status", "location_sharing":
                return sosActive
            case "location_deescalate":
                return !sosActive
            default:
                return true
            }
        }

        func getValue(_ tag: String) -> String? {
            switch tag {
            case "location_mode":
                return locationMode
            case "alert_status":
                return sosActive ? "ACTIVE" : "INACTIVE"
            case "location_sharing":
                return "\(sosActive)"
            default:
                return mockElementValues[tag]
            }
        }

        func lastRoute() -> String? {
            visitedScreens.max()
        }
    }

    /// Create a mock execution context with simulated UI interactions.
    static func create() -> TestExecutionContext {
        let state = MockState()
        let logger = WatchLogger.shared

        return TestExecutionContext(
            navigateTo: { route in
                try? await Task.sleep(for: .milliseconds(150))
                await state.navigate(to: route)
                logger.debug(
                    source: "MockExecutionContext",
                    template: "Mock navigated to {Route}",
                    properties: ["Route": route]
                )
                return true
            },
            tapElement: { testTag in
                try? await Task.sleep(for: .milliseconds(100))
                await state.tap(testTag)
                logger.debug(
                    source: "MockExecutionContext",
                    template: "Mock tapped {Tag}",
                    properties: ["Tag": testTag]
                )
                return true
            },
            typeText: { testTag, text in
                try? await Task.sleep(for: .milliseconds(50))
                logger.debug(
                    source: "MockExecutionContext",
                    template: "Mock typed into {Tag}",
                    properties: ["Tag": testTag]
                )
                return true
            },
            isElementVisible: { testTag in
                try? await Task.sleep(for: .milliseconds(30))
                return await state.isVisible(testTag)
            },
            getElementValue: { testTag in
                try? await Task.sleep(for: .milliseconds(30))
                return await state.getValue(testTag)
            },
            triggerSOS: { mechanism, payload in
                try? await Task.sleep(for: .milliseconds(200))
                await state.triggerSOS(mechanism: mechanism)
                logger.debug(
                    source: "MockExecutionContext",
                    template: "Mock SOS trigger: {Mechanism} '{Payload}'",
                    properties: ["Mechanism": mechanism, "Payload": payload]
                )
                return true
            },
            cancelSOS: {
                await state.cancelSOS()
                return true
            },
            getCurrentRoute: {
                await state.lastRoute()
            },
            captureScreenshot: {
                nil // No screenshot in mock mode
            }
        )
    }
}
