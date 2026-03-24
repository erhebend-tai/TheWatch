package com.thewatch.app.ui.screens.history

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
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
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.RedPrimary
import com.thewatch.app.ui.theme.White
import com.thewatch.app.ui.theme.YellowWarning

@Composable
fun HistoryScreen(
    navController: NavController,
    viewModel: HistoryViewModel = hiltViewModel()
) {
    val uiState by viewModel.uiState.collectAsState()
    val selectedEventType by viewModel.selectedEventType.collectAsState()
    val selectedSeverity by viewModel.selectedSeverity.collectAsState()
    val selectedStatus by viewModel.selectedStatus.collectAsState()

    var showFilters by remember { mutableStateOf(false) }

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
                text = "Event History",
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold,
                color = White,
                modifier = Modifier.weight(1f)
            )
        }

        // Filter Section
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .background(Color(0xFFF5F5F5))
                .padding(12.dp)
        ) {
            Button(
                onClick = { showFilters = !showFilters },
                modifier = Modifier.fillMaxWidth(),
                colors = ButtonDefaults.buttonColors(containerColor = Navy)
            ) {
                Text("Filters", color = White)
            }

            if (showFilters) {
                Spacer(modifier = Modifier.height(12.dp))

                // Event Type Chips
                Row(modifier = Modifier.fillMaxWidth()) {
                    listOf("Alert", "Check-in", "Update").forEach { type ->
                        FilterChip(
                            label = type,
                            isSelected = selectedEventType == type,
                            onClick = { viewModel.filterByEventType(if (selectedEventType == type) null else type) }
                        )
                        Spacer(modifier = Modifier.width(8.dp))
                    }
                }

                Spacer(modifier = Modifier.height(12.dp))

                // Severity Chips
                Row(modifier = Modifier.fillMaxWidth()) {
                    listOf("Low", "Medium", "High", "Critical").forEach { severity ->
                        FilterChip(
                            label = severity,
                            isSelected = selectedSeverity == severity,
                            onClick = { viewModel.filterBySeverity(if (selectedSeverity == severity) null else severity) }
                        )
                        Spacer(modifier = Modifier.width(8.dp))
                    }
                }

                Spacer(modifier = Modifier.height(12.dp))

                // Status Chips
                Row(modifier = Modifier.fillMaxWidth()) {
                    listOf("Pending", "Active", "Resolved").forEach { status ->
                        FilterChip(
                            label = status,
                            isSelected = selectedStatus == status,
                            onClick = { viewModel.filterByStatus(if (selectedStatus == status) null else status) }
                        )
                        Spacer(modifier = Modifier.width(8.dp))
                    }
                }

                Spacer(modifier = Modifier.height(12.dp))

                Button(
                    onClick = { viewModel.clearFilters() },
                    modifier = Modifier.fillMaxWidth(),
                    colors = ButtonDefaults.buttonColors(containerColor = RedPrimary)
                ) {
                    Text("Clear Filters", color = White)
                }
            }
        }

        // Event List
        when (val state = uiState) {
            is HistoryUiState.Loading -> {
                Box(
                    modifier = Modifier
                        .fillMaxSize()
                        .padding(16.dp),
                    contentAlignment = Alignment.Center
                ) {
                    Text("Loading events...")
                }
            }

            is HistoryUiState.Success -> {
                if (state.events.isEmpty()) {
                    Box(
                        modifier = Modifier
                            .fillMaxSize()
                            .padding(16.dp),
                        contentAlignment = Alignment.Center
                    ) {
                        Text("No events found")
                    }
                } else {
                    LazyColumn(modifier = Modifier.padding(16.dp)) {
                        items(state.events) { event ->
                            HistoryEventCard(event)
                            Spacer(modifier = Modifier.height(12.dp))
                        }
                    }
                }
            }

            is HistoryUiState.Error -> {
                Box(
                    modifier = Modifier
                        .fillMaxSize()
                        .padding(16.dp),
                    contentAlignment = Alignment.Center
                ) {
                    Text(state.message, color = RedPrimary)
                }
            }

            else -> {}
        }
    }
}

@Composable
private fun FilterChip(
    label: String,
    isSelected: Boolean,
    onClick: () -> Unit
) {
    Button(
        onClick = onClick,
        modifier = Modifier.height(32.dp),
        colors = ButtonDefaults.buttonColors(
            containerColor = if (isSelected) Navy else Color(0xFFE0E0E0)
        ),
        shape = RoundedCornerShape(16.dp)
    ) {
        Text(
            label,
            fontSize = 12.sp,
            color = if (isSelected) White else Navy
        )
    }
}

@Composable
private fun HistoryEventCard(event: HistoryEvent) {
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .background(
                color = Color(0xFFFAFAFA),
                shape = RoundedCornerShape(8.dp)
            )
            .padding(12.dp)
    ) {
        Column {
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Box(
                    modifier = Modifier
                        .width(4.dp)
                        .height(40.dp)
                        .background(
                            color = when (event.severity) {
                                "Critical" -> RedPrimary
                                "High" -> YellowWarning
                                else -> Color.Gray
                            },
                            shape = RoundedCornerShape(2.dp)
                        )
                )

                Spacer(modifier = Modifier.width(12.dp))

                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = event.eventType,
                        fontSize = 14.sp,
                        fontWeight = FontWeight.Bold,
                        color = Navy
                    )
                    Text(
                        text = event.timestamp,
                        fontSize = 12.sp,
                        color = Color.Gray
                    )
                }

                Box(
                    modifier = Modifier
                        .background(
                            color = when (event.status) {
                                "Resolved" -> Color(0xFFE8F5E9)
                                "Active" -> Color(0xFFFFE0B2)
                                else -> Color(0xFFF3E5F5)
                            },
                            shape = RoundedCornerShape(4.dp)
                        )
                        .padding(6.dp, 2.dp)
                ) {
                    Text(
                        text = event.status,
                        fontSize = 10.sp,
                        fontWeight = FontWeight.Bold,
                        color = Navy
                    )
                }
            }

            Spacer(modifier = Modifier.height(8.dp))

            Text(
                text = "Location: ${event.location}",
                fontSize = 12.sp,
                color = Color.Gray
            )

            Text(
                text = "Responder: ${event.responderName}",
                fontSize = 12.sp,
                color = Color.Gray
            )
        }
    }
}
