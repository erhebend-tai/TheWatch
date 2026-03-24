// PhraseDetectionService — foreground service for continuous on-device speech recognition.
// Speech-to-text happens on-device (no cloud). Transcribed text is fed to the
// deterministic PhraseMatchingEngine for emergency phrase detection.
// Audio NEVER leaves the device.

package com.thewatch.app.service

import android.Manifest
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.Bundle
import android.os.IBinder
import android.speech.RecognitionListener
import android.speech.RecognizerIntent
import android.speech.SpeechRecognizer
import android.util.Log
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

/**
 * Foreground service that performs continuous on-device speech recognition.
 *
 * Architecture:
 * - SpeechRecognizer (Android native) handles audio → text transcription (ML is fine here)
 * - Transcribed text is emitted via [transcriptionFlow] for the PhraseMatchingEngine
 * - The matching engine (deterministic, DSL-compliant) runs in the ViewModel/Repository layer
 * - This service ONLY does transcription — it does not make matching decisions
 *
 * Restart strategy: SpeechRecognizer has a ~60s timeout. We auto-restart listening
 * after each result or error to maintain continuous monitoring.
 */
@AndroidEntryPoint
class PhraseDetectionService : Service() {

    companion object {
        private const val TAG = "PhraseDetection"
        private const val CHANNEL_ID = "phrase_detection_channel"
        private const val NOTIFICATION_ID = 3001
        private const val RESTART_DELAY_MS = 500L
        private const val ERROR_BACKOFF_MS = 2000L

        // Actions
        const val ACTION_START = "com.thewatch.app.action.START_PHRASE_DETECTION"
        const val ACTION_STOP = "com.thewatch.app.action.STOP_PHRASE_DETECTION"

        // State flows — accessible from ViewModel/Repository layer
        private val _isListening = MutableStateFlow(false)
        val isListening: StateFlow<Boolean> = _isListening.asStateFlow()

        private val _transcriptionFlow = MutableSharedFlow<TranscriptionEvent>(
            replay = 0,
            extraBufferCapacity = 16
        )
        val transcriptionFlow: SharedFlow<TranscriptionEvent> = _transcriptionFlow.asSharedFlow()

        private val _isAvailable = MutableStateFlow(false)
        val isAvailable: StateFlow<Boolean> = _isAvailable.asStateFlow()

        fun start(context: Context) {
            val intent = Intent(context, PhraseDetectionService::class.java).apply {
                action = ACTION_START
            }
            ContextCompat.startForegroundService(context, intent)
        }

        fun stop(context: Context) {
            val intent = Intent(context, PhraseDetectionService::class.java).apply {
                action = ACTION_STOP
            }
            context.startService(intent)
        }
    }

    private val serviceScope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private var speechRecognizer: SpeechRecognizer? = null
    private var shouldBeListening = false
    private var consecutiveErrors = 0
    private val maxConsecutiveErrors = 5

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
        _isAvailable.value = SpeechRecognizer.isRecognitionAvailable(this)
        Log.d(TAG, "Service created. Speech recognition available: ${_isAvailable.value}")
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_START -> startListening()
            ACTION_STOP -> stopListening()
        }
        return START_STICKY // Restart if killed by system
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onDestroy() {
        stopListening()
        serviceScope.cancel()
        super.onDestroy()
    }

    // ─────────────────────────────────────────────
    // Listening lifecycle
    // ─────────────────────────────────────────────

    private fun startListening() {
        if (!SpeechRecognizer.isRecognitionAvailable(this)) {
            Log.w(TAG, "Speech recognition not available on this device")
            _isAvailable.value = false
            return
        }

        if (ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO)
            != PackageManager.PERMISSION_GRANTED
        ) {
            Log.w(TAG, "RECORD_AUDIO permission not granted")
            return
        }

        shouldBeListening = true

        // Start foreground with notification
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(
                NOTIFICATION_ID,
                buildNotification(),
                ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE
            )
        } else {
            startForeground(NOTIFICATION_ID, buildNotification())
        }

        initializeAndStartRecognition()
    }

    private fun stopListening() {
        shouldBeListening = false
        _isListening.value = false

        try {
            speechRecognizer?.stopListening()
            speechRecognizer?.destroy()
        } catch (e: Exception) {
            Log.w(TAG, "Error stopping recognizer", e)
        }
        speechRecognizer = null

        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    private fun initializeAndStartRecognition() {
        // Destroy previous instance
        speechRecognizer?.destroy()

        speechRecognizer = SpeechRecognizer.createSpeechRecognizer(this).apply {
            setRecognitionListener(PhraseRecognitionListener())
        }

        startRecognitionIntent()
    }

    private fun startRecognitionIntent() {
        val intent = Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH).apply {
            putExtra(
                RecognizerIntent.EXTRA_LANGUAGE_MODEL,
                RecognizerIntent.LANGUAGE_MODEL_FREE_FORM
            )
            // Request partial results for faster response
            putExtra(RecognizerIntent.EXTRA_PARTIAL_RESULTS, true)
            // Prefer on-device recognition (Android 11+)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                putExtra(RecognizerIntent.EXTRA_PREFER_OFFLINE, true)
            }
            // No limit on speech length
            putExtra(RecognizerIntent.EXTRA_SPEECH_INPUT_MINIMUM_LENGTH_MILLIS, 30_000L)
            putExtra(
                RecognizerIntent.EXTRA_SPEECH_INPUT_POSSIBLY_COMPLETE_SILENCE_LENGTH_MILLIS,
                5_000L
            )
            putExtra(
                RecognizerIntent.EXTRA_SPEECH_INPUT_COMPLETE_SILENCE_LENGTH_MILLIS,
                10_000L
            )
        }

        try {
            speechRecognizer?.startListening(intent)
            _isListening.value = true
            Log.d(TAG, "Started speech recognition")
        } catch (e: Exception) {
            Log.e(TAG, "Failed to start recognition", e)
            scheduleRestart(ERROR_BACKOFF_MS)
        }
    }

    /**
     * Restart recognition after a delay.
     * SpeechRecognizer has a built-in timeout (~60s), so we must
     * continuously restart to maintain monitoring.
     */
    private fun scheduleRestart(delayMs: Long = RESTART_DELAY_MS) {
        if (!shouldBeListening) return

        serviceScope.launch {
            delay(delayMs)
            if (shouldBeListening) {
                Log.d(TAG, "Restarting speech recognition")
                initializeAndStartRecognition()
            }
        }
    }

    // ─────────────────────────────────────────────
    // Recognition listener
    // ─────────────────────────────────────────────

    private inner class PhraseRecognitionListener : RecognitionListener {

        override fun onReadyForSpeech(params: Bundle?) {
            consecutiveErrors = 0
            _isListening.value = true
            Log.d(TAG, "Ready for speech")
        }

        override fun onBeginningOfSpeech() {
            Log.d(TAG, "Speech started")
        }

        override fun onRmsChanged(rmsdB: Float) {
            // Audio level — could be used for UI feedback
        }

        override fun onBufferReceived(buffer: ByteArray?) {
            // Raw audio buffer — not needed, we use transcription
        }

        override fun onEndOfSpeech() {
            Log.d(TAG, "Speech ended")
        }

        override fun onError(error: Int) {
            val errorName = when (error) {
                SpeechRecognizer.ERROR_AUDIO -> "ERROR_AUDIO"
                SpeechRecognizer.ERROR_CLIENT -> "ERROR_CLIENT"
                SpeechRecognizer.ERROR_INSUFFICIENT_PERMISSIONS -> "ERROR_PERMISSIONS"
                SpeechRecognizer.ERROR_NETWORK -> "ERROR_NETWORK"
                SpeechRecognizer.ERROR_NETWORK_TIMEOUT -> "ERROR_NETWORK_TIMEOUT"
                SpeechRecognizer.ERROR_NO_MATCH -> "ERROR_NO_MATCH"
                SpeechRecognizer.ERROR_RECOGNIZER_BUSY -> "ERROR_BUSY"
                SpeechRecognizer.ERROR_SERVER -> "ERROR_SERVER"
                SpeechRecognizer.ERROR_SPEECH_TIMEOUT -> "ERROR_SPEECH_TIMEOUT"
                else -> "ERROR_UNKNOWN($error)"
            }
            Log.d(TAG, "Recognition error: $errorName")

            // NO_MATCH and SPEECH_TIMEOUT are normal — user wasn't speaking
            // Just restart immediately
            when (error) {
                SpeechRecognizer.ERROR_NO_MATCH,
                SpeechRecognizer.ERROR_SPEECH_TIMEOUT -> {
                    consecutiveErrors = 0
                    scheduleRestart()
                }
                SpeechRecognizer.ERROR_INSUFFICIENT_PERMISSIONS -> {
                    // Can't recover without user action
                    Log.e(TAG, "Insufficient permissions — stopping")
                    stopListening()
                }
                else -> {
                    consecutiveErrors++
                    if (consecutiveErrors >= maxConsecutiveErrors) {
                        Log.e(TAG, "Too many consecutive errors ($consecutiveErrors) — backing off")
                        consecutiveErrors = 0
                        scheduleRestart(ERROR_BACKOFF_MS * 5)
                    } else {
                        scheduleRestart(ERROR_BACKOFF_MS)
                    }
                }
            }
        }

        override fun onResults(results: Bundle?) {
            val matches = results?.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION)
            val confidences = results?.getFloatArray(SpeechRecognizer.CONFIDENCE_SCORES)

            if (!matches.isNullOrEmpty()) {
                val text = matches[0]
                val confidence = confidences?.getOrNull(0) ?: 0.5f

                Log.d(TAG, "Final result: \"$text\" (confidence: $confidence)")

                serviceScope.launch {
                    _transcriptionFlow.emit(
                        TranscriptionEvent(
                            text = text,
                            confidence = confidence,
                            isFinal = true,
                            timestamp = System.currentTimeMillis()
                        )
                    )
                }
            }

            // Restart for continuous listening
            scheduleRestart()
        }

        override fun onPartialResults(partialResults: Bundle?) {
            val matches = partialResults?.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION)

            if (!matches.isNullOrEmpty()) {
                val text = matches[0]
                Log.d(TAG, "Partial: \"$text\"")

                serviceScope.launch {
                    _transcriptionFlow.emit(
                        TranscriptionEvent(
                            text = text,
                            confidence = 0.5f, // Partials don't have confidence scores
                            isFinal = false,
                            timestamp = System.currentTimeMillis()
                        )
                    )
                }
            }
        }

        override fun onEvent(eventType: Int, params: Bundle?) {
            Log.d(TAG, "Recognition event: $eventType")
        }
    }

    // ─────────────────────────────────────────────
    // Notification
    // ─────────────────────────────────────────────

    private fun createNotificationChannel() {
        val channel = NotificationChannel(
            CHANNEL_ID,
            "Phrase Detection",
            NotificationManager.IMPORTANCE_LOW
        ).apply {
            description = "Listening for emergency phrases"
            setShowBadge(false)
        }
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(channel)
    }

    private fun buildNotification(): Notification {
        val stopIntent = Intent(this, PhraseDetectionService::class.java).apply {
            action = ACTION_STOP
        }
        val stopPendingIntent = PendingIntent.getService(
            this, 0, stopIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle("Safety Monitoring Active")
            .setContentText("Listening for emergency phrases")
            .setSmallIcon(android.R.drawable.ic_btn_speak_now)
            .setOngoing(true)
            .setSilent(true)
            .addAction(
                android.R.drawable.ic_media_pause,
                "Stop",
                stopPendingIntent
            )
            .build()
    }
}

/**
 * Transcription event emitted by the PhraseDetectionService.
 * Consumed by PhraseDetectionRepository which runs the deterministic matching engine.
 */
data class TranscriptionEvent(
    val text: String,
    val confidence: Float,
    val isFinal: Boolean,
    val timestamp: Long
)
