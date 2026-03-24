package com.thewatch.app.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.thewatch.app.ui.theme.Ring0Red
import com.thewatch.app.ui.theme.Ring1Orange
import com.thewatch.app.ui.theme.Ring2Yellow
import com.thewatch.app.ui.theme.Ring3Gray

@Composable
fun ProximityRingOverlay(
    modifier: Modifier = Modifier,
    centerLatitude: Double,
    centerLongitude: Double,
    userLatitude: Double,
    userLongitude: Double
) {
    Box(modifier = modifier, contentAlignment = Alignment.Center) {
        RingLayer(size = 160.dp, color = Ring3Gray)
        RingLayer(size = 120.dp, color = Ring2Yellow)
        RingLayer(size = 80.dp, color = Ring1Orange)
        RingLayer(size = 40.dp, color = Ring0Red)
    }
}

@Composable
private fun RingLayer(
    size: androidx.compose.ui.unit.Dp,
    color: Color
) {
    Box(
        modifier = Modifier
            .size(size)
            .background(
                color = color.copy(alpha = 0.2f),
                shape = CircleShape
            )
    )
}
