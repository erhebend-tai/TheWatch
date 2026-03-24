package com.thewatch.app.data.logging

/**
 * Structured log levels mirroring Serilog on the Aspire backend.
 * Ordered by severity — ordinal comparison is intentional.
 */
enum class LogLevel {
    Verbose,
    Debug,
    Information,
    Warning,
    Error,
    Fatal
}
