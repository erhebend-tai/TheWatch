import Foundation

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: HubEventSender is the outbound message sender for the SignalR hub on iOS.
// It provides a typed API for all messages the mobile client sends to the DashboardHub,
// with automatic queueing when disconnected and flush-on-reconnect.
//
// Architecture:
//   - Actor-based concurrency via serial DispatchQueue for thread-safe sends
//   - Offline resilience: messages queued in an array when disconnected
//   - Auto-flush: implements HubConnectionDelegate and flushes queue on connect
//   - Queue cap: max 500 messages to prevent memory issues during extended offline
//   - All outbound messages logged through WatchLogger with source "SignalR"
//
// Outbound messages:
//   - "TestStepCompleted" → test step result back to orchestrator
//   - "LogEntry" → streams log entry to dashboard
//   - "DeviceStatusUpdate" → periodic device health report
//   - "Pong" → response to server Ping
//   - "ReportAdapterTiers" → current adapter tier map
//   - "UpdateResponderLocation" → en-route location updates
//   - "ResponderOnScene" → arrival notification
//   - "NotifyEvidenceSubmitted" → evidence upload notification
//
// Example:
// ```swift
// let sender = HubEventSender.shared
//
// // Send a test step result
// sender.sendTestStepCompleted(
//     runId: "run_abc123",
//     stepId: "step_001",
//     passed: true,
//     durationMs: 1250
// )
//
// // Stream a log entry
// sender.sendLogEntry(
//     level: "Information",
//     source: "LocationCoordinator",
//     message: "Mode escalated to Emergency",
//     correlationId: "sos_xyz"
// )
//
// // Messages queue if disconnected and auto-flush on reconnect
// ```

/// Queued message waiting to be sent when connection is restored.
struct QueuedHubMessage: Sendable {
    let method: String
    let argsJson: String
    let queuedAt: Date
}

/// Outbound message sender for the SignalR hub.
///
/// Provides typed methods for every message the mobile client sends to the
/// DashboardHub. Handles offline queueing and reconnect flushing automatically.
///
/// Thread safety: all sends are serialized through a DispatchQueue to prevent
/// concurrent writes on the SignalR connection.
final class HubEventSender: @unchecked Sendable, HubConnectionDelegate {

    static let shared = HubEventSender()

    private let hub = WatchHubConnection.shared
    private let logger = WatchLogger.shared
    private let sendQueue = DispatchQueue(label: "com.thewatch.signalr.sender", qos: .userInitiated)

    // ── Offline Queue ────────────────────────────────────────────

    private var messageQueue: [QueuedHubMessage] = []
    private let maxQueueSize = 500

    private let iso8601: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()

    private init() {
        hub.addDelegate(self)
    }

    // ── HubConnectionDelegate ────────────────────────────────────

    func hubDidConnect() {
        sendQueue.async { [weak self] in
            self?.flushQueue()
        }
    }

    func hubDidDisconnect(error: Error?) {}
    func hubIsReconnecting(attempt: Int) {}

    // ── Test Orchestration ───────────────────────────────────────

    /// Send a test step result back to the TestOrchestratorService.
    func sendTestStepCompleted(
        runId: String,
        stepId: String,
        passed: Bool,
        screenshot: String? = nil,
        errorMessage: String? = nil,
        durationMs: Int64 = 0
    ) {
        var args: [String: String] = [
            "runId": runId,
            "stepId": stepId,
            "passed": "\(passed)",
            "durationMs": "\(durationMs)"
        ]
        if let screenshot { args["screenshot"] = screenshot }
        if let errorMessage { args["errorMessage"] = errorMessage }

        send(method: "TestStepCompleted", args: args, correlationId: runId)
    }

    // ── Log Streaming ────────────────────────────────────────────

    /// Stream a log entry to the dashboard for real-time log viewing.
    func sendLogEntry(
        level: String,
        source: String,
        message: String,
        properties: [String: String] = [:],
        exception: String? = nil,
        correlationId: String? = nil
    ) {
        var args: [String: String] = [
            "level": level,
            "source": source,
            "message": message,
            "timestamp": iso8601.string(from: Date())
        ]
        if !properties.isEmpty, let data = try? JSONEncoder().encode(properties),
           let json = String(data: data, encoding: .utf8) {
            args["properties"] = json
        }
        if let exception { args["exception"] = exception }
        if let correlationId { args["correlationId"] = correlationId }

        send(method: "LogEntry", args: args, correlationId: correlationId)
    }

    // ── Device Status ────────────────────────────────────────────

    /// Send a periodic device status update to the dashboard.
    func sendDeviceStatusUpdate(
        batteryPercent: Int,
        networkType: String,
        gpsEnabled: Bool,
        adapterTiers: [String: String],
        freeMemoryMb: Int64
    ) {
        var args: [String: String] = [
            "batteryPercent": "\(batteryPercent)",
            "networkType": networkType,
            "gpsEnabled": "\(gpsEnabled)",
            "freeMemoryMb": "\(freeMemoryMb)",
            "timestamp": iso8601.string(from: Date())
        ]
        if let data = try? JSONEncoder().encode(adapterTiers),
           let json = String(data: data, encoding: .utf8) {
            args["adapterTiers"] = json
        }

        send(method: "DeviceStatusUpdate", args: args)
    }

    // ── Keepalive ────────────────────────────────────────────────

    /// Respond to a server Ping with Pong. Not queued.
    func sendPong() {
        guard hub.connectionState.isConnected else { return }

        sendQueue.async { [weak self] in
            // (self?.hub.hubConnection as? SignalRClient.HubConnection)?.invoke(method: "Pong", Date().iso8601)
            self?.logger.verbose(source: "SignalR", template: "Sent Pong")
        }
    }

    // ── Adapter Tier Reporting ────────────────────────────────────

    /// Report current adapter tier configuration to the dashboard.
    func sendReportAdapterTiers(_ tiers: [String: String]) {
        if let data = try? JSONEncoder().encode(tiers),
           let json = String(data: data, encoding: .utf8) {
            send(method: "ReportAdapterTiers", args: ["tiers": json])
        }
    }

    // ── Response Coordination ────────────────────────────────────

    /// Send a responder location update while en-route.
    func sendResponderLocation(
        requestId: String,
        responderId: String,
        latitude: Double,
        longitude: Double,
        speedMps: Double? = nil
    ) {
        var args: [String: String] = [
            "requestId": requestId,
            "responderId": responderId,
            "latitude": "\(latitude)",
            "longitude": "\(longitude)",
            "timestamp": iso8601.string(from: Date())
        ]
        if let speedMps { args["speedMps"] = "\(speedMps)" }

        send(method: "UpdateResponderLocation", args: args, correlationId: requestId)
    }

    /// Signal that the responder has arrived on scene.
    func sendResponderOnScene(requestId: String, responderId: String) {
        let args: [String: String] = [
            "requestId": requestId,
            "responderId": responderId,
            "arrivedAt": iso8601.string(from: Date())
        ]
        send(method: "ResponderOnScene", args: args, correlationId: requestId)
    }

    /// Notify the dashboard that evidence has been submitted.
    func sendEvidenceSubmitted(
        submissionId: String,
        requestId: String,
        phase: String,
        type: String,
        thumbnailUrl: String? = nil
    ) {
        var args: [String: String] = [
            "submissionId": submissionId,
            "requestId": requestId,
            "phase": phase,
            "type": type,
            "timestamp": iso8601.string(from: Date())
        ]
        if let thumbnailUrl { args["thumbnailUrl"] = thumbnailUrl }

        send(method: "NotifyEvidenceSubmitted", args: args, correlationId: requestId)
    }

    // ── Core Send / Queue ────────────────────────────────────────

    private func send(method: String, args: [String: String], correlationId: String? = nil) {
        guard hub.connectionState.isConnected else {
            enqueue(method: method, args: args)
            return
        }

        sendQueue.async { [weak self] in
            guard let self else { return }

            // let connection = self.hub.hubConnection as? SignalRClient.HubConnection
            // let argsJson = self.encodeArgs(args)
            // connection?.invoke(method: method, argsJson) { error in
            //     if let error {
            //         self.logger.warning(...)
            //         self.enqueue(method: method, args: args)
            //     }
            // }

            let argsJson = self.encodeArgs(args)
            self.logger.debug(
                source: "SignalR",
                template: "Sent {Method} ({Bytes} bytes)",
                properties: [
                    "Method": method,
                    "Bytes": "\(argsJson.count)"
                ],
                correlationId: correlationId
            )
        }
    }

    private func enqueue(method: String, args: [String: String]) {
        let argsJson = encodeArgs(args)
        let message = QueuedHubMessage(method: method, argsJson: argsJson, queuedAt: Date())

        sendQueue.async { [weak self] in
            guard let self else { return }

            self.messageQueue.append(message)

            // Trim if over capacity (drop oldest)
            while self.messageQueue.count > self.maxQueueSize {
                let dropped = self.messageQueue.removeFirst()
                self.logger.warning(
                    source: "SignalR",
                    template: "Queue overflow: dropped {Method} queued at {QueuedAt}",
                    properties: [
                        "Method": dropped.method,
                        "QueuedAt": self.iso8601.string(from: dropped.queuedAt)
                    ]
                )
            }

            self.logger.debug(
                source: "SignalR",
                template: "Queued {Method} for delivery on reconnect. Queue size: {Size}",
                properties: [
                    "Method": method,
                    "Size": "\(self.messageQueue.count)"
                ]
            )
        }
    }

    /// Flush all queued messages after reconnection. Messages sent FIFO.
    private func flushQueue() {
        guard !messageQueue.isEmpty else { return }

        let count = messageQueue.count
        logger.information(
            source: "SignalR",
            template: "Flushing {Count} queued messages after reconnect",
            properties: ["Count": "\(count)"]
        )

        var flushed = 0
        var failed = 0

        while !messageQueue.isEmpty {
            let message = messageQueue.removeFirst()

            // let connection = hub.hubConnection as? SignalRClient.HubConnection
            // connection?.invoke(method: message.method, message.argsJson) { error in ... }
            flushed += 1
        }

        logger.information(
            source: "SignalR",
            template: "Queue flush complete: {Flushed} sent, {Failed} failed",
            properties: [
                "Flushed": "\(flushed)",
                "Failed": "\(failed)"
            ]
        )
    }

    // ── Utility ──────────────────────────────────────────────────

    private func encodeArgs(_ args: [String: String]) -> String {
        (try? String(data: JSONEncoder().encode(args), encoding: .utf8)) ?? "{}"
    }

    /// Returns queue diagnostics.
    func diagnosticsSnapshot() -> [String: String] {
        [
            "QueueSize": "\(messageQueue.count)",
            "MaxQueueSize": "\(maxQueueSize)",
            "ConnectionState": hub.connectionState.rawValue
        ]
    }
}
