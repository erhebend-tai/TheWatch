/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         HealthDashboardScreen.kt                               │
 * │ Purpose:      Health dashboard UI screen. Displays heart rate,       │
 * │               blood oxygen, steps, sleep, and calories from          │
 * │               Health Connect. Shows alert badges for abnormal        │
 * │               readings. Links to wearable management.                │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: Jetpack Compose Material 3, HealthPort, Hilt ViewModel│
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   // In NavGraph:                                                    │
 * │   composable("health_dashboard") {                                   │
 * │       HealthDashboardScreen(navController = navController)           │
 * │   }                                                                  │
 * │                                                                      │
 * │ NOTE: Health Connect data may be stale if the wearable hasn't       │
 * │ synced recently. Show "Last updated: X min ago" badge. Heart rate   │
 * │ values above 150 bpm or below 40 bpm show a red alert indicator.   │
 * │ SpO2 below 90% shows a yellow warning indicator.                    │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.ui.screens.health

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Favorite
import androidx.compose.material.icons.filled.DirectionsWalk
import androidx.compose.material.icons.filled.Air
import androidx.compose.material.icons.filled.Bedtime
import androidx.compose.material.icons.filled.LocalFireDepartment
import androidx.compose.material.icons.filled.Watch
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.navigation.NavController
import com.thewatch.app.data.health.HealthSummary
import com.thewatch.app.ui.theme.GreenSafe
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.RedPrimary
import com.thewatch.app.ui.theme.White
import com.thewatch.app.ui.theme.YellowWarning

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun HealthDashboardScreen(
    navController: NavController,
    viewModel: HealthDashboardViewModel = hiltViewModel()
) {
    val uiState = viewModel.uiState

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Health Dashboard", color = White) },
                navigationIcon = {
                    IconButton(onClick = { navController.popBackStack() }) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, "Back", tint = White)
                    }
                },
                actions = {
                    TextButton(onClick = { navController.navigate("wearable_management") }) {
                        Icon(Icons.Default.Watch, "Wearables", tint = White, modifier = Modifier.size(20.dp))
                        Spacer(Modifier.width(4.dp))
                        Text("Devices", color = White, fontSize = 12.sp)
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = Navy)
            )
        }
    ) { paddingValues ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(paddingValues)
                .padding(16.dp)
                .verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            if (uiState.isLoading) {
                Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    CircularProgressIndicator()
                }
            } else if (!uiState.isHealthConnectAvailable) {
                HealthConnectUnavailableCard()
            } else {
                uiState.summary?.let { summary ->
                    // Heart Rate Card
                    HealthMetricCard(
                        icon = Icons.Default.Favorite,
                        iconTint = RedPrimary,
                        title = "Heart Rate",
                        value = summary.latestHeartRate?.let { "${it.value.toInt()}" } ?: "--",
                        unit = "bpm",
                        subtitle = summary.restingHeartRate?.let { "Resting: ${it.toInt()} bpm" } ?: "",
                        alertLevel = when {
                            (summary.latestHeartRate?.value ?: 70.0) > 150 -> AlertLevel.CRITICAL
                            (summary.latestHeartRate?.value ?: 70.0) < 40 -> AlertLevel.CRITICAL
                            (summary.latestHeartRate?.value ?: 70.0) > 120 -> AlertLevel.WARNING
                            else -> AlertLevel.NORMAL
                        }
                    )

                    // Blood Oxygen Card
                    HealthMetricCard(
                        icon = Icons.Default.Air,
                        iconTint = Color(0xFF457B9D),
                        title = "Blood Oxygen",
                        value = summary.latestBloodOxygen?.let { "${it.value.toInt()}" } ?: "--",
                        unit = "%",
                        subtitle = "SpO2",
                        alertLevel = when {
                            (summary.latestBloodOxygen?.value ?: 98.0) < 90 -> AlertLevel.CRITICAL
                            (summary.latestBloodOxygen?.value ?: 98.0) < 94 -> AlertLevel.WARNING
                            else -> AlertLevel.NORMAL
                        }
                    )

                    // Steps Card
                    HealthMetricCard(
                        icon = Icons.Default.DirectionsWalk,
                        iconTint = GreenSafe,
                        title = "Steps Today",
                        value = "${summary.stepsToday}",
                        unit = "steps",
                        subtitle = "%.1f km".format(summary.distanceTodayMeters / 1000.0),
                        alertLevel = AlertLevel.NORMAL
                    )

                    // Calories Card
                    HealthMetricCard(
                        icon = Icons.Default.LocalFireDepartment,
                        iconTint = Color(0xFFF4A261),
                        title = "Calories Burned",
                        value = "${summary.caloriesToday.toInt()}",
                        unit = "kcal",
                        subtitle = "Active energy",
                        alertLevel = AlertLevel.NORMAL
                    )

                    // Sleep Card
                    HealthMetricCard(
                        icon = Icons.Default.Bedtime,
                        iconTint = Color(0xFF6C63FF),
                        title = "Sleep Last Night",
                        value = "${summary.sleepLastNightMinutes / 60}h ${summary.sleepLastNightMinutes % 60}m",
                        unit = "",
                        subtitle = when {
                            summary.sleepLastNightMinutes < 360 -> "Below recommended"
                            summary.sleepLastNightMinutes > 540 -> "Above average"
                            else -> "Within healthy range"
                        },
                        alertLevel = if (summary.sleepLastNightMinutes < 300) AlertLevel.WARNING else AlertLevel.NORMAL
                    )

                    // Last Updated
                    Text(
                        text = "Last updated: ${formatTimeSince(summary.lastUpdated)}",
                        fontSize = 12.sp,
                        color = Color.Gray,
                        modifier = Modifier.align(Alignment.CenterHorizontally)
                    )
                }
            }
        }
    }
}

private enum class AlertLevel { NORMAL, WARNING, CRITICAL }

@Composable
private fun HealthMetricCard(
    icon: ImageVector,
    iconTint: Color,
    title: String,
    value: String,
    unit: String,
    subtitle: String,
    alertLevel: AlertLevel
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(16.dp),
        elevation = CardDefaults.cardElevation(defaultElevation = 2.dp)
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Box(
                modifier = Modifier
                    .size(48.dp)
                    .clip(CircleShape)
                    .background(iconTint.copy(alpha = 0.1f)),
                contentAlignment = Alignment.Center
            ) {
                Icon(icon, title, tint = iconTint, modifier = Modifier.size(28.dp))
            }

            Spacer(Modifier.width(16.dp))

            Column(modifier = Modifier.weight(1f)) {
                Text(title, fontSize = 14.sp, color = Color.Gray)
                Row(verticalAlignment = Alignment.Bottom) {
                    Text(value, fontSize = 28.sp, fontWeight = FontWeight.Bold)
                    if (unit.isNotEmpty()) {
                        Spacer(Modifier.width(4.dp))
                        Text(unit, fontSize = 14.sp, color = Color.Gray, modifier = Modifier.padding(bottom = 4.dp))
                    }
                }
                if (subtitle.isNotEmpty()) {
                    Text(subtitle, fontSize = 12.sp, color = Color.Gray)
                }
            }

            if (alertLevel != AlertLevel.NORMAL) {
                Icon(
                    Icons.Default.Warning,
                    "Alert",
                    tint = if (alertLevel == AlertLevel.CRITICAL) RedPrimary else YellowWarning,
                    modifier = Modifier.size(24.dp)
                )
            }
        }
    }
}

@Composable
private fun HealthConnectUnavailableCard() {
    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(16.dp),
        colors = CardDefaults.cardColors(containerColor = YellowWarning.copy(alpha = 0.1f))
    ) {
        Column(modifier = Modifier.padding(24.dp), horizontalAlignment = Alignment.CenterHorizontally) {
            Icon(Icons.Default.Warning, "Unavailable", tint = YellowWarning, modifier = Modifier.size(48.dp))
            Spacer(Modifier.height(16.dp))
            Text("Health Connect Not Available", fontWeight = FontWeight.Bold, fontSize = 18.sp)
            Spacer(Modifier.height(8.dp))
            Text(
                "Install Health Connect from the Play Store and grant permissions to see your health data.",
                fontSize = 14.sp,
                color = Color.Gray
            )
        }
    }
}

private fun formatTimeSince(epochMillis: Long): String {
    val minutes = (System.currentTimeMillis() - epochMillis) / 60_000
    return when {
        minutes < 1 -> "Just now"
        minutes < 60 -> "$minutes min ago"
        minutes < 1440 -> "${minutes / 60}h ago"
        else -> "${minutes / 1440}d ago"
    }
}
