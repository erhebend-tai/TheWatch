package com.thewatch.app.data.signalr

import com.thewatch.app.data.logging.WatchLogger
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.math.min
import kotlin.random.Random

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: WatchHubConnection is the central SignalR hub connection manager for Android.
// It maintains a single persistent connection to the Aspire Dashboard's SignalR hub
// at {baseUrl}/hubs/dashboard, enabling bidirectional real-time communication.
//
// Architecture:
//   - Singleton lifecycle managed by Hilt DI
//   - Exposes connectionState as StateFlow<HubConnectionState> for reactive UI
//   - Auto-connects on app foreground, disconnects on background
//   - Reconnects with exponential backoff + jitter to prevent thundering herd
//   - Joins device group "device_{deviceId}" on connect for targeted test dispatching
//   - Joins user group "user_{userId}" for targeted notifications (surveys, alerts)
//   - All SignalR events logged through WatchLogger with sourceContext "SignalR"
//
// Dependencies:
//   - com.microsoft.signalr:signalr:7.0.0 (add to build.gradle.kts)
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
//   - MilestoneUpdated(milestone) — dashboard milestone progress
//   - WorkItemUpdated(workItem) — dashboard work item changes
//   - BuildCompleted(buildStatus) — CI/CD build results
//   - AgentActivityRecorded(activity) — agent/responder activity
//   - SimulationEventReceived(event) — simulation events
//
// Outbound server methods (we call these on the hub):
//   - JoinResponseGroup(requestId) — join SOS response coordination
//   - LeaveResponseGroup(requestId) — leave response group
//   - JoinUserGroup(userId) — join user-targeted notification group
//   - LeaveUserGroup(userId) — leave user group
//   - UpdateResponderLocation(requestId, responderId, lat, lng, speed) — en-route updates
//   - ResponderOnScene(requestId, responderId) — arrival signal
//   - NotifyEvidenceSubmitted(...) — evidence upload notification
//   - ReportAdapterTiers(tiers) — send current tier map to dashboard
//
// Example:
// ```kotlin
// @Inject lateinit var hubConnection: WatchHubConnection
//
// // Connect on app foreground
// hubConnection.connect("https://dashboard.thewatch.app", deviceId = "pixel_7_01", userId = "user_123")
//
// // Observe connection state in Compose
// val state by hubConnection.connectionState.collectAsState()
// when (state) {
//     HubConnectionState.Connected -> Icon(Icons.Default.Cloud, tint = Color.Green)
//     HubConnectionState.Reconnecting -> CircularProgressIndicator()
//     HubConnectionState.Disconnected -> Icon(Icons.Default.CloudOff, tint = Color.Red)
// }
//
// // Disconnect on background
// hubConnection.disconnect()
// ```

/**
 * Connection state exposed as a StateFlow for reactive UI observation.
 *
 * States mirror the Microsoft SignalR HubConnectionState enum:
 * - [Disconnected]: No active connection. Initial state and after explicit disconnect.
 * - [Connecting]: Connection attempt in progress.
 * - [Connected]: Active bidirectional connection. Messages can be sent/received.
 * - [Reconnecting]: Connection lost, auto-reconnect in progress with backoff.
 */
enum class HubConnectionState {
    Disconnected,
    Connecting,
    Connected,
    Reconnecting
}

/**
 * Callback interface for hub connection lifecycle events.
 * Registered by [HubMessageRouter] and other components that need to react to
 * connection state changes beyond what [connectionState] StateFlow provides.
 */
interface HubConnectionListener {
    /** Called immediately after connection is established and groups are joined. */
    fun onConnected()
    /** Called when connection is lost. [error] is null for graceful disconnect. */
    fun onDisconnected(error: Throwable?)
    /** Called when a reconnect attempt begins. [attemptNumber] is 1-based. */
    fun onReconnecting(attemptNumber: Int)
}

/**
 * Singleton SignalR hub connection manager.
 *
 * Maintains a single persistent connection to the Aspire Dashboard hub.
 * Handles connection lifecycle, group membership, and auto-reconnection
 * with exponential backoff and jitter.
 *
 * All SignalR library calls are wrapped behind this abstraction so the rest
 * of the app never touches com.microsoft.signalr directly.
 *
 * Thread-safe: all state mutations use MutableStateFlow.value (atomic) or
 * are synchronized through the single-threaded reconnect coroutine.
 */
@Singleton
class WatchHubConnection @Inject constructor(
    private val logger: WatchLogger
) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    // ── Connection State ─────────────────────────────────────────

    private val _connectionState = MutableStateFlow(HubConnectionState.Disconnected)

    /**
     * Observable connection state. Compose UI collects this to show
     * connection indicators (green dot, spinner, red dot).
     */
    val connectionState: StateFlow<HubConnectionState> = _connectionState.asStateFlow()

    /**
     * The underlying SignalR HubConnection instance.
     * Type is Any? to compile without the SignalR dependency present.
     * At runtime this is com.microsoft.signalr.HubConnection.
     *
     * Accessed by [HubMessageRouter] to register handlers and by
     * [HubEventSender] to invoke server methods.
     */
    @Volatile
    var hubConnection: Any? = null
        private set

    private var reconnectJob: Job? = null
    private val listeners = mutableListOf<HubConnectionListener>()

    // ── Configuration ────────────────────────────────────────────

    private var baseUrl: String? = null
    private var deviceId: String? = null
    private var userId: String? = null

    /** Maximum reconnect delay in milliseconds. */
    private val maxReconnectDelayMs = 60_000L

    /** Initial reconnect delay in milliseconds. */
    private val initialReconnectDelayMs = 2_000L

    /** Maximum jitter added to reconnect delay to prevent thundering herd. */
    private val maxJitterMs = 3_000L

    // ── Connection Lifecycle ─────────────────────────────────────

    /**
     * Connect to the Dashboard SignalR hub.
     *
     * Safe to call multiple times — disconnects existing connection first.
     * On successful connect:
     *   1. Joins device group "device_{deviceId}" (for test step dispatching)
     *   2. Joins user group "user_{userId}" (for targeted notifications)
     *   3. Notifies all registered [HubConnectionListener]s
     *
     * @param dashboardBaseUrl Base URL of the Aspire Dashboard API (e.g., "https://localhost:5001")
     * @param deviceId Stable device identifier for group membership
     * @param userId Current authenticated user ID (null if not logged in)
     */
    fun connect(dashboardBaseUrl: String, deviceId: String, userId: String? = null) {
        disconnect()

        this.baseUrl = dashboardBaseUrl
        this.deviceId = deviceId
        this.userId = userId

        val hubUrl = "$dashboardBaseUrl/hubs/dashboard"
        _connectionState.value = HubConnectionState.Connecting

        logger.information(
            sourceContext = "SignalR",
            messageTemplate = "Connecting to hub at {HubUrl} as device {DeviceId}",
            properties = mapOf("HubUrl" to hubUrl, "DeviceId" to deviceId)
        )

        // ── SignalR Hub Connection ───────────────────────────────
        // NOTE: Requires com.microsoft.signalr:signalr:7.0.0 dependency
        //
        // Implementation (uncomment when SignalR dependency is added):
        //
        // import com.microsoft.signalr.HubConnectionBuilder
        // import com.microsoft.signalr.HubConnectionState as MsHubState
        // import com.microsoft.signalr.TransportEnum
        //
        // val connection = HubConnectionBuilder.create(hubUrl)
        //     .withTransport(TransportEnum.WEBSOCKETS)
        //     .withHandshakeResponseTimeout(15_000)
        //     .shouldSkipNegotiate(false)
        //     // .withAccessTokenProvider { Single.just(authToken) }
        //     .build()
        //
        // connection.onClosed { error ->
        //     handleDisconnect(error)
        // }
        //
        // try {
        //     connection.start().blockingAwait()
        //     hubConnection = connection
        //     _connectionState.value = HubConnectionState.Connected
        //
        //     // Join device group for test orchestration dispatching
        //     connection.send("JoinUserGroup", userId)  // if userId != null
        //     joinDeviceGroup(connection, deviceId)
        //
        //     logger.information(
        //         sourceContext = "SignalR",
        //         messageTemplate = "Connected to hub. Joined device group device_{DeviceId}",
        //         properties = mapOf("DeviceId" to deviceId)
        //     )
        //
        //     listeners.forEach { it.onConnected() }
        // } catch (e: Exception) {
        //     logger.error(
        //         sourceContext = "SignalR",
        //         messageTemplate = "Failed to connect to hub: {Error}",
        //         properties = mapOf("Error" to (e.message ?: "unknown")),
        //         exception = e
        //     )
        //     handleDisconnect(e)
        // }

        // Mock mode — simulate successful connection
        _connectionState.value = HubConnectionState.Connected
        logger.information(
            sourceContext = "SignalR",
            messageTemplate = "Connected to hub (mock mode). Device group: device_{DeviceId}",
            properties = mapOf("DeviceId" to deviceId, "Mode" to "Mock")
        )
        listeners.forEach { it.onConnected() }
    }

    /**
     * Disconnect from the SignalR hub. Safe to call even if not connected.
     * Cancels any pending reconnect attempts.
     */
    fun disconnect() {
        reconnectJob?.cancel()
        reconnectJob = null

        val wasConnected = _connectionState.value == HubConnectionState.Connected

        // hubConnection?.let { conn ->
        //     val connection = conn as com.microsoft.signalr.HubConnection
        //     try {
        //         // Leave groups before disconnecting
        //         deviceId?.let { connection.send("LeaveUserGroup", userId) }
        //         connection.stop().blockingAwait()
        //     } catch (e: Exception) {
        //         logger.warning(
        //             sourceContext = "SignalR",
        //             messageTemplate = "Error during disconnect: {Error}",
        //             properties = mapOf("Error" to (e.message ?: "unknown"))
        //         )
        //     }
        // }

        hubConnection = null
        _connectionState.value = HubConnectionState.Disconnected

        if (wasConnected) {
            logger.information(
                sourceContext = "SignalR",
                messageTemplate = "Disconnected from hub"
            )
            listeners.forEach { it.onDisconnected(null) }
        }
    }

    /**
     * Update the user ID after login. Re-joins the user group if connected.
     */
    fun updateUserId(newUserId: String?) {
        val oldUserId = userId
        userId = newUserId

        if (_connectionState.value == HubConnectionState.Connected) {
            // Leave old user group, join new one
            // hubConnection?.let { conn ->
            //     val connection = conn as com.microsoft.signalr.HubConnection
            //     oldUserId?.let { connection.send("LeaveUserGroup", it) }
            //     newUserId?.let { connection.send("JoinUserGroup", it) }
            // }

            logger.information(
                sourceContext = "SignalR",
                messageTemplate = "User group updated: {OldUser} → {NewUser}",
                properties = mapOf(
                    "OldUser" to (oldUserId ?: "none"),
                    "NewUser" to (newUserId ?: "none")
                )
            )
        }
    }

    // ── Response Group Management ────────────────────────────────

    /**
     * Join a response group to receive real-time updates for an SOS request.
     * Called when accepting a dispatch notification.
     */
    fun joinResponseGroup(requestId: String) {
        // hubConnection?.let { conn ->
        //     val connection = conn as com.microsoft.signalr.HubConnection
        //     connection.send("JoinResponseGroup", requestId)
        // }

        logger.information(
            sourceContext = "SignalR",
            messageTemplate = "Joined response group: response-{RequestId}",
            properties = mapOf("RequestId" to requestId)
        )
    }

    /**
     * Leave a response group when the response is resolved or cancelled.
     */
    fun leaveResponseGroup(requestId: String) {
        // hubConnection?.let { conn ->
        //     val connection = conn as com.microsoft.signalr.HubConnection
        //     connection.send("LeaveResponseGroup", requestId)
        // }

        logger.information(
            sourceContext = "SignalR",
            messageTemplate = "Left response group: response-{RequestId}",
            properties = mapOf("RequestId" to requestId)
        )
    }

    // ── Reconnection ─────────────────────────────────────────────

    /**
     * Handle unexpected disconnection. Starts exponential backoff reconnection
     * with jitter to prevent all devices from reconnecting simultaneously.
     *
     * Backoff schedule: 2s, 4s, 8s, 16s, 32s, 60s (capped)
     * Jitter: random 0-3000ms added to each delay
     */
    private fun handleDisconnect(error: Throwable?) {
        _connectionState.value = HubConnectionState.Disconnected
        val url = baseUrl ?: return
        val device = deviceId ?: return

        if (error != null) {
            logger.warning(
                sourceContext = "SignalR",
                messageTemplate = "Hub disconnected with error: {Error}. Starting reconnection.",
                properties = mapOf("Error" to (error.message ?: "unknown")),
                exception = error
            )
        }

        listeners.forEach { it.onDisconnected(error) }

        reconnectJob = scope.launch {
            var delayMs = initialReconnectDelayMs
            var attempt = 0

            while (isActive) {
                attempt++
                val jitter = Random.nextLong(0, maxJitterMs)
                val totalDelay = delayMs + jitter

                _connectionState.value = HubConnectionState.Reconnecting
                listeners.forEach { it.onReconnecting(attempt) }

                logger.information(
                    sourceContext = "SignalR",
                    messageTemplate = "Reconnect attempt {Attempt} in {DelayMs}ms (backoff={Backoff}ms, jitter={Jitter}ms)",
                    properties = mapOf(
                        "Attempt" to attempt.toString(),
                        "DelayMs" to totalDelay.toString(),
                        "Backoff" to delayMs.toString(),
                        "Jitter" to jitter.toString()
                    )
                )

                delay(totalDelay)

                try {
                    connect(url, device, userId)
                    if (_connectionState.value == HubConnectionState.Connected) {
                        logger.information(
                            sourceContext = "SignalR",
                            messageTemplate = "Reconnected after {Attempt} attempts",
                            properties = mapOf("Attempt" to attempt.toString())
                        )
                        break
                    }
                } catch (e: Exception) {
                    logger.warning(
                        sourceContext = "SignalR",
                        messageTemplate = "Reconnect attempt {Attempt} failed: {Error}",
                        properties = mapOf(
                            "Attempt" to attempt.toString(),
                            "Error" to (e.message ?: "unknown")
                        )
                    )
                }

                delayMs = min(delayMs * 2, maxReconnectDelayMs)
            }
        }
    }

    // ── Listener Management ──────────────────────────────────────

    fun addListener(listener: HubConnectionListener) {
        listeners.add(listener)
    }

    fun removeListener(listener: HubConnectionListener) {
        listeners.remove(listener)
    }

    // ── Diagnostics ──────────────────────────────────────────────

    /**
     * Returns a snapshot of connection info for diagnostics display.
     */
    fun diagnosticsSnapshot(): Map<String, String> = mapOf(
        "State" to _connectionState.value.name,
        "HubUrl" to (baseUrl?.let { "$it/hubs/dashboard" } ?: "not configured"),
        "DeviceId" to (deviceId ?: "not set"),
        "UserId" to (userId ?: "not set"),
        "DeviceGroup" to (deviceId?.let { "device_$it" } ?: "not joined"),
        "UserGroup" to (userId?.let { "user_$it" } ?: "not joined")
    )
}
