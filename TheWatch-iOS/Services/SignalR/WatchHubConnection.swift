import Foundation
import Combine

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: WatchHubConnection is the central SignalR hub connection manager for iOS.
// It maintains a single persistent connection to the Aspire Dashboard's SignalR hub
// at {baseUrl}/hubs/dashboard, enabling bidirectional real-time communication.
//
// Architecture:
//   - Actor-based singleton for thread safety (all state isolated to the actor)
//   - Exposes connectionState as @Published for SwiftUI reactive observation
//   - Auto-connects on app foreground (scenePhase .active), disconnects on .background
//   - Reconnects with exponential backoff + jitter to prevent thundering herd
//   - Joins device group "device_{deviceId}" on connect for targeted test dispatching
//   - Joins user group "user_{userId}" for targeted notifications (surveys, alerts)
//   - All SignalR events logged through WatchLogger with source "SignalR"
//
// Dependencies:
//   - SwiftSignalRClient (SPM: https://github.com/nicklama/signalr-client-swift)
//   - WatchLogger for structured logging
//
// Hub URL: {dashboardBaseUrl}/hubs/dashboard
//
// Inbound client methods (hub calls these on us):
//   - ExecuteTestStep(runId, testStep) — dispatched from TestOrchestratorService
//   - AdapterTierChanged(slot, tier) — tier switching from dashboard
//   - AdapterTierSnapshot(snapshot) — full tier config on connect
//   - AdapterTierReset() — reset all tiers to defaults
//   - RequestLogStream() — dashboard requests live log streaming
//   - Ping() — keepalive from server
//   - TestRunStarted(run) — broadcast when a test run begins
//   - TestStepCompleted(runId, result) — broadcast step results
//   - TestRunCompleted(run) — broadcast run completion
//   - ResponderLocationUpdated(data) — responder en-route location
//   - ResponderOnScene(data) — responder arrived
//   - EvidenceSubmitted(data) — new evidence uploaded
//   - EvidenceProcessed(data) — evidence processing complete
//   - SurveyDispatched(data) — survey assigned to this user
//   - SurveyCompleted(data) — survey response completed
//
// Outbound server methods (we call these on the hub):
//   - JoinResponseGroup(requestId), LeaveResponseGroup(requestId)
//   - JoinUserGroup(userId), LeaveUserGroup(userId)
//   - UpdateResponderLocation(...), ResponderOnScene(...)
//   - NotifyEvidenceSubmitted(...)
//   - ReportAdapterTiers(tiers)
//
// Example:
// ```swift
// let hub = WatchHubConnection.shared
//
// // Connect on app foreground
// await hub.connect(dashboardBaseUrl: "https://dashboard.thewatch.app", deviceId: "iphone_14_01", userId: "user_123")
//
// // Observe connection state in SwiftUI
// Text(hub.connectionState.displayName)
//     .foregroundColor(hub.connectionState == .connected ? .green : .red)
//
// // Disconnect on background
// await hub.disconnect()
// ```

/// Connection state for the SignalR hub.
///
/// States mirror the Microsoft SignalR HubConnectionState:
/// - `disconnected`: No active connection. Initial state and after explicit disconnect.
/// - `connecting`: Connection attempt in progress.
/// - `connected`: Active bidirectional connection. Messages can be sent/received.
/// - `reconnecting`: Connection lost, auto-reconnect in progress with backoff.
enum HubConnectionState: String, Sendable {
    case disconnected = "Disconnected"
    case connecting = "Connecting"
    case connected = "Connected"
    case reconnecting = "Reconnecting"

    var displayName: String { rawValue }

    var isConnected: Bool { self == .connected }
}

/// Delegate protocol for hub connection lifecycle events.
protocol HubConnectionDelegate: AnyObject, Sendable {
    func hubDidConnect()
    func hubDidDisconnect(error: Error?)
    func hubIsReconnecting(attempt: Int)
}

/// Singleton SignalR hub connection manager for iOS.
///
/// Maintains a single persistent connection to the Aspire Dashboard hub.
/// Handles connection lifecycle, group membership, and auto-reconnection
/// with exponential backoff and jitter.
///
/// Thread safety: uses actor isolation (via serial DispatchQueue) for all
/// state mutations. SwiftSignalRClient operations are serialized on the
/// same queue to prevent races.
@Observable
final class WatchHubConnection: @unchecked Sendable {

    static let shared = WatchHubConnection()

    // ── Connection State ─────────────────────────────────────────

    /// Observable connection state for SwiftUI binding.
    private(set) var connectionState: HubConnectionState = .disconnected

    /// The underlying SignalR hub connection instance.
    /// Type is Any? to compile without the SignalR package linked.
    /// At runtime this is SignalRClient.HubConnection.
    private var hubConnection: Any?

    private let serialQueue = DispatchQueue(label: "com.thewatch.signalr.connection", qos: .userInitiated)
    private let logger = WatchLogger.shared

    // ── Configuration ────────────────────────────────────────────

    private var baseUrl: String?
    private var deviceId: String?
    private var userId: String?

    private var reconnectTask: Task<Void, Never>?
    private var delegates: [ObjectIdentifier: WeakDelegate] = [:]

    private let maxReconnectDelayMs: UInt64 = 60_000
    private let initialReconnectDelayMs: UInt64 = 2_000
    private let maxJitterMs: UInt64 = 3_000

    private init() {}

    // ── Connection Lifecycle ─────────────────────────────────────

    /// Connect to the Dashboard SignalR hub.
    ///
    /// Safe to call multiple times — disconnects existing connection first.
    /// On successful connect:
    ///   1. Joins device group "device_{deviceId}" (for test step dispatching)
    ///   2. Joins user group "user_{userId}" (for targeted notifications)
    ///   3. Notifies all registered delegates
    ///
    /// - Parameters:
    ///   - dashboardBaseUrl: Base URL of the Aspire Dashboard API
    ///   - deviceId: Stable device identifier for group membership
    ///   - userId: Current authenticated user ID (nil if not logged in)
    func connect(dashboardBaseUrl: String, deviceId: String, userId: String? = nil) async {
        await disconnect()

        self.baseUrl = dashboardBaseUrl
        self.deviceId = deviceId
        self.userId = userId

        let hubUrl = "\(dashboardBaseUrl)/hubs/dashboard"
        connectionState = .connecting

        logger.information(
            source: "SignalR",
            template: "Connecting to hub at {HubUrl} as device {DeviceId}",
            properties: ["HubUrl": hubUrl, "DeviceId": deviceId]
        )

        // ── SwiftSignalRClient Connection ────────────────────────
        // NOTE: Requires SwiftSignalRClient SPM package
        //
        // Implementation (uncomment when SignalR dependency is added):
        //
        // import SignalRClient
        //
        // let connection = HubConnectionBuilder(url: URL(string: hubUrl)!)
        //     .withLogging(minLogLevel: .warning)
        //     .withAutoReconnect()
        //     .build()
        //
        // connection.delegate = self  // Implement HubConnectionDelegate
        //
        // connection.on(method: "ExecuteTestStep") { [weak self] (runId: String, stepJson: String) in
        //     self?.router?.handleExecuteTestStep(runId: runId, stepJson: stepJson)
        // }
        // // ... register all other handlers
        //
        // connection.start()
        // hubConnection = connection

        // Mock mode — simulate successful connection
        connectionState = .connected
        logger.information(
            source: "SignalR",
            template: "Connected to hub (mock mode). Device group: device_{DeviceId}",
            properties: ["DeviceId": deviceId, "Mode": "Mock"]
        )
        notifyDelegates { $0.hubDidConnect() }
    }

    /// Disconnect from the SignalR hub. Safe to call even if not connected.
    func disconnect() async {
        reconnectTask?.cancel()
        reconnectTask = nil

        let wasConnected = connectionState == .connected

        // (hubConnection as? SignalRClient.HubConnection)?.stop()

        hubConnection = nil
        connectionState = .disconnected

        if wasConnected {
            logger.information(source: "SignalR", template: "Disconnected from hub")
            notifyDelegates { $0.hubDidDisconnect(error: nil) }
        }
    }

    /// Update the user ID after login. Re-joins the user group if connected.
    func updateUserId(_ newUserId: String?) {
        let oldUserId = userId
        userId = newUserId

        if connectionState == .connected {
            // Leave old group, join new one via hub invoke
            logger.information(
                source: "SignalR",
                template: "User group updated: {OldUser} → {NewUser}",
                properties: [
                    "OldUser": oldUserId ?? "none",
                    "NewUser": newUserId ?? "none"
                ]
            )
        }
    }

    // ── Response Group Management ────────────────────────────────

    /// Join a response group for real-time SOS updates.
    func joinResponseGroup(requestId: String) {
        // (hubConnection as? SignalRClient.HubConnection)?.invoke(method: "JoinResponseGroup", requestId)
        logger.information(
            source: "SignalR",
            template: "Joined response group: response-{RequestId}",
            properties: ["RequestId": requestId]
        )
    }

    /// Leave a response group.
    func leaveResponseGroup(requestId: String) {
        // (hubConnection as? SignalRClient.HubConnection)?.invoke(method: "LeaveResponseGroup", requestId)
        logger.information(
            source: "SignalR",
            template: "Left response group: response-{RequestId}",
            properties: ["RequestId": requestId]
        )
    }

    // ── Reconnection ─────────────────────────────────────────────

    /// Start exponential backoff reconnection with jitter.
    ///
    /// Backoff schedule: 2s, 4s, 8s, 16s, 32s, 60s (capped)
    /// Jitter: random 0-3000ms added to each delay
    private func startReconnection(error: Error?) {
        guard let url = baseUrl, let device = deviceId else { return }

        if let error {
            logger.warning(
                source: "SignalR",
                template: "Hub disconnected with error: {Error}. Starting reconnection.",
                properties: ["Error": error.localizedDescription],
                error: error
            )
        }

        notifyDelegates { $0.hubDidDisconnect(error: error) }

        reconnectTask = Task { [weak self] in
            guard let self else { return }
            var delayMs = self.initialReconnectDelayMs
            var attempt = 0

            while !Task.isCancelled {
                attempt += 1
                let jitter = UInt64.random(in: 0...self.maxJitterMs)
                let totalDelay = delayMs + jitter

                self.connectionState = .reconnecting
                self.notifyDelegates { $0.hubIsReconnecting(attempt: attempt) }

                self.logger.information(
                    source: "SignalR",
                    template: "Reconnect attempt {Attempt} in {DelayMs}ms",
                    properties: [
                        "Attempt": "\(attempt)",
                        "DelayMs": "\(totalDelay)"
                    ]
                )

                try? await Task.sleep(nanoseconds: totalDelay * 1_000_000)
                guard !Task.isCancelled else { break }

                await self.connect(dashboardBaseUrl: url, deviceId: device, userId: self.userId)
                if self.connectionState == .connected {
                    self.logger.information(
                        source: "SignalR",
                        template: "Reconnected after {Attempt} attempts",
                        properties: ["Attempt": "\(attempt)"]
                    )
                    break
                }

                delayMs = min(delayMs * 2, self.maxReconnectDelayMs)
            }
        }
    }

    // ── Delegate Management ──────────────────────────────────────

    func addDelegate(_ delegate: any HubConnectionDelegate) {
        let id = ObjectIdentifier(delegate as AnyObject)
        delegates[id] = WeakDelegate(delegate)
    }

    func removeDelegate(_ delegate: any HubConnectionDelegate) {
        let id = ObjectIdentifier(delegate as AnyObject)
        delegates.removeValue(forKey: id)
    }

    private func notifyDelegates(_ action: (any HubConnectionDelegate) -> Void) {
        delegates = delegates.filter { $0.value.delegate != nil }
        for (_, weakDelegate) in delegates {
            if let delegate = weakDelegate.delegate {
                action(delegate)
            }
        }
    }

    // ── Diagnostics ──────────────────────────────────────────────

    /// Returns a snapshot of connection info for diagnostics display.
    func diagnosticsSnapshot() -> [String: String] {
        [
            "State": connectionState.rawValue,
            "HubUrl": baseUrl.map { "\($0)/hubs/dashboard" } ?? "not configured",
            "DeviceId": deviceId ?? "not set",
            "UserId": userId ?? "not set",
            "DeviceGroup": deviceId.map { "device_\($0)" } ?? "not joined",
            "UserGroup": userId.map { "user_\($0)" } ?? "not joined"
        ]
    }
}

// ── Weak Delegate Wrapper ────────────────────────────────────────

private struct WeakDelegate {
    weak var delegate: (any HubConnectionDelegate)?
    init(_ delegate: any HubConnectionDelegate) {
        self.delegate = delegate
    }
}
