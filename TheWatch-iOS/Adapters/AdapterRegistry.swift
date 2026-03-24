import Foundation
import Combine

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: AdapterRegistry is the mobile equivalent of TheWatch.Shared.Configuration.AdapterRegistry.
// It controls which tier (Mock/Native/Live/Disabled) is active for every adapter slot at runtime.
//
// Key behaviors:
//   1. Loads defaults at init (all Mock for debug builds)
//   2. @Observable for SwiftUI binding — tierMap changes trigger view updates
//   3. Combine publisher for tier changes (tierChangePublisher)
//   4. switchTier(slot, tier) hot-swaps the active implementation — no app restart
//   5. SignalRAdapterSync listens for AdapterTierChanged messages from MAUI dashboard
//      and calls switchTier() to remotely control mobile adapter tiers
//
// Example:
// ```swift
// let registry = AdapterRegistry.shared
//
// // Check current tier
// let loggingTier = registry.getTier(.logging)
//
// // Switch at runtime (no restart needed)
// registry.switchTier(.logging, to: .native)
//
// // Observe in SwiftUI (auto-updates via @Observable)
// Text("Logging: \(registry.getTier(.logging).displayName)")
//
// // Subscribe to changes via Combine
// registry.tierChangePublisher.sink { slot, oldTier, newTier in
//     print("\(slot.rawValue): \(oldTier.rawValue) → \(newTier.rawValue)")
// }
// ```

/// Tier change event emitted via Combine publisher.
struct TierChangeEvent: Sendable {
    let slot: AdapterSlot
    let oldTier: AdapterTier
    let newTier: AdapterTier
}

/// Central registry controlling which adapter tier is active for each slot.
///
/// This is the backbone of TheWatch's three-tier architecture on mobile.
/// The MAUI dashboard can remotely switch tiers via SignalR, and UI components
/// observe changes reactively via `@Observable` or Combine publishers.
///
/// Switching from Mock→Native is seamless — no app restart required.
@Observable
final class AdapterRegistry: @unchecked Sendable {

    static let shared = AdapterRegistry()

    // MARK: - State

    /// Current tier assignments for all slots. Observable by SwiftUI.
    private(set) var tierMap: [AdapterSlot: AdapterTier]

    /// Combine publisher that emits tier change events.
    nonisolated let tierChangePublisher = PassthroughSubject<TierChangeEvent, Never>()

    /// Combine publisher of full tier map snapshots.
    nonisolated let tierMapPublisher = CurrentValueSubject<[AdapterSlot: AdapterTier], Never>([:])

    private let logger = WatchLogger.shared
    private let lock = NSLock()

    // MARK: - Init

    private init() {
        // Default all slots to Mock — matches Aspire's appsettings.Development.json
        var defaults: [AdapterSlot: AdapterTier] = [:]
        for slot in AdapterSlot.allCases {
            defaults[slot] = .mock
        }
        self.tierMap = defaults
        tierMapPublisher.send(defaults)
    }

    // MARK: - Query

    /// Get the current tier for a slot.
    /// Returns `.disabled` if the slot is somehow not in the map.
    func getTier(_ slot: AdapterSlot) -> AdapterTier {
        lock.lock()
        defer { lock.unlock() }
        return tierMap[slot] ?? .disabled
    }

    /// Whether a slot is active (not Disabled).
    func isEnabled(_ slot: AdapterSlot) -> Bool { getTier(slot).isActive }

    /// Whether a slot is running in Live (cloud) mode.
    func isLive(_ slot: AdapterSlot) -> Bool { getTier(slot) == .live }

    /// Whether a slot is running in Native (on-device) mode.
    func isNative(_ slot: AdapterSlot) -> Bool { getTier(slot) == .native }

    /// Whether a slot is running in Mock (in-memory) mode.
    func isMock(_ slot: AdapterSlot) -> Bool { getTier(slot) == .mock }

    // MARK: - Mutation

    /// Switch a slot to a new tier at runtime. No app restart required.
    ///
    /// - Parameters:
    ///   - slot: The adapter slot to switch
    ///   - tier: The new tier to activate
    /// - Returns: true if the tier actually changed
    @discardableResult
    func switchTier(_ slot: AdapterSlot, to tier: AdapterTier) -> Bool {
        lock.lock()
        let oldTier = tierMap[slot] ?? .disabled
        guard oldTier != tier else {
            lock.unlock()
            return false
        }
        tierMap[slot] = tier
        let snapshot = tierMap
        lock.unlock()

        logger.information(
            source: "AdapterRegistry",
            template: "Tier switched: {Slot} {OldTier} → {NewTier}",
            properties: [
                "Slot": slot.rawValue,
                "OldTier": oldTier.rawValue,
                "NewTier": tier.rawValue
            ]
        )

        tierChangePublisher.send(TierChangeEvent(slot: slot, oldTier: oldTier, newTier: tier))
        tierMapPublisher.send(snapshot)

        return true
    }

    /// Batch-switch multiple slots at once.
    ///
    /// Used when the MAUI dashboard sends a full configuration snapshot.
    ///
    /// - Parameter changes: Map of slot→tier to apply
    /// - Returns: Number of slots that actually changed
    @discardableResult
    func switchTiers(_ changes: [AdapterSlot: AdapterTier]) -> Int {
        lock.lock()
        var changeCount = 0
        var events: [TierChangeEvent] = []

        for (slot, tier) in changes {
            let old = tierMap[slot] ?? .disabled
            if old != tier {
                tierMap[slot] = tier
                changeCount += 1
                events.append(TierChangeEvent(slot: slot, oldTier: old, newTier: tier))
            }
        }
        let snapshot = tierMap
        lock.unlock()

        if changeCount > 0 {
            logger.information(
                source: "AdapterRegistry",
                template: "Batch tier switch: {Count} slots changed",
                properties: ["Count": "\(changeCount)"]
            )
            for event in events {
                tierChangePublisher.send(event)
            }
            tierMapPublisher.send(snapshot)
        }

        return changeCount
    }

    /// Reset all slots to their default tiers.
    func resetToDefaults() {
        lock.lock()
        for slot in AdapterSlot.allCases {
            tierMap[slot] = .mock
        }
        let snapshot = tierMap
        lock.unlock()

        logger.information(
            source: "AdapterRegistry",
            template: "All adapter tiers reset to defaults"
        )
        tierMapPublisher.send(snapshot)
    }

    // MARK: - Serialization

    /// Export current tier map as a String→String dictionary for SignalR / REST.
    func toSerializableMap() -> [String: String] {
        lock.lock()
        defer { lock.unlock() }
        return Dictionary(uniqueKeysWithValues: tierMap.map { ($0.key.rawValue, $0.value.rawValue) })
    }

    /// Import a tier map from a serialized String→String dictionary.
    /// Unrecognized slots or tiers are silently skipped.
    func fromSerializableMap(_ map: [String: String]) {
        var changes: [AdapterSlot: AdapterTier] = [:]
        for (slotName, tierName) in map {
            guard let slot = AdapterSlot.fromString(slotName) else { continue }
            let tier = AdapterTier.fromString(tierName)
            changes[slot] = tier
        }
        switchTiers(changes)
    }
}
