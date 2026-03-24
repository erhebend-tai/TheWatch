/**
 * ═══════════════════════════════════════════════════════════════════════════════
 * WRITE-AHEAD LOG — MockCheckInScheduleAdapter.kt
 * ═══════════════════════════════════════════════════════════════════════════════
 * Purpose:   Mock adapter for CheckInSchedulePort. In-memory implementation for
 *            development and UI testing. Production would use WorkManager
 *            PeriodicWorkRequest with ExistingPeriodicWorkPolicy.UPDATE.
 * Date:      2026-03-24
 * Author:    Claude (Anthropic)
 * Deps:      CheckInSchedulePort, kotlinx.coroutines
 * Package:   com.thewatch.app.data.checkin.mock
 *
 * Usage Example:
 *   @Provides fun provideCheckInSchedulePort(): CheckInSchedulePort =
 *       MockCheckInScheduleAdapter()
 * ═══════════════════════════════════════════════════════════════════════════════
 */
package com.thewatch.app.data.checkin.mock

import com.thewatch.app.data.checkin.CheckInEvent
import com.thewatch.app.data.checkin.CheckInInterval
import com.thewatch.app.data.checkin.CheckInSchedule
import com.thewatch.app.data.checkin.CheckInSchedulePort
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import java.util.UUID
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class MockCheckInScheduleAdapter @Inject constructor() : CheckInSchedulePort {

    private val _checkInEvents = MutableSharedFlow<CheckInEvent>(replay = 1)
    override val checkInEvents: SharedFlow<CheckInEvent> = _checkInEvents.asSharedFlow()

    private val _currentSchedule = MutableStateFlow<CheckInSchedule?>(null)
    override val currentSchedule: StateFlow<CheckInSchedule?> = _currentSchedule.asStateFlow()

    private val schedules = mutableMapOf<String, CheckInSchedule>()

    override suspend fun getSchedule(userId: String): CheckInSchedule {
        return schedules.getOrPut(userId) { CheckInSchedule(userId = userId) }
    }

    override suspend fun setSchedule(userId: String, interval: CheckInInterval): Result<Unit> {
        val current = getSchedule(userId)
        val updated = current.copy(interval = interval)
        schedules[userId] = updated
        _currentSchedule.value = updated
        return Result.success(Unit)
    }

    override suspend fun setCustomIntervalMinutes(userId: String, minutes: Int): Result<Unit> {
        val clamped = minutes.coerceIn(15, 1440)
        val current = getSchedule(userId)
        val updated = current.copy(
            interval = CheckInInterval.CUSTOM,
            customIntervalMinutes = clamped
        )
        schedules[userId] = updated
        _currentSchedule.value = updated
        return Result.success(Unit)
    }

    override suspend fun setQuietHours(userId: String, startHour: Int, endHour: Int): Result<Unit> {
        val current = getSchedule(userId)
        val updated = current.copy(
            startHour = startHour.coerceIn(0, 23),
            endHour = endHour.coerceIn(0, 23)
        )
        schedules[userId] = updated
        _currentSchedule.value = updated
        return Result.success(Unit)
    }

    override suspend fun enable(userId: String): Result<Unit> {
        val current = getSchedule(userId)
        val nextTimestamp = System.currentTimeMillis() + (current.effectiveMinutes * 60_000L)
        val updated = current.copy(
            enabled = true,
            nextCheckInTimestamp = nextTimestamp
        )
        schedules[userId] = updated
        _currentSchedule.value = updated
        _checkInEvents.emit(CheckInEvent.Scheduled(userId, nextTimestamp))
        return Result.success(Unit)
    }

    override suspend fun disable(userId: String): Result<Unit> {
        val current = getSchedule(userId)
        val updated = current.copy(enabled = false, nextCheckInTimestamp = null)
        schedules[userId] = updated
        _currentSchedule.value = updated
        _checkInEvents.emit(CheckInEvent.Disabled(userId))
        return Result.success(Unit)
    }

    override suspend fun acknowledge(userId: String, checkInId: String): Result<Unit> {
        val now = System.currentTimeMillis()
        _checkInEvents.emit(CheckInEvent.Acknowledged(userId, checkInId, now))

        // Schedule next check-in
        val current = getSchedule(userId)
        if (current.enabled) {
            val nextTimestamp = now + (current.effectiveMinutes * 60_000L)
            val updated = current.copy(
                lastCheckInTimestamp = now,
                nextCheckInTimestamp = nextTimestamp
            )
            schedules[userId] = updated
            _currentSchedule.value = updated
            _checkInEvents.emit(CheckInEvent.Scheduled(userId, nextTimestamp))
        }
        return Result.success(Unit)
    }

    override suspend fun getSecondsUntilNext(userId: String): Long? {
        val schedule = schedules[userId] ?: return null
        val next = schedule.nextCheckInTimestamp ?: return null
        val remaining = (next - System.currentTimeMillis()) / 1_000
        return if (remaining > 0) remaining else null
    }
}
