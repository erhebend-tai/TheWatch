package com.thewatch.app.data.logging

import android.os.Build
import android.provider.Settings
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Structured logger facade — the primary API all components use.
 *
 * Mirrors Serilog's API: `logger.information("User {UserId} triggered SOS at {Lat},{Lng}", ...)`
 *
 * Each log call is fire-and-forget on a background coroutine,
 * so callers never block on persistence. The port handles buffering.
 *
 * Usage:
 * ```kotlin
 * @Inject lateinit var logger: WatchLogger
 *
 * logger.information(
 *     sourceContext = "LocationCoordinator",
 *     messageTemplate = "Escalated to {Mode} mode",
 *     properties = mapOf("Mode" to "Emergency"),
 *     correlationId = activeAlertId
 * )
 * ```
 */
@Singleton
class WatchLogger @Inject constructor(
    private val port: LoggingPort
) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    /** Current user ID — set on login, cleared on logout. */
    var userId: String? = null

    /** Stable device ID — set once at app startup. */
    var deviceId: String? = null

    // ── Convenience methods per level ────────────────────────────

    fun verbose(
        sourceContext: String,
        messageTemplate: String,
        properties: Map<String, String> = emptyMap(),
        correlationId: String? = null
    ) = log(LogLevel.Verbose, sourceContext, messageTemplate, properties, null, correlationId)

    fun debug(
        sourceContext: String,
        messageTemplate: String,
        properties: Map<String, String> = emptyMap(),
        correlationId: String? = null
    ) = log(LogLevel.Debug, sourceContext, messageTemplate, properties, null, correlationId)

    fun information(
        sourceContext: String,
        messageTemplate: String,
        properties: Map<String, String> = emptyMap(),
        correlationId: String? = null
    ) = log(LogLevel.Information, sourceContext, messageTemplate, properties, null, correlationId)

    fun warning(
        sourceContext: String,
        messageTemplate: String,
        properties: Map<String, String> = emptyMap(),
        exception: Throwable? = null,
        correlationId: String? = null
    ) = log(LogLevel.Warning, sourceContext, messageTemplate, properties, exception, correlationId)

    fun error(
        sourceContext: String,
        messageTemplate: String,
        properties: Map<String, String> = emptyMap(),
        exception: Throwable? = null,
        correlationId: String? = null
    ) = log(LogLevel.Error, sourceContext, messageTemplate, properties, exception, correlationId)

    fun fatal(
        sourceContext: String,
        messageTemplate: String,
        properties: Map<String, String> = emptyMap(),
        exception: Throwable? = null,
        correlationId: String? = null
    ) = log(LogLevel.Fatal, sourceContext, messageTemplate, properties, exception, correlationId)

    // ── Core write ───────────────────────────────────────────────

    private fun log(
        level: LogLevel,
        sourceContext: String,
        messageTemplate: String,
        properties: Map<String, String>,
        exception: Throwable?,
        correlationId: String?
    ) {
        val enriched = properties.toMutableMap().apply {
            put("Platform", "Android")
            put("OsVersion", Build.VERSION.RELEASE)
            put("AppVersion", BuildConfig.VERSION_NAME ?: "dev")
        }

        val entry = LogEntry(
            level = level,
            sourceContext = sourceContext,
            messageTemplate = messageTemplate,
            properties = enriched,
            exception = exception?.let { "${it::class.simpleName}: ${it.message}\n${it.stackTraceToString().take(2000)}" },
            correlationId = correlationId,
            userId = userId,
            deviceId = deviceId
        )

        scope.launch {
            port.write(entry)
        }
    }

    /** Flush buffered logs — call from Application.onTrimMemory or background transition. */
    fun flush() {
        scope.launch { port.flush() }
    }
}
