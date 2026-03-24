package com.thewatch.app.data.adapters

import com.thewatch.app.data.logging.WatchLogger
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import javax.inject.Inject
import javax.inject.Singleton

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: AdapterRegistry is the mobile equivalent of TheWatch.Shared.Configuration.AdapterRegistry.
// It controls which tier (Mock/Native/Live/Disabled) is active for every adapter slot at runtime.
//
// Key behaviors:
//   1. Loads defaults from BuildConfig / local config at startup (all Mock for debug builds)
//   2. Exposes a StateFlow<Map<AdapterSlot, AdapterTier>> for reactive UI observation
//   3. switchTier(slot, tier) hot-swaps the active implementation — no app restart
//   4. SignalRAdapterSync listens for AdapterTierChanged messages from MAUI dashboard
//      and calls switchTier() to remotely control mobile adapter tiers
//   5. Adapter providers (in AppModule) call registry.resolve(slot) to get the current tier
//      and instantiate the appropriate implementation
//
// Example:
// ```kotlin
// @Inject lateinit var registry: AdapterRegistry
//
// // Check current tier
// val loggingTier = registry.getTier(AdapterSlot.Logging)
//
// // Switch at runtime (no restart needed)
// registry.switchTier(AdapterSlot.Logging, AdapterTier.Native)
//
// // Observe all tier changes in Compose
// val tiers by registry.tierMap.collectAsState()
//
// // Resolve in DI provider
// val loggingPort: LoggingPort = when (registry.getTier(AdapterSlot.Logging)) {
//     AdapterTier.Mock -> mockLogging
//     AdapterTier.Native -> nativeLogging
//     else -> mockLogging
// }
// ```

/**
 * Callback invoked when a tier switch occurs.
 *
 * @param slot The adapter slot that changed
 * @param oldTier The previous tier
 * @param newTier The new tier
 */
typealias TierChangeListener = (slot: AdapterSlot, oldTier: AdapterTier, newTier: AdapterTier) -> Unit

/**
 * Central registry controlling which adapter tier is active for each slot.
 *
 * This is the backbone of TheWatch's three-tier architecture on mobile.
 * The MAUI dashboard can remotely switch tiers via SignalR, and UI components
 * observe changes reactively via [tierMap] StateFlow.
 *
 * Switching from Mock→Native is seamless — no app restart required. Adapters are
 * resolved lazily through DI providers that consult the registry.
 *
 * Thread-safe: all mutations go through [MutableStateFlow.update] which is atomic.
 */
@Singleton
class AdapterRegistry @Inject constructor(
    private val logger: WatchLogger
) {
    // ── Default Configuration ────────────────────────────────────
    // Debug builds default to Mock for all slots.
    // Release builds could load from SharedPreferences, remote config, or BuildConfig.

    private val defaults: Map<AdapterSlot, AdapterTier> = buildMap {
        // All slots start as Mock in development — matches Aspire's appsettings.Development.json
        AdapterSlot.entries.forEach { slot ->
            put(slot, AdapterTier.Mock)
        }
        // Override specific slots that should be Native even in debug
        // (e.g., Location needs real GPS for testing, Security for biometric testing)
        // put(AdapterSlot.Location, AdapterTier.Native)
        // put(AdapterSlot.Security, AdapterTier.Native)
    }

    // ── Reactive State ───────────────────────────────────────────

    private val _tierMap = MutableStateFlow(defaults)

    /**
     * Observable map of all slot→tier assignments.
     * Compose UI collects this to display adapter status.
     * Changes are atomic — observers always see a consistent snapshot.
     */
    val tierMap: StateFlow<Map<AdapterSlot, AdapterTier>> = _tierMap.asStateFlow()

    private val listeners = mutableListOf<TierChangeListener>()

    // ── Query ────────────────────────────────────────────────────

    /**
     * Get the current tier for a slot.
     * Returns [AdapterTier.Disabled] if the slot is somehow not in the map (shouldn't happen).
     */
    fun getTier(slot: AdapterSlot): AdapterTier {
        return _tierMap.value[slot] ?: AdapterTier.Disabled
    }

    /**
     * Whether a slot is active (not Disabled).
     */
    fun isEnabled(slot: AdapterSlot): Boolean = getTier(slot).isActive

    /**
     * Whether a slot is running in Live (cloud) mode.
     */
    fun isLive(slot: AdapterSlot): Boolean = getTier(slot) == AdapterTier.Live

    /**
     * Whether a slot is running in Native (on-device) mode.
     */
    fun isNative(slot: AdapterSlot): Boolean = getTier(slot) == AdapterTier.Native

    /**
     * Whether a slot is running in Mock (in-memory) mode.
     */
    fun isMock(slot: AdapterSlot): Boolean = getTier(slot) == AdapterTier.Mock

    // ── Mutation ─────────────────────────────────────────────────

    /**
     * Switch a slot to a new tier at runtime. No app restart required.
     *
     * DI providers that consult the registry will return the appropriate
     * implementation on their next resolution. Already-injected references
     * use wrapper/delegate patterns to hot-swap.
     *
     * Logs the transition and notifies all registered listeners.
     *
     * @param slot The adapter slot to switch
     * @param tier The new tier to activate
     * @return true if the tier actually changed, false if it was already at that tier
     */
    fun switchTier(slot: AdapterSlot, tier: AdapterTier): Boolean {
        val oldTier = getTier(slot)
        if (oldTier == tier) return false

        _tierMap.update { current ->
            current.toMutableMap().apply { put(slot, tier) }
        }

        logger.information(
            sourceContext = "AdapterRegistry",
            messageTemplate = "Tier switched: {Slot} {OldTier} → {NewTier}",
            properties = mapOf(
                "Slot" to slot.name,
                "OldTier" to oldTier.name,
                "NewTier" to tier.name
            )
        )

        // Notify listeners
        listeners.forEach { it(slot, oldTier, tier) }

        return true
    }

    /**
     * Batch-switch multiple slots at once. Emits a single state update.
     *
     * Used when the MAUI dashboard sends a full configuration snapshot
     * (e.g., on app reconnect after being offline).
     *
     * @param changes Map of slot→tier to apply
     * @return Number of slots that actually changed
     */
    fun switchTiers(changes: Map<AdapterSlot, AdapterTier>): Int {
        var changeCount = 0

        _tierMap.update { current ->
            val mutable = current.toMutableMap()
            for ((slot, tier) in changes) {
                val old = mutable[slot]
                if (old != tier) {
                    mutable[slot] = tier
                    changeCount++
                    listeners.forEach { it(slot, old ?: AdapterTier.Disabled, tier) }
                }
            }
            mutable
        }

        if (changeCount > 0) {
            logger.information(
                sourceContext = "AdapterRegistry",
                messageTemplate = "Batch tier switch: {Count} slots changed",
                properties = mapOf("Count" to changeCount.toString())
            )
        }

        return changeCount
    }

    /**
     * Reset all slots to their default tiers.
     * Useful for diagnostics or when the dashboard connection is lost.
     */
    fun resetToDefaults() {
        _tierMap.value = defaults
        logger.information(
            sourceContext = "AdapterRegistry",
            messageTemplate = "All adapter tiers reset to defaults"
        )
    }

    // ── Listeners ────────────────────────────────────────────────

    /**
     * Register a callback for tier changes. Used by adapter wrappers
     * that need to hot-swap their delegate on tier change.
     */
    fun addListener(listener: TierChangeListener) {
        listeners.add(listener)
    }

    fun removeListener(listener: TierChangeListener) {
        listeners.remove(listener)
    }

    // ── Snapshot for Dashboard ───────────────────────────────────

    /**
     * Export current tier map as a simple String→String map for SignalR / REST responses.
     * Keys are slot names, values are tier names.
     */
    fun toSerializableMap(): Map<String, String> {
        return _tierMap.value.map { (slot, tier) -> slot.name to tier.name }.toMap()
    }

    /**
     * Import a tier map from a serialized String→String map (from SignalR / REST).
     * Unrecognized slots or tiers are silently skipped.
     */
    fun fromSerializableMap(map: Map<String, String>) {
        val changes = map.mapNotNull { (slotName, tierName) ->
            val slot = AdapterSlot.fromString(slotName) ?: return@mapNotNull null
            val tier = AdapterTier.fromString(tierName)
            slot to tier
        }.toMap()
        switchTiers(changes)
    }
}
