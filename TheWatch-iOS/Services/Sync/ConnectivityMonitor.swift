/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         ConnectivityMonitor.swift                              │
 * │ Purpose:      NWPathMonitor wrapper that observes network state      │
 * │               and triggers immediate sync flush on reconnection.     │
 * │               Mirrors Android's ConnectivityMonitor.kt.              │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: Network framework, Combine                            │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   let monitor = SyncConnectivityMonitor()                            │
 * │   monitor.start()                                                    │
 * │                                                                      │
 * │   // Observe:                                                        │
 * │   monitor.$isConnected.sink { online in                              │
 * │       print("Online: \(online)")                                     │
 * │   }                                                                  │
 * │                                                                      │
 * │   // On reconnect, SyncEngine.flush() is called automatically.       │
 * │                                                                      │
 * │ NOTE: NWPathMonitor fires on a dedicated serial queue. Published     │
 * │ state updates are dispatched to MainActor for SwiftUI compatibility. │
 * │ The monitor distinguishes between "satisfied" (has a route) and      │
 * │ "requiresConnection" (e.g., VPN needs activation). Only "satisfied"  │
 * │ triggers sync.                                                       │
 * └──────────────────────────────────────────────────────────────────────┘
 */

import Foundation
import Network
import Combine
import os.log

/// Observes network connectivity and triggers sync on reconnection.
@Observable
final class SyncConnectivityMonitor {

    // MARK: - Published State

    private(set) var isConnected: Bool = false
    private(set) var connectionType: ConnectionType = .unknown
    private(set) var isExpensive: Bool = false      // Cellular / hotspot
    private(set) var isConstrained: Bool = false     // Low Data Mode

    enum ConnectionType: String {
        case wifi, cellular, wiredEthernet, unknown
    }

    // MARK: - Private

    private let monitor = NWPathMonitor()
    private let queue = DispatchQueue(label: "com.thewatch.connectivity", qos: .utility)
    private let logger = Logger(subsystem: "com.thewatch.app", category: "Connectivity")

    private var wasOffline = true
    private var started = false

    /// Callback invoked on offline -> online transition.
    /// Set by SyncEngine to trigger expedited flush.
    var onReconnect: (() -> Void)?

    // MARK: - Lifecycle

    func start() {
        guard !started else {
            logger.debug("ConnectivityMonitor already started")
            return
        }
        started = true

        monitor.pathUpdateHandler = { [weak self] path in
            guard let self else { return }

            let online = path.status == .satisfied
            let type: ConnectionType = {
                if path.usesInterfaceType(.wifi) { return .wifi }
                if path.usesInterfaceType(.cellular) { return .cellular }
                if path.usesInterfaceType(.wiredEthernet) { return .wiredEthernet }
                return .unknown
            }()

            let expensive = path.isExpensive
            let constrained = path.isConstrained

            self.logger.info("Path update: status=\(path.status == .satisfied ? "online" : "offline"), type=\(type.rawValue), expensive=\(expensive), constrained=\(constrained)")

            // Detect offline -> online transition
            let reconnected = online && self.wasOffline

            // Update state on main thread for SwiftUI
            Task { @MainActor in
                self.isConnected = online
                self.connectionType = type
                self.isExpensive = expensive
                self.isConstrained = constrained
            }

            if online {
                self.wasOffline = false
            } else {
                self.wasOffline = true
            }

            // Trigger sync on reconnection
            if reconnected {
                self.logger.info("Connectivity restored — triggering sync flush")
                self.onReconnect?()
            }
        }

        monitor.start(queue: queue)
        logger.info("ConnectivityMonitor started")
    }

    func stop() {
        guard started else { return }
        monitor.cancel()
        started = false
        logger.info("ConnectivityMonitor stopped")
    }

    deinit {
        stop()
    }
}
