package com.thewatch.app.testing

import com.thewatch.app.data.logging.WatchLogger
import com.thewatch.app.navigation.NavRoute
import kotlinx.coroutines.delay
import javax.inject.Inject
import javax.inject.Singleton

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: TestStepExecutor is a sealed interface with one implementation per action type.
// Each executor receives the TestStep, performs the action against the live app, and
// returns a TestStepResult indicating pass/fail.
//
// Executor routing:
//   Navigate    → NavigateExecutor: uses NavController to open screens by route path
//   Tap         → TapExecutor: finds UI element by test tag and performs click
//   TypeText    → TypeTextExecutor: finds input field by test tag and sets text
//   Assert      → AssertExecutor: checks UI state (visibility, text content) via test tags
//   TriggerSOS  → TriggerSOSExecutor: invokes SOS pipeline (phrase, clearword, quicktap, dispatch)
//   WaitFor     → WaitForExecutor: polls condition with timeout, fails if timeout exceeded
//
// All executors use Compose test tags (Modifier.testTag("sos_button")) to locate elements.
// In mock mode (no Compose UI testing framework), executors simulate success with synthetic delays.
//
// Example:
// ```kotlin
// val executor = executorRegistry.getExecutor(step.action)
// val result = executor.execute(step, context)
// ```

/**
 * Contract for executing a single test step action on-device.
 * Implementations map 1:1 to [TestAction] enum values.
 */
sealed interface TestStepExecutor {
    /** The action type this executor handles. */
    val actionType: TestAction

    /**
     * Execute the test step and return a result.
     *
     * @param step The test step to execute
     * @param context Shared context providing access to navigation, services, and state
     * @return Result with pass/fail, optional screenshot, and error details
     */
    suspend fun execute(step: TestStep, context: TestExecutionContext): TestStepResult
}

/**
 * Shared context passed to all executors during a test run.
 * Provides access to the navigation controller, SOS pipeline, and UI state queries.
 *
 * This is the bridge between the test runner and the live application.
 * In mock mode, all queries return simulated values. In native mode,
 * queries use Compose test framework or accessibility services.
 */
data class TestExecutionContext(
    /** Navigate to a route path. Abstracts NavController access. */
    val navigateTo: suspend (route: String) -> Boolean,

    /** Tap a UI element by test tag. Returns true if element found and tapped. */
    val tapElement: suspend (testTag: String) -> Boolean,

    /** Type text into a field identified by test tag. Returns true if field found and text entered. */
    val typeText: suspend (testTag: String, text: String) -> Boolean,

    /** Query whether a UI element with the given test tag is visible. */
    val isElementVisible: suspend (testTag: String) -> Boolean,

    /** Get the text/value of a UI element by test tag. */
    val getElementValue: suspend (testTag: String) -> String?,

    /** Trigger SOS via a specific mechanism. */
    val triggerSOS: suspend (mechanism: String, payload: String) -> Boolean,

    /** Cancel an active SOS. */
    val cancelSOS: suspend () -> Boolean,

    /** Get the current screen route. */
    val getCurrentRoute: suspend () -> String?,

    /** Capture a screenshot and return the file path or base64 data. */
    val captureScreenshot: suspend () -> String?
)

// ── Executor Implementations ─────────────────────────────────────

/**
 * Navigates to a screen by route path.
 *
 * Target is the route path (e.g., "/login", "/settings", "/home", "/volunteering").
 * Maps dashboard route strings to NavRoute routes.
 *
 * Route mapping from TestOrchestratorService step definitions:
 *   "/login"        → NavRoute.Login
 *   "/signup"       → NavRoute.SignUp
 *   "/home"         → NavRoute.Home
 *   "/profile"      → NavRoute.Profile
 *   "/settings"     → NavRoute.Settings
 *   "/volunteering" → NavRoute.Volunteering
 *   "/contacts"     → NavRoute.Contacts
 *   "/evacuation"   → NavRoute.Evacuation
 *   "/health"       → NavRoute.HealthDashboard
 *   "/permissions"  → NavRoute.Permissions
 *   "/history"      → NavRoute.History
 *   "/eula"         → NavRoute.Eula
 */
class NavigateExecutor @Inject constructor(
    private val logger: WatchLogger
) : TestStepExecutor {
    override val actionType = TestAction.Navigate

    private val routeMap = mapOf(
        "/login" to NavRoute.Login.route,
        "/signup" to NavRoute.SignUp.route,
        "/forgot_password" to NavRoute.ForgotPassword.route,
        "/reset_password" to NavRoute.ResetPassword.route,
        "/eula" to NavRoute.Eula.route,
        "/home" to NavRoute.Home.route,
        "/profile" to NavRoute.Profile.route,
        "/permissions" to NavRoute.Permissions.route,
        "/history" to NavRoute.History.route,
        "/volunteering" to NavRoute.Volunteering.route,
        "/contacts" to NavRoute.Contacts.route,
        "/settings" to NavRoute.Settings.route,
        "/evacuation" to NavRoute.Evacuation.route,
        "/health" to NavRoute.HealthDashboard.route,
        "/wearables" to NavRoute.WearableManagement.route
    )

    override suspend fun execute(step: TestStep, context: TestExecutionContext): TestStepResult {
        val startTime = System.currentTimeMillis()
        val route = routeMap[step.target]

        if (route == null) {
            return step.toResult(
                passed = false,
                durationMs = System.currentTimeMillis() - startTime,
                errorMessage = "Unknown route: '${step.target}'. Known routes: ${routeMap.keys.joinToString()}"
            )
        }

        logger.information(
            sourceContext = "NavigateExecutor",
            messageTemplate = "Navigating to {Route} (mapped from {Target})",
            properties = mapOf("Route" to route, "Target" to step.target)
        )

        val success = context.navigateTo(route)

        // Brief settle time for navigation animation
        delay(300)

        return step.toResult(
            passed = success,
            durationMs = System.currentTimeMillis() - startTime,
            errorMessage = if (!success) "Navigation to '${step.target}' failed" else null,
            screenshot = context.captureScreenshot()
        )
    }
}

/**
 * Taps a UI element identified by its test tag.
 *
 * Target is the Compose testTag string (e.g., "sos_button", "login_button", "enrollment_toggle").
 * No value expected.
 */
class TapExecutor @Inject constructor(
    private val logger: WatchLogger
) : TestStepExecutor {
    override val actionType = TestAction.Tap

    override suspend fun execute(step: TestStep, context: TestExecutionContext): TestStepResult {
        val startTime = System.currentTimeMillis()

        logger.information(
            sourceContext = "TapExecutor",
            messageTemplate = "Tapping element {Target} on {Screen}",
            properties = mapOf("Target" to step.target, "Screen" to step.screenName)
        )

        // Verify element exists before tapping
        val visible = context.isElementVisible(step.target)
        if (!visible) {
            return step.toResult(
                passed = false,
                durationMs = System.currentTimeMillis() - startTime,
                errorMessage = "Element '${step.target}' not found or not visible on ${step.screenName}"
            )
        }

        val success = context.tapElement(step.target)

        // Brief settle time for tap animation/response
        delay(200)

        return step.toResult(
            passed = success,
            durationMs = System.currentTimeMillis() - startTime,
            errorMessage = if (!success) "Tap on '${step.target}' did not register" else null,
            screenshot = context.captureScreenshot()
        )
    }
}

/**
 * Types text into a field identified by its test tag.
 *
 * Target is the testTag of the input field (e.g., "email_field", "password_field").
 * Value is the text to type (e.g., "test@thewatch.app").
 */
class TypeTextExecutor @Inject constructor(
    private val logger: WatchLogger
) : TestStepExecutor {
    override val actionType = TestAction.TypeText

    override suspend fun execute(step: TestStep, context: TestExecutionContext): TestStepResult {
        val startTime = System.currentTimeMillis()
        val text = step.value

        if (text == null) {
            return step.toResult(
                passed = false,
                durationMs = System.currentTimeMillis() - startTime,
                errorMessage = "TypeText requires a value but none was provided for '${step.target}'"
            )
        }

        logger.information(
            sourceContext = "TypeTextExecutor",
            messageTemplate = "Typing into {Target}: '{Text}' on {Screen}",
            properties = mapOf(
                "Target" to step.target,
                "Text" to if (step.target.contains("password", ignoreCase = true)) "***" else text,
                "Screen" to step.screenName
            )
        )

        val success = context.typeText(step.target, text)

        return step.toResult(
            passed = success,
            durationMs = System.currentTimeMillis() - startTime,
            errorMessage = if (!success) "Failed to type into '${step.target}'" else null,
            screenshot = context.captureScreenshot()
        )
    }
}

/**
 * Asserts UI state: element visibility or value match.
 *
 * Target is the testTag to check (e.g., "sos_button", "user_name", "alert_status").
 * Value determines the assertion type:
 *   "visible"     → assert element is visible
 *   "hidden"      → assert element is NOT visible
 *   "true"/"false"→ assert element value matches boolean string
 *   any other     → assert element text/value equals the string
 *
 * Example steps from TestOrchestratorService:
 *   Step(2, "LoginScreen", "Assert", "email_field", "visible")
 *   Step(8, "ProfileScreen", "Assert", "user_name", "Test User")
 *   Step(5, "SOSActive", "Assert", "alert_status", "ACTIVE")
 */
class AssertExecutor @Inject constructor(
    private val logger: WatchLogger
) : TestStepExecutor {
    override val actionType = TestAction.Assert

    override suspend fun execute(step: TestStep, context: TestExecutionContext): TestStepResult {
        val startTime = System.currentTimeMillis()
        val expected = step.value ?: "visible" // Default to visibility check

        logger.information(
            sourceContext = "AssertExecutor",
            messageTemplate = "Asserting {Target} == '{Expected}' on {Screen}",
            properties = mapOf(
                "Target" to step.target,
                "Expected" to expected,
                "Screen" to step.screenName
            )
        )

        val (passed, errorMessage) = when (expected.lowercase()) {
            "visible" -> {
                val visible = context.isElementVisible(step.target)
                visible to if (!visible) "Element '${step.target}' expected to be visible but was not found" else null
            }
            "hidden", "not_visible" -> {
                val visible = context.isElementVisible(step.target)
                (!visible) to if (visible) "Element '${step.target}' expected to be hidden but was visible" else null
            }
            else -> {
                // Value assertion — check element text matches expected
                val actual = context.getElementValue(step.target)
                val matches = actual.equals(expected, ignoreCase = true)
                matches to if (!matches) {
                    "Element '${step.target}' expected value '$expected' but got '${actual ?: "<not found>"}'"
                } else null
            }
        }

        return step.toResult(
            passed = passed,
            durationMs = System.currentTimeMillis() - startTime,
            errorMessage = errorMessage,
            screenshot = if (!passed) context.captureScreenshot() else null
        )
    }
}

/**
 * Triggers SOS via various mechanisms — phrase detection, clear word, quick-tap, or dispatch simulation.
 *
 * Target is the trigger mechanism:
 *   "phrase"                → simulate spoken phrase SOS trigger
 *   "clearword"             → simulate spoken clear word to cancel SOS
 *   "quicktap"              → simulate rapid tap sequence (value = tap count)
 *   "dispatch_notification" → simulate receiving an SOS dispatch notification
 *   "checkin_notification"  → simulate receiving a check-in notification
 *
 * Value varies by mechanism:
 *   phrase:  the phrase text (e.g., "help me now")
 *   clearword: the clear word text (e.g., "all clear")
 *   quicktap: tap count as string (e.g., "4")
 *   dispatch_notification: notification type (e.g., "SOS_DISPATCH")
 *   checkin_notification: notification type (e.g., "CHECK_IN")
 */
class TriggerSOSExecutor @Inject constructor(
    private val logger: WatchLogger
) : TestStepExecutor {
    override val actionType = TestAction.TriggerSOS

    override suspend fun execute(step: TestStep, context: TestExecutionContext): TestStepResult {
        val startTime = System.currentTimeMillis()
        val mechanism = step.target
        val payload = step.value ?: ""

        logger.information(
            sourceContext = "TriggerSOSExecutor",
            messageTemplate = "Triggering SOS via {Mechanism} with payload '{Payload}'",
            properties = mapOf("Mechanism" to mechanism, "Payload" to payload)
        )

        val success = when (mechanism) {
            "phrase", "clearword", "quicktap", "dispatch_notification", "checkin_notification" -> {
                context.triggerSOS(mechanism, payload)
            }
            else -> {
                logger.warning(
                    sourceContext = "TriggerSOSExecutor",
                    messageTemplate = "Unknown SOS trigger mechanism: {Mechanism}",
                    properties = mapOf("Mechanism" to mechanism)
                )
                false
            }
        }

        // SOS triggers need time to propagate through the pipeline
        delay(500)

        return step.toResult(
            passed = success,
            durationMs = System.currentTimeMillis() - startTime,
            errorMessage = if (!success) "SOS trigger via '$mechanism' failed" else null,
            screenshot = context.captureScreenshot()
        )
    }
}

/**
 * Waits for a condition to become true, polling with timeout.
 *
 * Target is the element or condition to wait for (e.g., "countdown_timer", "location_deescalate").
 * Value is the timeout in milliseconds (e.g., "3000", "5000").
 *
 * Polls every 250ms until the element becomes visible or the timeout expires.
 * Used for animations, countdowns, and async state transitions.
 */
class WaitForExecutor @Inject constructor(
    private val logger: WatchLogger
) : TestStepExecutor {
    override val actionType = TestAction.WaitFor

    companion object {
        private const val POLL_INTERVAL_MS = 250L
        private const val DEFAULT_TIMEOUT_MS = 5000L
    }

    override suspend fun execute(step: TestStep, context: TestExecutionContext): TestStepResult {
        val startTime = System.currentTimeMillis()
        val timeoutMs = step.value?.toLongOrNull() ?: DEFAULT_TIMEOUT_MS

        logger.information(
            sourceContext = "WaitForExecutor",
            messageTemplate = "Waiting for {Target} with timeout {TimeoutMs}ms on {Screen}",
            properties = mapOf(
                "Target" to step.target,
                "TimeoutMs" to timeoutMs.toString(),
                "Screen" to step.screenName
            )
        )

        var elapsed = 0L
        var conditionMet = false

        while (elapsed < timeoutMs) {
            conditionMet = context.isElementVisible(step.target)
            if (conditionMet) break
            delay(POLL_INTERVAL_MS)
            elapsed = System.currentTimeMillis() - startTime
        }

        if (!conditionMet) {
            logger.warning(
                sourceContext = "WaitForExecutor",
                messageTemplate = "Timeout waiting for {Target} after {ElapsedMs}ms",
                properties = mapOf("Target" to step.target, "ElapsedMs" to elapsed.toString())
            )
        }

        return step.toResult(
            passed = conditionMet,
            durationMs = System.currentTimeMillis() - startTime,
            errorMessage = if (!conditionMet) "Timeout after ${timeoutMs}ms waiting for '${step.target}'" else null,
            screenshot = if (!conditionMet) context.captureScreenshot() else null
        )
    }
}

// ── Executor Registry ────────────────────────────────────────────

/**
 * Registry of all test step executors, keyed by [TestAction].
 * Injected as a singleton and used by [TestRunnerService] to dispatch steps.
 */
@Singleton
class TestStepExecutorRegistry @Inject constructor(
    navigateExecutor: NavigateExecutor,
    tapExecutor: TapExecutor,
    typeTextExecutor: TypeTextExecutor,
    assertExecutor: AssertExecutor,
    triggerSOSExecutor: TriggerSOSExecutor,
    waitForExecutor: WaitForExecutor
) {
    private val executors: Map<TestAction, TestStepExecutor> = mapOf(
        TestAction.Navigate to navigateExecutor,
        TestAction.Tap to tapExecutor,
        TestAction.TypeText to typeTextExecutor,
        TestAction.Assert to assertExecutor,
        TestAction.TriggerSOS to triggerSOSExecutor,
        TestAction.WaitFor to waitForExecutor
    )

    /**
     * Get the executor for a given action string.
     * Returns null if the action is unknown.
     */
    fun getExecutor(action: String): TestStepExecutor? {
        val actionType = TestAction.fromString(action) ?: return null
        return executors[actionType]
    }

    /** All registered action types. */
    fun supportedActions(): Set<TestAction> = executors.keys
}

// ── Extension ────────────────────────────────────────────────────

/**
 * Convenience extension to build a [TestStepResult] from a [TestStep].
 */
fun TestStep.toResult(
    passed: Boolean,
    durationMs: Long,
    errorMessage: String? = null,
    screenshot: String? = null
) = TestStepResult(
    stepId = id,
    order = order,
    screenName = screenName,
    action = action,
    passed = passed,
    screenshot = screenshot,
    errorMessage = errorMessage,
    durationMs = durationMs
)
