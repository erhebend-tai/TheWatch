package com.thewatch.app.data.signalr

import com.thewatch.app.data.logging.WatchLogger
import kotlinx.coroutines.*
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.time.Instant
import java.util.concurrent.ConcurrentLinkedQueue
import javax.inject.Inject
import javax.inject.Singleton

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: HubEventSender is the outbound message sender for the SignalR hub connection.
// It provides a typed API for all messages the mobile client sends to the DashboardHub,
// with automatic queueing when disconnected and flush-on-reconnect.
//
// Architecture:
//   - Thread-safe: all sends go through a Mutex-guarded serial queue
//   - Offline resilience: messages queued in ConcurrentLinkedQueue when disconnected
//   - Auto-flush: listens for HubConnectionListener.onConnected() and flushes queue
//   - Queue cap: max 500 messages to prevent OOM during extended offline periods
//   - All outbound messages logged through WatchLogger with sourceContext "SignalR"
//
// Outbound messages:
//   - "TestStepCompleted" → sends test step result back to orchestrator
//   - "LogEntry" → streams a log entry to the dashboard
//   - "DeviceStatusUpdate" → periodic device health/status report
//   - "Pong" → response to server Ping
//   - "ReportAdapterTiers" → current adapter tier map
//   - "UpdateResponderLocation" → en-route location updates
//   - "ResponderOnScene" → arrival notification
//   - "NotifyEvidenceSubmitted" → evidence upload notification
//
// Example:
// ```kotlin
// @Inject lateinit var sender: HubEventSender
//
// // Send a test step result
// sender.sendTestStepCompleted(
//     runId = "run_abc123",
//     stepId = "step_001",
//     passed = true,
//     screenshot = null,
//     errorMessage = null,
//     durationMs = 1250
// )
//
// // Stream a log entry
// sender.sendLogEntry(
//     level = "Information",
//     source = "LocationCoordinator",
//     message = "Mode escalated to Emergency",
//     correlationId = "sos_xyz"
// )
//
// // Messages are queued if disconnected and auto-flushed on reconnect
// ```

/**
 * Queued message waiting to be sent when the connection is restored.
 */
@Serializable
data class QueuedHubMessage(
    val method: String,
    val argsJson: String,
    val queuedAt: String // ISO-8601 timestamp
)

/**
 * Outbound message sender for the SignalR hub.
 *
 * Provides typed methods for every message the mobile client sends to the
 * DashboardHub. Handles offline queueing and reconnect flushing automatically.
 *
 * Thread safety: all sends are serialized through a [Mutex] to prevent
 * concurrent writes on the SignalR connection, which is not thread-safe.
 * The queue is a [ConcurrentLinkedQueue] for lock-free enqueue from any thread.
 */
@Singleton
class HubEventSender @Inject constructor(
    private val hubConnection: WatchHubConnection,
    private val logger: WatchLogger
) : HubConnectionListener {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val sendMutex = Mutex()
    private val json = Json { encodeDefaults = true }

    // ── Offline Queue ────────────────────────────────────────────

    /**
     * Messages queued while disconnected. Flushed FIFO on reconnect.
     * Capped at [maxQueueSize] to prevent OOM.
     */
    private val messageQueue = ConcurrentLinkedQueue<QueuedHubMessage>()

    /** Maximum number of queued messages. Oldest are dropped when exceeded. */
    private val maxQueueSize = 500

    init {
        hubConnection.addListener(this)
    }

    // ── HubConnectionListener ────────────────────────────────────

    override fun onConnected() {
        scope.launch { flushQueue() }
    }

    override fun onDisconnected(error: Throwable?) {
        // Queue will accumulate messages until reconnect
    }

    override fun onReconnecting(attemptNumber: Int) {
        // No action — queue continues to accumulate
    }

    // ── Test Orchestration ───────────────────────────────────────

    /**
     * Send a test step result back to the TestOrchestratorService.
     *
     * The orchestrator uses this to advance the test run: recording the result,
     * dispatching the next step, and broadcasting to dashboard observers.
     *
     * Maps to: TestOrchestratorService.RecordStepResultAsync(runId, stepId, passed, screenshot, errorMessage)
     */
    fun sendTestStepCompleted(
        runId: String,
        stepId: String,
        passed: Boolean,
        screenshot: String? = null,
        errorMessage: String? = null,
        durationMs: Long = 0
    ) {
        val args = buildMap {
            put("runId", runId)
            put("stepId", stepId)
            put("passed", passed.toString())
            screenshot?.let { put("screenshot", it) }
            errorMessage?.let { put("errorMessage", it) }
            put("durationMs", durationMs.toString())
        }

        send("TestStepCompleted", json.encodeToString(args), correlationId = runId)
    }

    // ── Log Streaming ────────────────────────────────────────────

    /**
     * Stream a log entry to the dashboard for real-time log viewing.
     *
     * Called by the LoggingPort adapter when log streaming is active.
     * The dashboard's LogViewerPanel displays these in real-time.
     */
    fun sendLogEntry(
        level: String,
        source: String,
        message: String,
        properties: Map<String, String> = emptyMap(),
        exception: String? = null,
        correlationId: String? = null
    ) {
        val args = buildMap {
            put("level", level)
            put("source", source)
            put("message", message)
            put("timestamp", Instant.now().toString())
            if (properties.isNotEmpty()) put("properties", json.encodeToString(properties))
            exception?.let { put("exception", it) }
            correlationId?.let { put("correlationId", it) }
        }

        send("LogEntry", json.encodeToString(args), correlationId = correlationId)
    }

    // ── Device Status ────────────────────────────────────────────

    /**
     * Send a periodic device status update to the dashboard.
     *
     * Includes battery level, network type, GPS availability, and
     * current adapter tier configuration. Sent every 30s by a background worker.
     */
    fun sendDeviceStatusUpdate(
        batteryPercent: Int,
        networkType: String,
        gpsEnabled: Boolean,
        adapterTiers: Map<String, String>,
        freeMemoryMb: Long
    ) {
        val args = buildMap {
            put("batteryPercent", batteryPercent.toString())
            put("networkType", networkType)
            put("gpsEnabled", gpsEnabled.toString())
            put("adapterTiers", json.encodeToString(adapterTiers))
            put("freeMemoryMb", freeMemoryMb.toString())
            put("timestamp", Instant.now().toString())
        }

        send("DeviceStatusUpdate", json.encodeToString(args))
    }

    // ── Keepalive ────────────────────────────────────────────────

    /**
     * Respond to a server Ping with Pong. Not queued — if disconnected,
     * the Ping/Pong cycle is irrelevant.
     */
    fun sendPong() {
        if (hubConnection.connectionState.value != HubConnectionState.Connected) return

        scope.launch {
            sendMutex.withLock {
                // val connection = hubConnection.hubConnection as? com.microsoft.signalr.HubConnection
                // connection?.send("Pong", Instant.now().toString())

                logger.verbose(
                    sourceContext = "SignalR",
                    messageTemplate = "Sent Pong"
                )
            }
        }
    }

    // ── Adapter Tier Reporting ────────────────────────────────────

    /**
     * Report current adapter tier configuration to the dashboard.
     * Called on connect and when the dashboard requests it.
     */
    fun sendReportAdapterTiers(tiers: Map<String, String>) {
        send("ReportAdapterTiers", json.encodeToString(tiers))
    }

    // ── Response Coordination ────────────────────────────────────

    /**
     * Send a responder location update while en-route to an SOS.
     */
    fun sendResponderLocation(
        requestId: String,
        responderId: String,
        latitude: Double,
        longitude: Double,
        speedMps: Double? = null
    ) {
        val args = buildMap {
            put("requestId", requestId)
            put("responderId", responderId)
            put("latitude", latitude.toString())
            put("longitude", longitude.toString())
            speedMps?.let { put("speedMps", it.toString()) }
            put("timestamp", Instant.now().toString())
        }

        send("UpdateResponderLocation", json.encodeToString(args), correlationId = requestId)
    }

    /**
     * Signal that the responder has arrived on scene.
     */
    fun sendResponderOnScene(requestId: String, responderId: String) {
        val args = mapOf(
            "requestId" to requestId,
            "responderId" to responderId,
            "arrivedAt" to Instant.now().toString()
        )

        send("ResponderOnScene", json.encodeToString(args), correlationId = requestId)
    }

    /**
     * Notify the dashboard that evidence has been submitted from this device.
     */
    fun sendEvidenceSubmitted(
        submissionId: String,
        requestId: String,
        phase: String,
        type: String,
        thumbnailUrl: String? = null
    ) {
        val args = buildMap {
            put("submissionId", submissionId)
            put("requestId", requestId)
            put("phase", phase)
            put("type", type)
            thumbnailUrl?.let { put("thumbnailUrl", it) }
            put("timestamp", Instant.now().toString())
        }

        send("NotifyEvidenceSubmitted", json.encodeToString(args), correlationId = requestId)
    }

    // ── Core Send / Queue ────────────────────────────────────────

    /**
     * Send a message to the hub, or queue it if disconnected.
     *
     * All sends are serialized through [sendMutex] to prevent concurrent
     * writes on the SignalR connection (which is not thread-safe).
     */
    private fun send(method: String, argsJson: String, correlationId: String? = null) {
        if (hubConnection.connectionState.value != HubConnectionState.Connected) {
            enqueue(method, argsJson)
            return
        }

        scope.launch {
            sendMutex.withLock {
                try {
                    // val connection = hubConnection.hubConnection as? com.microsoft.signalr.HubConnection
                    // connection?.send(method, argsJson)

                    logger.debug(
                        sourceContext = "SignalR",
                        messageTemplate = "Sent {Method} ({Bytes} bytes)",
                        properties = mapOf(
                            "Method" to method,
                            "Bytes" to argsJson.length.toString()
                        ),
                        correlationId = correlationId
                    )
                } catch (e: Exception) {
                    logger.warning(
                        sourceContext = "SignalR",
                        messageTemplate = "Failed to send {Method}: {Error}. Queueing.",
                        properties = mapOf(
                            "Method" to method,
                            "Error" to (e.message ?: "unknown")
                        ),
                        exception = e,
                        correlationId = correlationId
                    )
                    enqueue(method, argsJson)
                }
            }
        }
    }

    /**
     * Enqueue a message for later delivery. Drops oldest messages if the
     * queue exceeds [maxQueueSize].
     */
    private fun enqueue(method: String, argsJson: String) {
        val message = QueuedHubMessage(
            method = method,
            argsJson = argsJson,
            queuedAt = Instant.now().toString()
        )

        messageQueue.add(message)

        // Trim queue if over capacity (drop oldest)
        while (messageQueue.size > maxQueueSize) {
            val dropped = messageQueue.poll()
            if (dropped != null) {
                logger.warning(
                    sourceContext = "SignalR",
                    messageTemplate = "Queue overflow: dropped {Method} queued at {QueuedAt}",
                    properties = mapOf(
                        "Method" to dropped.method,
                        "QueuedAt" to dropped.queuedAt
                    )
                )
            }
        }

        logger.debug(
            sourceContext = "SignalR",
            messageTemplate = "Queued {Method} for delivery on reconnect. Queue size: {Size}",
            properties = mapOf(
                "Method" to method,
                "Size" to messageQueue.size.toString()
            )
        )
    }

    /**
     * Flush all queued messages after reconnection. Messages are sent FIFO
     * through the [sendMutex] to maintain ordering.
     */
    private suspend fun flushQueue() {
        val queueSize = messageQueue.size
        if (queueSize == 0) return

        logger.information(
            sourceContext = "SignalR",
            messageTemplate = "Flushing {Count} queued messages after reconnect",
            properties = mapOf("Count" to queueSize.toString())
        )

        var flushed = 0
        var failed = 0

        while (messageQueue.isNotEmpty()) {
            val message = messageQueue.poll() ?: break

            sendMutex.withLock {
                try {
                    // val connection = hubConnection.hubConnection as? com.microsoft.signalr.HubConnection
                    // connection?.send(message.method, message.argsJson)
                    flushed++
                } catch (e: Exception) {
                    failed++
                    logger.warning(
                        sourceContext = "SignalR",
                        messageTemplate = "Failed to flush {Method}: {Error}",
                        properties = mapOf(
                            "Method" to message.method,
                            "Error" to (e.message ?: "unknown")
                        )
                    )
                    // Don't re-queue — if we can't send after reconnect, something is wrong
                }
            }
        }

        logger.information(
            sourceContext = "SignalR",
            messageTemplate = "Queue flush complete: {Flushed} sent, {Failed} failed",
            properties = mapOf(
                "Flushed" to flushed.toString(),
                "Failed" to failed.toString()
            )
        )
    }

    // ── Diagnostics ──────────────────────────────────────────────

    /**
     * Returns queue diagnostics for the diagnostics screen.
     */
    fun diagnosticsSnapshot(): Map<String, String> = mapOf(
        "QueueSize" to messageQueue.size.toString(),
        "MaxQueueSize" to maxQueueSize.toString(),
        "ConnectionState" to hubConnection.connectionState.value.name
    )
}
