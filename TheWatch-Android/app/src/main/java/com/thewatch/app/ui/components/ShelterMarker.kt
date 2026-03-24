/**
 * ┌─────────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                            │
 * │ File:    ShelterMarker.kt                                                  │
 * │ Purpose: Google Maps Compose marker for emergency shelters. Displays a     │
 * │          blue house-icon pin with a capacity fill indicator (e.g., 45/100).│
 * │          Marker color shifts based on occupancy:                           │
 * │            - Green: < 50% occupancy (plenty of room)                      │
 * │            - Yellow: 50-80% occupancy (filling up)                        │
 * │            - Red: > 80% occupancy (near/at capacity)                      │
 * │          Tapping opens an info window with shelter details: name, address, │
 * │          capacity bar, amenities (accessibility, medical, pet area),      │
 * │          and contact phone.                                                │
 * │ Date:    2026-03-24                                                        │
 * │ Author:  Claude (Anthropic)                                                │
 * │ Deps:    com.google.maps.android.compose (MarkerInfoWindowContent)         │
 * │          com.thewatch.app.data.maps.Shelter                                │
 * │          com.thewatch.app.ui.theme.MarkerShelterBlue                       │
 * │ License: Proprietary — TheWatch Safety Platform                            │
 * │                                                                            │
 * │ Usage Example (inside a GoogleMap content lambda):                         │
 * │   val shelters: List<Shelter> = viewModel.shelters.collectAsState()       │
 * │   GoogleMap(...) {                                                         │
 * │       shelters.forEach { shelter ->                                        │
 * │           ShelterMarker(                                                   │
 * │               shelter = shelter,                                           │
 * │               onTap = { selectedShelter = shelter }                        │
 * │           )                                                                │
 * │       }                                                                    │
 * │   }                                                                        │
 * │                                                                            │
 * │ Occupancy Color Thresholds:                                                │
 * │   occupancy < 50%  → GreenSafe (#06A77D) — "Available"                   │
 * │   50% <= occ < 80% → YellowWarning (#FFD60A) — "Filling Up"             │
 * │   occupancy >= 80% → RedPrimary (#E63946) — "Near Capacity"              │
 * │                                                                            │
 * │ Amenity Icons:                                                             │
 * │   Accessibility → wheelchair icon                                          │
 * │   Medical       → hospital cross icon                                      │
 * │   Pet Area      → pets icon                                                │
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
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Accessible
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.LocalHospital
import androidx.compose.material.icons.filled.Pets
import androidx.compose.material.icons.filled.Phone
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.google.android.gms.maps.model.BitmapDescriptorFactory
import com.google.android.gms.maps.model.LatLng
import com.google.maps.android.compose.MarkerInfoWindowContent
import com.google.maps.android.compose.MarkerState
import com.thewatch.app.data.maps.Shelter
import com.thewatch.app.ui.theme.GreenSafe
import com.thewatch.app.ui.theme.MarkerShelterBlue
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.RedPrimary
import com.thewatch.app.ui.theme.White
import com.thewatch.app.ui.theme.YellowWarning

// ─────────────────────────────────────────────────────────────────────────────
// Occupancy thresholds
// ─────────────────────────────────────────────────────────────────────────────
private const val MODERATE_THRESHOLD = 0.50f
private const val HIGH_THRESHOLD = 0.80f

/**
 * Determine the occupancy status color for a shelter.
 */
private fun occupancyColor(percent: Float): Color = when {
    percent >= HIGH_THRESHOLD     -> RedPrimary
    percent >= MODERATE_THRESHOLD -> YellowWarning
    else                          -> GreenSafe
}

/**
 * Human-readable occupancy status label.
 */
private fun occupancyLabel(percent: Float): String = when {
    percent >= HIGH_THRESHOLD     -> "Near Capacity"
    percent >= MODERATE_THRESHOLD -> "Filling Up"
    else                          -> "Available"
}

/**
 * Map marker HUE based on occupancy.
 */
private fun occupancyHue(percent: Float): Float = when {
    percent >= HIGH_THRESHOLD     -> BitmapDescriptorFactory.HUE_RED
    percent >= MODERATE_THRESHOLD -> BitmapDescriptorFactory.HUE_YELLOW
    else                          -> BitmapDescriptorFactory.HUE_GREEN
}

/**
 * Renders a shelter marker on the Google Map with occupancy-colored pin
 * and a custom info window showing capacity, amenities, and contact info.
 *
 * @param shelter The shelter data model
 * @param onTap Optional callback when marker is tapped
 */
@Composable
fun ShelterMarker(
    shelter: Shelter,
    onTap: () -> Unit = {}
) {
    val occPercent = shelter.occupancyPercent
    val occColor = occupancyColor(occPercent)

    MarkerInfoWindowContent(
        state = MarkerState(position = shelter.location),
        title = shelter.name,
        snippet = "${shelter.currentOccupancy}/${shelter.capacity} — ${occupancyLabel(occPercent)}",
        icon = BitmapDescriptorFactory.defaultMarker(occupancyHue(occPercent)),
        onClick = {
            onTap()
            false // show info window
        }
    ) {
        ShelterInfoWindowContent(shelter = shelter, occPercent = occPercent, occColor = occColor)
    }
}

/**
 * Custom info window content for a shelter marker.
 */
@Composable
private fun ShelterInfoWindowContent(
    shelter: Shelter,
    occPercent: Float,
    occColor: Color
) {
    Column(
        modifier = Modifier
            .width(230.dp)
            .background(White, RoundedCornerShape(8.dp))
            .border(1.dp, MarkerShelterBlue.copy(alpha = 0.3f), RoundedCornerShape(8.dp))
            .padding(12.dp)
    ) {
        // ── Header: house icon + name ──
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Box(
                modifier = Modifier
                    .size(28.dp)
                    .clip(CircleShape)
                    .background(MarkerShelterBlue),
                contentAlignment = Alignment.Center
            ) {
                Icon(
                    imageVector = Icons.Filled.Home,
                    contentDescription = "Shelter",
                    tint = White,
                    modifier = Modifier.size(18.dp)
                )
            }
            Column(modifier = Modifier.padding(start = 8.dp)) {
                Text(
                    text = shelter.name,
                    fontSize = 13.sp,
                    fontWeight = FontWeight.Bold,
                    color = Navy,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis
                )
                if (shelter.address.isNotBlank()) {
                    Text(
                        text = shelter.address,
                        fontSize = 10.sp,
                        color = Color.Gray,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                }
            }
        }

        // ── Capacity bar ──
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 10.dp)
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                Text(
                    text = "${shelter.currentOccupancy} / ${shelter.capacity}",
                    fontSize = 12.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = Navy
                )
                Text(
                    text = occupancyLabel(occPercent),
                    fontSize = 10.sp,
                    fontWeight = FontWeight.Medium,
                    color = occColor
                )
            }

            LinearProgressIndicator(
                progress = { occPercent.coerceIn(0f, 1f) },
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(top = 4.dp)
                    .height(8.dp)
                    .clip(RoundedCornerShape(4.dp)),
                color = occColor,
                trackColor = Color(0xFFE0E0E0),
            )

            Text(
                text = "${(occPercent * 100).toInt()}% occupied",
                fontSize = 9.sp,
                color = Color.Gray,
                modifier = Modifier.padding(top = 2.dp)
            )
        }

        // ── Amenities row ──
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 8.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            if (shelter.hasAccessibility) {
                AmenityChip(
                    icon = Icons.Filled.Accessible,
                    label = "ADA"
                )
            }
            if (shelter.hasMedical) {
                AmenityChip(
                    icon = Icons.Filled.LocalHospital,
                    label = "Medical"
                )
            }
            if (shelter.hasPetArea) {
                AmenityChip(
                    icon = Icons.Filled.Pets,
                    label = "Pets"
                )
            }
        }

        // ── Contact phone ──
        if (shelter.contactPhone.isNotBlank()) {
            Row(
                modifier = Modifier.padding(top = 8.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Icon(
                    imageVector = Icons.Filled.Phone,
                    contentDescription = "Phone",
                    tint = MarkerShelterBlue,
                    modifier = Modifier.size(14.dp)
                )
                Text(
                    text = " ${shelter.contactPhone}",
                    fontSize = 11.sp,
                    color = Navy
                )
            }
        }
    }
}

/**
 * Small amenity chip with icon and label.
 */
@Composable
private fun AmenityChip(
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    label: String
) {
    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = Modifier
            .background(
                MarkerShelterBlue.copy(alpha = 0.1f),
                RoundedCornerShape(4.dp)
            )
            .padding(horizontal = 6.dp, vertical = 3.dp)
    ) {
        Icon(
            imageVector = icon,
            contentDescription = label,
            tint = MarkerShelterBlue,
            modifier = Modifier.size(12.dp)
        )
        Text(
            text = " $label",
            fontSize = 9.sp,
            color = MarkerShelterBlue,
            fontWeight = FontWeight.Medium
        )
    }
}
