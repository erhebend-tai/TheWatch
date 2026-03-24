package com.thewatch.app.ui.theme

import androidx.compose.foundation.isSystemInDarkMode
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable

private val LightColors = lightColorScheme(
    primary = Navy,
    onPrimary = White,
    primaryContainer = NavyLight,
    onPrimaryContainer = White,
    secondary = RedPrimary,
    onSecondary = White,
    secondaryContainer = RedLight,
    onSecondaryContainer = Navy,
    tertiary = GreenSafe,
    onTertiary = White,
    tertiaryContainer = GreenSafeLight,
    onTertiaryContainer = Navy,
    error = RedPrimary,
    onError = White,
    errorContainer = RedLight,
    onErrorContainer = Navy,
    background = GrayLight,
    onBackground = Navy,
    surface = White,
    onSurface = Navy,
    surfaceVariant = GrayMedium,
    onSurfaceVariant = GrayDark,
    outline = GrayDark,
    outlineVariant = GrayMedium,
    scrim = Black
)

@Composable
fun TheWatchTheme(
    darkTheme: Boolean = isSystemInDarkMode(),
    content: @Composable () -> Unit
) {
    val colorScheme = LightColors

    MaterialTheme(
        colorScheme = colorScheme,
        typography = AppTypography,
        content = content
    )
}
