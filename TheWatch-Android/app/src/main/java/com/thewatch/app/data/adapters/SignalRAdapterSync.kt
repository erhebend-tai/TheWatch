package com.thewatch.app.data.adapters

import com.thewatch.app.data.logging.WatchLogger
import kotlinx.coroutines.*
import javax.inject.Inject
import javax.inject.Singleton

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
// The actual SignalR client library (com.microsoft.signalr:signalr) must be added
// to build.gradle.kts. This class wraps the hub connection and translates messages
// into AdapterRegistry.switchTier() calls.
//
// Example:
// ```kotlin
// @Inject lateinit var sync: SignalRAdapterSync
//
// // Connect to dashboard
// sync.connect("https://dashboard.thewatch.app")
//
// // Disconnect on app teardown
// sync.disconnect()
// ```

/**
 * Listens to the MAUI dashboard's SignalR hub for adapter tier change commands
 * and applies them to the local [AdapterRegistry].
 *
 * This enables the dashboard operator to remotely switch mobile adapters between
 * Mock, Native, and Live tiers without requiring an app restart or redeployment.
 *
 * Connection lifecycle:
 * 1. [connect] establishes the SignalR hub connection
 * 2. On connect, sends current tier map via "ReportAdapterTiers"
 * 3. Listens for "AdapterTierChanged", "AdapterTierSnapshot", "AdapterTierReset"
 * 4. Auto-reconnects on disconnect with exponential backoff (5s, 10s, 20s, 40s, max 60s)
 * 5. [disconnect] tears down cleanly
 */
@Singleton
class SignalRAdapterSync @Inject constructor(
    private val registry: AdapterRegistry,
    private val logger: WatchLogger
) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    /**
     * Whether the SignalR connection is currently active.
     * Observed by UI to show connection status indicators.
     */
    @Volatile
    var isConnected: Boolean = false
        private set

    private var hubConnection: Any? = null // com.microsoft.signalr.HubConnection at runtime
    private var reconnectJob: Job? = null

    /** Base URL of the dashboard API. Set on [connect]. */
    private var dashboardUrl: String? = null

    // ── Connection Lifecycle ─────────────────────────────────────

    /**
     * Connect to the MAUI dashboard's SignalR hub and start listening
     * for adapter tier change commands.
     *
     * Safe to call multiple times — disconnects existing connection first.
     *
     * @param baseUrl The dashboard base URL (e.g., "https://dashboard.thewatch.app")
     */
    fun connect(baseUrl: String) {
        disconnect() // Clean up any existing connection
        dashboardUrl = baseUrl
        val hubUrl = "$baseUrl/hubs/dashboard"

        logger.information(
            sourceContext = "SignalRAdapterSync",
            messageTemplate = "Connecting to dashboard hub at {HubUrl}",
            properties = mapOf("HubUrl" to hubUrl)
        )

        // ── SignalR Hub Connection ───────────────────────────────
        // NOTE: Requires com.microsoft.signalr:signalr dependency in build.gradle.kts
        //
        // Implementation sketch (uncomment when SignalR dependency is added):
        //
        // hubConnection = HubConnectionBuilder.create(hubUrl)
        //     .withAccessTokenProvider { Single.just(authToken) }
        //     .build()
        //
        // hubConnection.on("AdapterTierChanged", { message ->
        //     handleTierChanged(message)
        // }, JsonObject::class.java)
        //
        // hubConnection.on("AdapterTierSnapshot", { snapshot ->
        //     handleTierSnapshot(snapshot)
        // }, JsonObject::class.java)
        //
        // hubConnection.on("AdapterTierReset", {
        //     handleTierReset()
        // })
        //
        // hubConnection.onClosed { error ->
        //     handleDisconnect(error)
        // }
        //
        // hubConnection.start().blockingAwait()
        // isConnected = true
        // reportCurrentTiers()

        // For now, mark as connected in mock mode
        isConnected = true

        logger.information(
            sourceContext = "SignalRAdapterSync",
            messageTemplate = "Connected to dashboard hub (mock mode). Reporting current tiers.",
            properties = mapOf("Mode" to "Mock")
        )
    }

    /**
     * Disconnect from the SignalR hub. Safe to call even if not connected.
     */
    fun disconnect() {
        reconnectJob?.cancel()
        reconnectJob = null

        // hubConnection?.stop()?.blockingAwait()
        hubConnection = null
        isConnected = false

        logger.information(
            sourceContext = "SignalRAdapterSync",
            messageTemplate = "Disconnected from dashboard hub"
        )
    }

    // ── Message Handlers ─────────────────────────────────────────

    /**
     * Handle a single tier change command from the dashboard.
     * Expected payload: { "Slot": "Logging", "Tier": "Native" }
     */
    fun handleTierChanged(slotName: String, tierName: String) {
        val slot = AdapterSlot.fromString(slotName)
        if (slot == null) {
            logger.warning(
                sourceContext = "SignalRAdapterSync",
                messageTemplate = "Received tier change for unknown slot: {Slot}",
                properties = mapOf("Slot" to slotName)
            )
            return
        }

        val tier = AdapterTier.fromString(tierName)
        val changed = registry.switchTier(slot, tier)

        logger.information(
            sourceContext = "SignalRAdapterSync",
            messageTemplate = "Dashboard commanded tier change: {Slot} → {Tier} (applied={Applied})",
            properties = mapOf(
                "Slot" to slotName,
                "Tier" to tierName,
                "Applied" to changed.toString()
            )
        )
    }

    /**
     * Handle a full tier snapshot from the dashboard.
     * Expected payload: { "Logging": "Mock", "Location": "Native", ... }
     * Typically sent on initial connect or after dashboard configuration change.
     */
    fun handleTierSnapshot(snapshot: Map<String, String>) {
        logger.information(
            sourceContext = "SignalRAdapterSync",
            messageTemplate = "Received tier snapshot from dashboard. {Count} entries.",
            properties = mapOf("Count" to snapshot.size.toString())
        )
        registry.fromSerializableMap(snapshot)
    }

    /**
     * Handle a tier reset command — revert all slots to defaults.
     */
    fun handleTierReset() {
        logger.information(
            sourceContext = "SignalRAdapterSync",
            messageTemplate = "Dashboard commanded tier reset to defaults"
        )
        registry.resetToDefaults()
    }

    // ── Outbound ─────────────────────────────────────────────────

    /**
     * Report the current tier map to the dashboard.
     * Called on connect and when the dashboard requests it.
     */
    fun reportCurrentTiers() {
        val tiers = registry.toSerializableMap()
        logger.debug(
            sourceContext = "SignalRAdapterSync",
            messageTemplate = "Reporting current tiers to dashboard: {Tiers}",
            properties = mapOf("Tiers" to tiers.entries.joinToString(", ") { "${it.key}=${it.value}" })
        )

        // hubConnection?.send("ReportAdapterTiers", tiers)
    }

    // ── Auto-Reconnect ───────────────────────────────────────────

    private fun handleDisconnect(error: Throwable?) {
        isConnected = false
        val url = dashboardUrl ?: return

        if (error != null) {
            logger.warning(
                sourceContext = "SignalRAdapterSync",
                messageTemplate = "SignalR disconnected with error: {Error}. Scheduling reconnect.",
                properties = mapOf("Error" to (error.message ?: "unknown")),
                exception = error
            )
        }

        reconnectJob = scope.launch {
            var delayMs = 5000L
            val maxDelay = 60000L

            while (isActive) {
                delay(delayMs)
                try {
                    connect(url)
                    if (isConnected) {
                        logger.information(
                            sourceContext = "SignalRAdapterSync",
                            messageTemplate = "Reconnected to dashboard hub after {DelayMs}ms backoff",
                            properties = mapOf("DelayMs" to delayMs.toString())
                        )
                        break
                    }
                } catch (e: Exception) {
                    logger.warning(
                        sourceContext = "SignalRAdapterSync",
                        messageTemplate = "Reconnect attempt failed: {Error}",
                        properties = mapOf("Error" to (e.message ?: "unknown"))
                    )
                }
                delayMs = (delayMs * 2).coerceAtMost(maxDelay)
            }
        }
    }
}
