package com.thewatch.app.ui.screens.evacuation

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.LocationOn
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.navigation.NavController
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.RedPrimary
import com.thewatch.app.ui.theme.White

data class EvacuationRoute(
    val id: String,
    val name: String,
    val description: String,
    val distance: String,
    val estimatedTime: String,
    val waypoints: List<String>,
    val shelterName: String,
    val shelterAddress: String,
    val capacity: Int
)

@Composable
fun EvacuationScreen(navController: NavController) {
    val routes = listOf(
        EvacuationRoute(
            id = "1",
            name = "Route 1: Direct Path North",
            description = "Main evacuation route to Central Park",
            distance = "2.5 km",
            estimatedTime = "12 minutes walking",
            waypoints = listOf("5th Avenue", "Central Park South", "Park Entrance"),
            shelterName = "Central Park Emergency Shelter",
            shelterAddress = "Central Park, Manhattan",
            capacity = 500
        ),
        EvacuationRoute(
            id = "2",
            name = "Route 2: Riverside Alternative",
            description = "Alternative route via Hudson River Park",
            distance = "3.2 km",
            estimatedTime = "16 minutes walking",
            waypoints = listOf("10th Avenue", "Hudson River Park", "Pier 72"),
            shelterName = "Riverside Community Center",
            shelterAddress = "Riverside Drive, Manhattan",
            capacity = 300
        ),
        EvacuationRoute(
            id = "3",
            name = "Route 3: Subway Junction",
            description = "Route with access to subway stations",
            distance = "1.8 km",
            estimatedTime = "9 minutes walking",
            waypoints = listOf("Broadway", "Times Square Station", "Transit Hub"),
            shelterName = "Grand Central Terminal Shelter",
            shelterAddress = "42nd Street, Manhattan",
            capacity = 1000
        )
    )

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(White)
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
                text = "Evacuation Routes",
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold,
                color = White,
                modifier = Modifier.weight(1f)
            )
        }

        // Routes List
        LazyColumn(modifier = Modifier.padding(16.dp)) {
            items(routes) { route ->
                EvacuationRouteCard(route)
                Spacer(modifier = Modifier.height(12.dp))
            }
        }
    }
}

@Composable
private fun EvacuationRouteCard(route: EvacuationRoute) {
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .background(
                color = Color(0xFFFAFAFA),
                shape = RoundedCornerShape(8.dp)
            )
            .padding(12.dp)
    ) {
        Column(modifier = Modifier.fillMaxWidth()) {
            // Route Header
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Icon(
                    imageVector = Icons.Filled.LocationOn,
                    contentDescription = "Location",
                    tint = RedPrimary,
                    modifier = Modifier.padding(end = 8.dp)
                )
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = route.name,
                        fontSize = 14.sp,
                        fontWeight = FontWeight.Bold,
                        color = Navy
                    )
                    Text(
                        text = route.description,
                        fontSize = 12.sp,
                        color = Color.Gray
                    )
                }
            }

            Spacer(modifier = Modifier.height(12.dp))

            // Distance and Time
            Row(modifier = Modifier.fillMaxWidth()) {
                Box(
                    modifier = Modifier
                        .weight(1f)
                        .background(
                            color = Color(0xFFE3F2FD),
                            shape = RoundedCornerShape(4.dp)
                        )
                        .padding(8.dp)
                ) {
                    Column {
                        Text(
                            text = "Distance",
                            fontSize = 10.sp,
                            color = Color.Gray
                        )
                        Text(
                            text = route.distance,
                            fontSize = 12.sp,
                            fontWeight = FontWeight.Bold,
                            color = Navy
                        )
                    }
                }

                Spacer(modifier = Modifier.padding(start = 8.dp))

                Box(
                    modifier = Modifier
                        .weight(1f)
                        .background(
                            color = Color(0xFFE8F5E9),
                            shape = RoundedCornerShape(4.dp)
                        )
                        .padding(8.dp)
                ) {
                    Column {
                        Text(
                            text = "Estimated Time",
                            fontSize = 10.sp,
                            color = Color.Gray
                        )
                        Text(
                            text = route.estimatedTime,
                            fontSize = 12.sp,
                            fontWeight = FontWeight.Bold,
                            color = Navy
                        )
                    }
                }
            }

            Spacer(modifier = Modifier.height(12.dp))

            // Waypoints
            Text(
                text = "Waypoints: ${route.waypoints.joinToString(" → ")}",
                fontSize = 11.sp,
                color = Color.Gray
            )

            Spacer(modifier = Modifier.height(12.dp))

            // Shelter Info
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .background(
                        color = Color(0xFFFFF3E0),
                        shape = RoundedCornerShape(4.dp)
                    )
                    .padding(8.dp)
            ) {
                Column {
                    Text(
                        text = "Shelter: ${route.shelterName}",
                        fontSize = 12.sp,
                        fontWeight = FontWeight.Bold,
                        color = Navy
                    )
                    Text(
                        text = route.shelterAddress,
                        fontSize = 11.sp,
                        color = Color.Gray
                    )
                    Text(
                        text = "Capacity: ${route.capacity} people",
                        fontSize = 11.sp,
                        color = Color.Gray
                    )
                }
            }

            Spacer(modifier = Modifier.height(12.dp))

            // Action Button
            Button(
                onClick = { },
                modifier = Modifier.fillMaxWidth(),
                colors = ButtonDefaults.buttonColors(containerColor = RedPrimary)
            ) {
                Text("Get Directions", color = White, fontSize = 12.sp)
            }
        }
    }
}
