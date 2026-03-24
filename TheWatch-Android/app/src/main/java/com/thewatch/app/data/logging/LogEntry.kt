package com.thewatch.app.data.logging

import java.time.Instant
import java.util.UUID

/**
 * Structured log entry — the domain model for all logging in TheWatch.
 *
 * Mirrors Serilog's structured logging semantics:
 * - messageTemplate with {PropertyName} placeholders
 * - properties map for structured data
 * - sourceContext identifies the originating component
 * - correlationId links related events across an SOS lifecycle
 *
 * @property id           Unique log entry identifier
 * @property timestamp    UTC instant the event occurred
 * @property level        Severity level (Verbose → Fatal)
 * @property sourceContext Fully-qualified class/component name (e.g. "LocationCoordinator")
 * @property messageTemplate Serilog-style template: "Location updated to {Lat},{Lng} in {Mode} mode"
 * @property properties   Structured key-value pairs extracted from the template + extras
 * @property exception    Optional exception message + stack trace
 * @property correlationId Optional SOS/incident ID linking related log entries
 * @property userId       Current authenticated user (null if pre-auth)
 * @property deviceId     Stable device identifier for cross-device correlation
 * @property synced       Whether this entry has been pushed to Firestore
 */
data class LogEntry(
    val id: String = UUID.randomUUID().toString(),
    val timestamp: Instant = Instant.now(),
    val level: LogLevel,
    val sourceContext: String,
    val messageTemplate: String,
    val properties: Map<String, String> = emptyMap(),
    val exception: String? = null,
    val correlationId: String? = null,
    val userId: String? = null,
    val deviceId: String? = null,
    val synced: Boolean = false
) {
    /** Render the message template with property values substituted. */
    fun renderedMessage(): String {
        var result = messageTemplate
        for ((key, value) in properties) {
            result = result.replace("{$key}", value)
        }
        return result
    }
}
