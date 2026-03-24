/**
 * ═══════════════════════════════════════════════════════════════════════════════
 * WRITE-AHEAD LOG — ThemePreferencePort.kt
 * ═══════════════════════════════════════════════════════════════════════════════
 * Purpose:   Hexagonal port for persisting and observing theme mode preference
 *            (System/Light/Dark). Observed by TheWatchTheme composable to
 *            reactively switch color schemes.
 * Date:      2026-03-24
 * Author:    Claude (Anthropic)
 * Deps:      kotlinx.coroutines.flow, ThemeMode
 * Package:   com.thewatch.app.data.settings
 *
 * Usage Example:
 *   @Inject lateinit var themePort: ThemePreferencePort
 *   val mode by themePort.themeMode.collectAsState()
 *   themePort.setThemeMode(ThemeMode.DARK)
 * ═══════════════════════════════════════════════════════════════════════════════
 */
package com.thewatch.app.data.settings

import com.thewatch.app.data.model.ThemeMode
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Port interface for theme preference persistence.
 */
interface ThemePreferencePort {
    val themeMode: StateFlow<ThemeMode>
    fun setThemeMode(mode: ThemeMode)
}

/**
 * In-memory implementation. Production would use DataStore<Preferences>.
 *
 * Example production implementation sketch:
 *   class DataStoreThemePreferenceAdapter(private val dataStore: DataStore<Preferences>) {
 *       override val themeMode = dataStore.data.map { prefs ->
 *           ThemeMode.fromName(prefs[THEME_KEY] ?: "SYSTEM")
 *       }.stateIn(scope, SharingStarted.Eagerly, ThemeMode.SYSTEM)
 *   }
 */
@Singleton
class InMemoryThemePreferenceAdapter @Inject constructor() : ThemePreferencePort {
    private val _themeMode = MutableStateFlow(ThemeMode.SYSTEM)
    override val themeMode: StateFlow<ThemeMode> = _themeMode.asStateFlow()

    override fun setThemeMode(mode: ThemeMode) {
        _themeMode.value = mode
    }
}
