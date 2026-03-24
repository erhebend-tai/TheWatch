/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         GuestSOSScreen.kt                                      │
 * │ Purpose:      Compose screen for unauthenticated Guest SOS.          │
 * │               Accessible from the Login screen — bypasses all auth.  │
 * │               Shows a large red SOS button and current location.     │
 * │               Minimal UI for maximum urgency and zero confusion.     │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: GuestSOSViewModel, Hilt, NavController, Material 3    │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   // In NavGraph.kt authGraph:                                       │
 * │   composable(NavRoute.GuestSOS.route) {                              │
 * │       GuestSOSScreen(navController = navController)                   │
 * │   }                                                                  │
 * │                                                                      │
 * │ UX RATIONALE: This screen is intentionally stripped down.            │
 * │ In a real emergency, cognitive load must be near zero. The user      │
 * │ sees: (1) big red button, (2) their approximate location, (3) a     │
 * │ back button. That's it. No forms, no menus, no distractions.        │
 * │                                                                      │
 * │ Accessibility: Button has minimum 48dp touch target, high-contrast  │
 * │ colors, and contentDescription for TalkBack.                         │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.ui.screens.guestsos

import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.LocationOn
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.navigation.NavController
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.RedPrimary
import com.thewatch.app.ui.theme.White

@Composable
fun GuestSOSScreen(
    navController: NavController,
    viewModel: GuestSOSViewModel = hiltViewModel()
) {
    val state by viewModel.uiState.collectAsState()

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(if (state.sosActive) Color(0xFF1A0000) else Navy),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        // Top bar with back button
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp)
        ) {
            IconButton(
                onClick = { navController.popBackStack() },
                modifier = Modifier.align(Alignment.CenterStart)
            ) {
                Icon(
                    imageVector = Icons.Filled.ArrowBack,
                    contentDescription = "Go back to login",
                    tint = White
                )
            }

            Text(
                text = "GUEST EMERGENCY",
                fontSize = 18.sp,
                fontWeight = FontWeight.Bold,
                color = White,
                modifier = Modifier.align(Alignment.Center)
            )
        }

        Spacer(modifier = Modifier.weight(1f))

        // SOS button with pulsing animation when active
        if (state.sosActive) {
            val infiniteTransition = rememberInfiniteTransition(label = "sos_pulse")
            val scale by infiniteTransition.animateFloat(
                initialValue = 1f,
                targetValue = 1.15f,
                animationSpec = infiniteRepeatable(
                    animation = tween(600),
                    repeatMode = RepeatMode.Reverse
                ),
                label = "sos_scale"
            )

            Box(
                modifier = Modifier
                    .size(200.dp)
                    .scale(scale)
                    .clip(CircleShape)
                    .background(Color.Red)
                    .clickable { viewModel.cancelSOS() }
                    .semantics { contentDescription = "SOS Active. Tap to cancel." },
                contentAlignment = Alignment.Center
            ) {
                if (state.isLoading) {
                    CircularProgressIndicator(color = White, modifier = Modifier.size(60.dp))
                } else {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        Text(
                            text = if (state.sosDispatched) "SOS SENT" else "SOS",
                            fontSize = 36.sp,
                            fontWeight = FontWeight.ExtraBold,
                            color = White
                        )
                        if (state.sosDispatched) {
                            Text(
                                text = "Help is coming",
                                fontSize = 14.sp,
                                color = White.copy(alpha = 0.9f)
                            )
                        }
                        Text(
                            text = "Tap to cancel",
                            fontSize = 10.sp,
                            color = White.copy(alpha = 0.7f),
                            modifier = Modifier.padding(top = 4.dp)
                        )
                    }
                }
            }
        } else {
            Box(
                modifier = Modifier
                    .size(200.dp)
                    .clip(CircleShape)
                    .background(RedPrimary)
                    .border(4.dp, Color.Red.copy(alpha = 0.5f), CircleShape)
                    .clickable { viewModel.activateGuestSOS() }
                    .semantics { contentDescription = "Press and hold for emergency SOS" },
                contentAlignment = Alignment.Center
            ) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text(
                        text = "SOS",
                        fontSize = 48.sp,
                        fontWeight = FontWeight.ExtraBold,
                        color = White
                    )
                    Text(
                        text = "TAP FOR HELP",
                        fontSize = 12.sp,
                        color = White.copy(alpha = 0.8f),
                        fontWeight = FontWeight.SemiBold
                    )
                }
            }
        }

        Spacer(modifier = Modifier.height(32.dp))

        // Location display
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 24.dp)
                .background(Color.White.copy(alpha = 0.1f), RoundedCornerShape(12.dp))
                .padding(16.dp)
        ) {
            Column(
                horizontalAlignment = Alignment.CenterHorizontally,
                modifier = Modifier.fillMaxWidth()
            ) {
                Icon(
                    imageVector = Icons.Filled.LocationOn,
                    contentDescription = "Location",
                    tint = if (state.locationAvailable) Color(0xFF4CAF50) else Color.Gray,
                    modifier = Modifier.size(24.dp)
                )

                Text(
                    text = if (state.locationAvailable) {
                        "Location: %.4f, %.4f".format(state.latitude, state.longitude)
                    } else {
                        "Acquiring location..."
                    },
                    fontSize = 14.sp,
                    color = White.copy(alpha = 0.8f),
                    textAlign = TextAlign.Center,
                    modifier = Modifier.padding(top = 4.dp)
                )

                if (!state.locationAvailable) {
                    Text(
                        text = "SOS will still be sent without precise location",
                        fontSize = 11.sp,
                        color = White.copy(alpha = 0.5f),
                        textAlign = TextAlign.Center,
                        modifier = Modifier.padding(top = 4.dp)
                    )
                }
            }
        }

        Spacer(modifier = Modifier.weight(1f))

        // Error display
        state.errorMessage?.let { error ->
            Text(
                text = error,
                fontSize = 14.sp,
                color = Color(0xFFFF6B6B),
                textAlign = TextAlign.Center,
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 24.dp, vertical = 8.dp)
            )
        }

        // Footer
        Text(
            text = "No account needed. This sends an emergency alert\nwith your location to nearby responders.",
            fontSize = 11.sp,
            color = White.copy(alpha = 0.5f),
            textAlign = TextAlign.Center,
            modifier = Modifier.padding(bottom = 24.dp, start = 24.dp, end = 24.dp)
        )
    }
}
