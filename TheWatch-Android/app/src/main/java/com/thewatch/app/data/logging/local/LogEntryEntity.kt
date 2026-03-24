package com.thewatch.app.data.logging.local

import androidx.room.ColumnInfo
import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey
import com.thewatch.app.data.logging.LogEntry
import com.thewatch.app.data.logging.LogLevel
import java.time.Instant

/**
 * Room entity for persisting structured log entries.
 *
 * Indices:
 * - timestamp DESC for recent-first queries
 * - correlation_id for incident-scoped queries
 * - synced for efficient "push unsynced" batch queries
 * - level for severity-filtered queries
 */
@Entity(
    tableName = "log_entries",
    indices = [
        Index(value = ["timestamp"]),
        Index(value = ["correlation_id"]),
        Index(value = ["synced"]),
        Index(value = ["level"]),
        Index(value = ["source_context"])
    ]
)
data class LogEntryEntity(
    @PrimaryKey
    val id: String,

    @ColumnInfo(name = "timestamp")
    val timestampEpochMs: Long,

    @ColumnInfo(name = "level")
    val level: Int, // LogLevel.ordinal

    @ColumnInfo(name = "source_context")
    val sourceContext: String,

    @ColumnInfo(name = "message_template")
    val messageTemplate: String,

    @ColumnInfo(name = "rendered_message")
    val renderedMessage: String,

    /** JSON-serialized properties map */
    @ColumnInfo(name = "properties")
    val propertiesJson: String,

    @ColumnInfo(name = "exception")
    val exception: String? = null,

    @ColumnInfo(name = "correlation_id")
    val correlationId: String? = null,

    @ColumnInfo(name = "user_id")
    val userId: String? = null,

    @ColumnInfo(name = "device_id")
    val deviceId: String? = null,

    @ColumnInfo(name = "synced")
    val synced: Boolean = false
) {
    fun toDomain(): LogEntry = LogEntry(
        id = id,
        timestamp = Instant.ofEpochMilli(timestampEpochMs),
        level = LogLevel.entries[level],
        sourceContext = sourceContext,
        messageTemplate = messageTemplate,
        properties = parseProperties(propertiesJson),
        exception = exception,
        correlationId = correlationId,
        userId = userId,
        deviceId = deviceId,
        synced = synced
    )

    companion object {
        fun fromDomain(entry: LogEntry): LogEntryEntity = LogEntryEntity(
            id = entry.id,
            timestampEpochMs = entry.timestamp.toEpochMilli(),
            level = entry.level.ordinal,
            sourceContext = entry.sourceContext,
            messageTemplate = entry.messageTemplate,
            renderedMessage = entry.renderedMessage(),
            propertiesJson = serializeProperties(entry.properties),
            exception = entry.exception,
            correlationId = entry.correlationId,
            userId = entry.userId,
            deviceId = entry.deviceId,
            synced = entry.synced
        )

        /** Simple JSON serialization — no Gson/Moshi dependency needed. */
        private fun serializeProperties(props: Map<String, String>): String {
            if (props.isEmpty()) return "{}"
            return props.entries.joinToString(",", "{", "}") { (k, v) ->
                "\"${k.replace("\"", "\\\"")}\":\"${v.replace("\"", "\\\"")}\""
            }
        }

        private fun parseProperties(json: String): Map<String, String> {
            if (json == "{}" || json.isBlank()) return emptyMap()
            return try {
                json.trim('{', '}')
                    .split(",(?=\")".toRegex())
                    .associate { pair ->
                        val parts = pair.split(":", limit = 2)
                        val key = parts[0].trim().trim('"')
                        val value = parts.getOrElse(1) { "" }.trim().trim('"')
                        key to value
                    }
            } catch (e: Exception) {
                mapOf("_raw" to json)
            }
        }
    }
}
