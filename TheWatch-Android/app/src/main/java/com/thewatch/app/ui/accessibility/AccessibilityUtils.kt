/**
 * WRITE-AHEAD LOG | File: AccessibilityUtils.kt | Purpose: WCAG 2.1 AA compliance helpers
 * Created: 2026-03-24 | Author: Claude | Deps: Compose UI, Compose Foundation
 * Usage: Modifier.minimumTouchTarget().accessibleClickLabel("Send SOS")
 * WCAG: 1.4.3 Contrast, 1.4.4 Resize, 1.4.11 Non-text Contrast, 2.4.6 Headings,
 *        2.4.7 Focus Visible, 2.5.5 Target Size (48dp), 4.1.2 Name/Role/Value
 */
package com.thewatch.app.ui.accessibility

import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.ReadOnlyComposable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp

// ── Touch Target (WCAG 2.5.5) ──
val MINIMUM_TOUCH_TARGET_SIZE: Dp = 48.dp

fun Modifier.minimumTouchTarget(): Modifier = this.defaultMinSize(minWidth = MINIMUM_TOUCH_TARGET_SIZE, minHeight = MINIMUM_TOUCH_TARGET_SIZE)
fun Modifier.touchTargetSpacing(): Modifier = this.padding(4.dp)

// ── Screen Reader Semantics (WCAG 4.1.2, 2.4.6) ──
fun Modifier.accessibleHeading(): Modifier = this.semantics { heading() }
fun Modifier.accessibleClickLabel(label: String): Modifier = this.semantics { contentDescription = label }
fun Modifier.accessibleToggleState(isOn: Boolean, featureName: String): Modifier = this.semantics { stateDescription = if (isOn) "$featureName enabled" else "$featureName disabled"; role = Role.Switch }
fun Modifier.accessibleImage(altText: String): Modifier = this.semantics { contentDescription = altText; role = Role.Image }
fun Modifier.accessibleButton(description: String): Modifier = this.semantics { contentDescription = description; role = Role.Button }

// ── Contrast (WCAG 1.4.3, 1.4.11) ──
fun Color.relativeLuminance(): Double {
    fun lin(c: Float): Double { val d = c.toDouble(); return if (d <= 0.03928) d / 12.92 else Math.pow((d + 0.055) / 1.055, 2.4) }
    return 0.2126 * lin(red) + 0.7152 * lin(green) + 0.0722 * lin(blue)
}
fun contrastRatio(fg: Color, bg: Color): Double { val l1 = fg.relativeLuminance(); val l2 = bg.relativeLuminance(); return (maxOf(l1, l2) + 0.05) / (minOf(l1, l2) + 0.05) }
fun meetsAANormalText(fg: Color, bg: Color) = contrastRatio(fg, bg) >= 4.5
fun meetsAALargeText(fg: Color, bg: Color) = contrastRatio(fg, bg) >= 3.0
fun meetsAAUIComponent(fg: Color, bg: Color) = contrastRatio(fg, bg) >= 3.0

// ── High Contrast Colors ──
object HighContrastColors {
    val background = Color.Black; val surface = Color(0xFF1A1A1A); val onBackground = Color.White; val onSurface = Color.White
    val primary = Color(0xFFFF6B6B); val onPrimary = Color.Black; val error = Color(0xFFFF4444); val warning = Color(0xFFFFFF00); val success = Color(0xFF00FF00); val border = Color.White
}

// ── Text Scaling (WCAG 1.4.4) ──
@Composable @ReadOnlyComposable fun currentFontScale(): Float = LocalConfiguration.current.fontScale

// ── Focus Indicator (WCAG 2.4.7) ──
object FocusIndicator { val strokeWidth = 3.dp; val cornerRadius = 8.dp; val color = Color(0xFF2196F3); val highContrastColor = Color.Yellow; val offset = 2.dp }
