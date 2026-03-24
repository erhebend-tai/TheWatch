/**
 * ┌─────────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                            │
 * │ File:    CommunityAlertMarker.kt                                           │
 * │ Purpose: Google Maps Compose marker for community-sourced safety alerts.   │
 * │          Amber-colored pins with alert type icon and a confidence           │
 * │          percentage badge. Tapping a marker expands an info window with     │
 * │          full details: description, timestamp, severity, responder count.  │
 * │ Date:    2026-03-24                                                        │
 * │ Author:  Claude (Anthropic)                                                │
 * │ Deps:    com.google.maps.android.compose (MarkerInfoWindowContent)         │
 * │          com.thewatch.app.data.model.CommunityAlert                        │
 * │          com.thewatch.app.ui.theme.MarkerAlertAmber                        │
 * │ License: Proprietary — TheWatch Safety Platform                            │
 * │                                                                            │
 * │ Usage Example (inside a GoogleMap content lambda):                         │
 * │   val alerts: List<CommunityAlert> = viewModel.alerts.collectAsState()    │
 * │   GoogleMap(...) {                                                         │
 * │       alerts.forEach { alert ->                                            │
 * │           CommunityAlertMarker(                                            │
 * │               alert = alert,                                               │
 * │               confidencePercent = computeConfidence(alert),                │
 * │               onTap = { selectedAlert = alert }                            │
 * │           )                                                                │
 * │       }                                                                    │
 * │   }                                                                        │
 * │                                                                            │
 * │ Confidence Calculation Logic (external to this component):                │
 * │   - Base: 40% for a single report                                         │
 * │   - +10% per corroborating reporter (max +40%)                            │
 * │   - +10% if verified by official source                                   │
 * │   - +10% if within last 15 minutes                                        │
 * │   - Capped at 99%                                                         │
 * │                                                                            │
 * │ Severity Color Mapping:                                                    │
 * │   Critical → Red, High → Orange-Red, Medium → Amber, Low → Yellow-Green  │
 * └─────────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Bolt
import androidx.compose.material.icons.filled.LocalHospital
import androidx.compose.material.icons.filled.People
import androidx.compose.material.icons.filled.ReportProblem
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material.icons.filled.Water
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.google.android.gms.maps.model.BitmapDescriptorFactory
import com.google.android.gms.maps.model.LatLng
import com.google.maps.android.compose.MarkerInfoWindowContent
import com.google.maps.android.compose.MarkerState
import com.thewatch.app.data.model.CommunityAlert
import com.thewatch.app.ui.theme.MarkerAlertAmber
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.RedPrimary
import com.thewatch.app.ui.theme.White
import java.time.format.DateTimeFormatter
import java.time.temporal.ChronoUnit

// ─────────────────────────────────────────────────────────────────────────────
// Alert type → icon mapping
// ─────────────────────────────────────────────────────────────────────────────
private fun alertTypeIcon(type: String): ImageVector = when {
    type.contains("Suspicious", ignoreCase = true) -> Icons.Filled.ReportProblem
    type.contains("Traffic", ignoreCase = true)    -> Icons.Filled.Warning
    type.contains("Medical", ignoreCase = true)    -> Icons.Filled.LocalHospital
    type.contains("Power", ignoreCase = true)      -> Icons.Filled.Bolt
    type.contains("Flood", ignoreCase = true)      -> Icons.Filled.Water
    else                                            -> Icons.Filled.Warning
}

// ─────────────────────────────────────────────────────────────────────────────
// Severity → color mapping
// ─────────────────────────────────────────────────────────────────────────────
private fun severityColor(severity: String): Color = when (severity) {
    "Critical" -> Color(0xFFD32F2F) // deep red
    "High"     -> Color(0xFFE64A19) // orange-red
    "Medium"   -> MarkerAlertAmber  // amber
    "Low"      -> Color(0xFF7CB342) // yellow-green
    else       -> MarkerAlertAmber
}

/**
 * Compute a mock confidence percentage for a community alert.
 *
 * Production implementation should aggregate corroborating reports,
 * verified sources, and recency.
 *
 * @param alert The community alert
 * @return Confidence as an integer percentage (0-99)
 */
fun computeAlertConfidence(alert: CommunityAlert): Int {
    var confidence = 40 // base for a single report
    confidence += (alert.respondersCount * 10).coerceAtMost(40) // corroborating
    val minutesAgo = ChronoUnit.MINUTES.between(alert.timestamp, java.time.LocalDateTime.now())
    if (minutesAgo <= 15) confidence += 10 // recency bonus
    return confidence.coerceAtMost(99)
}

/**
 * Renders a community alert marker on the Google Map with an amber pin
 * and a custom info window showing alert details and confidence badge.
 *
 * @param alert The community alert data model
 * @param confidencePercent Confidence level (0-99). Use [computeAlertConfidence] or your own logic.
 * @param onTap Optional callback when marker is tapped
 */
@Composable
fun CommunityAlertMarker(
    alert: CommunityAlert,
    confidencePercent: Int = computeAlertConfidence(alert),
    onTap: () -> Unit = {}
) {
    val position = LatLng(alert.latitude, alert.longitude)
    val sevColor = severityColor(alert.severity)

    MarkerInfoWindowContent(
        state = MarkerState(position = position),
        title = alert.type,
        snippet = "${alert.severity} - ${confidencePercent}% confidence",
        icon = BitmapDescriptorFactory.defaultMarker(BitmapDescriptorFactory.HUE_ORANGE),
        onClick = {
            onTap()
            false // show info window
        }
    ) {
        CommunityAlertInfoWindow(
            alert = alert,
            confidencePercent = confidencePercent,
            sevColor = sevColor
        )
    }
}

/**
 * Custom info window content for a community alert marker.
 */
@Composable
private fun CommunityAlertInfoWindow(
    alert: CommunityAlert,
    confidencePercent: Int,
    sevColor: Color
) {
    val timeFormatter = DateTimeFormatter.ofPattern("h:mm a")

    Column(
        modifier = Modifier
            .width(240.dp)
            .background(White, RoundedCornerShape(8.dp))
            .border(1.dp, MarkerAlertAmber.copy(alpha = 0.3f), RoundedCornerShape(8.dp))
            .padding(12.dp)
    ) {
        // ── Header: type icon + title + confidence badge ──
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween
        ) {
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.weight(1f)) {
                Box(
                    modifier = Modifier
                        .size(28.dp)
                        .clip(CircleShape)
                        .background(sevColor),
                    contentAlignment = Alignment.Center
                ) {
                    Icon(
                        imageVector = alertTypeIcon(alert.type),
                        contentDescription = alert.type,
                        tint = White,
                        modifier = Modifier.size(16.dp)
                    )
                }
                Column(modifier = Modifier.padding(start = 8.dp)) {
                    Text(
                        text = alert.type,
                        fontSize = 13.sp,
                        fontWeight = FontWeight.Bold,
                        color = Navy,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                    Text(
                        text = alert.timestamp.format(timeFormatter),
                        fontSize = 10.sp,
                        color = Color.Gray
                    )
                }
            }

            // ── Confidence badge ──
            Box(
                modifier = Modifier
                    .background(
                        color = if (confidencePercent >= 70) sevColor else Color.Gray,
                        shape = RoundedCornerShape(10.dp)
                    )
                    .padding(horizontal = 8.dp, vertical = 3.dp),
                contentAlignment = Alignment.Center
            ) {
                Text(
                    text = "${confidencePercent}%",
                    fontSize = 11.sp,
                    fontWeight = FontWeight.Bold,
                    color = White
                )
            }
        }

        // ── Description ──
        Text(
            text = alert.description,
            fontSize = 11.sp,
            color = Color.DarkGray,
            maxLines = 3,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.padding(top = 8.dp)
        )

        // ── Footer: severity + responder count ──
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 8.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            // Severity chip
            Text(
                text = alert.severity.uppercase(),
                fontSize = 10.sp,
                fontWeight = FontWeight.Bold,
                color = sevColor,
                modifier = Modifier
                    .background(
                        sevColor.copy(alpha = 0.1f),
                        RoundedCornerShape(4.dp)
                    )
                    .padding(horizontal = 6.dp, vertical = 2.dp)
            )

            // Responder count
            if (alert.respondersCount > 0) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(
                        imageVector = Icons.Filled.People,
                        contentDescription = "Responders",
                        tint = Navy,
                        modifier = Modifier.size(14.dp)
                    )
                    Text(
                        text = " ${alert.respondersCount} responding",
                        fontSize = 10.sp,
                        color = Navy
                    )
                }
            }
        }
    }
}
