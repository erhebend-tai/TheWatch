import Foundation

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: AdapterTier mirrors the Aspire backend's tier system (TheWatch.Shared.Configuration.AdapterRegistry).
// Mobile apps use the same four-tier model so the MAUI dashboard can command tier switches
// at runtime via SignalR → AdapterTierController → SignalRAdapterSync.
//
// Example:
// ```swift
// let tier = AdapterTier.fromString("Native") // .native
// let label = AdapterTier.mock.displayName     // "Mock (In-Memory)"
// ```

/// Adapter implementation tier — controls which concrete adapter is active for a given slot.
///
/// Matches the Aspire backend's valid values: "Mock", "Native", "Live", "Disabled"
///
/// - **Mock**:     In-memory/fake implementation. Always available, no cloud/hardware needed.
///                 Used for development, testing, and CI. Zero external dependencies.
/// - **Native**:   Platform-local hardware + OS APIs (SwiftData, CoreLocation, AVFoundation).
///                 Works fully offline. The "field deployment" tier.
/// - **Live**:     Real cloud adapters (Firestore, Cloud Messaging, Azure SignalR).
///                 Requires credentials, network, and provisioned backend services.
/// - **Disabled**: Slot is turned off. No adapter loaded. Used to shed non-critical features
///                 when battery/network is constrained, or for feature flagging.
enum AdapterTier: String, Codable, CaseIterable, Sendable {
    case mock = "Mock"
    case native = "Native"
    case live = "Live"
    case disabled = "Disabled"

    /// Human-readable label for UI display.
    var displayName: String {
        switch self {
        case .mock:     return "Mock (In-Memory)"
        case .native:   return "Native (On-Device)"
        case .live:     return "Live (Cloud)"
        case .disabled: return "Disabled"
        }
    }

    /// Whether this tier represents an active (non-disabled) adapter.
    var isActive: Bool { self != .disabled }

    /// Parse a tier string from Aspire config or SignalR message.
    /// Case-insensitive. Defaults to `.mock` for unrecognized values.
    ///
    /// ```swift
    /// AdapterTier.fromString("native") // .native
    /// AdapterTier.fromString("LIVE")   // .live
    /// AdapterTier.fromString("junk")   // .mock (safe default)
    /// ```
    static func fromString(_ value: String) -> AdapterTier {
        AdapterTier.allCases.first { $0.rawValue.caseInsensitiveCompare(value) == .orderedSame } ?? .mock
    }
}
