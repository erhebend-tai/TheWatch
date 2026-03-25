/**
 * +----------------------------------------------------------------------+
 * | WRITE-AHEAD LOG                                                       |
 * +----------------------------------------------------------------------+
 * | File:         SosCountdownScreen.kt                                   |
 * | Purpose:      Full-screen red overlay with 3-second SOS countdown.    |
 * |               Displays urgent visual feedback during SOS activation:  |
 * |                 - Pulsing red background                              |
 * |                 - Large countdown numbers (3, 2, 1)                   |
 * |                 - Accessible cancel button                            |
 * |                 - "Contacting responders..." after countdown          |
 * |                 - Responder count when server responds                |
 * | Created:      2026-03-24                                             |
 * | Author:       Claude                                                 |
 * | Dependencies: SosTriggerService, Compose, Hilt                        |
 * |                                                                       |
 * | Usage example:                                                        |
 * |   // In NavGraph:                                                     |
 * |   composable(NavRoute.SosCountdown.route) {                           |
 * |       SosCountdownScreen(                                             |
 * |           triggerSource = SosTriggerSource.MANUAL_BUTTON,             |
 * |           onDismiss = { navController.popBackStack() }                |
 * |       )                                                               |
 * |   }                                                                   |
 * |                                                                       |
 * |   // From HomeScreen SOS button:                                      |
 * |   navController.navigate(NavRoute.SosCountdown.route)                 |
 * |                                                                       |
 * | Accessibility:                                                        |
 * |   - Cancel button is 64dp minimum touch target                        |
 * |   - All text has contentDescription                                   |
 * |   - High contrast: white text on red background                       |
 * |   - Announces countdown changes via semantics                         |
 * +----------------------------------------------------------------------+
 */
package com.thewatch.app.ui.screens.sos

import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.*
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.scaleOut
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material.icons.filled.WifiOff
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.thewatch.app.services.SosTriggerService
import com.thewatch.app.services.SosTriggerSource
import com.thewatch.app.services.SosTriggerState

// ── Color palette for SOS screen ──────────────────────────────
private val SosRedDark = Color(0xFF8B0000)
private val SosRedPrimary = Color(0xFFCC0000)
private val SosRedLight = Color(0xFFE53935)
private val SosWhite = Color(0xFFFFFFFF)
private val SosGreen = Color(0xFF4CAF50)
private val SosAmber = Color(0xFFFFC107)

/**
 * Full-screen SOS countdown overlay. Shows 3-2-1 countdown with pulsing
 * red background, then transitions to "contacting responders..." state.
 *
 * @param sosTriggerService Injected SOS trigger orchestrator
 * @param triggerSource What initiated this SOS (manual, phrase, quick-tap)
 * @param description Optional description of the emergency
 * @param onDismiss Called when the screen should be dismissed
 */
@Composable
fun SosCountdownScreen(
    sosTriggerService: SosTriggerService,
    triggerSource: SosTriggerSource = SosTriggerSource.MANUAL_BUTTON,
    description: String? = null,
    onDismiss: () -> Unit = {}
) {
    val state by sosTriggerService.state.collectAsState()
    val haptic = LocalHapticFeedback.current

    // Start trigger on first composition
    LaunchedEffect(Unit) {
        sosTriggerService.trigger(
            source = triggerSource,
            description = description
        )
    }

    // Auto-dismiss after active state is shown for a few seconds
    LaunchedEffect(state) {
        if (state is SosTriggerState.Active) {
            kotlinx.coroutines.delay(5000)
            onDismiss()
        }
        if (state is SosTriggerState.Cancelled) {
            kotlinx.coroutines.delay(1500)
            onDismiss()
        }
    }

    // Pulsing background animation
    val infiniteTransition = rememberInfiniteTransition(label = "sos_pulse")
    val pulseAlpha by infiniteTransition.animateFloat(
        initialValue = 0.85f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(
            animation = tween(800, easing = FastOutSlowInEasing),
            repeatMode = RepeatMode.Reverse
        ),
        label = "pulse_alpha"
    )

    val backgroundBrush = Brush.radialGradient(
        colors = listOf(
            SosRedLight.copy(alpha = pulseAlpha),
            SosRedPrimary.copy(alpha = pulseAlpha),
            SosRedDark
        )
    )

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(backgroundBrush)
            .semantics { contentDescription = "Emergency SOS screen" },
        contentAlignment = Alignment.Center
    ) {
        when (val currentState = state) {
            is SosTriggerState.Countdown -> {
                CountdownContent(
                    secondsRemaining = currentState.secondsRemaining,
                    onCancel = {
                        haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                        sosTriggerService.cancel()
                    }
                )
            }

            is SosTriggerState.Dispatching -> {
                DispatchingContent()
            }

            is SosTriggerState.Active -> {
                ActiveContent(
                    requestId = currentState.requestId,
                    responderCount = currentState.responderCount,
                    radiusMeters = currentState.radiusMeters,
                    onDismiss = onDismiss
                )
            }

            is SosTriggerState.QueuedOffline -> {
                QueuedOfflineContent(onDismiss = onDismiss)
            }

            is SosTriggerState.Cancelled -> {
                CancelledContent()
            }

            is SosTriggerState.Error -> {
                ErrorContent(
                    message = currentState.message,
                    queuedOffline = currentState.queuedOffline,
                    onDismiss = onDismiss
                )
            }

            is SosTriggerState.Idle -> {
                // Briefly shown before trigger starts
                DispatchingContent()
            }
        }
    }
}

@Composable
private fun CountdownContent(
    secondsRemaining: Int,
    onCancel: () -> Unit
) {
    // Scale animation for the countdown number
    val scale by animateFloatAsState(
        targetValue = if (secondsRemaining > 0) 1.2f else 0.8f,
        animationSpec = spring(
            dampingRatio = Spring.DampingRatioMediumBouncy,
            stiffness = Spring.StiffnessLow
        ),
        label = "countdown_scale"
    )

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(32.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.SpaceBetween
    ) {
        // Top: Emergency warning icon and text
        Column(
            modifier = Modifier.padding(top = 48.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Icon(
                imageVector = Icons.Filled.Warning,
                contentDescription = null,
                tint = SosWhite,
                modifier = Modifier.size(48.dp)
            )
            Spacer(modifier = Modifier.height(8.dp))
            Text(
                text = "EMERGENCY SOS",
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold,
                color = SosWhite,
                letterSpacing = 4.sp
            )
        }

        // Center: Large countdown number
        Column(
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            AnimatedContent(
                targetState = secondsRemaining,
                transitionSpec = {
                    (scaleIn(initialScale = 1.5f) + fadeIn())
                        .togetherWith(scaleOut(targetScale = 0.5f) + fadeOut())
                },
                label = "countdown_number"
            ) { seconds ->
                Text(
                    text = if (seconds > 0) seconds.toString() else "!",
                    fontSize = 120.sp,
                    fontWeight = FontWeight.ExtraBold,
                    color = SosWhite,
                    modifier = Modifier
                        .scale(scale)
                        .semantics {
                            contentDescription = if (seconds > 0) {
                                "$seconds seconds until SOS is sent. Tap cancel to stop."
                            } else {
                                "Sending SOS now"
                            }
                            liveRegion = LiveRegionMode.Assertive
                        }
                )
            }

            Spacer(modifier = Modifier.height(16.dp))

            Text(
                text = "SOS will be sent in ${secondsRemaining}s",
                fontSize = 18.sp,
                color = SosWhite.copy(alpha = 0.9f),
                fontWeight = FontWeight.Medium
            )
        }

        // Bottom: Cancel button — large, accessible
        Column(
            modifier = Modifier.padding(bottom = 48.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Button(
                onClick = onCancel,
                modifier = Modifier
                    .fillMaxWidth(0.7f)
                    .height(64.dp)
                    .semantics { contentDescription = "Cancel SOS" },
                colors = ButtonDefaults.buttonColors(
                    containerColor = SosWhite.copy(alpha = 0.25f),
                    contentColor = SosWhite
                ),
                shape = RoundedCornerShape(32.dp)
            ) {
                Icon(
                    imageVector = Icons.Filled.Close,
                    contentDescription = null,
                    modifier = Modifier.size(28.dp)
                )
                Spacer(modifier = Modifier.width(12.dp))
                Text(
                    text = "CANCEL",
                    fontSize = 20.sp,
                    fontWeight = FontWeight.Bold,
                    letterSpacing = 2.sp
                )
            }

            Spacer(modifier = Modifier.height(8.dp))

            Text(
                text = "Tap to cancel emergency alert",
                fontSize = 14.sp,
                color = SosWhite.copy(alpha = 0.7f)
            )
        }
    }
}

@Composable
private fun DispatchingContent() {
    Column(
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
        modifier = Modifier.semantics(mergeDescendants = true) {
            contentDescription = "Contacting responders. Sending your location to nearby volunteers."
            liveRegion = LiveRegionMode.Polite
        }
    ) {
        CircularProgressIndicator(
            color = SosWhite,
            strokeWidth = 4.dp,
            modifier = Modifier.size(64.dp)
        )

        Spacer(modifier = Modifier.height(24.dp))

        Text(
            text = "Contacting responders...",
            fontSize = 24.sp,
            fontWeight = FontWeight.Bold,
            color = SosWhite,
            textAlign = TextAlign.Center
        )

        Spacer(modifier = Modifier.height(8.dp))

        Text(
            text = "Sending your location to nearby volunteers",
            fontSize = 16.sp,
            color = SosWhite.copy(alpha = 0.8f),
            textAlign = TextAlign.Center
        )
    }
}

@Composable
private fun ActiveContent(
    requestId: String,
    responderCount: Int,
    radiusMeters: Double,
    onDismiss: () -> Unit
) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(32.dp)
            .semantics(mergeDescendants = true) {
                contentDescription = "SOS sent. $responderCount responders being notified. Help is on the way."
                liveRegion = LiveRegionMode.Assertive
            },
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center
    ) {
        // Success checkmark
        Box(
            modifier = Modifier
                .size(80.dp)
                .background(SosGreen, CircleShape),
            contentAlignment = Alignment.Center
        ) {
            Icon(
                imageVector = Icons.Filled.Check,
                contentDescription = "SOS sent successfully",
                tint = SosWhite,
                modifier = Modifier.size(48.dp)
            )
        }

        Spacer(modifier = Modifier.height(24.dp))

        Text(
            text = "SOS SENT",
            fontSize = 28.sp,
            fontWeight = FontWeight.ExtraBold,
            color = SosWhite,
            letterSpacing = 4.sp
        )

        Spacer(modifier = Modifier.height(16.dp))

        // Responder count badge
        Box(
            modifier = Modifier
                .background(SosWhite.copy(alpha = 0.2f), RoundedCornerShape(12.dp))
                .padding(horizontal = 24.dp, vertical = 12.dp)
        ) {
            Text(
                text = if (responderCount > 0) {
                    "$responderCount responders being notified"
                } else {
                    "Notifying nearby volunteers"
                },
                fontSize = 18.sp,
                fontWeight = FontWeight.SemiBold,
                color = SosWhite
            )
        }

        Spacer(modifier = Modifier.height(12.dp))

        Text(
            text = "Search radius: ${(radiusMeters / 1000).toInt()}km",
            fontSize = 14.sp,
            color = SosWhite.copy(alpha = 0.7f)
        )

        Spacer(modifier = Modifier.height(32.dp))

        Text(
            text = "Help is on the way. Stay where you are.",
            fontSize = 16.sp,
            color = SosWhite.copy(alpha = 0.9f),
            textAlign = TextAlign.Center,
            fontWeight = FontWeight.Medium
        )

        Spacer(modifier = Modifier.height(48.dp))

        Button(
            onClick = onDismiss,
            colors = ButtonDefaults.buttonColors(
                containerColor = SosWhite.copy(alpha = 0.2f),
                contentColor = SosWhite
            ),
            shape = RoundedCornerShape(24.dp),
            modifier = Modifier.height(48.dp)
        ) {
            Text(
                text = "Back to Map",
                fontSize = 16.sp,
                fontWeight = FontWeight.SemiBold
            )
        }
    }
}

@Composable
private fun QueuedOfflineContent(onDismiss: () -> Unit) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(32.dp)
            .semantics(mergeDescendants = true) {
                contentDescription = "SOS queued offline. Will be sent automatically when you reconnect."
                liveRegion = LiveRegionMode.Assertive
            },
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center
    ) {
        Icon(
            imageVector = Icons.Filled.WifiOff,
            contentDescription = "No internet connection",
            tint = SosAmber,
            modifier = Modifier.size(64.dp)
        )

        Spacer(modifier = Modifier.height(24.dp))

        Text(
            text = "SOS QUEUED",
            fontSize = 28.sp,
            fontWeight = FontWeight.ExtraBold,
            color = SosWhite,
            letterSpacing = 4.sp
        )

        Spacer(modifier = Modifier.height(16.dp))

        Text(
            text = "You appear to be offline. Your SOS has been saved\n" +
                "and will be sent automatically when you reconnect.",
            fontSize = 16.sp,
            color = SosWhite.copy(alpha = 0.9f),
            textAlign = TextAlign.Center,
            lineHeight = 24.sp
        )

        Spacer(modifier = Modifier.height(12.dp))

        Box(
            modifier = Modifier
                .background(SosAmber.copy(alpha = 0.3f), RoundedCornerShape(8.dp))
                .padding(horizontal = 16.dp, vertical = 8.dp)
        ) {
            Text(
                text = "Priority: CRITICAL — sends first on reconnect",
                fontSize = 14.sp,
                color = SosAmber,
                fontWeight = FontWeight.SemiBold
            )
        }

        Spacer(modifier = Modifier.height(48.dp))

        Button(
            onClick = onDismiss,
            colors = ButtonDefaults.buttonColors(
                containerColor = SosWhite.copy(alpha = 0.2f),
                contentColor = SosWhite
            ),
            shape = RoundedCornerShape(24.dp),
            modifier = Modifier.height(48.dp)
        ) {
            Text(
                text = "Back to Map",
                fontSize = 16.sp,
                fontWeight = FontWeight.SemiBold
            )
        }
    }
}

@Composable
private fun CancelledContent() {
    Column(
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
        modifier = Modifier.semantics(mergeDescendants = true) {
            contentDescription = "SOS cancelled. No alert was sent."
            liveRegion = LiveRegionMode.Polite
        }
    ) {
        Icon(
            imageVector = Icons.Filled.Close,
            contentDescription = "Cancelled",
            tint = SosWhite.copy(alpha = 0.7f),
            modifier = Modifier.size(48.dp)
        )

        Spacer(modifier = Modifier.height(16.dp))

        Text(
            text = "SOS Cancelled",
            fontSize = 24.sp,
            fontWeight = FontWeight.Bold,
            color = SosWhite
        )

        Spacer(modifier = Modifier.height(8.dp))

        Text(
            text = "No alert was sent",
            fontSize = 16.sp,
            color = SosWhite.copy(alpha = 0.7f)
        )
    }
}

@Composable
private fun ErrorContent(
    message: String,
    queuedOffline: Boolean,
    onDismiss: () -> Unit
) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(32.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center
    ) {
        Icon(
            imageVector = Icons.Filled.Warning,
            contentDescription = null,
            tint = SosAmber,
            modifier = Modifier.size(64.dp)
        )

        Spacer(modifier = Modifier.height(24.dp))

        Text(
            text = if (queuedOffline) "SOS QUEUED (Error)" else "SOS Error",
            fontSize = 24.sp,
            fontWeight = FontWeight.Bold,
            color = SosWhite
        )

        Spacer(modifier = Modifier.height(12.dp))

        Text(
            text = message,
            fontSize = 14.sp,
            color = SosWhite.copy(alpha = 0.7f),
            textAlign = TextAlign.Center
        )

        if (queuedOffline) {
            Spacer(modifier = Modifier.height(12.dp))
            Text(
                text = "Your SOS has been saved and will retry automatically.",
                fontSize = 16.sp,
                color = SosWhite.copy(alpha = 0.9f),
                textAlign = TextAlign.Center
            )
        }

        Spacer(modifier = Modifier.height(48.dp))

        Button(
            onClick = onDismiss,
            colors = ButtonDefaults.buttonColors(
                containerColor = SosWhite.copy(alpha = 0.2f),
                contentColor = SosWhite
            ),
            shape = RoundedCornerShape(24.dp),
            modifier = Modifier.height(48.dp)
        ) {
            Text(
                text = "Back to Map",
                fontSize = 16.sp,
                fontWeight = FontWeight.SemiBold
            )
        }
    }
}
