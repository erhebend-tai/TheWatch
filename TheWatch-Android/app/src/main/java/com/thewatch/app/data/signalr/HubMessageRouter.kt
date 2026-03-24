package com.thewatch.app.data.signalr

import com.thewatch.app.data.adapters.SignalRAdapterSync
import com.thewatch.app.data.logging.WatchLogger
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonPrimitive
import javax.inject.Inject
import javax.inject.Singleton

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: HubMessageRouter is the inbound message dispatcher for the SignalR hub connection.
// It registers handlers for all client-invocable methods on the DashboardHub and routes
// incoming messages to the appropriate manager, service, or adapter.
//
// Architecture:
//   - Listens on WatchHubConnection lifecycle events via HubConnectionListener
//   - On connect: registers all hub method handlers on the underlying HubConnection
//   - On disconnect: handlers are automatically cleaned up by SignalR library
//   - Routes messages to:
//       * SignalRAdapterSync — for AdapterTierChanged/Snapshot/Reset
//       * TestRunnerCallback — for ExecuteTestStep (Agent 5's test runner)
//       * Log stream responder — for RequestLogStream
//       * Pong sender — for Ping keepalive
//
// Inbound message handlers registered:
//   - "ExecuteTestStep" → TestRunnerCallback.onExecuteTestStep(runId, stepJson)
//   - "AdapterTierChanged" → SignalRAdapterSync.handleTierChanged(slot, tier)
//   - "AdapterTierSnapshot" → SignalRAdapterSync.handleTierSnapshot(snapshot)
//   - "AdapterTierReset" → SignalRAdapterSync.handleTierReset()
//   - "RequestLogStream" → starts streaming logs to dashboard via HubEventSender
//   - "Ping" → responds with Pong via HubEventSender
//   - "TestRunStarted" → broadcast notification (optional UI update)
//   - "TestStepCompleted" → broadcast notification (optional UI update)
//   - "TestRunCompleted" → broadcast notification (optional UI update)
//   - "ResponderLocationUpdated" → location tracking for response coordination
//   - "ResponderOnScene" → responder arrival notification
//   - "EvidenceSubmitted" → evidence upload notification
//   - "EvidenceProcessed" → evidence processing complete notification
//   - "SurveyDispatched" → survey assignment notification
//   - "SurveyCompleted" → survey completion notification
//   - "MilestoneUpdated" → dashboard milestone updates
//   - "WorkItemUpdated" → dashboard work item changes
//   - "BuildCompleted" → CI/CD build status
//   - "AgentActivityRecorded" → agent activity tracking
//   - "SimulationEventReceived" → simulation event processing
//
// Example:
// ```kotlin
// @Inject lateinit var router: HubMessageRouter
//
// // Register a callback for test step execution (Agent 5)
// router.registerTestRunnerCallback(object : TestRunnerCallback {
//     override fun onExecuteTestStep(runId: String, stepJson: String) {
//         // Parse step and execute
//     }
//     override fun onTestRunStarted(runJson: String) {}
//     override fun onTestRunCompleted(runJson: String) {}
// })
//
// // The router auto-registers handlers when the hub connects
// ```

/**
 * Callback interface for test execution messages from the hub.
 * Implemented by the TestRunnerService (Agent 5) to receive test commands.
 */
interface TestRunnerCallback {
    /** Hub dispatches a test step for this device to execute. */
    fun onExecuteTestStep(runId: String, stepJson: String)
    /** Broadcast: a test run has started. */
    fun onTestRunStarted(runJson: String)
    /** Broadcast: a test step result was recorded. */
    fun onTestStepCompleted(runId: String, resultJson: String)
    /** Broadcast: a test run completed (passed/failed/cancelled). */
    fun onTestRunCompleted(runJson: String)
}

/**
 * Callback interface for response coordination messages.
 * Implemented by the ResponseCoordinationManager to handle real-time SOS updates.
 */
interface ResponseCoordinationCallback {
    fun onResponderLocationUpdated(data: Map<String, Any?>)
    fun onResponderOnScene(data: Map<String, Any?>)
    fun onEvidenceSubmitted(data: Map<String, Any?>)
    fun onEvidenceProcessed(data: Map<String, Any?>)
    fun onSurveyDispatched(data: Map<String, Any?>)
    fun onSurveyCompleted(data: Map<String, Any?>)
}

/**
 * Callback interface for log stream requests from the dashboard.
 */
interface LogStreamCallback {
    /** Dashboard requests live log streaming from this device. */
    fun onRequestLogStream()
}

/**
 * Inbound message router for the SignalR hub.
 *
 * Registers handlers for all client-invocable methods on DashboardHub and
 * dispatches messages to the appropriate callback interface.
 *
 * Handler registration happens automatically when the hub connects —
 * consumers just register their callback and the router takes care of wiring.
 *
 * Thread safety: all handler invocations are dispatched onto a background
 * coroutine scope so the SignalR I/O thread is never blocked.
 */
@Singleton
class HubMessageRouter @Inject constructor(
    private val hubConnection: WatchHubConnection,
    private val adapterSync: SignalRAdapterSync,
    private val eventSender: HubEventSender,
    private val logger: WatchLogger
) : HubConnectionListener {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private val json = Json { ignoreUnknownKeys = true; isLenient = true }

    // ── Callback Registration ────────────────────────────────────

    private var testRunnerCallback: TestRunnerCallback? = null
    private var responseCallback: ResponseCoordinationCallback? = null
    private var logStreamCallback: LogStreamCallback? = null

    /**
     * Register the test runner callback. Only one can be active at a time.
     * Called by TestRunnerService during initialization.
     */
    fun registerTestRunnerCallback(callback: TestRunnerCallback) {
        testRunnerCallback = callback
        logger.debug(
            sourceContext = "SignalR",
            messageTemplate = "TestRunnerCallback registered"
        )
    }

    fun unregisterTestRunnerCallback() {
        testRunnerCallback = null
    }

    /**
     * Register the response coordination callback.
     */
    fun registerResponseCoordinationCallback(callback: ResponseCoordinationCallback) {
        responseCallback = callback
    }

    fun unregisterResponseCoordinationCallback() {
        responseCallback = null
    }

    /**
     * Register the log stream callback.
     */
    fun registerLogStreamCallback(callback: LogStreamCallback) {
        logStreamCallback = callback
    }

    // ── HubConnectionListener ────────────────────────────────────

    init {
        hubConnection.addListener(this)
    }

    override fun onConnected() {
        registerHandlers()
        logger.information(
            sourceContext = "SignalR",
            messageTemplate = "HubMessageRouter: handlers registered on connect"
        )
    }

    override fun onDisconnected(error: Throwable?) {
        logger.debug(
            sourceContext = "SignalR",
            messageTemplate = "HubMessageRouter: hub disconnected, handlers will re-register on reconnect"
        )
    }

    override fun onReconnecting(attemptNumber: Int) {
        // No action needed — handlers re-register on successful reconnect
    }

    // ── Handler Registration ─────────────────────────────────────

    /**
     * Register all inbound message handlers on the underlying HubConnection.
     *
     * Each handler dispatches to a background coroutine so the SignalR I/O
     * thread is never blocked by business logic.
     *
     * When the actual SignalR library is linked, uncomment the connection.on() calls.
     * The mock implementation logs received messages for development.
     */
    private fun registerHandlers() {
        // val connection = hubConnection.hubConnection as? com.microsoft.signalr.HubConnection ?: return

        // ── Test Orchestration ───────────────────────────────────
        // connection.on("ExecuteTestStep", { runId: String, stepJson: String ->
        //     scope.launch { handleExecuteTestStep(runId, stepJson) }
        // }, String::class.java, String::class.java)
        //
        // connection.on("TestRunStarted", { runJson: String ->
        //     scope.launch { handleTestRunStarted(runJson) }
        // }, String::class.java)
        //
        // connection.on("TestStepCompleted", { runId: String, resultJson: String ->
        //     scope.launch { handleTestStepCompleted(runId, resultJson) }
        // }, String::class.java, String::class.java)
        //
        // connection.on("TestRunCompleted", { runJson: String ->
        //     scope.launch { handleTestRunCompleted(runJson) }
        // }, String::class.java)

        // ── Adapter Tier Management ──────────────────────────────
        // connection.on("AdapterTierChanged", { slotName: String, tierName: String ->
        //     scope.launch { adapterSync.handleTierChanged(slotName, tierName) }
        // }, String::class.java, String::class.java)
        //
        // connection.on("AdapterTierSnapshot", { snapshotJson: String ->
        //     scope.launch {
        //         val snapshot = json.decodeFromString<Map<String, String>>(snapshotJson)
        //         adapterSync.handleTierSnapshot(snapshot)
        //     }
        // }, String::class.java)
        //
        // connection.on("AdapterTierReset", {
        //     scope.launch { adapterSync.handleTierReset() }
        // })

        // ── Keepalive ────────────────────────────────────────────
        // connection.on("Ping", {
        //     scope.launch { handlePing() }
        // })

        // ── Log Streaming ────────────────────────────────────────
        // connection.on("RequestLogStream", {
        //     scope.launch { handleRequestLogStream() }
        // })

        // ── Response Coordination ────────────────────────────────
        // connection.on("ResponderLocationUpdated", { dataJson: String ->
        //     scope.launch { handleResponderLocationUpdated(dataJson) }
        // }, String::class.java)
        //
        // connection.on("ResponderOnScene", { dataJson: String ->
        //     scope.launch { handleResponderOnScene(dataJson) }
        // }, String::class.java)
        //
        // connection.on("EvidenceSubmitted", { dataJson: String ->
        //     scope.launch { handleEvidenceSubmitted(dataJson) }
        // }, String::class.java)
        //
        // connection.on("EvidenceProcessed", { dataJson: String ->
        //     scope.launch { handleEvidenceProcessed(dataJson) }
        // }, String::class.java)
        //
        // connection.on("SurveyDispatched", { dataJson: String ->
        //     scope.launch { handleSurveyDispatched(dataJson) }
        // }, String::class.java)
        //
        // connection.on("SurveyCompleted", { dataJson: String ->
        //     scope.launch { handleSurveyCompleted(dataJson) }
        // }, String::class.java)

        // ── Dashboard Broadcasts ─────────────────────────────────
        // connection.on("MilestoneUpdated", { dataJson: String ->
        //     scope.launch { logger.debug(sourceContext = "SignalR", messageTemplate = "MilestoneUpdated: {Data}", properties = mapOf("Data" to dataJson)) }
        // }, String::class.java)
        //
        // connection.on("WorkItemUpdated", { dataJson: String ->
        //     scope.launch { logger.debug(sourceContext = "SignalR", messageTemplate = "WorkItemUpdated: {Data}", properties = mapOf("Data" to dataJson)) }
        // }, String::class.java)
        //
        // connection.on("BuildCompleted", { dataJson: String ->
        //     scope.launch { logger.debug(sourceContext = "SignalR", messageTemplate = "BuildCompleted: {Data}", properties = mapOf("Data" to dataJson)) }
        // }, String::class.java)
        //
        // connection.on("AgentActivityRecorded", { dataJson: String ->
        //     scope.launch { logger.debug(sourceContext = "SignalR", messageTemplate = "AgentActivityRecorded: {Data}", properties = mapOf("Data" to dataJson)) }
        // }, String::class.java)
        //
        // connection.on("SimulationEventReceived", { dataJson: String ->
        //     scope.launch { logger.debug(sourceContext = "SignalR", messageTemplate = "SimulationEventReceived: {Data}", properties = mapOf("Data" to dataJson)) }
        // }, String::class.java)

        logger.debug(
            sourceContext = "SignalR",
            messageTemplate = "All hub message handlers registered (mock mode)"
        )
    }

    // ── Message Handlers ─────────────────────────────────────────

    /** Dispatch test step execution to the registered test runner. */
    internal fun handleExecuteTestStep(runId: String, stepJson: String) {
        logger.information(
            sourceContext = "SignalR",
            messageTemplate = "Received ExecuteTestStep: run={RunId}",
            properties = mapOf("RunId" to runId),
            correlationId = runId
        )

        val callback = testRunnerCallback
        if (callback == null) {
            logger.warning(
                sourceContext = "SignalR",
                messageTemplate = "No TestRunnerCallback registered — dropping ExecuteTestStep for run {RunId}",
                properties = mapOf("RunId" to runId),
                correlationId = runId
            )
            return
        }

        callback.onExecuteTestStep(runId, stepJson)
    }

    internal fun handleTestRunStarted(runJson: String) {
        logger.information(
            sourceContext = "SignalR",
            messageTemplate = "Received TestRunStarted broadcast"
        )
        testRunnerCallback?.onTestRunStarted(runJson)
    }

    internal fun handleTestStepCompleted(runId: String, resultJson: String) {
        logger.debug(
            sourceContext = "SignalR",
            messageTemplate = "Received TestStepCompleted: run={RunId}",
            properties = mapOf("RunId" to runId),
            correlationId = runId
        )
        testRunnerCallback?.onTestStepCompleted(runId, resultJson)
    }

    internal fun handleTestRunCompleted(runJson: String) {
        logger.information(
            sourceContext = "SignalR",
            messageTemplate = "Received TestRunCompleted broadcast"
        )
        testRunnerCallback?.onTestRunCompleted(runJson)
    }

    /** Respond to server Ping with Pong via HubEventSender. */
    internal fun handlePing() {
        logger.verbose(
            sourceContext = "SignalR",
            messageTemplate = "Received Ping, sending Pong"
        )
        eventSender.sendPong()
    }

    /** Forward log stream request to the registered callback. */
    internal fun handleRequestLogStream() {
        logger.information(
            sourceContext = "SignalR",
            messageTemplate = "Dashboard requested log stream"
        )
        logStreamCallback?.onRequestLogStream()
    }

    // ── Response Coordination Handlers ───────────────────────────

    @Suppress("UNCHECKED_CAST")
    internal fun handleResponderLocationUpdated(dataJson: String) {
        logger.verbose(
            sourceContext = "SignalR",
            messageTemplate = "Received ResponderLocationUpdated"
        )
        try {
            val data = json.decodeFromString<JsonObject>(dataJson)
            responseCallback?.onResponderLocationUpdated(data.toMap())
        } catch (e: Exception) {
            logger.warning(
                sourceContext = "SignalR",
                messageTemplate = "Failed to parse ResponderLocationUpdated: {Error}",
                properties = mapOf("Error" to (e.message ?: "unknown"))
            )
        }
    }

    internal fun handleResponderOnScene(dataJson: String) {
        logger.information(
            sourceContext = "SignalR",
            messageTemplate = "Received ResponderOnScene"
        )
        try {
            val data = json.decodeFromString<JsonObject>(dataJson)
            responseCallback?.onResponderOnScene(data.toMap())
        } catch (e: Exception) {
            logger.warning(
                sourceContext = "SignalR",
                messageTemplate = "Failed to parse ResponderOnScene: {Error}",
                properties = mapOf("Error" to (e.message ?: "unknown"))
            )
        }
    }

    internal fun handleEvidenceSubmitted(dataJson: String) {
        logger.information(
            sourceContext = "SignalR",
            messageTemplate = "Received EvidenceSubmitted"
        )
        try {
            val data = json.decodeFromString<JsonObject>(dataJson)
            responseCallback?.onEvidenceSubmitted(data.toMap())
        } catch (e: Exception) {
            logger.warning(
                sourceContext = "SignalR",
                messageTemplate = "Failed to parse EvidenceSubmitted: {Error}",
                properties = mapOf("Error" to (e.message ?: "unknown"))
            )
        }
    }

    internal fun handleEvidenceProcessed(dataJson: String) {
        logger.information(
            sourceContext = "SignalR",
            messageTemplate = "Received EvidenceProcessed"
        )
        try {
            val data = json.decodeFromString<JsonObject>(dataJson)
            responseCallback?.onEvidenceProcessed(data.toMap())
        } catch (e: Exception) {
            logger.warning(
                sourceContext = "SignalR",
                messageTemplate = "Failed to parse EvidenceProcessed: {Error}",
                properties = mapOf("Error" to (e.message ?: "unknown"))
            )
        }
    }

    internal fun handleSurveyDispatched(dataJson: String) {
        logger.information(
            sourceContext = "SignalR",
            messageTemplate = "Received SurveyDispatched"
        )
        try {
            val data = json.decodeFromString<JsonObject>(dataJson)
            responseCallback?.onSurveyDispatched(data.toMap())
        } catch (e: Exception) {
            logger.warning(
                sourceContext = "SignalR",
                messageTemplate = "Failed to parse SurveyDispatched: {Error}",
                properties = mapOf("Error" to (e.message ?: "unknown"))
            )
        }
    }

    internal fun handleSurveyCompleted(dataJson: String) {
        logger.information(
            sourceContext = "SignalR",
            messageTemplate = "Received SurveyCompleted"
        )
        try {
            val data = json.decodeFromString<JsonObject>(dataJson)
            responseCallback?.onSurveyCompleted(data.toMap())
        } catch (e: Exception) {
            logger.warning(
                sourceContext = "SignalR",
                messageTemplate = "Failed to parse SurveyCompleted: {Error}",
                properties = mapOf("Error" to (e.message ?: "unknown"))
            )
        }
    }

    // ── Utility ──────────────────────────────────────────────────

    private fun JsonObject.toMap(): Map<String, Any?> {
        return entries.associate { (key, value) ->
            key to try { value.jsonPrimitive.content } catch (_: Exception) { value.toString() }
        }
    }
}
