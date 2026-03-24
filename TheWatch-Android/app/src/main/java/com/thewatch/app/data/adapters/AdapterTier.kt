package com.thewatch.app.data.adapters

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: AdapterTier mirrors the Aspire backend's tier system (TheWatch.Shared.Configuration.AdapterRegistry).
// Mobile apps use the same four-tier model so the MAUI dashboard can command tier switches
// at runtime via SignalR → AdapterTierController → SignalRAdapterSync.
//
// Example:
// ```kotlin
// val tier = AdapterTier.fromString("Native") // AdapterTier.Native
// val label = AdapterTier.Mock.displayName     // "Mock (In-Memory)"
// ```

/**
 * Adapter implementation tier — controls which concrete adapter is active for a given slot.
 *
 * Matches the Aspire backend's valid values: "Mock", "Native", "Live", "Disabled"
 *
 * - **Mock**:     In-memory/fake implementation. Always available, no cloud/hardware needed.
 *                 Used for development, testing, and CI. Zero external dependencies.
 * - **Native**:   Platform-local hardware + OS APIs (Room/SwiftData, CoreLocation, AVFoundation).
 *                 Works fully offline. The "field deployment" tier.
 * - **Live**:     Real cloud adapters (Firestore, Cloud Messaging, Azure SignalR).
 *                 Requires credentials, network, and provisioned backend services.
 * - **Disabled**: Slot is turned off. No adapter loaded. Used to shed non-critical features
 *                 when battery/network is constrained, or for feature flagging.
 */
enum class AdapterTier {
    Mock,
    Native,
    Live,
    Disabled;

    /** Human-readable label for UI display. */
    val displayName: String
        get() = when (this) {
            Mock -> "Mock (In-Memory)"
            Native -> "Native (On-Device)"
            Live -> "Live (Cloud)"
            Disabled -> "Disabled"
        }

    /** Whether this tier represents an active (non-disabled) adapter. */
    val isActive: Boolean get() = this != Disabled

    companion object {
        /**
         * Parse a tier string from Aspire config or SignalR message.
         * Case-insensitive. Defaults to [Mock] for unrecognized values.
         *
         * ```kotlin
         * AdapterTier.fromString("native") // Native
         * AdapterTier.fromString("LIVE")   // Live
         * AdapterTier.fromString("junk")   // Mock (safe default)
         * ```
         */
        fun fromString(value: String): AdapterTier {
            return entries.firstOrNull { it.name.equals(value, ignoreCase = true) } ?: Mock
        }
    }
}
