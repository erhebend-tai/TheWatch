import Foundation

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: HubMessageRouter is the inbound message dispatcher for the SignalR hub on iOS.
// It registers handlers for all client-invocable methods on the DashboardHub and routes
// incoming messages to the appropriate manager, service, or adapter.
//
// Architecture:
//   - Singleton, implements HubConnectionDelegate to auto-register on connect
//   - Routes messages to:
//       * SignalRAdapterSync — for AdapterTierChanged/Snapshot/Reset
//       * TestRunnerCallback — for ExecuteTestStep (Agent 5's test runner)
//       * LogStreamCallback — for RequestLogStream
//       * HubEventSender — for Ping→Pong
//       * ResponseCoordinationCallback — for responder/evidence/survey messages
//
// Inbound message handlers:
//   - "ExecuteTestStep" → TestRunnerCallback.onExecuteTestStep(runId:stepJson:)
//   - "AdapterTierChanged" → SignalRAdapterSync.shared.handleTierChanged(slot:tier:)
//   - "AdapterTierSnapshot" → SignalRAdapterSync.shared.handleTierSnapshot(_:)
//   - "AdapterTierReset" → SignalRAdapterSync.shared.handleTierReset()
//   - "RequestLogStream" → LogStreamCallback.onRequestLogStream()
//   - "Ping" → HubEventSender.shared.sendPong()
//   - "TestRunStarted/Completed" → TestRunnerCallback broadcasts
//   - "ResponderLocationUpdated/OnScene" → ResponseCoordinationCallback
//   - "EvidenceSubmitted/Processed" → ResponseCoordinationCallback
//   - "SurveyDispatched/Completed" → ResponseCoordinationCallback
//
// Example:
// ```swift
// let router = HubMessageRouter.shared
//
// // Register a test runner callback (Agent 5)
// router.registerTestRunnerCallback(myTestRunner)
//
// // The router auto-registers handlers when hub connects
// ```

/// Callback protocol for test execution messages.
/// Implemented by TestRunnerService (Agent 5) to receive test commands.
protocol TestRunnerCallbackProtocol: AnyObject {
    func onExecuteTestStep(runId: String, stepJson: String)
    func onTestRunStarted(runJson: String)
    func onTestStepCompleted(runId: String, resultJson: String)
    func onTestRunCompleted(runJson: String)
}

/// Callback protocol for response coordination messages.
protocol ResponseCoordinationCallbackProtocol: AnyObject {
    func onResponderLocationUpdated(data: [String: Any])
    func onResponderOnScene(data: [String: Any])
    func onEvidenceSubmitted(data: [String: Any])
    func onEvidenceProcessed(data: [String: Any])
    func onSurveyDispatched(data: [String: Any])
    func onSurveyCompleted(data: [String: Any])
}

/// Callback protocol for log stream requests.
protocol LogStreamCallbackProtocol: AnyObject {
    func onRequestLogStream()
}

/// Inbound message router for the SignalR hub.
///
/// Registers handlers for all client-invocable methods on DashboardHub and
/// dispatches messages to the appropriate callback protocol.
///
/// Handler registration happens automatically when the hub connects.
/// Consumers just register their callback and the router handles wiring.
final class HubMessageRouter: @unchecked Sendable, HubConnectionDelegate {

    static let shared = HubMessageRouter()

    private let hub = WatchHubConnection.shared
    private let adapterSync = SignalRAdapterSync.shared
    private let eventSender = HubEventSender.shared
    private let logger = WatchLogger.shared

    // ── Callback Registration ────────────────────────────────────

    weak var testRunnerCallback: (any TestRunnerCallbackProtocol)?
    weak var responseCallback: (any ResponseCoordinationCallbackProtocol)?
    weak var logStreamCallback: (any LogStreamCallbackProtocol)?

    private init() {
        hub.addDelegate(self)
    }

    /// Register the test runner callback. Only one can be active at a time.
    func registerTestRunnerCallback(_ callback: any TestRunnerCallbackProtocol) {
        testRunnerCallback = callback
        logger.debug(source: "SignalR", template: "TestRunnerCallback registered")
    }

    func unregisterTestRunnerCallback() {
        testRunnerCallback = nil
    }

    func registerResponseCoordinationCallback(_ callback: any ResponseCoordinationCallbackProtocol) {
        responseCallback = callback
    }

    func unregisterResponseCoordinationCallback() {
        responseCallback = nil
    }

    func registerLogStreamCallback(_ callback: any LogStreamCallbackProtocol) {
        logStreamCallback = callback
    }

    // ── HubConnectionDelegate ────────────────────────────────────

    func hubDidConnect() {
        registerHandlers()
        logger.information(
            source: "SignalR",
            template: "HubMessageRouter: handlers registered on connect"
        )
    }

    func hubDidDisconnect(error: Error?) {
        logger.debug(
            source: "SignalR",
            template: "HubMessageRouter: hub disconnected, handlers will re-register on reconnect"
        )
    }

    func hubIsReconnecting(attempt: Int) {
        // No action — handlers re-register on successful reconnect
    }

    // ── Handler Registration ─────────────────────────────────────

    /// Register all inbound message handlers on the underlying HubConnection.
    ///
    /// When the actual SwiftSignalRClient library is linked, uncomment the
    /// connection.on() calls. The mock implementation logs received messages.
    private func registerHandlers() {
        // guard let connection = hub.hubConnection as? SignalRClient.HubConnection else { return }
        //
        // // ── Test Orchestration
        // connection.on(method: "ExecuteTestStep") { [weak self] (runId: String, stepJson: String) in
        //     self?.handleExecuteTestStep(runId: runId, stepJson: stepJson)
        // }
        // connection.on(method: "TestRunStarted") { [weak self] (runJson: String) in
        //     self?.handleTestRunStarted(runJson: runJson)
        // }
        // connection.on(method: "TestStepCompleted") { [weak self] (runId: String, resultJson: String) in
        //     self?.handleTestStepCompleted(runId: runId, resultJson: resultJson)
        // }
        // connection.on(method: "TestRunCompleted") { [weak self] (runJson: String) in
        //     self?.handleTestRunCompleted(runJson: runJson)
        // }
        //
        // // ── Adapter Tier Management
        // connection.on(method: "AdapterTierChanged") { [weak self] (slot: String, tier: String) in
        //     self?.adapterSync.handleTierChanged(slot: slot, tier: tier)
        // }
        // connection.on(method: "AdapterTierSnapshot") { [weak self] (snapshotJson: String) in
        //     if let data = snapshotJson.data(using: .utf8),
        //        let snapshot = try? JSONDecoder().decode([String: String].self, from: data) {
        //         self?.adapterSync.handleTierSnapshot(snapshot)
        //     }
        // }
        // connection.on(method: "AdapterTierReset") { [weak self] in
        //     self?.adapterSync.handleTierReset()
        // }
        //
        // // ── Keepalive
        // connection.on(method: "Ping") { [weak self] in
        //     self?.handlePing()
        // }
        //
        // // ── Log Streaming
        // connection.on(method: "RequestLogStream") { [weak self] in
        //     self?.handleRequestLogStream()
        // }
        //
        // // ── Response Coordination
        // connection.on(method: "ResponderLocationUpdated") { [weak self] (dataJson: String) in
        //     self?.handleResponderLocationUpdated(dataJson: dataJson)
        // }
        // connection.on(method: "ResponderOnScene") { ... }
        // connection.on(method: "EvidenceSubmitted") { ... }
        // connection.on(method: "EvidenceProcessed") { ... }
        // connection.on(method: "SurveyDispatched") { ... }
        // connection.on(method: "SurveyCompleted") { ... }

        logger.debug(
            source: "SignalR",
            template: "All hub message handlers registered (mock mode)"
        )
    }

    // ── Message Handlers ─────────────────────────────────────────

    /// Dispatch test step execution to the registered test runner.
    func handleExecuteTestStep(runId: String, stepJson: String) {
        logger.information(
            source: "SignalR",
            template: "Received ExecuteTestStep: run={RunId}",
            properties: ["RunId": runId],
            correlationId: runId
        )

        guard let callback = testRunnerCallback else {
            logger.warning(
                source: "SignalR",
                template: "No TestRunnerCallback registered — dropping ExecuteTestStep for run {RunId}",
                properties: ["RunId": runId],
                correlationId: runId
            )
            return
        }

        callback.onExecuteTestStep(runId: runId, stepJson: stepJson)
    }

    func handleTestRunStarted(runJson: String) {
        logger.information(source: "SignalR", template: "Received TestRunStarted broadcast")
        testRunnerCallback?.onTestRunStarted(runJson: runJson)
    }

    func handleTestStepCompleted(runId: String, resultJson: String) {
        logger.debug(
            source: "SignalR",
            template: "Received TestStepCompleted: run={RunId}",
            properties: ["RunId": runId],
            correlationId: runId
        )
        testRunnerCallback?.onTestStepCompleted(runId: runId, resultJson: resultJson)
    }

    func handleTestRunCompleted(runJson: String) {
        logger.information(source: "SignalR", template: "Received TestRunCompleted broadcast")
        testRunnerCallback?.onTestRunCompleted(runJson: runJson)
    }

    /// Respond to server Ping with Pong.
    func handlePing() {
        logger.verbose(source: "SignalR", template: "Received Ping, sending Pong")
        eventSender.sendPong()
    }

    /// Forward log stream request to the registered callback.
    func handleRequestLogStream() {
        logger.information(source: "SignalR", template: "Dashboard requested log stream")
        logStreamCallback?.onRequestLogStream()
    }

    // ── Response Coordination Handlers ───────────────────────────

    func handleResponderLocationUpdated(dataJson: String) {
        logger.verbose(source: "SignalR", template: "Received ResponderLocationUpdated")
        if let data = parseJson(dataJson) {
            responseCallback?.onResponderLocationUpdated(data: data)
        }
    }

    func handleResponderOnScene(dataJson: String) {
        logger.information(source: "SignalR", template: "Received ResponderOnScene")
        if let data = parseJson(dataJson) {
            responseCallback?.onResponderOnScene(data: data)
        }
    }

    func handleEvidenceSubmitted(dataJson: String) {
        logger.information(source: "SignalR", template: "Received EvidenceSubmitted")
        if let data = parseJson(dataJson) {
            responseCallback?.onEvidenceSubmitted(data: data)
        }
    }

    func handleEvidenceProcessed(dataJson: String) {
        logger.information(source: "SignalR", template: "Received EvidenceProcessed")
        if let data = parseJson(dataJson) {
            responseCallback?.onEvidenceProcessed(data: data)
        }
    }

    func handleSurveyDispatched(dataJson: String) {
        logger.information(source: "SignalR", template: "Received SurveyDispatched")
        if let data = parseJson(dataJson) {
            responseCallback?.onSurveyDispatched(data: data)
        }
    }

    func handleSurveyCompleted(dataJson: String) {
        logger.information(source: "SignalR", template: "Received SurveyCompleted")
        if let data = parseJson(dataJson) {
            responseCallback?.onSurveyCompleted(data: data)
        }
    }

    // ── Utility ──────────────────────────────────────────────────

    private func parseJson(_ jsonString: String) -> [String: Any]? {
        guard let data = jsonString.data(using: .utf8),
              let dict = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            logger.warning(
                source: "SignalR",
                template: "Failed to parse JSON message: {Preview}",
                properties: ["Preview": String(jsonString.prefix(100))]
            )
            return nil
        }
        return dict
    }
}
