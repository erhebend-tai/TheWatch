/**
 * ═══════════════════════════════════════════════════════════════════════════════
 * WRITE-AHEAD LOG — CheckInScheduleScreen.kt
 * ═══════════════════════════════════════════════════════════════════════════════
 * Purpose:   Jetpack Compose screen for configuring periodic check-in reminders.
 *            Users select an interval (daily, 12h, 6h, custom), set quiet hours,
 *            and enable/disable the schedule. Integrates with WorkManager for
 *            reliable background delivery.
 * Date:      2026-03-24
 * Author:    Claude (Anthropic)
 * Deps:      Hilt, Compose Material3, CheckInScheduleViewModel
 * Package:   com.thewatch.app.ui.screens.checkin
 *
 * Usage Example:
 *   // In NavGraph:
 *   composable(NavRoute.CheckInSchedule.route) {
 *       CheckInScheduleScreen(navController = navController)
 *   }
 * ═══════════════════════════════════════════════════════════════════════════════
 */
package com.thewatch.app.ui.screens.checkin

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.navigation.NavController
import com.thewatch.app.data.checkin.CheckInInterval
import com.thewatch.app.ui.theme.GreenSafe
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.RedPrimary
import com.thewatch.app.ui.theme.White

@Composable
fun CheckInScheduleScreen(
    navController: NavController,
    viewModel: CheckInScheduleViewModel = hiltViewModel()
) {
    val uiState by viewModel.uiState.collectAsState()
    val schedule = uiState.schedule
    var customMinutes by remember { mutableFloatStateOf(schedule.customIntervalMinutes?.toFloat() ?: 60f) }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
            .verticalScroll(rememberScrollState())
    ) {
        // Top Bar
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .background(Navy)
                .padding(16.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            IconButton(onClick = { navController.navigateUp() }) {
                Icon(
                    imageVector = Icons.AutoMirrored.Filled.ArrowBack,
                    contentDescription = "Back",
                    tint = White
                )
            }
            Text(
                text = "Check-In Schedule",
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold,
                color = White,
                modifier = Modifier.weight(1f)
            )
        }

        Column(modifier = Modifier.padding(24.dp)) {
            // Enable/Disable Toggle
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .background(
                        color = if (schedule.enabled) GreenSafe.copy(alpha = 0.1f)
                        else Color(0xFFF5F5F5),
                        shape = RoundedCornerShape(8.dp)
                    )
                    .padding(16.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = "Enable Check-In Reminders",
                        fontSize = 16.sp,
                        fontWeight = FontWeight.Bold,
                        color = Navy
                    )
                    Text(
                        text = if (schedule.enabled) "Active — you will receive periodic check-ins"
                        else "Disabled — no automatic check-ins",
                        fontSize = 12.sp,
                        color = Color.Gray
                    )
                }
                Switch(
                    checked = schedule.enabled,
                    onCheckedChange = { viewModel.toggleEnabled(it) }
                )
            }

            Spacer(modifier = Modifier.height(24.dp))

            // Interval Selection
            Text(
                text = "Check-In Interval",
                fontSize = 16.sp,
                fontWeight = FontWeight.Bold,
                color = Navy
            )
            Spacer(modifier = Modifier.height(8.dp))
            Text(
                text = "How often should we ask if you're OK?",
                fontSize = 12.sp,
                color = Color.Gray
            )

            Spacer(modifier = Modifier.height(12.dp))

            // Interval option cards
            CheckInInterval.entries.forEach { interval ->
                val isSelected = schedule.interval == interval
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(vertical = 4.dp)
                        .background(
                            color = if (isSelected) Navy.copy(alpha = 0.1f) else Color(0xFFF5F5F5),
                            shape = RoundedCornerShape(8.dp)
                        )
                        .border(
                            width = if (isSelected) 2.dp else 0.dp,
                            color = if (isSelected) Navy else Color.Transparent,
                            shape = RoundedCornerShape(8.dp)
                        )
                        .clickable { viewModel.setInterval(interval) }
                        .padding(16.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            text = interval.displayName,
                            fontSize = 14.sp,
                            fontWeight = if (isSelected) FontWeight.Bold else FontWeight.Normal,
                            color = Navy
                        )
                        if (interval != CheckInInterval.CUSTOM && interval.minutes > 0) {
                            Text(
                                text = "${interval.minutes / 60}h intervals",
                                fontSize = 12.sp,
                                color = Color.Gray
                            )
                        }
                    }
                    if (isSelected) {
                        Text(
                            text = "Selected",
                            fontSize = 12.sp,
                            fontWeight = FontWeight.Bold,
                            color = GreenSafe
                        )
                    }
                }
            }

            // Custom interval slider (shown when CUSTOM is selected)
            if (schedule.interval == CheckInInterval.CUSTOM) {
                Spacer(modifier = Modifier.height(16.dp))

                Text(
                    text = "Custom Interval: ${customMinutes.toInt()} minutes",
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Bold,
                    color = Navy
                )

                Slider(
                    value = customMinutes,
                    onValueChange = { customMinutes = it },
                    onValueChangeFinished = {
                        viewModel.setCustomMinutes(customMinutes.toInt())
                    },
                    valueRange = 15f..1440f,
                    steps = 0,
                    colors = SliderDefaults.colors(
                        thumbColor = Navy,
                        activeTrackColor = Navy,
                        inactiveTrackColor = Navy.copy(alpha = 0.2f)
                    ),
                    modifier = Modifier.fillMaxWidth()
                )

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    Text("15 min", fontSize = 10.sp, color = Color.Gray)
                    Text("6h", fontSize = 10.sp, color = Color.Gray)
                    Text("12h", fontSize = 10.sp, color = Color.Gray)
                    Text("24h", fontSize = 10.sp, color = Color.Gray)
                }
            }

            Spacer(modifier = Modifier.height(24.dp))

            // Quiet Hours
            Text(
                text = "Quiet Hours",
                fontSize = 16.sp,
                fontWeight = FontWeight.Bold,
                color = Navy
            )
            Spacer(modifier = Modifier.height(8.dp))
            Text(
                text = "No check-ins will be sent during quiet hours",
                fontSize = 12.sp,
                color = Color.Gray
            )
            Spacer(modifier = Modifier.height(12.dp))

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text("Start", fontSize = 12.sp, color = Color.Gray)
                    Text(
                        text = formatHour(schedule.startHour),
                        fontSize = 16.sp,
                        fontWeight = FontWeight.Bold,
                        color = Navy
                    )
                }
                Spacer(modifier = Modifier.width(16.dp))
                Column(modifier = Modifier.weight(1f)) {
                    Text("End", fontSize = 12.sp, color = Color.Gray)
                    Text(
                        text = formatHour(schedule.endHour),
                        fontSize = 16.sp,
                        fontWeight = FontWeight.Bold,
                        color = Navy
                    )
                }
            }

            Spacer(modifier = Modifier.height(24.dp))

            // Info Card
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .background(
                        color = RedPrimary.copy(alpha = 0.08f),
                        shape = RoundedCornerShape(8.dp)
                    )
                    .padding(16.dp)
            ) {
                Text(
                    text = "How It Works",
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Bold,
                    color = RedPrimary
                )
                Spacer(modifier = Modifier.height(8.dp))
                Text(
                    text = "1. You receive a check-in notification at your chosen interval\n" +
                            "2. Tap \"I'm OK\" to confirm you're safe\n" +
                            "3. If you don't respond within the escalation timer, your " +
                            "emergency contacts are automatically notified\n" +
                            "4. If 911 auto-dial is enabled, emergency services are contacted next",
                    fontSize = 12.sp,
                    color = Navy,
                    lineHeight = 18.sp
                )
            }

            // Error display
            uiState.error?.let { error ->
                Spacer(modifier = Modifier.height(12.dp))
                Text(
                    text = error,
                    fontSize = 12.sp,
                    color = RedPrimary
                )
            }

            Spacer(modifier = Modifier.height(32.dp))
        }
    }
}

private fun formatHour(hour: Int): String {
    val h = if (hour == 0) 12 else if (hour > 12) hour - 12 else hour
    val amPm = if (hour < 12) "AM" else "PM"
    return "$h:00 $amPm"
}
