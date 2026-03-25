package com.thewatch.app.ui.components

import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.thewatch.app.ui.theme.RedPrimary
import kotlinx.coroutines.delay

@Composable
fun SOSButton(
    modifier: Modifier = Modifier,
    onSOSActivate: () -> Unit = {},
    isActive: Boolean = false
) {
    val hapticFeedback = LocalHapticFeedback.current
    var countdownSeconds by remember { mutableStateOf(3) }
    var isPressed by remember { mutableStateOf(false) }
    var showCountdown by remember { mutableStateOf(false) }

    val scale by animateFloatAsState(if (isPressed) 0.95f else 1f, label = "sos_scale")

    LaunchedEffect(showCountdown) {
        if (showCountdown && countdownSeconds > 0) {
            delay(1000)
            countdownSeconds--
            if (countdownSeconds == 0) {
                hapticFeedback.performHapticFeedback(HapticFeedbackType.LongPress)
                onSOSActivate()
                showCountdown = false
                countdownSeconds = 3
                isPressed = false
            }
        }
    }

    Column(
        modifier = modifier,
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Box(
            modifier = Modifier
                .size(80.dp)
                .scale(scale)
                .background(
                    color = if (isActive) Color(0xFF4CAF50) else RedPrimary,
                    shape = CircleShape
                )
                .semantics {
                    contentDescription = if (isActive) {
                        "SOS is active. Emergency response in progress."
                    } else if (showCountdown) {
                        "SOS activating in $countdownSeconds seconds. Tap to cancel."
                    } else {
                        "SOS emergency button. Tap to activate emergency response."
                    }
                    role = Role.Button
                }
                .clickable {
                    if (!showCountdown) {
                        isPressed = true
                        showCountdown = true
                        hapticFeedback.performHapticFeedback(HapticFeedbackType.LongPress)
                    }
                },
            contentAlignment = Alignment.Center
        ) {
            Column(
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Text(
                    text = if (isActive) "ACTIVE" else "SOS",
                    fontSize = if (isActive) 18.sp else 24.sp,
                    fontWeight = FontWeight.Bold,
                    color = Color.White
                )
                if (showCountdown) {
                    Text(
                        text = countdownSeconds.toString(),
                        fontSize = 16.sp,
                        fontWeight = FontWeight.Bold,
                        color = Color.White
                    )
                }
            }
        }
        if (showCountdown) {
            Text(
                text = "Release to cancel",
                fontSize = 12.sp,
                color = Color.Gray,
                modifier = Modifier
                    .clickable {
                        showCountdown = false
                        countdownSeconds = 3
                        isPressed = false
                    }
            )
        }
    }
}
