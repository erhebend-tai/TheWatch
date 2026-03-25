/**
 * +----------------------------------------------------------------------+
 * | WRITE-AHEAD LOG                                                       |
 * +----------------------------------------------------------------------+
 * | File:         SosTriggerService.kt                                    |
 * | Purpose:      Central orchestrator for all SOS trigger methods on     |
 * |               Android: phrase detection, quick-tap pattern, and       |
 * |               manual button press. When any trigger fires, this       |
 * |               service handles the full lifecycle:                     |
 * |                 1. Begin SOS correlation (SosCorrelationManager)      |
 * |                 2. Escalate location to emergency mode                |
 * |                 3. Play haptic feedback countdown pattern             |
 * |                 4. POST to /api/response/trigger with auth + bypass   |
 * |                 5. Queue offline if network unavailable (SyncEngine)  |
 * |                 6. Play alert sound on confirmation                   |
 * | Created:      2026-03-24                                             |
 * | Author:       Claude                                                 |
 * | Dependencies: SosCorrelationManager, SyncEngine, WatchLogger,        |
 * |               LocationRepository, ConnectivityMonitor                 |
 * |                                                                       |
 * | Usage example:                                                        |
 * |   @Inject lateinit var sosTrigger: SosTriggerService                  |
 * |                                                                       |
 * |   // From phrase detection coordinator:                               |
 * |   sosTrigger.trigger(SosTriggerSource.PHRASE, "Duress phrase")        |
 * |                                                                       |
 * |   // From quick-tap coordinator:                                      |
 * |   sosTrigger.trigger(SosTriggerSource.QUICK_TAP, "4 taps in 3s")     |
 * |                                                                       |
 * |   // From manual SOS button (after countdown):                        |
 * |   sosTrigger.trigger(SosTriggerSource.MANUAL_BUTTON)                  |
 * |                                                                       |
 * |   // Cancel during countdown:                                         |
 * |   sosTrigger.cancel()                                                 |
 * |                                                                       |
 * | Life-Safety Critical:                                                 |
 * |   - This service NEVER blocks on auth failure. If Bearer token is     |
 * |     expired, it falls back to X-SOS-Bypass-Token header.              |
 * |   - If both tokens fail, it sends with userId only (server allows).   |
 * |   - If offline, the SOS is queued via SyncEngine with CRITICAL        |
 * |     priority and retried immediately when connectivity returns.        |
 * |                                                                       |
 * | Potential additions:                                                   |
 * |   - Camera snapshot for situational awareness                         |
 * |   - BLE beacon broadcast for nearby TheWatch devices                  |
 * |   - Integration with Android Emergency SOS (Android 12+)             |
 * |   - SMS fallback via SMSFallbackPort                                  |
 * |   - NG911 integration via NG911Port                                   |
 * +----------------------------------------------------------------------+
 */
package com.thewatch.app.services

import android.content.Context
import android.media.AudioAttributes
import android.media.AudioManager
import android.media.ToneGenerator
import android.os.Build
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import android.util.Log
import com.thewatch.app.data.logging.WatchLogger
import com.thewatch.app.data.repository.LocationRepository
import com.thewatch.app.data.sos.SosCorrelationManager
import com.thewatch.app.data.sos.SosResolutionReason
import com.thewatch.app.data.sos.SosTriggerMethod
import com.thewatch.app.data.sync.ConnectivityMonitor
import com.thewatch.app.data.sync.SyncEngine
import com.thewatch.app.data.sync.SyncEntityType
import com.thewatch.app.data.sync.SyncPriority
import com.thewatch.app.data.sync.SyncTaskAction
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.net.HttpURLConnection
import java.net.URL
import java.util.UUID
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Identifies how the SOS was triggered — sent to the backend as triggerSource.
 * Maps to the ResponseController's TriggerResponseRequest.TriggerSource field.
 */
enum class SosTriggerSource(val apiValue: String) {
    PHRASE("PHRASE"),
    QUICK_TAP("QUICK_TAP"),
    MANUAL_BUTTON("MANUAL_BUTTON"),
    IMPLICIT_DETECTION("IMPLICIT_DETECTION"),
    SILENT_DURESS("SILENT_DURESS"),
    WEARABLE("WEARABLE")
}

/**
 * Response scope — maps to the backend's ResponseScope enum.
 * CheckIn is the default (lightest weight: nearby volunteers check on user).
 */
enum class ResponseScope(val apiValue: String) {
    CHECK_IN("CheckIn"),
    FULL_EMERGENCY("FullEmergency"),
    MEDICAL("Medical"),
    FIRE("Fire"),
    SILENT("Silent")
}

/**
 * Current state of an SOS trigger lifecycle, observable from UI.
 */
sealed class SosTriggerState {
    /** No active SOS. */
    object Idle : SosTriggerState()

    /** Countdown in progress. [secondsRemaining] counts down from 3 to 0. */
    data class Countdown(val secondsRemaining: Int) : SosTriggerState()

    /** SOS dispatched, waiting for server response. */
    object Dispatching : SosTriggerState()

    /** Server responded with responder info. */
    data class Active(
        val requestId: String,
        val responderCount: Int,
        val radiusMeters: Double
    ) : SosTriggerState()

    /** SOS queued offline for retry when connectivity returns. */
    object QueuedOffline : SosTriggerState()

    /** SOS was cancelled during countdown. */
    object Cancelled : SosTriggerState()

    /** Error during dispatch — but SOS was queued as fallback. */
    data class Error(val message: String, val queuedOffline: Boolean) : SosTriggerState()
}

/**
 * JSON payload for POST /api/response/trigger.
 */
@Serializable
data class TriggerRequestPayload(
    val userId: String,
    val scope: String,
    val latitude: Double,
    val longitude: Double,
    val description: String? = null,
    val triggerSource: String? = null
)

/**
 * Central SOS trigger orchestrator. All trigger methods (phrase, tap, button)
 * funnel through this service. Handles auth, offline queuing, haptics, and sound.
 *
 * LIFE-SAFETY CRITICAL: This service MUST NOT be blocked by authentication.
 * If Bearer token is expired, falls back to X-SOS-Bypass-Token.
 * If both fail, sends with userId only (server allows for life-safety).
 * If offline, queues with CRITICAL priority via SyncEngine.
 */
@Singleton
class SosTriggerService @Inject constructor(
    @ApplicationContext private val context: Context,
    private val sosCorrelation: SosCorrelationManager,
    private val syncEngine: SyncEngine,
    private val connectivityMonitor: ConnectivityMonitor,
    private val locationRepository: LocationRepository,
    private val logger: WatchLogger
) {
    companion object {
        private const val TAG = "TheWatch.SosTrigger"
        private const val COUNTDOWN_SECONDS = 3
        private const val API_BASE_URL = "https://thewatch-api.azurewebsites.net"
        private const val TRIGGER_ENDPOINT = "/api/response/trigger"
        private const val CONNECT_TIMEOUT_MS = 10_000
        private const val READ_TIMEOUT_MS = 15_000

        /**
         * Hilt-injected singleton reference, set during Application.onCreate.
         * Provides access for Compose navigation routes that can't inject directly.
         * In production, prefer @Inject from ViewModels instead.
         */
        @Volatile
        lateinit var instance: SosTriggerService
    }

    init {
        instance = this
    }

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val json = Json { ignoreUnknownKeys = true; encodeDefaults = true }

    // ── Observable state ──────────────────────────────────────────

    private val _state = MutableStateFlow<SosTriggerState>(SosTriggerState.Idle)
    val state: StateFlow<SosTriggerState> = _state.asStateFlow()

    private val _lastRequestId = MutableStateFlow<String?>(null)
    val lastRequestId: StateFlow<String?> = _lastRequestId.asStateFlow()

    // ── Auth tokens (set by auth layer on login) ──────────────────

    /** Firebase/MSAL Bearer token — may be expired during SOS. */
    @Volatile var bearerToken: String? = null

    /** Pre-issued SOS bypass token — cached locally, survives auth expiry. */
    @Volatile var sosBypassToken: String? = null

    /** Current authenticated user ID. */
    @Volatile var userId: String = ""

    // ── Countdown management ──────────────────────────────────────

    private var countdownJob: Job? = null
    private var pendingTriggerSource: SosTriggerSource? = null
    private var pendingDescription: String? = null
    private var pendingScope: ResponseScope? = null

    // ── Public API ────────────────────────────────────────────────

    /**
     * Begin the SOS trigger flow with a 3-second countdown.
     * During countdown, haptic feedback pulses each second.
     * After countdown expires, the SOS is dispatched to the backend.
     *
     * For silent/duress triggers, set [skipCountdown] = true.
     *
     * @param source How the SOS was triggered
     * @param description Optional human-readable description
     * @param responseScope Default CheckIn — can be escalated
     * @param skipCountdown True for silent/duress — no countdown, immediate dispatch
     */
    fun trigger(
        source: SosTriggerSource,
        description: String? = null,
        responseScope: ResponseScope = ResponseScope.CHECK_IN,
        skipCountdown: Boolean = false
    ) {
        // If already in an active SOS, don't re-trigger
        if (_state.value is SosTriggerState.Active ||
            _state.value is SosTriggerState.Dispatching) {
            Log.w(TAG, "SOS already active — ignoring duplicate trigger")
            return
        }

        // Cancel any in-progress countdown
        countdownJob?.cancel()

        pendingTriggerSource = source
        pendingDescription = description
        pendingScope = responseScope

        if (skipCountdown) {
            // Silent/duress — dispatch immediately, no haptics, no sound
            dispatchSOS(source, description, responseScope)
        } else {
            // Start countdown with haptic feedback
            startCountdown(source, description, responseScope)
        }
    }

    /**
     * Cancel the SOS during countdown. No-op if already dispatched.
     * Cancelling after dispatch requires calling the /cancel endpoint.
     */
    fun cancel() {
        when (val current = _state.value) {
            is SosTriggerState.Countdown -> {
                countdownJob?.cancel()
                countdownJob = null
                _state.value = SosTriggerState.Cancelled

                // End correlation as cancelled
                sosCorrelation.endCorrelation(SosResolutionReason.UserCancelled)

                // Light haptic feedback on cancel
                vibratePattern(longArrayOf(0, 50))

                logger.information(
                    sourceContext = TAG,
                    messageTemplate = "SOS cancelled during countdown at {Seconds}s remaining",
                    properties = mapOf("Seconds" to current.secondsRemaining.toString()),
                    correlationId = sosCorrelation.currentId
                )

                // Reset to idle after a brief pause
                scope.launch {
                    delay(2000)
                    if (_state.value is SosTriggerState.Cancelled) {
                        _state.value = SosTriggerState.Idle
                    }
                }
            }
            else -> {
                Log.w(TAG, "Cannot cancel — state is ${current::class.simpleName}")
            }
        }
    }

    /**
     * Reset state back to idle. Call when user navigates away from SOS screen.
     */
    fun reset() {
        countdownJob?.cancel()
        countdownJob = null
        _state.value = SosTriggerState.Idle
    }

    // ── Countdown ─────────────────────────────────────────────────

    private fun startCountdown(
        source: SosTriggerSource,
        description: String?,
        responseScope: ResponseScope
    ) {
        // Begin correlation early so logs during countdown are tracked
        val method = when (source) {
            SosTriggerSource.PHRASE -> SosTriggerMethod.Phrase
            SosTriggerSource.QUICK_TAP -> SosTriggerMethod.QuickTap
            SosTriggerSource.MANUAL_BUTTON -> SosTriggerMethod.Manual
            SosTriggerSource.IMPLICIT_DETECTION -> SosTriggerMethod.ImplicitDetection
            SosTriggerSource.SILENT_DURESS -> SosTriggerMethod.SilentDuress
            SosTriggerSource.WEARABLE -> SosTriggerMethod.WearableTrigger
        }
        sosCorrelation.beginCorrelation(method)

        sosCorrelation.logStage(
            stage = "CONFIRMATION",
            sourceContext = TAG,
            messageTemplate = "SOS countdown started. Source: {Source}, Scope: {Scope}",
            properties = mapOf(
                "Source" to source.apiValue,
                "Scope" to responseScope.apiValue
            )
        )

        _state.value = SosTriggerState.Countdown(COUNTDOWN_SECONDS)

        countdownJob = scope.launch {
            for (remaining in COUNTDOWN_SECONDS downTo 1) {
                _state.value = SosTriggerState.Countdown(remaining)

                // Haptic pulse — intensifies as countdown progresses
                val amplitude = when (remaining) {
                    3 -> 80
                    2 -> 160
                    1 -> 255
                    else -> 128
                }
                vibrateOnce(150L, amplitude)

                delay(1000)
            }

            // Countdown complete — dispatch
            _state.value = SosTriggerState.Countdown(0)

            // Strong haptic burst to confirm SOS activation
            vibratePattern(longArrayOf(0, 100, 50, 100, 50, 200))

            dispatchSOS(source, description, responseScope)
        }
    }

    // ── Dispatch ───────────────────────────────────────────────────

    private fun dispatchSOS(
        source: SosTriggerSource,
        description: String?,
        responseScope: ResponseScope
    ) {
        _state.value = SosTriggerState.Dispatching

        // Begin correlation if not already started (silent/duress skips countdown)
        if (!sosCorrelation.isActive) {
            val method = when (source) {
                SosTriggerSource.PHRASE -> SosTriggerMethod.Phrase
                SosTriggerSource.QUICK_TAP -> SosTriggerMethod.QuickTap
                SosTriggerSource.MANUAL_BUTTON -> SosTriggerMethod.Manual
                SosTriggerSource.IMPLICIT_DETECTION -> SosTriggerMethod.ImplicitDetection
                SosTriggerSource.SILENT_DURESS -> SosTriggerMethod.SilentDuress
                SosTriggerSource.WEARABLE -> SosTriggerMethod.WearableTrigger
            }
            sosCorrelation.beginCorrelation(method)
        }

        // Get current location — use whatever we have, never block on it
        val lat = locationRepository.lastLatitude ?: 0.0
        val lng = locationRepository.lastLongitude ?: 0.0

        val payload = TriggerRequestPayload(
            userId = userId,
            scope = responseScope.apiValue,
            latitude = lat,
            longitude = lng,
            description = description ?: "SOS triggered via ${source.apiValue}",
            triggerSource = source.apiValue
        )

        val payloadJson = json.encodeToString(payload)

        sosCorrelation.logStage(
            stage = "ALERT_DISPATCH",
            sourceContext = TAG,
            messageTemplate = "Dispatching SOS to backend. Lat={Lat}, Lng={Lng}, Source={Source}",
            properties = mapOf(
                "Lat" to lat.toString(),
                "Lng" to lng.toString(),
                "Source" to source.apiValue
            )
        )

        scope.launch {
            if (connectivityMonitor.isOnline()) {
                try {
                    val response = postTrigger(payloadJson)
                    if (response != null) {
                        _lastRequestId.value = response.requestId
                        _state.value = SosTriggerState.Active(
                            requestId = response.requestId,
                            responderCount = response.desiredResponderCount,
                            radiusMeters = response.radiusMeters
                        )

                        sosCorrelation.logStage(
                            stage = "ALERT_DISPATCH",
                            sourceContext = TAG,
                            messageTemplate = "SOS accepted by server. RequestId={RequestId}, " +
                                "Responders={Responders}, Radius={Radius}m",
                            properties = mapOf(
                                "RequestId" to response.requestId,
                                "Responders" to response.desiredResponderCount.toString(),
                                "Radius" to response.radiusMeters.toString()
                            )
                        )

                        // Play alert tone on successful dispatch
                        playAlertTone()
                        return@launch
                    }
                } catch (e: Exception) {
                    Log.e(TAG, "API call failed — falling back to offline queue", e)
                    logger.warning(
                        sourceContext = TAG,
                        messageTemplate = "SOS API call failed: {Error}. Queuing offline.",
                        properties = mapOf("Error" to (e.message ?: "Unknown")),
                        exception = e,
                        correlationId = sosCorrelation.currentId
                    )
                }
            }

            // Offline fallback — queue via SyncEngine with CRITICAL priority
            queueOffline(payloadJson, source)
        }
    }

    /**
     * POST /api/response/trigger with auth token + bypass token fallback.
     * Returns parsed response or null on failure.
     */
    private fun postTrigger(payloadJson: String): TriggerApiResponse? {
        val url = URL("$API_BASE_URL$TRIGGER_ENDPOINT")
        val conn = url.openConnection() as HttpURLConnection

        try {
            conn.requestMethod = "POST"
            conn.setRequestProperty("Content-Type", "application/json; charset=UTF-8")
            conn.setRequestProperty("Accept", "application/json")
            conn.connectTimeout = CONNECT_TIMEOUT_MS
            conn.readTimeout = READ_TIMEOUT_MS
            conn.doOutput = true

            // Auth: prefer Bearer token, always include bypass token as fallback
            bearerToken?.let { token ->
                if (token.isNotBlank()) {
                    conn.setRequestProperty("Authorization", "Bearer $token")
                }
            }
            sosBypassToken?.let { bypass ->
                if (bypass.isNotBlank()) {
                    conn.setRequestProperty("X-SOS-Bypass-Token", bypass)
                }
            }

            // Write payload
            conn.outputStream.use { os ->
                os.write(payloadJson.toByteArray(Charsets.UTF_8))
            }

            val responseCode = conn.responseCode

            // 202 Accepted is the success code from ResponseController.TriggerResponse
            if (responseCode in 200..299) {
                val responseBody = conn.inputStream.bufferedReader().use { it.readText() }
                return parseTriggerResponse(responseBody)
            } else {
                val errorBody = try {
                    conn.errorStream?.bufferedReader()?.use { it.readText() } ?: ""
                } catch (_: Exception) { "" }

                Log.e(TAG, "API returned HTTP $responseCode: $errorBody")

                // If auth failed (401/403), retry without Bearer (bypass only)
                if (responseCode in 401..403 && bearerToken != null) {
                    Log.w(TAG, "Auth failed — retrying with bypass token only")
                    bearerToken = null // Clear expired token
                    return postTrigger(payloadJson) // Recursive retry once
                }

                return null
            }
        } finally {
            conn.disconnect()
        }
    }

    /**
     * Parse the JSON response from the trigger endpoint.
     * Fields: RequestId, Scope, Strategy, Escalation, Status, RadiusMeters,
     *         DesiredResponderCount, CreatedAt
     */
    private fun parseTriggerResponse(body: String): TriggerApiResponse? {
        return try {
            // Simple JSON parsing — avoid pulling in full kotlinx.serialization
            // for response objects that may change
            val requestId = extractJsonString(body, "RequestId") ?: return null
            val status = extractJsonString(body, "Status") ?: "Unknown"
            val radiusMeters = extractJsonDouble(body, "RadiusMeters") ?: 5000.0
            val responderCount = extractJsonInt(body, "DesiredResponderCount") ?: 0

            TriggerApiResponse(
                requestId = requestId,
                status = status,
                radiusMeters = radiusMeters,
                desiredResponderCount = responderCount
            )
        } catch (e: Exception) {
            Log.e(TAG, "Failed to parse trigger response: ${e.message}")
            null
        }
    }

    // ── Offline Queue ─────────────────────────────────────────────

    private suspend fun queueOffline(payloadJson: String, source: SosTriggerSource) {
        val sosEventId = "sos-${UUID.randomUUID()}"

        syncEngine.enqueue(
            entityType = SyncEntityType.SOS_EVENT,
            entityId = sosEventId,
            action = SyncTaskAction.CREATE,
            payload = payloadJson,
            priority = SyncPriority.CRITICAL,
            userId = userId,
            idempotencyKey = "sos-trigger-$sosEventId"
        )

        _state.value = SosTriggerState.QueuedOffline
        _lastRequestId.value = sosEventId

        sosCorrelation.logStage(
            stage = "ALERT_DISPATCH",
            sourceContext = TAG,
            messageTemplate = "SOS queued offline with CRITICAL priority. " +
                "EventId={EventId}, Source={Source}",
            properties = mapOf(
                "EventId" to sosEventId,
                "Source" to source.apiValue,
                "Offline" to "true"
            )
        )

        // Play alert tone even when offline — user needs confirmation
        playAlertTone()

        logger.warning(
            sourceContext = TAG,
            messageTemplate = "SOS queued offline — will retry when connectivity returns",
            correlationId = sosCorrelation.currentId
        )
    }

    // ── Haptics ───────────────────────────────────────────────────

    private fun vibrateOnce(durationMs: Long, amplitude: Int = 128) {
        try {
            val vibrator = getVibrator()
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                vibrator.vibrate(
                    VibrationEffect.createOneShot(durationMs, amplitude.coerceIn(1, 255))
                )
            } else {
                @Suppress("DEPRECATION")
                vibrator.vibrate(durationMs)
            }
        } catch (e: Exception) {
            Log.w(TAG, "Haptic feedback failed: ${e.message}")
        }
    }

    private fun vibratePattern(pattern: LongArray) {
        try {
            val vibrator = getVibrator()
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                vibrator.vibrate(VibrationEffect.createWaveform(pattern, -1))
            } else {
                @Suppress("DEPRECATION")
                vibrator.vibrate(pattern, -1)
            }
        } catch (e: Exception) {
            Log.w(TAG, "Haptic pattern failed: ${e.message}")
        }
    }

    private fun getVibrator(): Vibrator {
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            val vibratorManager = context.getSystemService(Context.VIBRATOR_MANAGER_SERVICE) as VibratorManager
            vibratorManager.defaultVibrator
        } else {
            @Suppress("DEPRECATION")
            context.getSystemService(Context.VIBRATOR_SERVICE) as Vibrator
        }
    }

    // ── Alert Sound ───────────────────────────────────────────────

    private fun playAlertTone() {
        try {
            // Play a distinctive alert tone — 3 short beeps
            val toneGen = ToneGenerator(AudioManager.STREAM_ALARM, 100)
            scope.launch(Dispatchers.Main) {
                repeat(3) {
                    toneGen.startTone(ToneGenerator.TONE_CDMA_EMERGENCY_RINGBACK, 200)
                    delay(300)
                }
                delay(500)
                toneGen.release()
            }
        } catch (e: Exception) {
            Log.w(TAG, "Alert tone failed: ${e.message}")
        }
    }

    // ── JSON Helpers (minimal, no extra deps) ─────────────────────

    private fun extractJsonString(json: String, key: String): String? {
        val pattern = "\"$key\"\\s*:\\s*\"([^\"]*)\""
        return Regex(pattern).find(json)?.groupValues?.getOrNull(1)
    }

    private fun extractJsonDouble(json: String, key: String): Double? {
        val pattern = "\"$key\"\\s*:\\s*([\\d.]+)"
        return Regex(pattern).find(json)?.groupValues?.getOrNull(1)?.toDoubleOrNull()
    }

    private fun extractJsonInt(json: String, key: String): Int? {
        val pattern = "\"$key\"\\s*:\\s*(\\d+)"
        return Regex(pattern).find(json)?.groupValues?.getOrNull(1)?.toIntOrNull()
    }
}

/**
 * Parsed response from POST /api/response/trigger.
 */
data class TriggerApiResponse(
    val requestId: String,
    val status: String,
    val radiusMeters: Double,
    val desiredResponderCount: Int
)
