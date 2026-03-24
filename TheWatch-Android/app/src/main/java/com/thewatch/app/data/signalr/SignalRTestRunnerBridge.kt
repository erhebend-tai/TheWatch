package com.thewatch.app.data.signalr

import com.thewatch.app.data.logging.WatchLogger
import com.thewatch.app.testing.HubEventSender as TestHubEventSender
import com.thewatch.app.testing.HubMessageRouter as TestHubMessageRouter
import com.thewatch.app.testing.TestStep
import com.thewatch.app.testing.TestRunState
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.intOrNull
import javax.inject.Inject
import javax.inject.Singleton

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: SignalRTestRunnerBridge connects Agent 4's SignalR client (WatchHubConnection,
// HubMessageRouter, HubEventSender) to Agent 5's test runner (TestRunnerService).
//
// The test runner defines its own port interfaces (HubMessageRouter, HubEventSender)
// to allow development without a live SignalR connection. This bridge implements those
// interfaces using the real SignalR client, so when both agents are merged the test
// runner automatically routes through SignalR.
//
// Architecture:
//   - Implements testing.HubMessageRouter by registering as a TestRunnerCallback
//     on the signalr.HubMessageRouter
//   - Implements testing.HubEventSender by delegating to signalr.HubEventSender
//   - Parses JSON payloads from SignalR into TestStep objects
//   - Serializes TestStepResult back to SignalR-compatible format
//
// Example:
// ```kotlin
// @Inject lateinit var bridge: SignalRTestRunnerBridge
//
// // Use as the router/sender for TestRunnerService
// testRunnerService.initialize(
//     router = bridge,
//     sender = bridge,
//     context = executionContext
// )
// ```

/**
 * Bridges Agent 4's SignalR client to Agent 5's test runner.
 *
 * Implements both [TestHubMessageRouter] and [TestHubEventSender] by
 * delegating to the real SignalR [HubMessageRouter] and [HubEventSender].
 *
 * When this bridge is used, the test runner receives steps from the dashboard
 * in real-time and reports results back over SignalR — no mocks needed.
 */
@Singleton
class SignalRTestRunnerBridge @Inject constructor(
    private val signalRRouter: HubMessageRouter,
    private val signalRSender: HubEventSender,
    private val logger: WatchLogger
) : TestHubMessageRouter, TestHubEventSender, TestRunnerCallback {

    private val json = Json { ignoreUnknownKeys = true; isLenient = true }
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    private var stepHandler: (suspend (String, TestStep) -> Unit)? = null
    private var cancelHandler: (suspend (String) -> Unit)? = null

    init {
        // Register this bridge as the test runner callback on the SignalR router
        signalRRouter.registerTestRunnerCallback(this)
        logger.information(
            sourceContext = "SignalRTestRunnerBridge",
            messageTemplate = "Bridge initialized — SignalR ↔ TestRunner connected"
        )
    }

    // ── TestHubMessageRouter (Agent 5's interface) ───────────────

    override fun onTestStepReceived(handler: suspend (runId: String, step: TestStep) -> Unit) {
        stepHandler = handler
    }

    override fun onTestRunCancelled(handler: suspend (runId: String) -> Unit) {
        cancelHandler = handler
    }

    // ── TestRunnerCallback (Agent 4's interface) ─────────────────

    override fun onExecuteTestStep(runId: String, stepJson: String) {
        val step = parseTestStep(stepJson) ?: run {
            logger.error(
                sourceContext = "SignalRTestRunnerBridge",
                messageTemplate = "Failed to parse TestStep JSON for run {RunId}: {Json}",
                properties = mapOf("RunId" to runId, "Json" to stepJson.take(200)),
                correlationId = runId
            )
            return
        }

        // Dispatch to the test runner's handler on a coroutine
        scope.launch {
            stepHandler?.invoke(runId, step)
        }
    }

    override fun onTestRunStarted(runJson: String) {
        logger.debug(
            sourceContext = "SignalRTestRunnerBridge",
            messageTemplate = "Test run started (broadcast): {Preview}",
            properties = mapOf("Preview" to runJson.take(100))
        )
    }

    override fun onTestStepCompleted(runId: String, resultJson: String) {
        logger.debug(
            sourceContext = "SignalRTestRunnerBridge",
            messageTemplate = "Step completed (broadcast) for run {RunId}",
            properties = mapOf("RunId" to runId),
            correlationId = runId
        )
    }

    override fun onTestRunCompleted(runJson: String) {
        logger.debug(
            sourceContext = "SignalRTestRunnerBridge",
            messageTemplate = "Test run completed (broadcast): {Preview}",
            properties = mapOf("Preview" to runJson.take(100))
        )
    }

    // ── TestHubEventSender (Agent 5's interface) ─────────────────

    override suspend fun reportStepResult(
        runId: String,
        stepId: String,
        passed: Boolean,
        screenshot: String?,
        errorMessage: String?
    ) {
        signalRSender.sendTestStepCompleted(
            runId = runId,
            stepId = stepId,
            passed = passed,
            screenshot = screenshot,
            errorMessage = errorMessage
        )
    }

    override suspend fun reportRunStatus(runState: TestRunState) {
        // Send device status update with run info encoded
        logger.information(
            sourceContext = "SignalRTestRunnerBridge",
            messageTemplate = "Reporting run status: {RunId} {Status} [{Completed}/{Total}]",
            properties = mapOf(
                "RunId" to runState.runId,
                "Status" to runState.status.name,
                "Completed" to runState.completedSteps.toString(),
                "Total" to runState.totalSteps.toString()
            )
        )
    }

    // ── JSON Parsing ─────────────────────────────────────────────

    /**
     * Parse a TestStep from the JSON payload sent by the SignalR hub.
     *
     * Expected format matches TestOrchestratorService.cs:
     * {
     *   "Id": "step_001",
     *   "Order": 1,
     *   "ScreenName": "LoginScreen",
     *   "Action": "Navigate",
     *   "Target": "/login",
     *   "Value": null
     * }
     */
    private fun parseTestStep(jsonStr: String): TestStep? {
        return try {
            val obj = json.parseToJsonElement(jsonStr).jsonObject
            TestStep(
                id = obj["Id"]?.jsonPrimitive?.content ?: return null,
                order = obj["Order"]?.jsonPrimitive?.intOrNull ?: return null,
                screenName = obj["ScreenName"]?.jsonPrimitive?.content ?: return null,
                action = obj["Action"]?.jsonPrimitive?.content ?: return null,
                target = obj["Target"]?.jsonPrimitive?.content ?: return null,
                value = obj["Value"]?.jsonPrimitive?.content
            )
        } catch (e: Exception) {
            logger.warning(
                sourceContext = "SignalRTestRunnerBridge",
                messageTemplate = "JSON parse error: {Error}",
                properties = mapOf("Error" to (e.message ?: "unknown"))
            )
            null
        }
    }
}
