package com.thewatch.app.testing

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import androidx.core.app.NotificationCompat
import com.thewatch.app.MainActivity
import com.thewatch.app.R
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.StateFlow

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: TestRunnerNotification manages a foreground notification that shows the
// current test execution status. It observes TestRunnerService.runState and
// updates the notification in real time as steps execute.
//
// Notification content examples:
//   - "Running: Authentication Flow — Step 3/8: TypeText email_field"
//   - "PASSED: Authentication Flow — 8/8 steps passed"
//   - "FAILED: SOS Trigger Pipeline — Step 5 failed: Assert alert_status"
//   - "Cancelled: Phrase Detection"
//
// The notification uses a dedicated channel "test_runner" with low importance
// (no sound/vibration) to avoid interfering with real SOS notifications.
//
// Example:
// ```kotlin
// val notification = TestRunnerNotification(context)
// notification.startObserving(testRunnerService.runState)
// // ... later
// notification.stopObserving()
// ```

/**
 * Manages the foreground notification for active test runs.
 * Observes [TestRunnerService.runState] and updates notification content in real time.
 */
class TestRunnerNotification(private val context: Context) {

    companion object {
        const val CHANNEL_ID = "test_runner"
        const val CHANNEL_NAME = "Test Runner"
        const val NOTIFICATION_ID = 9001
    }

    private val notificationManager =
        context.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
    private var observeJob: Job? = null
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)

    init {
        createChannel()
    }

    private fun createChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                CHANNEL_ID,
                CHANNEL_NAME,
                NotificationManager.IMPORTANCE_LOW // No sound — don't interfere with SOS notifications
            ).apply {
                description = "Shows test execution progress from the MAUI dashboard"
                setShowBadge(false)
                enableVibration(false)
                enableLights(false)
            }
            notificationManager.createNotificationChannel(channel)
        }
    }

    /**
     * Start observing the test runner state and updating the notification.
     */
    fun startObserving(runState: StateFlow<TestRunState?>) {
        stopObserving()
        observeJob = scope.launch {
            runState.collect { state ->
                if (state != null) {
                    show(state)
                } else {
                    dismiss()
                }
            }
        }
    }

    /**
     * Stop observing and dismiss the notification.
     */
    fun stopObserving() {
        observeJob?.cancel()
        observeJob = null
        dismiss()
    }

    /**
     * Build and show/update the notification for the current run state.
     */
    private fun show(state: TestRunState) {
        val notification = buildNotification(state)
        notificationManager.notify(NOTIFICATION_ID, notification)
    }

    /**
     * Dismiss the test runner notification.
     */
    private fun dismiss() {
        notificationManager.cancel(NOTIFICATION_ID)
    }

    /**
     * Build the notification content based on the current run state.
     */
    private fun buildNotification(state: TestRunState): Notification {
        val (title, content, progress) = when (state.status) {
            TestRunStatus.Running -> {
                val step = state.currentStep
                val stepInfo = if (step != null) {
                    "Step ${step.order}: ${step.action} ${step.target}"
                } else {
                    "Waiting for next step..."
                }
                Triple(
                    "Running: ${state.suiteName.ifEmpty { state.runId }}",
                    stepInfo,
                    state.completedSteps to state.totalSteps
                )
            }
            TestRunStatus.Passed -> Triple(
                "PASSED: ${state.suiteName.ifEmpty { state.runId }}",
                "${state.passedSteps}/${state.totalSteps} steps passed",
                state.totalSteps to state.totalSteps
            )
            TestRunStatus.Failed -> Triple(
                "FAILED: ${state.suiteName.ifEmpty { state.runId }}",
                "${state.failedSteps} step(s) failed, ${state.passedSteps} passed",
                state.completedSteps to state.totalSteps
            )
            TestRunStatus.Cancelled -> Triple(
                "Cancelled: ${state.suiteName.ifEmpty { state.runId }}",
                "${state.completedSteps}/${state.totalSteps} steps completed before cancellation",
                state.completedSteps to state.totalSteps
            )
            TestRunStatus.Idle -> Triple(
                "Test Runner",
                "Idle — waiting for test dispatch",
                0 to 0
            )
        }

        val tapIntent = Intent(context, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_SINGLE_TOP
        }
        val pendingIntent = PendingIntent.getActivity(
            context, 0, tapIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        return NotificationCompat.Builder(context, CHANNEL_ID)
            .setSmallIcon(android.R.drawable.ic_menu_manage) // TODO: replace with app icon
            .setContentTitle(title)
            .setContentText(content)
            .setContentIntent(pendingIntent)
            .setOngoing(state.status == TestRunStatus.Running)
            .setAutoCancel(state.status != TestRunStatus.Running)
            .apply {
                if (progress.second > 0) {
                    setProgress(progress.second, progress.first, false)
                }
            }
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .setCategory(NotificationCompat.CATEGORY_PROGRESS)
            .build()
    }
}
