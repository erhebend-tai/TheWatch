import Foundation

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: SignalRTestRunnerBridge connects Agent 4's SignalR client (WatchHubConnection,
// HubMessageRouter, HubEventSender) to Agent 5's test runner (TestRunnerService).
//
// The test runner defines its own port protocols (HubMessageRouterProtocol,
// HubEventSenderProtocol) to allow development without a live SignalR connection.
// This bridge implements those protocols using the real SignalR client.
//
// Architecture:
//   - Implements HubMessageRouterProtocol by registering as testRunnerCallback
//     on the HubMessageRouter singleton
//   - Implements HubEventSenderProtocol by delegating to HubEventSender singleton
//   - Parses JSON payloads from SignalR into TestStep objects
//   - Serializes TestStepResult back to SignalR-compatible format
//
// Example:
// ```swift
// let bridge = SignalRTestRunnerBridge.shared
//
// // Use as the router/sender for TestRunnerService
// TestRunnerService.shared.initialize(
//     router: bridge,
//     sender: bridge,
//     context: executionContext
// )
// ```

/// Bridges Agent 4's SignalR client to Agent 5's test runner.
///
/// Implements both `HubMessageRouterProtocol` and `HubEventSenderProtocol` by
/// delegating to the real SignalR `HubMessageRouter` and `HubEventSender`.
final class SignalRTestRunnerBridge: HubMessageRouterProtocol, HubEventSenderProtocol, TestRunnerCallbackProtocol, @unchecked Sendable {

    static let shared = SignalRTestRunnerBridge()

    private let signalRRouter = HubMessageRouter.shared
    private let signalRSender = HubEventSender.shared
    private let logger = WatchLogger.shared

    private var stepHandler: (@Sendable (String, TestStep) async -> Void)?
    private var cancelHandler: (@Sendable (String) async -> Void)?

    private init() {
        // Register this bridge as the test runner callback on the SignalR router
        signalRRouter.registerTestRunnerCallback(self)
        logger.information(
            source: "SignalRTestRunnerBridge",
            template: "Bridge initialized — SignalR <-> TestRunner connected"
        )
    }

    // MARK: - HubMessageRouterProtocol (Agent 5's interface)

    func onTestStepReceived(_ handler: @escaping @Sendable (String, TestStep) async -> Void) {
        stepHandler = handler
    }

    func onTestRunCancelled(_ handler: @escaping @Sendable (String) async -> Void) {
        cancelHandler = handler
    }

    // MARK: - TestRunnerCallbackProtocol (Agent 4's interface)

    func onExecuteTestStep(runId: String, stepJson: String) {
        guard let step = parseTestStep(stepJson) else {
            logger.error(
                source: "SignalRTestRunnerBridge",
                template: "Failed to parse TestStep JSON for run {RunId}: {Json}",
                properties: ["RunId": runId, "Json": String(stepJson.prefix(200))],
                correlationId: runId
            )
            return
        }

        Task {
            await stepHandler?(runId, step)
        }
    }

    func onTestRunStarted(runJson: String) {
        logger.debug(
            source: "SignalRTestRunnerBridge",
            template: "Test run started (broadcast): {Preview}",
            properties: ["Preview": String(runJson.prefix(100))]
        )
    }

    func onTestStepCompleted(runId: String, resultJson: String) {
        logger.debug(
            source: "SignalRTestRunnerBridge",
            template: "Step completed (broadcast) for run {RunId}",
            properties: ["RunId": runId],
            correlationId: runId
        )
    }

    func onTestRunCompleted(runJson: String) {
        logger.debug(
            source: "SignalRTestRunnerBridge",
            template: "Test run completed (broadcast): {Preview}",
            properties: ["Preview": String(runJson.prefix(100))]
        )
    }

    // MARK: - HubEventSenderProtocol (Agent 5's interface)

    func reportStepResult(
        runId: String,
        stepId: String,
        passed: Bool,
        screenshot: String?,
        errorMessage: String?
    ) async {
        signalRSender.sendTestStepCompleted(
            runId: runId,
            stepId: stepId,
            passed: passed,
            screenshot: screenshot,
            errorMessage: errorMessage
        )
    }

    func reportRunStatus(_ runState: TestRunState) async {
        logger.information(
            source: "SignalRTestRunnerBridge",
            template: "Reporting run status: {RunId} {Status} [{Completed}/{Total}]",
            properties: [
                "RunId": runState.runId,
                "Status": runState.status.rawValue,
                "Completed": "\(runState.completedSteps)",
                "Total": "\(runState.totalSteps)"
            ]
        )
    }

    // MARK: - JSON Parsing

    /// Parse a TestStep from the JSON payload sent by the SignalR hub.
    ///
    /// Expected format matches TestOrchestratorService.cs:
    /// {
    ///   "Id": "step_001",
    ///   "Order": 1,
    ///   "ScreenName": "LoginScreen",
    ///   "Action": "Navigate",
    ///   "Target": "/login",
    ///   "Value": null
    /// }
    private func parseTestStep(_ jsonStr: String) -> TestStep? {
        guard let data = jsonStr.data(using: .utf8) else { return nil }
        do {
            return try JSONDecoder().decode(TestStep.self, from: data)
        } catch {
            logger.warning(
                source: "SignalRTestRunnerBridge",
                template: "JSON parse error: {Error}",
                properties: ["Error": error.localizedDescription]
            )
            return nil
        }
    }
}
