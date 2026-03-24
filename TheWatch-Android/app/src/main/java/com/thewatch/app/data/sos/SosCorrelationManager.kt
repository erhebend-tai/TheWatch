package com.thewatch.app.data.sos

import com.thewatch.app.data.logging.WatchLogger
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.util.UUID
import javax.inject.Inject
import javax.inject.Singleton

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: SosCorrelationManager generates a single UUID at SOS trigger time.
// That correlationId threads through every WatchLogger call in the SOS lifecycle:
//   phrase detection → confirmation → alert dispatch → volunteer notification → resolution
// MobileLogController on the Aspire dashboard can filter by correlationId to reconstruct
// a complete SOS timeline across devices.
//
// Example usage:
// ```kotlin
// @Inject lateinit var sosCorrelation: SosCorrelationManager
//
// // At SOS trigger time:
// val corrId = sosCorrelation.beginCorrelation(SosTriggerMethod.Phrase)
//
// // Every log in the pipeline:
// logger.information("SOS", "Alert dispatched to {Count} volunteers",
//     mapOf("Count" to "5"), correlationId = sosCorrelation.currentId)
//
// // Scoped block (auto-attaches correlationId):
// sosCorrelation.withCorrelation { corrId ->
//     logger.information("SOS", "Volunteer {Id} acknowledged", mapOf("Id" to volId), correlationId = corrId)
// }
//
// // On resolution or timeout:
// sosCorrelation.endCorrelation(SosResolutionReason.UserResolved)
// ```

/**
 * Trigger method that initiated the SOS lifecycle.
 * Stored as a property in the first log entry for audit trail.
 */
enum class SosTriggerMethod {
    /** Voice phrase detection via speech-to-text pipeline */
    Phrase,
    /** Rapid tap pattern on hardware button / screen */
    QuickTap,
    /** Manual SOS button press with countdown confirmation */
    Manual,
    /** Implicit detection: fall, crash, elevated HR, etc. */
    ImplicitDetection,
    /** Silent/duress SOS — no UI confirmation shown */
    SilentDuress,
    /** External trigger via wearable BLE beacon */
    WearableTrigger
}

/**
 * Why the SOS correlation ended — logged as the final entry.
 */
enum class SosResolutionReason {
    /** User explicitly marked themselves safe */
    UserResolved,
    /** Volunteer confirmed user is safe via check-in */
    VolunteerConfirmed,
    /** First responders took over — authority handoff */
    FirstResponderHandoff,
    /** User cancelled during confirmation countdown */
    UserCancelled,
    /** Auto-cleared after timeout (default 30 min) */
    Timeout,
    /** System override — admin or dashboard forced close */
    SystemOverride
}

/**
 * Singleton that manages SOS lifecycle correlation IDs.
 *
 * Generates a single UUID at SOS trigger time and makes it available to every
 * component in the pipeline. Auto-clears on resolution or configurable timeout.
 *
 * Thread-safe: backed by StateFlow, all mutations on a dedicated coroutine scope.
 *
 * The MAUI dashboard's MobileLogController queries by correlationId to reconstruct
 * the full SOS timeline: trigger → confirmation → alert → volunteer dispatch → resolution.
 */
@Singleton
class SosCorrelationManager @Inject constructor(
    private val logger: WatchLogger
) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    /** Timeout in milliseconds after which an unresolved SOS auto-clears. Default: 30 minutes. */
    var timeoutMillis: Long = 30L * 60L * 1000L

    // ── State ────────────────────────────────────────────────────

    private val _currentCorrelationId = MutableStateFlow<String?>(null)

    /** The active SOS correlation ID, or null if no SOS is in progress. */
    val currentCorrelationId: StateFlow<String?> = _currentCorrelationId.asStateFlow()

    /** Convenience accessor — null if no active SOS. */
    val currentId: String? get() = _currentCorrelationId.value

    /** Whether an SOS lifecycle is currently active. */
    val isActive: Boolean get() = _currentCorrelationId.value != null

    private val _triggerMethod = MutableStateFlow<SosTriggerMethod?>(null)
    val triggerMethod: StateFlow<SosTriggerMethod?> = _triggerMethod.asStateFlow()

    private val _triggerTimestamp = MutableStateFlow<Long?>(null)
    val triggerTimestamp: StateFlow<Long?> = _triggerTimestamp.asStateFlow()

    private var timeoutJob: Job? = null

    // ── Lifecycle Control ────────────────────────────────────────

    /**
     * Begin a new SOS correlation. Generates a fresh UUID, logs the trigger event,
     * and starts the auto-timeout watchdog.
     *
     * If a correlation is already active, the previous one is force-ended with
     * [SosResolutionReason.SystemOverride] before starting the new one.
     *
     * @param method How the SOS was triggered (phrase, quick tap, manual, etc.)
     * @return The new correlationId
     */
    fun beginCorrelation(method: SosTriggerMethod): String {
        // If there's an existing active SOS, close it first
        if (isActive) {
            endCorrelation(SosResolutionReason.SystemOverride)
        }

        val correlationId = UUID.randomUUID().toString()
        val now = System.currentTimeMillis()

        _currentCorrelationId.value = correlationId
        _triggerMethod.value = method
        _triggerTimestamp.value = now

        logger.information(
            sourceContext = "SosCorrelationManager",
            messageTemplate = "SOS triggered by {Method}. CorrelationId: {CorrelationId}",
            properties = mapOf(
                "Method" to method.name,
                "CorrelationId" to correlationId,
                "Stage" to "TRIGGER"
            ),
            correlationId = correlationId
        )

        // Start timeout watchdog
        startTimeoutWatchdog(correlationId)

        return correlationId
    }

    /**
     * End the active SOS correlation with a reason.
     * Logs the resolution event and clears state.
     *
     * No-op if no correlation is active.
     *
     * @param reason Why the SOS lifecycle ended
     */
    fun endCorrelation(reason: SosResolutionReason) {
        val corrId = _currentCorrelationId.value ?: return
        val method = _triggerMethod.value
        val triggerTime = _triggerTimestamp.value
        val durationMs = triggerTime?.let { System.currentTimeMillis() - it }

        logger.information(
            sourceContext = "SosCorrelationManager",
            messageTemplate = "SOS resolved. Reason: {Reason}, Duration: {DurationMs}ms, TriggerMethod: {Method}",
            properties = buildMap {
                put("Reason", reason.name)
                put("Stage", "RESOLUTION")
                if (method != null) put("Method", method.name)
                if (durationMs != null) put("DurationMs", durationMs.toString())
            },
            correlationId = corrId
        )

        // Clear state
        timeoutJob?.cancel()
        timeoutJob = null
        _currentCorrelationId.value = null
        _triggerMethod.value = null
        _triggerTimestamp.value = null
    }

    // ── Scoped Correlation ───────────────────────────────────────

    /**
     * Execute a block with the current correlationId. If no SOS is active,
     * the block receives null and can decide whether to proceed.
     *
     * Example:
     * ```kotlin
     * sosCorrelation.withCorrelation { corrId ->
     *     logger.information("VolunteerDispatch", "Dispatched {Count} volunteers",
     *         mapOf("Count" to "5"), correlationId = corrId)
     * }
     * ```
     */
    inline fun <T> withCorrelation(block: (String?) -> T): T {
        return block(currentId)
    }

    /**
     * Execute a block only if an SOS is active. Returns null if no SOS in progress.
     */
    inline fun <T> withActiveCorrelation(block: (String) -> T): T? {
        val corrId = currentId ?: return null
        return block(corrId)
    }

    /**
     * Log a pipeline stage event with the current correlationId.
     * Convenience method that enriches properties with the Stage tag.
     *
     * Stages in order:
     *   TRIGGER → CONFIRMATION → ALERT_DISPATCH → VOLUNTEER_NOTIFY →
     *   VOLUNTEER_ACKNOWLEDGE → CHECK_IN → ESCALATION → RESOLUTION
     *
     * Example:
     * ```kotlin
     * sosCorrelation.logStage("ALERT_DISPATCH", "AlertService",
     *     "Dispatched alert {AlertId} to NG911",
     *     mapOf("AlertId" to alert.id))
     * ```
     */
    fun logStage(
        stage: String,
        sourceContext: String,
        messageTemplate: String,
        properties: Map<String, String> = emptyMap()
    ) {
        val corrId = currentId ?: return
        val enriched = properties.toMutableMap().apply {
            put("Stage", stage)
        }
        logger.information(
            sourceContext = sourceContext,
            messageTemplate = messageTemplate,
            properties = enriched,
            correlationId = corrId
        )
    }

    // ── Timeout Watchdog ─────────────────────────────────────────

    private fun startTimeoutWatchdog(correlationId: String) {
        timeoutJob?.cancel()
        timeoutJob = scope.launch {
            delay(timeoutMillis)
            // Only timeout if the same correlation is still active
            if (_currentCorrelationId.value == correlationId) {
                logger.warning(
                    sourceContext = "SosCorrelationManager",
                    messageTemplate = "SOS auto-timeout after {TimeoutMin} minutes. CorrelationId: {CorrelationId}",
                    properties = mapOf(
                        "TimeoutMin" to (timeoutMillis / 60000).toString(),
                        "CorrelationId" to correlationId,
                        "Stage" to "TIMEOUT"
                    ),
                    correlationId = correlationId
                )
                endCorrelation(SosResolutionReason.Timeout)
            }
        }
    }
}
