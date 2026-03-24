/**
 * ═══════════════════════════════════════════════════════════════════════════════
 * WRITE-AHEAD LOG — MockAutoEscalationAdapter.kt
 * ═══════════════════════════════════════════════════════════════════════════════
 * Purpose:   Mock adapter for AutoEscalationPort. In-memory implementation
 *            for development and UI testing. Simulates timer behavior with
 *            coroutine delays. Production adapter would use WorkManager +
 *            AlarmManager for reliable background execution.
 * Date:      2026-03-24
 * Author:    Claude (Anthropic)
 * Deps:      kotlinx.coroutines, AutoEscalationPort
 * Package:   com.thewatch.app.data.settings.mock
 *
 * Usage Example:
 *   // Injected via Hilt in AppModule:
 *   @Provides fun provideAutoEscalationPort(): AutoEscalationPort = MockAutoEscalationAdapter()
 *
 *   // Then in ViewModel:
 *   val port: AutoEscalationPort = ... // injected
 *   port.setTimerMinutes("user_001", 10)
 *   port.startEscalationTimer("user_001")
 * ═══════════════════════════════════════════════════════════════════════════════
 */
package com.thewatch.app.data.settings.mock

import com.thewatch.app.data.settings.AutoEscalationPort
import com.thewatch.app.data.settings.EscalationConfig
import com.thewatch.app.data.settings.EscalationEvent
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.launch
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class MockAutoEscalationAdapter @Inject constructor() : AutoEscalationPort {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private val _escalationEvents = MutableSharedFlow<EscalationEvent>(replay = 1)
    override val escalationEvents: SharedFlow<EscalationEvent> = _escalationEvents.asSharedFlow()

    private val configs = mutableMapOf<String, EscalationConfig>()
    private val activeTimers = mutableMapOf<String, Job>()
    private val timerStartTimes = mutableMapOf<String, Long>()

    override suspend fun getConfig(userId: String): EscalationConfig {
        return configs.getOrPut(userId) { EscalationConfig(userId = userId) }
    }

    override suspend fun saveConfig(config: EscalationConfig): Result<Unit> {
        configs[config.userId] = config
        return Result.success(Unit)
    }

    override suspend fun setTimerMinutes(userId: String, minutes: Int): Result<Unit> {
        val current = getConfig(userId)
        configs[userId] = current.copy(timerMinutes = minutes.coerceIn(5, 120))
        return Result.success(Unit)
    }

    override suspend fun setAutoEscalateTo911(userId: String, enabled: Boolean): Result<Unit> {
        val current = getConfig(userId)
        configs[userId] = current.copy(autoEscalateTo911 = enabled)
        return Result.success(Unit)
    }

    override suspend fun startEscalationTimer(userId: String): Result<Unit> {
        // Cancel any existing timer first
        activeTimers[userId]?.cancel()

        val config = getConfig(userId)
        if (!config.enabled) return Result.success(Unit)

        val durationMs = config.timerMinutes * 60_000L
        timerStartTimes[userId] = System.currentTimeMillis()

        _escalationEvents.emit(EscalationEvent.TimerStarted(userId, config.timerMinutes))

        activeTimers[userId] = scope.launch {
            // 50% reminder
            if (config.reminderAt50Percent) {
                delay(durationMs / 2)
                _escalationEvents.emit(EscalationEvent.ReminderSent(userId, 50))
            }

            // 75% reminder
            if (config.reminderAt75Percent) {
                delay(durationMs / 4) // additional 25% after 50%
                _escalationEvents.emit(EscalationEvent.ReminderSent(userId, 75))
            }

            // Remaining time until 100%
            delay(durationMs / 4)

            // Timer expired — escalate
            _escalationEvents.emit(
                EscalationEvent.ContactsNotified(
                    userId = userId,
                    contactIds = listOf("contact_001", "contact_002", "contact_003"),
                    timestamp = System.currentTimeMillis()
                )
            )

            if (config.autoEscalateTo911) {
                delay(5_000) // 5-second grace period before 911
                _escalationEvents.emit(
                    EscalationEvent.EmergencyServicesDialed(
                        userId = userId,
                        timestamp = System.currentTimeMillis(),
                        ng911SessionId = "mock_ng911_${System.currentTimeMillis()}"
                    )
                )
            }

            activeTimers.remove(userId)
            timerStartTimes.remove(userId)
        }

        return Result.success(Unit)
    }

    override suspend fun cancelEscalation(userId: String): Result<Unit> {
        activeTimers[userId]?.cancel()
        activeTimers.remove(userId)
        timerStartTimes.remove(userId)
        _escalationEvents.emit(EscalationEvent.Cancelled(userId))
        return Result.success(Unit)
    }

    override suspend fun isTimerActive(userId: String): Boolean {
        return activeTimers[userId]?.isActive == true
    }

    override suspend fun getRemainingSeconds(userId: String): Long? {
        val startTime = timerStartTimes[userId] ?: return null
        val config = getConfig(userId)
        val elapsed = System.currentTimeMillis() - startTime
        val totalMs = config.timerMinutes * 60_000L
        val remaining = (totalMs - elapsed) / 1_000
        return if (remaining > 0) remaining else null
    }

    override suspend fun forceEscalate(userId: String): Result<Unit> {
        activeTimers[userId]?.cancel()
        activeTimers.remove(userId)
        timerStartTimes.remove(userId)

        val config = getConfig(userId)
        _escalationEvents.emit(
            EscalationEvent.ContactsNotified(
                userId = userId,
                contactIds = listOf("contact_001", "contact_002", "contact_003"),
                timestamp = System.currentTimeMillis()
            )
        )
        if (config.autoEscalateTo911) {
            _escalationEvents.emit(
                EscalationEvent.EmergencyServicesDialed(
                    userId = userId,
                    timestamp = System.currentTimeMillis(),
                    ng911SessionId = "mock_force_${System.currentTimeMillis()}"
                )
            )
        }
        return Result.success(Unit)
    }
}
