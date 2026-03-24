package com.thewatch.app.data.sos

import com.thewatch.app.data.logging.LogEntry
import com.thewatch.app.data.logging.LoggingPort
import javax.inject.Inject
import javax.inject.Singleton

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: SosTimelineBuilder queries the LoggingPort by correlationId to reconstruct
// the full SOS lifecycle as a sorted timeline. This powers both:
//   1. On-device history view (HistoryScreen / SOS detail)
//   2. MAUI dashboard timeline (MobileLogController → SignalR → DashboardHub)
//
// The timeline is sorted chronologically and annotated with stage labels
// extracted from the "Stage" property in each log entry.
//
// Example usage:
// ```kotlin
// @Inject lateinit var timeline: SosTimelineBuilder
//
// val events = timeline.build("abc-123-def")
// events.forEach { event ->
//     println("${event.timestamp} [${event.stage}] ${event.renderedMessage}")
// }
//
// val summary = timeline.summarize("abc-123-def")
// println("SOS ${summary.correlationId}: ${summary.triggerMethod} → ${summary.resolution}")
// println("Duration: ${summary.durationFormatted}, Stages: ${summary.stageCount}")
// ```

/**
 * A single event in the SOS timeline, enriched with stage metadata.
 *
 * @property logEntry The underlying structured log entry
 * @property stage Pipeline stage label (TRIGGER, CONFIRMATION, ALERT_DISPATCH, etc.)
 * @property timestamp Epoch millis for sorting
 * @property renderedMessage Human-readable message with properties substituted
 */
data class SosTimelineEvent(
    val logEntry: LogEntry,
    val stage: String?,
    val timestamp: Long,
    val renderedMessage: String
) {
    companion object {
        fun from(entry: LogEntry): SosTimelineEvent {
            return SosTimelineEvent(
                logEntry = entry,
                stage = entry.properties["Stage"],
                timestamp = entry.timestamp.toEpochMilli(),
                renderedMessage = entry.renderedMessage()
            )
        }
    }
}

/**
 * Summary of a complete SOS lifecycle — computed from the timeline entries.
 *
 * @property correlationId The SOS correlation ID
 * @property triggerMethod How the SOS was initiated (Phrase, QuickTap, Manual, etc.)
 * @property resolution How the SOS ended (UserResolved, Timeout, etc.) — null if still active
 * @property triggerTimestamp When the SOS started
 * @property resolutionTimestamp When the SOS ended — null if still active
 * @property durationMs Total SOS duration in milliseconds — null if still active
 * @property durationFormatted Human-readable duration (e.g. "4m 32s")
 * @property stageCount Number of distinct pipeline stages logged
 * @property entryCount Total number of log entries in the lifecycle
 * @property stages Ordered list of distinct stage names encountered
 * @property hasEscalation Whether the SOS escalated (NG911, first responder dispatch)
 * @property volunteerCount Number of unique volunteers who acknowledged
 */
data class SosTimelineSummary(
    val correlationId: String,
    val triggerMethod: String?,
    val resolution: String?,
    val triggerTimestamp: Long?,
    val resolutionTimestamp: Long?,
    val durationMs: Long?,
    val durationFormatted: String?,
    val stageCount: Int,
    val entryCount: Int,
    val stages: List<String>,
    val hasEscalation: Boolean,
    val volunteerCount: Int
)

/**
 * Builds a chronological timeline of an SOS lifecycle from log entries.
 *
 * Queries [LoggingPort.queryByCorrelation] and transforms raw log entries into
 * an ordered, annotated timeline that both on-device UI and MAUI dashboard consume.
 *
 * Thread-safe: all operations are suspend functions delegating to the port.
 */
@Singleton
class SosTimelineBuilder @Inject constructor(
    private val loggingPort: LoggingPort
) {

    /**
     * Build a sorted timeline for the given correlationId.
     *
     * @param correlationId The SOS correlation ID to query
     * @return List of timeline events, sorted chronologically (oldest first)
     */
    suspend fun build(correlationId: String): List<SosTimelineEvent> {
        val entries = loggingPort.queryByCorrelation(correlationId)
        return entries
            .sortedBy { it.timestamp }
            .map { SosTimelineEvent.from(it) }
    }

    /**
     * Build a summary of the SOS lifecycle — trigger method, resolution, duration,
     * stages traversed, volunteer count, escalation status.
     *
     * @param correlationId The SOS correlation ID to summarize
     * @return Summary object, or null if no entries found
     */
    suspend fun summarize(correlationId: String): SosTimelineSummary? {
        val events = build(correlationId)
        if (events.isEmpty()) return null

        val triggerEvent = events.firstOrNull { it.stage == "TRIGGER" }
        val resolutionEvent = events.lastOrNull { it.stage == "RESOLUTION" }

        val triggerMethod = triggerEvent?.logEntry?.properties?.get("Method")
        val resolution = resolutionEvent?.logEntry?.properties?.get("Reason")

        val triggerTs = triggerEvent?.timestamp ?: events.first().timestamp
        val resolutionTs = resolutionEvent?.timestamp

        val durationMs = resolutionTs?.let { it - triggerTs }
        val durationFormatted = durationMs?.let { formatDuration(it) }

        val stages = events.mapNotNull { it.stage }.distinct()

        val hasEscalation = stages.any { stage ->
            stage in setOf("ESCALATION", "FIRST_RESPONDER_DISPATCH", "NG911_CALL")
        }

        // Count unique volunteer IDs from VOLUNTEER_ACKNOWLEDGE stage entries
        val volunteerCount = events
            .filter { it.stage == "VOLUNTEER_ACKNOWLEDGE" }
            .mapNotNull { it.logEntry.properties["VolunteerId"] }
            .distinct()
            .size

        return SosTimelineSummary(
            correlationId = correlationId,
            triggerMethod = triggerMethod,
            resolution = resolution,
            triggerTimestamp = triggerTs,
            resolutionTimestamp = resolutionTs,
            durationMs = durationMs,
            durationFormatted = durationFormatted,
            stageCount = stages.size,
            entryCount = events.size,
            stages = stages,
            hasEscalation = hasEscalation,
            volunteerCount = volunteerCount
        )
    }

    /**
     * Get only events for a specific pipeline stage.
     *
     * @param correlationId The SOS correlation ID
     * @param stage Stage name (TRIGGER, ALERT_DISPATCH, VOLUNTEER_NOTIFY, etc.)
     * @return Filtered and sorted events for that stage
     */
    suspend fun eventsForStage(correlationId: String, stage: String): List<SosTimelineEvent> {
        return build(correlationId).filter { it.stage == stage }
    }

    /**
     * Check whether a correlation is still active (has TRIGGER but no RESOLUTION).
     */
    suspend fun isActive(correlationId: String): Boolean {
        val events = build(correlationId)
        val hasTrigger = events.any { it.stage == "TRIGGER" }
        val hasResolution = events.any { it.stage == "RESOLUTION" }
        return hasTrigger && !hasResolution
    }

    private fun formatDuration(ms: Long): String {
        val totalSeconds = ms / 1000
        val hours = totalSeconds / 3600
        val minutes = (totalSeconds % 3600) / 60
        val seconds = totalSeconds % 60
        return buildString {
            if (hours > 0) append("${hours}h ")
            if (minutes > 0) append("${minutes}m ")
            append("${seconds}s")
        }.trim()
    }
}
