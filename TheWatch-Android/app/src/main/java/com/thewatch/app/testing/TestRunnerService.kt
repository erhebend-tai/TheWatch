package com.thewatch.app.testing

import com.thewatch.app.data.logging.WatchLogger
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.time.Instant
import javax.inject.Inject
import javax.inject.Singleton

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: TestRunnerService receives test step commands from the SignalR hub
// (routed through SignalRAdapterSync or a dedicated test hub listener),
// executes them sequentially via the TestStepExecutorRegistry, and reports
// results back via the outbound SignalR channel.
//
// Flow:
// 1. Dashboard calls StartRunAsync → hub sends "ExecuteTestStep" with (runId, step) to device group
// 2. TestRunnerService.onStepReceived(runId, step) is called by the SignalR message router
// 3. Service looks up the executor for step.action from TestStepExecutorRegistry
// 4. Executor.execute(step, context) runs against the live app
// 5. Result is recorded locally and sent back via hubEventSender.reportStepResult(...)
// 6. Dashboard's RecordStepResultAsync dispatches the next step (or completes the run)
//
// If Agent 4's SignalR client isn't merged yet, the service defines the
// HubMessageRouter and HubEventSender interfaces and stubs them.
//
// Example:
// ```kotlin
// @Inject lateinit var testRunner: TestRunnerService
//
// // Called by SignalR message router when "ExecuteTestStep" arrives
// testRunner.onStepReceived(runId = "run_abc123", step = testStep)
//
// // Observe current run state
// testRunner.runState.collect { state -> updateNotification(state) }
// ```

/**
 * Port interface for receiving test commands from the SignalR hub.
 * Agent 4's SignalR client implements this to route "ExecuteTestStep" messages.
 * If Agent 4 isn't merged yet, use [MockHubMessageRouter].
 */
interface HubMessageRouter {
    /**
     * Register a handler for incoming test step execution commands.
     * The handler receives (runId, step) pairs dispatched by the dashboard.
     */
    fun onTestStepReceived(handler: suspend (runId: String, step: TestStep) -> Unit)

    /**
     * Register a handler for test run cancellation commands.
     */
    fun onTestRunCancelled(handler: suspend (runId: String) -> Unit)
}

/**
 * Port interface for sending test results back to the SignalR hub.
 * Agent 4's SignalR client implements this to send "ReportTestStepResult" messages.
 * If Agent 4 isn't merged yet, use [MockHubEventSender].
 */
interface HubEventSender {
    /**
     * Report the result of a completed test step back to the dashboard.
     * Maps to calling "ReportTestStepResult" on the SignalR hub.
     */
    suspend fun reportStepResult(
        runId: String,
        stepId: String,
        passed: Boolean,
        screenshot: String? = null,
        errorMessage: String? = null
    )

    /**
     * Report the overall run status to the dashboard.
     * Maps to calling "ReportTestRunStatus" on the SignalR hub.
     */
    suspend fun reportRunStatus(runState: TestRunState)
}

/**
 * Core service that receives test steps from the MAUI dashboard via SignalR,
 * executes them against the live application, and reports results back.
 *
 * Designed for sequential step execution — each step completes before the next begins.
 * The dashboard controls step sequencing; this service just executes what arrives.
 *
 * Exposes [runState] as a StateFlow for the notification and UI to observe.
 */
@Singleton
class TestRunnerService @Inject constructor(
    private val executorRegistry: TestStepExecutorRegistry,
    private val logger: WatchLogger
) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)

    private val _runState = MutableStateFlow<TestRunState?>(null)
    /** Observable state of the current test run. Null when idle. */
    val runState: StateFlow<TestRunState?> = _runState.asStateFlow()

    private var hubEventSender: HubEventSender? = null
    private var executionContext: TestExecutionContext? = null
    private var executionJob: Job? = null

    /**
     * Initialize the service with the hub communication interfaces and execution context.
     * Called during app startup after DI and navigation are ready.
     *
     * @param router The SignalR message router (or mock) that delivers test steps
     * @param sender The SignalR event sender (or mock) that reports results
     * @param context The execution context providing access to navigation, UI queries, and SOS pipeline
     */
    fun initialize(
        router: HubMessageRouter,
        sender: HubEventSender,
        context: TestExecutionContext
    ) {
        this.hubEventSender = sender
        this.executionContext = context

        router.onTestStepReceived { runId, step ->
            onStepReceived(runId, step)
        }

        router.onTestRunCancelled { runId ->
            onRunCancelled(runId)
        }

        logger.information(
            sourceContext = "TestRunnerService",
            messageTemplate = "Test runner initialized. Supported actions: {Actions}",
            properties = mapOf("Actions" to executorRegistry.supportedActions().joinToString())
        )
    }

    /**
     * Handle an incoming test step from the dashboard.
     * Creates a new run state if this is the first step, then executes.
     */
    suspend fun onStepReceived(runId: String, step: TestStep) {
        val ctx = executionContext ?: run {
            logger.error(
                sourceContext = "TestRunnerService",
                messageTemplate = "Received step {StepId} but execution context is not initialized",
                properties = mapOf("StepId" to step.id)
            )
            return
        }

        // Initialize run state if this is a new run
        val currentState = _runState.value
        if (currentState == null || currentState.runId != runId) {
            _runState.value = TestRunState(
                runId = runId,
                currentStep = step,
                startedAt = Instant.now()
            )
        } else {
            _runState.value = currentState.copy(currentStep = step)
        }

        logger.information(
            sourceContext = "TestRunnerService",
            messageTemplate = "Executing step {StepId} ({Order}): {Action} on {Screen} target='{Target}'",
            properties = mapOf(
                "StepId" to step.id,
                "Order" to step.order.toString(),
                "Action" to step.action,
                "Screen" to step.screenName,
                "Target" to step.target,
                "RunId" to runId
            )
        )

        // Cancel any in-flight execution (shouldn't happen with sequential dispatch, but safety)
        executionJob?.cancel()

        executionJob = scope.launch {
            val result = executeStep(step, ctx)
            recordResult(runId, result)
        }
    }

    /**
     * Execute a single step using the appropriate executor.
     */
    private suspend fun executeStep(step: TestStep, context: TestExecutionContext): TestStepResult {
        val executor = executorRegistry.getExecutor(step.action)

        if (executor == null) {
            logger.error(
                sourceContext = "TestRunnerService",
                messageTemplate = "No executor registered for action '{Action}'. Skipping step {StepId}.",
                properties = mapOf("Action" to step.action, "StepId" to step.id)
            )
            return step.toResult(
                passed = false,
                durationMs = 0,
                errorMessage = "Unsupported action: '${step.action}'. Supported: ${executorRegistry.supportedActions()}"
            )
        }

        return try {
            executor.execute(step, context)
        } catch (e: CancellationException) {
            throw e // Don't catch coroutine cancellation
        } catch (e: Exception) {
            logger.error(
                sourceContext = "TestRunnerService",
                messageTemplate = "Step {StepId} threw exception: {Error}",
                properties = mapOf("StepId" to step.id, "Error" to (e.message ?: "unknown")),
                exception = e
            )
            step.toResult(
                passed = false,
                durationMs = 0,
                errorMessage = "FATAL: ${e::class.simpleName}: ${e.message}"
            )
        }
    }

    /**
     * Record a step result locally and report it back to the dashboard.
     */
    private suspend fun recordResult(runId: String, result: TestStepResult) {
        val state = _runState.value ?: return

        state.results.add(result)
        _runState.value = state.copy(
            completedSteps = state.results.size,
            passedSteps = state.results.count { it.passed },
            failedSteps = state.results.count { !it.passed },
            currentStep = null
        )

        logger.information(
            sourceContext = "TestRunnerService",
            messageTemplate = "Step {StepId} {Result} in {DurationMs}ms — run {RunId} [{Completed} done]",
            properties = mapOf(
                "StepId" to result.stepId,
                "Result" to if (result.passed) "PASS" else "FAIL",
                "DurationMs" to result.durationMs.toString(),
                "RunId" to runId,
                "Completed" to state.results.size.toString()
            )
        )

        // Report back to dashboard
        try {
            hubEventSender?.reportStepResult(
                runId = runId,
                stepId = result.stepId,
                passed = result.passed,
                screenshot = result.screenshot,
                errorMessage = result.errorMessage
            )
        } catch (e: Exception) {
            logger.error(
                sourceContext = "TestRunnerService",
                messageTemplate = "Failed to report step result to dashboard: {Error}",
                properties = mapOf("Error" to (e.message ?: "unknown"))
            )
        }
    }

    /**
     * Handle run cancellation from the dashboard.
     */
    private fun onRunCancelled(runId: String) {
        val state = _runState.value ?: return
        if (state.runId != runId) return

        executionJob?.cancel()
        _runState.value = state.copy(status = TestRunStatus.Cancelled)

        logger.warning(
            sourceContext = "TestRunnerService",
            messageTemplate = "Test run {RunId} cancelled by dashboard",
            properties = mapOf("RunId" to runId)
        )
    }

    /**
     * Tear down the service. Called on app shutdown.
     */
    fun shutdown() {
        executionJob?.cancel()
        scope.cancel()
        _runState.value = null
        logger.information(
            sourceContext = "TestRunnerService",
            messageTemplate = "Test runner shut down"
        )
    }
}
