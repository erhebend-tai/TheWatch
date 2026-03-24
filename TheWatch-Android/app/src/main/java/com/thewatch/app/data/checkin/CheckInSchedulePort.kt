/**
 * ═══════════════════════════════════════════════════════════════════════════════
 * WRITE-AHEAD LOG — CheckInSchedulePort.kt
 * ═══════════════════════════════════════════════════════════════════════════════
 * Purpose:   Hexagonal port for the periodic check-in reminder system.
 *            Configurable intervals: daily (24h), 12h, 6h, or custom (1-1440 min).
 *            Check-ins are delivered as notifications. If the user doesn't respond
 *            within the auto-escalation timer, the escalation chain fires.
 * Date:      2026-03-24
 * Author:    Claude (Anthropic)
 * Deps:      kotlinx.coroutines.flow
 * Package:   com.thewatch.app.data.checkin
 *
 * Usage Example:
 *   @Inject lateinit var checkInPort: CheckInSchedulePort
 *
 *   // Set a 6-hour check-in schedule
 *   checkInPort.setSchedule("user_001", CheckInInterval.EVERY_6H)
 *
 *   // Or custom interval
 *   checkInPort.setCustomIntervalMinutes("user_001", 90) // every 90 minutes
 *
 *   // Enable the schedule
 *   checkInPort.enable("user_001")
 *
 *   // Observe check-in events
 *   checkInPort.checkInEvents.collect { event ->
 *       when (event) {
 *           is CheckInEvent.Scheduled -> { /* next check-in set */ }
 *           is CheckInEvent.Sent -> { /* notification delivered */ }
 *           is CheckInEvent.Acknowledged -> { /* user confirmed OK */ }
 *           is CheckInEvent.Missed -> { /* triggers auto-escalation */ }
 *       }
 *   }
 *
 * WorkManager Integration (production):
 *   - PeriodicWorkRequest with flex interval for battery optimization
 *   - ExistingPeriodicWorkPolicy.UPDATE to change intervals without duplicate work
 *   - Constraints: NetworkType.CONNECTED for cloud sync
 *   - setExpedited() for time-sensitive check-ins
 *
 * Related:
 *   - AutoEscalationPort — triggered when check-in is missed
 *   - NotificationService — delivers the check-in notification
 *   - WorkManager setup in TheWatchApplication.kt
 * ═══════════════════════════════════════════════════════════════════════════════
 */
package com.thewatch.app.data.checkin

import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow

/**
 * Preset check-in intervals.
 */
enum class CheckInInterval(val displayName: String, val minutes: Int) {
    DAILY("Every 24 hours", 1440),
    EVERY_12H("Every 12 hours", 720),
    EVERY_6H("Every 6 hours", 360),
    CUSTOM("Custom interval", -1);

    companion object {
        fun fromMinutes(minutes: Int): CheckInInterval = when (minutes) {
            1440 -> DAILY
            720 -> EVERY_12H
            360 -> EVERY_6H
            else -> CUSTOM
        }
    }
}

/**
 * Full check-in schedule configuration.
 */
data class CheckInSchedule(
    val userId: String,
    val interval: CheckInInterval = CheckInInterval.DAILY,
    val customIntervalMinutes: Int? = null,
    val enabled: Boolean = false,
    val startHour: Int = 8,   // Don't send before this hour (0-23)
    val endHour: Int = 22,    // Don't send after this hour (0-23)
    val lastCheckInTimestamp: Long? = null,
    val nextCheckInTimestamp: Long? = null
) {
    /** Effective interval in minutes, resolving CUSTOM to the custom value */
    val effectiveMinutes: Int
        get() = if (interval == CheckInInterval.CUSTOM) {
            customIntervalMinutes ?: 360 // default to 6h if custom not set
        } else {
            interval.minutes
        }
}

/**
 * Events in the check-in lifecycle.
 */
sealed class CheckInEvent {
    data class Scheduled(val userId: String, val nextTimestamp: Long) : CheckInEvent()
    data class Sent(val userId: String, val checkInId: String, val timestamp: Long) : CheckInEvent()
    data class Acknowledged(val userId: String, val checkInId: String, val timestamp: Long) : CheckInEvent()
    data class Missed(val userId: String, val checkInId: String, val timestamp: Long) : CheckInEvent()
    data class Disabled(val userId: String) : CheckInEvent()
}

/**
 * Hexagonal port for check-in schedule management.
 *
 * Adapters:
 *   - MockCheckInScheduleAdapter  — in-memory for dev/testing
 *   - (Future) WorkManagerCheckInAdapter — production with PeriodicWorkRequest
 */
interface CheckInSchedulePort {

    /** Observable stream of check-in events */
    val checkInEvents: SharedFlow<CheckInEvent>

    /** Current schedule as observable state */
    val currentSchedule: StateFlow<CheckInSchedule?>

    /** Get the check-in schedule for a user */
    suspend fun getSchedule(userId: String): CheckInSchedule

    /** Set a preset interval */
    suspend fun setSchedule(userId: String, interval: CheckInInterval): Result<Unit>

    /** Set a custom interval in minutes (min 15, max 1440) */
    suspend fun setCustomIntervalMinutes(userId: String, minutes: Int): Result<Unit>

    /** Set quiet hours (no check-ins sent outside this window) */
    suspend fun setQuietHours(userId: String, startHour: Int, endHour: Int): Result<Unit>

    /** Enable the check-in schedule (starts WorkManager periodic work) */
    suspend fun enable(userId: String): Result<Unit>

    /** Disable the check-in schedule (cancels WorkManager periodic work) */
    suspend fun disable(userId: String): Result<Unit>

    /** Acknowledge a check-in (user is OK) */
    suspend fun acknowledge(userId: String, checkInId: String): Result<Unit>

    /** Get time until next check-in in seconds, or null if disabled */
    suspend fun getSecondsUntilNext(userId: String): Long?
}
