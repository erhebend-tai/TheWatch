/**
 * ═══════════════════════════════════════════════════════════════════════════════
 * WRITE-AHEAD LOG — ThemeMode.kt
 * ═══════════════════════════════════════════════════════════════════════════════
 * Purpose:   Enum representing user's theme preference: System, Light, or Dark.
 *            Persisted in SharedPreferences/DataStore and observed by TheWatchTheme.
 * Date:      2026-03-24
 * Author:    Claude (Anthropic)
 * Deps:      None (pure Kotlin enum)
 * Package:   com.thewatch.app.data.model
 *
 * Usage Example:
 *   val mode = ThemeMode.DARK
 *   // In TheWatchTheme composable:
 *   val isDark = when (mode) {
 *       ThemeMode.SYSTEM -> isSystemInDarkMode()
 *       ThemeMode.LIGHT -> false
 *       ThemeMode.DARK -> true
 *   }
 * ═══════════════════════════════════════════════════════════════════════════════
 */
package com.thewatch.app.data.model

/**
 * Theme display mode preference.
 *
 * SYSTEM — follow Android system dark/light setting
 * LIGHT  — always light theme
 * DARK   — always dark theme (navy/red TheWatch palette)
 */
enum class ThemeMode(val displayName: String) {
    SYSTEM("System Default"),
    LIGHT("Light"),
    DARK("Dark");

    companion object {
        fun fromOrdinal(ordinal: Int): ThemeMode =
            entries.getOrElse(ordinal) { SYSTEM }

        fun fromName(name: String): ThemeMode =
            entries.firstOrNull { it.name.equals(name, ignoreCase = true) } ?: SYSTEM
    }
}
