package com.thewatch.app.testing

import com.thewatch.app.data.logging.WatchLogger
import kotlinx.coroutines.delay
import javax.inject.Inject
import javax.inject.Singleton

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: Mock implementations of HubMessageRouter and HubEventSender for use
// when Agent 4's SignalR client isn't merged yet. Also includes
// MockTestExecutionContext that simulates UI interactions by always succeeding.
//
// These mocks enable the test runner to be exercised end-to-end without
// requiring a real SignalR connection or Compose testing framework.
//
// When Agent 4 merges:
//   1. Replace MockHubMessageRouter with a real router that listens on
//      hubConnection.on("ExecuteTestStep", ...) and hubConnection.on("CancelTestRun", ...)
//   2. Replace MockHubEventSender with a real sender that calls
//      hubConnection.send("ReportTestStepResult", ...) and hubConnection.send("ReportTestRunStatus", ...)
//
// When Compose UI test integration is ready:
//   Replace MockTestExecutionContext with a real context that uses
//   ComposeTestRule or UiAutomator to find/tap/type/assert UI elements.
//
// Example:
// ```kotlin
// // Development mode — mock everything
// val router = MockHubMessageRouter(logger)
// val sender = MockHubEventSender(logger)
// val context = MockTestExecutionContext.create(logger)
// testRunnerService.initialize(router, sender, context)
//
// // Simulate receiving a step (for local testing)
// router.simulateStep("run_123", TestStep("step_001", 1, "LoginScreen", "Navigate", "/login"))
// ```

/**
 * Mock [HubMessageRouter] that stores handlers and allows manual step injection.
 * Use [simulateStep] and [simulateCancel] to test the runner without SignalR.
 */
@Singleton
class MockHubMessageRouter @Inject constructor(
    private val logger: WatchLogger
) : HubMessageRouter {
    private var stepHandler: (suspend (String, TestStep) -> Unit)? = null
    private var cancelHandler: (suspend (String) -> Unit)? = null

    override fun onTestStepReceived(handler: suspend (runId: String, step: TestStep) -> Unit) {
        stepHandler = handler
        logger.debug(
            sourceContext = "MockHubMessageRouter",
            messageTemplate = "Test step handler registered"
        )
    }

    override fun onTestRunCancelled(handler: suspend (runId: String) -> Unit) {
        cancelHandler = handler
        logger.debug(
            sourceContext = "MockHubMessageRouter",
            messageTemplate = "Test cancel handler registered"
        )
    }

    /**
     * Simulate receiving an "ExecuteTestStep" message from the dashboard.
     * Used for local development and testing without a live SignalR connection.
     */
    suspend fun simulateStep(runId: String, step: TestStep) {
        logger.information(
            sourceContext = "MockHubMessageRouter",
            messageTemplate = "Simulating step dispatch: {StepId} for run {RunId}",
            properties = mapOf("StepId" to step.id, "RunId" to runId)
        )
        stepHandler?.invoke(runId, step)
    }

    /**
     * Simulate receiving a "CancelTestRun" message from the dashboard.
     */
    suspend fun simulateCancel(runId: String) {
        logger.information(
            sourceContext = "MockHubMessageRouter",
            messageTemplate = "Simulating run cancellation: {RunId}",
            properties = mapOf("RunId" to runId)
        )
        cancelHandler?.invoke(runId)
    }
}

/**
 * Mock [HubEventSender] that logs results instead of sending them over SignalR.
 */
@Singleton
class MockHubEventSender @Inject constructor(
    private val logger: WatchLogger
) : HubEventSender {

    /** All results reported during the session, for inspection/debugging. */
    val reportedResults = mutableListOf<ReportedResult>()

    data class ReportedResult(
        val runId: String,
        val stepId: String,
        val passed: Boolean,
        val screenshot: String?,
        val errorMessage: String?
    )

    override suspend fun reportStepResult(
        runId: String,
        stepId: String,
        passed: Boolean,
        screenshot: String?,
        errorMessage: String?
    ) {
        val result = ReportedResult(runId, stepId, passed, screenshot, errorMessage)
        reportedResults.add(result)

        logger.information(
            sourceContext = "MockHubEventSender",
            messageTemplate = "Reported step result: {StepId} {Result} for run {RunId}",
            properties = mapOf(
                "StepId" to stepId,
                "Result" to if (passed) "PASS" else "FAIL",
                "RunId" to runId,
                "Error" to (errorMessage ?: "none")
            )
        )
    }

    override suspend fun reportRunStatus(runState: TestRunState) {
        logger.information(
            sourceContext = "MockHubEventSender",
            messageTemplate = "Reported run status: {RunId} {Status} [{Completed}/{Total}]",
            properties = mapOf(
                "RunId" to runState.runId,
                "Status" to runState.status.name,
                "Completed" to runState.completedSteps.toString(),
                "Total" to runState.totalSteps.toString()
            )
        )
    }
}

/**
 * Factory for creating a mock [TestExecutionContext] that simulates all UI interactions.
 * All operations succeed with synthetic delays mimicking real UI response times.
 *
 * Simulated element state:
 *   - All elements are "visible" after navigation
 *   - Value queries return mock data based on common element names
 *   - SOS triggers always succeed
 *   - Screenshots return null (no screenshot capture in mock mode)
 */
object MockTestExecutionContext {
    /** Known element values for mock assertions */
    private val mockElementValues = mapOf(
        "user_name" to "Test User",
        "alert_status" to "ACTIVE",
        "location_sharing" to "true",
        "location_mode" to "Normal",
        "enrollment_status" to "Active",
        "trigger_source" to "QuickTap",
        "response_status" to "ACCEPTED"
    )

    /** Screens that have been "navigated to" during this mock session */
    private val visitedScreens = mutableSetOf<String>()

    /** Current simulated location mode */
    private var locationMode = "Normal"

    /** Whether SOS is currently active */
    private var sosActive = false

    fun create(logger: WatchLogger): TestExecutionContext {
        visitedScreens.clear()
        locationMode = "Normal"
        sosActive = false

        return TestExecutionContext(
            navigateTo = { route ->
                delay(150) // Simulate navigation animation
                visitedScreens.add(route)
                logger.debug(
                    sourceContext = "MockExecutionContext",
                    messageTemplate = "Mock navigated to {Route}",
                    properties = mapOf("Route" to route)
                )
                true
            },
            tapElement = { testTag ->
                delay(100) // Simulate tap response
                // Simulate side effects of common taps
                when (testTag) {
                    "sos_button" -> { sosActive = true; locationMode = "Emergency" }
                    "cancel_sos_button" -> { sosActive = false; locationMode = "Normal" }
                    "enrollment_toggle" -> { /* toggle enrollment */ }
                    "phrase_detection_toggle" -> { /* toggle phrase detection */ }
                    "quicktap_toggle" -> { /* toggle quick tap */ }
                }
                logger.debug(
                    sourceContext = "MockExecutionContext",
                    messageTemplate = "Mock tapped {Tag}",
                    properties = mapOf("Tag" to testTag)
                )
                true
            },
            typeText = { testTag, text ->
                delay(50) // Simulate typing
                logger.debug(
                    sourceContext = "MockExecutionContext",
                    messageTemplate = "Mock typed into {Tag}",
                    properties = mapOf("Tag" to testTag)
                )
                true
            },
            isElementVisible = { testTag ->
                delay(30)
                // Most elements are visible in mock mode
                // Special handling for state-dependent elements
                when (testTag) {
                    "countdown_ring" -> sosActive
                    "alert_status" -> sosActive
                    "location_sharing" -> sosActive
                    "location_deescalate" -> !sosActive
                    else -> true
                }
            },
            getElementValue = { testTag ->
                delay(30)
                when (testTag) {
                    "location_mode" -> locationMode
                    "alert_status" -> if (sosActive) "ACTIVE" else "INACTIVE"
                    "location_sharing" -> sosActive.toString()
                    else -> mockElementValues[testTag]
                }
            },
            triggerSOS = { mechanism, payload ->
                delay(200)
                when (mechanism) {
                    "phrase" -> { sosActive = true; locationMode = "Emergency" }
                    "clearword" -> { sosActive = false; locationMode = "Normal" }
                    "quicktap" -> { sosActive = true; locationMode = "Emergency" }
                    "dispatch_notification" -> { /* handled externally */ }
                    "checkin_notification" -> { /* handled externally */ }
                }
                logger.debug(
                    sourceContext = "MockExecutionContext",
                    messageTemplate = "Mock SOS trigger: {Mechanism} '{Payload}'",
                    properties = mapOf("Mechanism" to mechanism, "Payload" to payload)
                )
                true
            },
            cancelSOS = {
                sosActive = false
                locationMode = "Normal"
                true
            },
            getCurrentRoute = {
                visitedScreens.lastOrNull()
            },
            captureScreenshot = {
                null // No screenshot in mock mode
            }
        )
    }
}
