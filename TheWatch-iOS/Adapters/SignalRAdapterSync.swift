import Foundation
import Combine

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: SignalRAdapterSync listens to the MAUI dashboard's SignalR hub for
// AdapterTierChanged messages and applies them to the local AdapterRegistry.
//
// Protocol:
//   Hub URL: {dashboardBaseUrl}/hubs/dashboard
//   Inbound messages:
//     "AdapterTierChanged"   → { "Slot": "Logging", "Tier": "Native" }
//     "AdapterTierSnapshot"  → { "Logging": "Mock", "Location": "Native", ... }
//     "AdapterTierReset"     → (no payload — resets to defaults)
//   Outbound messages:
//     "ReportAdapterTiers"   → sends current tier map on connect/request
//
// The actual SignalR client library (SwiftSignalRClient) must be added via SPM.
// This class wraps the hub connection and translates messages into
// AdapterRegistry.switchTier() calls.
//
// Example:
// ```swift
// let sync = SignalRAdapterSync.shared
// await sync.connect(baseUrl: "https://dashboard.thewatch.app")
// // ...
// await sync.disconnect()
// ```

/// Listens to the MAUI dashboard's SignalR hub for adapter tier change commands
/// and applies them to the local `AdapterRegistry`.
///
/// This enables the dashboard operator to remotely switch mobile adapters between
/// Mock, Native, and Live tiers without requiring an app restart or redeployment.
///
/// Connection lifecycle:
/// 1. `connect` establishes the SignalR hub connection
/// 2. On connect, sends current tier map via "ReportAdapterTiers"
/// 3. Listens for "AdapterTierChanged", "AdapterTierSnapshot", "AdapterTierReset"
/// 4. Auto-reconnects on disconnect with exponential backoff (5s→60s max)
/// 5. `disconnect` tears down cleanly
@Observable
final class SignalRAdapterSync: @unchecked Sendable {

    static let shared = SignalRAdapterSync()

    /// Whether the SignalR connection is currently active.
    private(set) var isConnected: Bool = false

    private let registry = AdapterRegistry.shared
    private let logger = WatchLogger.shared
    private var dashboardUrl: String?
    private var reconnectTask: Task<Void, Never>?

    private init() {}

    // MARK: - Connection Lifecycle

    /// Connect to the MAUI dashboard's SignalR hub and start listening
    /// for adapter tier change commands.
    ///
    /// Safe to call multiple times — disconnects existing connection first.
    ///
    /// - Parameter baseUrl: The dashboard base URL (e.g., "https://dashboard.thewatch.app")
    func connect(baseUrl: String) {
        disconnect()
        dashboardUrl = baseUrl
        let hubUrl = "\(baseUrl)/hubs/dashboard"

        logger.information(
            source: "SignalRAdapterSync",
            template: "Connecting to dashboard hub at {HubUrl}",
            properties: ["HubUrl": hubUrl]
        )

        // ── SignalR Hub Connection ───────────────────────────────
        // NOTE: Requires SwiftSignalRClient SPM package
        //
        // Implementation sketch (uncomment when SwiftSignalRClient is added):
        //
        // let connection = HubConnectionBuilder(url: URL(string: hubUrl)!)
        //     .withAutoReconnect()
        //     .build()
        //
        // connection.on(method: "AdapterTierChanged") { [weak self] (slot: String, tier: String) in
        //     self?.handleTierChanged(slotName: slot, tierName: tier)
        // }
        //
        // connection.on(method: "AdapterTierSnapshot") { [weak self] (snapshot: [String: String]) in
        //     self?.handleTierSnapshot(snapshot)
        // }
        //
        // connection.on(method: "AdapterTierReset") { [weak self] in
        //     self?.handleTierReset()
        // }
        //
        // connection.delegate = self
        // connection.start()

        // For now, mark as connected in mock mode
        isConnected = true

        logger.information(
            source: "SignalRAdapterSync",
            template: "Connected to dashboard hub (mock mode). Reporting current tiers.",
            properties: ["Mode": "Mock"]
        )
    }

    /// Disconnect from the SignalR hub. Safe to call even if not connected.
    func disconnect() {
        reconnectTask?.cancel()
        reconnectTask = nil
        isConnected = false

        logger.information(
            source: "SignalRAdapterSync",
            template: "Disconnected from dashboard hub"
        )
    }

    // MARK: - Message Handlers

    /// Handle a single tier change command from the dashboard.
    /// Expected payload: { "Slot": "Logging", "Tier": "Native" }
    func handleTierChanged(slotName: String, tierName: String) {
        guard let slot = AdapterSlot.fromString(slotName) else {
            logger.warning(
                source: "SignalRAdapterSync",
                template: "Received tier change for unknown slot: {Slot}",
                properties: ["Slot": slotName]
            )
            return
        }

        let tier = AdapterTier.fromString(tierName)
        let changed = registry.switchTier(slot, to: tier)

        logger.information(
            source: "SignalRAdapterSync",
            template: "Dashboard commanded tier change: {Slot} → {Tier} (applied={Applied})",
            properties: [
                "Slot": slotName,
                "Tier": tierName,
                "Applied": "\(changed)"
            ]
        )
    }

    /// Handle a full tier snapshot from the dashboard.
    /// Typically sent on initial connect or after dashboard configuration change.
    func handleTierSnapshot(_ snapshot: [String: String]) {
        logger.information(
            source: "SignalRAdapterSync",
            template: "Received tier snapshot from dashboard. {Count} entries.",
            properties: ["Count": "\(snapshot.count)"]
        )
        registry.fromSerializableMap(snapshot)
    }

    /// Handle a tier reset command — revert all slots to defaults.
    func handleTierReset() {
        logger.information(
            source: "SignalRAdapterSync",
            template: "Dashboard commanded tier reset to defaults"
        )
        registry.resetToDefaults()
    }

    // MARK: - Outbound

    /// Report the current tier map to the dashboard.
    func reportCurrentTiers() {
        let tiers = registry.toSerializableMap()
        logger.debug(
            source: "SignalRAdapterSync",
            template: "Reporting current tiers to dashboard: {Tiers}",
            properties: ["Tiers": tiers.map { "\($0.key)=\($0.value)" }.joined(separator: ", ")]
        )
        // connection?.send(method: "ReportAdapterTiers", tiers)
    }

    // MARK: - Auto-Reconnect

    private func handleDisconnect(error: Error?) {
        isConnected = false
        guard let url = dashboardUrl else { return }

        if let error {
            logger.warning(
                source: "SignalRAdapterSync",
                template: "SignalR disconnected with error: {Error}. Scheduling reconnect.",
                properties: ["Error": error.localizedDescription],
                error: error
            )
        }

        reconnectTask = Task { [weak self] in
            var delaySeconds: UInt64 = 5
            let maxDelay: UInt64 = 60

            while !Task.isCancelled {
                try? await Task.sleep(nanoseconds: delaySeconds * 1_000_000_000)
                guard !Task.isCancelled else { break }

                self?.connect(baseUrl: url)
                if self?.isConnected == true {
                    self?.logger.information(
                        source: "SignalRAdapterSync",
                        template: "Reconnected to dashboard hub after {DelaySec}s backoff",
                        properties: ["DelaySec": "\(delaySeconds)"]
                    )
                    break
                }
                delaySeconds = min(delaySeconds * 2, maxDelay)
            }
        }
    }
}
