/**
 * ┌─────────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                            │
 * │ File:    ResponderMapMarker.kt                                             │
 * │ Purpose: Google Maps Compose marker for volunteer/professional responders. │
 * │          Green pin with custom info window showing responder name, type,   │
 * │          ETA (computed from distance and assumed speed), certifications,   │
 * │          and status icon. Supports marker clustering when zoomed out via   │
 * │          the clustering composable wrapper.                                │
 * │ Date:    2026-03-24                                                        │
 * │ Author:  Claude (Anthropic)                                                │
 * │ Deps:    com.google.maps.android.compose (Marker, MarkerInfoWindow)        │
 * │          com.thewatch.app.data.model.Responder                             │
 * │          com.thewatch.app.ui.theme.MarkerResponderGreen                    │
 * │ License: Proprietary — TheWatch Safety Platform                            │
 * │                                                                            │
 * │ Usage Example (inside a GoogleMap content lambda):                         │
 * │   val responders: List<Responder> = viewModel.responders.collectAsState()  │
 * │   GoogleMap(...) {                                                         │
 * │       responders.forEach { responder ->                                    │
 * │           ResponderMapMarker(responder = responder)                        │
 * │       }                                                                    │
 * │   }                                                                        │
 * │                                                                            │
 * │ Clustering Example:                                                        │
 * │   // When using google-maps-compose-utils clustering:                      │
 * │   Clustering(                                                              │
 * │       items = responders.map { ResponderClusterItem(it) },                │
 * │       onClusterClick = { /* zoom in */ },                                 │
 * │       clusterContent = { cluster ->                                        │
 * │           ResponderClusterBadge(count = cluster.size)                      │
 * │       },                                                                   │
 * │       clusterItemContent = { item ->                                       │
 * │           ResponderMapMarker(responder = item.responder)                   │
 * │       }                                                                    │
 * │   )                                                                        │
 * │                                                                            │
 * │ ETA Calculation:                                                           │
 * │   - Walking: 5 km/h (1.39 m/s) — used for Volunteer type                 │
 * │   - Driving: 30 km/h (8.33 m/s) — used for EMT, Paramedic, Officer       │
 * │   - If Responder.eta > 0, that value is used directly.                    │
 * │   - Haversine distance is used as-is (no routing correction factor yet).  │
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
import androidx.compose.foundation.layout.wrapContentSize
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.DirectionsCar
import androidx.compose.material.icons.filled.DirectionsWalk
import androidx.compose.material.icons.filled.Person
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import com.google.android.gms.maps.model.BitmapDescriptorFactory
import com.google.android.gms.maps.model.LatLng
import com.google.maps.android.compose.MarkerInfoWindowContent
import com.google.maps.android.compose.MarkerState
import com.thewatch.app.data.model.Responder
import com.thewatch.app.ui.theme.GreenSafe
import com.thewatch.app.ui.theme.MarkerResponderGreen
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.White
import kotlin.math.roundToInt

// ─────────────────────────────────────────────────────────────────────────────
// Speed constants for ETA estimation
// ─────────────────────────────────────────────────────────────────────────────
private const val WALKING_SPEED_MS = 1.39   // 5 km/h in m/s
private const val DRIVING_SPEED_MS = 8.33   // 30 km/h in m/s (urban average)

/** Types that are assumed to have vehicle access. */
private val DRIVING_TYPES = setOf("EMT", "Paramedic", "Firefighter", "Off-Duty Officer")

/**
 * Renders a responder marker on the Google Map with a custom info window.
 *
 * The marker uses a green HUE pin. Tapping it shows an info window with:
 * - Responder name and type
 * - ETA (from Responder.eta or computed from distance)
 * - Distance in meters
 * - Certifications list
 * - Walking/driving icon based on responder type
 *
 * @param responder The responder data model
 * @param onClick Optional callback when marker is tapped (before info window shows)
 */
@Composable
fun ResponderMapMarker(
    responder: Responder,
    onClick: () -> Unit = {}
) {
    val position = LatLng(responder.latitude, responder.longitude)
    val eta = if (responder.eta > 0) {
        responder.eta
    } else {
        computeEta(responder.distance, responder.type)
    }
    val isDriving = responder.type in DRIVING_TYPES

    MarkerInfoWindowContent(
        state = MarkerState(position = position),
        title = responder.name,
        snippet = "${responder.type} - ${eta}min ETA",
        icon = BitmapDescriptorFactory.defaultMarker(BitmapDescriptorFactory.HUE_GREEN),
        onClick = {
            onClick()
            false // return false to show info window
        }
    ) {
        // ── Custom info window content ──
        ResponderInfoWindowContent(
            responder = responder,
            eta = eta,
            isDriving = isDriving
        )
    }
}

/**
 * Custom info window content for a responder marker.
 */
@Composable
private fun ResponderInfoWindowContent(
    responder: Responder,
    eta: Int,
    isDriving: Boolean
) {
    Column(
        modifier = Modifier
            .width(220.dp)
            .background(White, RoundedCornerShape(8.dp))
            .border(1.dp, MarkerResponderGreen.copy(alpha = 0.3f), RoundedCornerShape(8.dp))
            .padding(12.dp)
    ) {
        // ── Name + Status ──
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(
                    modifier = Modifier
                        .size(28.dp)
                        .clip(CircleShape)
                        .background(MarkerResponderGreen),
                    contentAlignment = Alignment.Center
                ) {
                    Icon(
                        imageVector = Icons.Filled.Person,
                        contentDescription = null,
                        tint = White,
                        modifier = Modifier.size(18.dp)
                    )
                }
                Column(modifier = Modifier.padding(start = 8.dp)) {
                    Text(
                        text = responder.name,
                        fontSize = 13.sp,
                        fontWeight = FontWeight.Bold,
                        color = Navy,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                    Text(
                        text = responder.type,
                        fontSize = 11.sp,
                        color = Color.Gray
                    )
                }
            }
            Icon(
                imageVector = Icons.Filled.CheckCircle,
                contentDescription = "Available",
                tint = GreenSafe,
                modifier = Modifier.size(18.dp)
            )
        }

        // ── ETA + Distance ──
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 8.dp),
            horizontalArrangement = Arrangement.SpaceBetween
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(
                    imageVector = if (isDriving) Icons.Filled.DirectionsCar else Icons.Filled.DirectionsWalk,
                    contentDescription = if (isDriving) "Driving" else "Walking",
                    tint = Navy,
                    modifier = Modifier.size(14.dp)
                )
                Text(
                    text = " ${eta} min",
                    fontSize = 12.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = Navy
                )
            }
            Text(
                text = "${responder.distance.roundToInt()}m away",
                fontSize = 11.sp,
                color = Color.Gray
            )
        }

        // ── Certifications ──
        if (responder.certifications.isNotEmpty()) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(top = 6.dp),
                horizontalArrangement = Arrangement.spacedBy(4.dp)
            ) {
                responder.certifications.take(3).forEach { cert ->
                    Text(
                        text = cert,
                        fontSize = 9.sp,
                        color = MarkerResponderGreen,
                        fontWeight = FontWeight.Medium,
                        modifier = Modifier
                            .background(
                                MarkerResponderGreen.copy(alpha = 0.1f),
                                RoundedCornerShape(4.dp)
                            )
                            .padding(horizontal = 4.dp, vertical = 2.dp)
                    )
                }
            }
        }
    }
}

/**
 * Cluster badge shown when multiple responders are grouped at low zoom levels.
 *
 * @param count Number of responders in the cluster
 */
@Composable
fun ResponderClusterBadge(count: Int) {
    Box(
        modifier = Modifier
            .size(40.dp)
            .clip(CircleShape)
            .background(MarkerResponderGreen)
            .border(2.dp, White, CircleShape),
        contentAlignment = Alignment.Center
    ) {
        Text(
            text = count.toString(),
            fontSize = 14.sp,
            fontWeight = FontWeight.Bold,
            color = White
        )
    }
}

/**
 * Compute ETA in minutes from distance (meters) and responder type.
 */
private fun computeEta(distanceMeters: Double, type: String): Int {
    val speedMs = if (type in DRIVING_TYPES) DRIVING_SPEED_MS else WALKING_SPEED_MS
    val seconds = distanceMeters / speedMs
    return (seconds / 60.0).roundToInt().coerceAtLeast(1)
}
