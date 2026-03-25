package com.thewatch.app.ui.screens.home

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Menu
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Notifications
import androidx.compose.material3.Badge
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.runtime.collectAsState
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.navigation.NavController
import com.google.android.gms.maps.model.CameraPosition
import com.google.android.gms.maps.model.LatLng
import com.google.maps.android.compose.Circle
import com.google.maps.android.compose.GoogleMap
import com.google.maps.android.compose.Marker
import com.google.maps.android.compose.MarkerState
import com.google.maps.android.compose.rememberCameraPositionState
import com.thewatch.app.navigation.NavRoute
import com.thewatch.app.ui.components.SOSButton
import com.thewatch.app.ui.components.NavigationDrawer
import com.thewatch.app.ui.theme.GreenSafe
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.White
import kotlinx.coroutines.launch

@Composable
fun HomeScreen(
    navController: NavController,
    viewModel: HomeViewModel = hiltViewModel()
) {
    var showNavigationDrawer by remember { mutableStateOf(false) }
    var sosActive by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    // Collect live data from ViewModel
    val nearbyResponders by viewModel.nearbyResponders.collectAsState()
    val isAlertActive by viewModel.isAlertActive.collectAsState()
    val unreadNotificationsCount by viewModel.unreadNotifications.collectAsState()
    var unreadNotifications by remember { mutableStateOf(unreadNotificationsCount) }

    val userLocation = LatLng(40.7128, -74.0060)
    val cameraPositionState = rememberCameraPositionState {
        position = CameraPosition.fromLatLngZoom(userLocation, 15f)
    }

    Box(
        modifier = Modifier.fillMaxSize()
    ) {
        GoogleMap(
            modifier = Modifier.fillMaxSize(),
            cameraPositionState = cameraPositionState
        ) {
            // User location marker
            Marker(
                state = MarkerState(position = userLocation),
                title = "Your Location"
            )

            // Proximity ring circles at scope radii (only visible during active alert)
            if (isAlertActive) {
                // 1km ring — CheckIn scope
                Circle(
                    center = userLocation,
                    radius = 1000.0,
                    strokeColor = Color.Red.copy(alpha = 0.6f),
                    fillColor = Color.Red.copy(alpha = 0.05f),
                    strokeWidth = 3f
                )
                // 3km ring — Emergency scope
                Circle(
                    center = userLocation,
                    radius = 3000.0,
                    strokeColor = Color(0xFFFF9800).copy(alpha = 0.5f),
                    fillColor = Color(0xFFFF9800).copy(alpha = 0.03f),
                    strokeWidth = 2f
                )
                // 10km ring — CommunityWatch scope
                Circle(
                    center = userLocation,
                    radius = 10000.0,
                    strokeColor = Color(0xFFFFC107).copy(alpha = 0.3f),
                    fillColor = Color(0xFFFFC107).copy(alpha = 0.02f),
                    strokeWidth = 1f
                )
            }

            // Real-time responder position markers from API / SignalR
            nearbyResponders.forEach { responder ->
                Marker(
                    state = MarkerState(
                        position = LatLng(responder.latitude, responder.longitude)
                    ),
                    title = "${responder.name} (${responder.type})",
                    snippet = buildString {
                        append("${responder.distance.toInt()}m away")
                        if (responder.eta > 0) {
                            append(" — ETA: ${responder.eta} min")
                        }
                        if (responder.hasVehicle) {
                            append(" (vehicle)")
                        }
                    }
                )
            }
        }

        Column(
            modifier = Modifier
                .fillMaxWidth()
                .align(Alignment.TopCenter)
                .background(Navy)
                .padding(12.dp)
        ) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(8.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween
            ) {
                IconButton(
                    onClick = { showNavigationDrawer = !showNavigationDrawer },
                    modifier = Modifier.size(48.dp)
                ) {
                    Icon(
                        imageVector = Icons.Filled.Menu,
                        contentDescription = "Open navigation menu",
                        tint = White,
                        modifier = Modifier.size(24.dp)
                    )
                }

                Text(
                    text = "TheWatch",
                    fontSize = 18.sp,
                    fontWeight = FontWeight.Bold,
                    color = White
                )

                Row {
                    IconButton(
                        onClick = {},
                        modifier = Modifier.size(48.dp)
                    ) {
                        Icon(
                            imageVector = Icons.Filled.Search,
                            contentDescription = "Search locations",
                            tint = White,
                            modifier = Modifier.size(24.dp)
                        )
                    }

                    IconButton(
                        onClick = {},
                        modifier = Modifier
                            .size(48.dp)
                            .semantics {
                                contentDescription = if (unreadNotifications > 0) {
                                    "Notifications, $unreadNotifications unread"
                                } else {
                                    "Notifications, none unread"
                                }
                            }
                    ) {
                        Box {
                            Icon(
                                imageVector = Icons.Filled.Notifications,
                                contentDescription = null,
                                tint = White,
                                modifier = Modifier.size(24.dp)
                            )
                            if (unreadNotifications > 0) {
                                Badge(
                                    modifier = Modifier
                                        .align(Alignment.TopEnd)
                                        .padding(top = 2.dp, end = 2.dp)
                                ) {
                                    Text(
                                        text = unreadNotifications.toString(),
                                        fontSize = 8.sp
                                    )
                                }
                            }
                        }
                    }
                }
            }
        }

        Box(
            modifier = Modifier
                .align(Alignment.Center)
                .padding(bottom = 100.dp)
        ) {
            if (sosActive) {
                Box(
                    modifier = Modifier
                        .size(140.dp)
                        .background(
                            color = Color.Red.copy(alpha = 0.1f),
                            shape = RoundedCornerShape(70.dp)
                        )
                        .align(Alignment.Center)
                )
            }
        }

        Column(
            modifier = Modifier
                .fillMaxWidth()
                .align(Alignment.BottomCenter)
                .background(
                    color = Color.White.copy(alpha = 0.95f),
                    shape = RoundedCornerShape(topStart = 16.dp, topEnd = 16.dp)
                )
                .padding(16.dp)
        ) {
            Text(
                text = "Status: Safe",
                fontSize = 14.sp,
                color = GreenSafe,
                fontWeight = FontWeight.SemiBold,
                modifier = Modifier.semantics { heading() }
            )

            Text(
                text = "All systems normal",
                fontSize = 12.sp,
                color = Color.Gray,
                modifier = Modifier.padding(top = 4.dp)
            )

            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(top = 16.dp),
                horizontalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                Box(
                    modifier = Modifier
                        .weight(1f)
                        .background(Color(0xFFF5F5F5), RoundedCornerShape(8.dp))
                        .padding(12.dp)
                        .clickable { navController.navigate(NavRoute.Contacts.route) }
                        .semantics { contentDescription = "Emergency contacts. Tap to view." }
                ) {
                    Column {
                        Text("Emergency", fontSize = 10.sp, color = Color.Gray)
                        Text("Contacts", fontSize = 12.sp, fontWeight = FontWeight.SemiBold, color = Navy)
                    }
                }

                Box(
                    modifier = Modifier
                        .weight(1f)
                        .background(Color(0xFFF5F5F5), RoundedCornerShape(8.dp))
                        .padding(12.dp)
                        .clickable { navController.navigate(NavRoute.History.route) }
                        .semantics { contentDescription = "Recent activity. Tap to view history." }
                ) {
                    Column {
                        Text("Recent", fontSize = 10.sp, color = Color.Gray)
                        Text("Activity", fontSize = 12.sp, fontWeight = FontWeight.SemiBold, color = Navy)
                    }
                }

                Box(
                    modifier = Modifier
                        .weight(1f)
                        .background(Color(0xFFF5F5F5), RoundedCornerShape(8.dp))
                        .padding(12.dp)
                        .clickable { navController.navigate(NavRoute.Evacuation.route) }
                        .semantics { contentDescription = "Evacuation routes. Tap to view." }
                ) {
                    Column {
                        Text("Evacuation", fontSize = 10.sp, color = Color.Gray)
                        Text("Routes", fontSize = 12.sp, fontWeight = FontWeight.SemiBold, color = Navy)
                    }
                }
            }
        }

        Box(
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .padding(bottom = 32.dp)
        ) {
            SOSButton(
                onSOSActivate = {
                    // Navigate to full-screen SOS countdown overlay
                    navController.navigate(NavRoute.SosCountdown.route)
                }
            )
        }

        if (showNavigationDrawer) {
            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .background(Color.Black.copy(alpha = 0.5f))
                    .clickable { showNavigationDrawer = false }
            )

            NavigationDrawer(
                userName = "Alex Rivera",
                userStatus = "Safe",
                onHomeClick = { showNavigationDrawer = false },
                onProfileClick = { navController.navigate(NavRoute.Profile.route); showNavigationDrawer = false },
                onContactsClick = { navController.navigate(NavRoute.Contacts.route); showNavigationDrawer = false },
                onHistoryClick = { navController.navigate(NavRoute.History.route); showNavigationDrawer = false },
                onPermissionsClick = { navController.navigate(NavRoute.Permissions.route); showNavigationDrawer = false },
                onVolunteeringClick = { navController.navigate(NavRoute.Volunteering.route); showNavigationDrawer = false },
                onEvacuationClick = { navController.navigate(NavRoute.Evacuation.route); showNavigationDrawer = false },
                onSettingsClick = { navController.navigate(NavRoute.Settings.route); showNavigationDrawer = false },
                onSignOutClick = { navController.navigate(NavRoute.Login.route) { popUpTo(NavRoute.AppGraph.route) { inclusive = true } }; showNavigationDrawer = false }
            )
        }
    }
}
