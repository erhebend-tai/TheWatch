/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         WearableManagementScreen.kt                            │
 * │ Purpose:      UI screen for managing paired wearable devices. Shows  │
 * │               paired devices with connection status and battery,     │
 * │               scan for new devices, pair/unpair, sync health data.   │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: Jetpack Compose Material 3, WearablePort, Hilt        │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   composable("wearable_management") {                                │
 * │       WearableManagementScreen(navController = navController)        │
 * │   }                                                                  │
 * │                                                                      │
 * │ NOTE: BLE scanning requires BLUETOOTH_SCAN permission on API 31+.   │
 * │ The scan results list auto-updates as new devices are discovered.   │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.ui.screens.health

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
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.BatteryFull
import androidx.compose.material.icons.filled.Bluetooth
import androidx.compose.material.icons.filled.BluetoothConnected
import androidx.compose.material.icons.filled.BluetoothDisabled
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Watch
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Divider
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.navigation.NavController
import com.thewatch.app.data.model.WearableDevice
import com.thewatch.app.data.wearables.WearableConnectionState
import com.thewatch.app.data.wearables.WearablePort
import com.thewatch.app.ui.theme.GreenSafe
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.RedPrimary
import com.thewatch.app.ui.theme.White
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.launch
import javax.inject.Inject
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope

@HiltViewModel
class WearableManagementViewModel @Inject constructor(
    private val wearablePort: WearablePort
) : ViewModel() {

    var pairedDevices by mutableStateOf<List<WearableDevice>>(emptyList())
        private set
    var isScanning by mutableStateOf(false)
        private set
    var discoveredDevices = mutableStateListOf<WearableDevice>()
        private set
    var isLoading by mutableStateOf(true)
        private set

    init {
        loadPairedDevices()
    }

    fun loadPairedDevices() {
        viewModelScope.launch {
            isLoading = true
            pairedDevices = wearablePort.getPairedDevices()
            isLoading = false
        }
    }

    fun startScan() {
        viewModelScope.launch {
            isScanning = true
            discoveredDevices.clear()
            wearablePort.scanForDevices().collect { device ->
                if (discoveredDevices.none { it.id == device.id }) {
                    discoveredDevices.add(device)
                }
            }
            isScanning = false
        }
    }

    fun pairDevice(deviceId: String) {
        viewModelScope.launch {
            wearablePort.pairDevice(deviceId)
            discoveredDevices.removeAll { it.id == deviceId }
            loadPairedDevices()
        }
    }

    fun unpairDevice(deviceId: String) {
        viewModelScope.launch {
            wearablePort.unpairDevice(deviceId)
            loadPairedDevices()
        }
    }

    fun syncDevice(deviceId: String) {
        viewModelScope.launch {
            wearablePort.syncHealthData(deviceId)
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun WearableManagementScreen(
    navController: NavController,
    viewModel: WearableManagementViewModel = hiltViewModel()
) {
    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Wearable Devices", color = White) },
                navigationIcon = {
                    IconButton(onClick = { navController.popBackStack() }) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, "Back", tint = White)
                    }
                },
                actions = {
                    IconButton(onClick = { viewModel.startScan() }) {
                        Icon(Icons.Default.Search, "Scan", tint = White)
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = Navy)
            )
        }
    ) { paddingValues ->
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .padding(paddingValues)
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            // Paired Devices Section
            item {
                Text("Paired Devices", fontWeight = FontWeight.Bold, fontSize = 18.sp)
                Spacer(Modifier.height(8.dp))
            }

            if (viewModel.isLoading) {
                item {
                    Box(Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
                        CircularProgressIndicator()
                    }
                }
            } else if (viewModel.pairedDevices.isEmpty()) {
                item {
                    Text("No paired devices. Tap the search icon to scan.", color = Color.Gray, fontSize = 14.sp)
                }
            } else {
                items(viewModel.pairedDevices) { device ->
                    PairedDeviceCard(
                        device = device,
                        onSync = { viewModel.syncDevice(device.id) },
                        onUnpair = { viewModel.unpairDevice(device.id) }
                    )
                }
            }

            // Scanning Section
            if (viewModel.isScanning || viewModel.discoveredDevices.isNotEmpty()) {
                item {
                    Spacer(Modifier.height(16.dp))
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("Available Devices", fontWeight = FontWeight.Bold, fontSize = 18.sp)
                        if (viewModel.isScanning) {
                            Spacer(Modifier.width(8.dp))
                            CircularProgressIndicator(modifier = Modifier.size(16.dp), strokeWidth = 2.dp)
                        }
                    }
                    Spacer(Modifier.height(8.dp))
                }

                items(viewModel.discoveredDevices) { device ->
                    DiscoveredDeviceCard(
                        device = device,
                        onPair = { viewModel.pairDevice(device.id) }
                    )
                }
            }
        }
    }
}

@Composable
private fun PairedDeviceCard(
    device: WearableDevice,
    onSync: () -> Unit,
    onUnpair: () -> Unit
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(12.dp),
        elevation = CardDefaults.cardElevation(defaultElevation = 2.dp)
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Icon(
                if (device.isActive) Icons.Default.BluetoothConnected else Icons.Default.BluetoothDisabled,
                "Connection",
                tint = if (device.isActive) GreenSafe else Color.Gray,
                modifier = Modifier.size(32.dp)
            )
            Spacer(Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(device.name, fontWeight = FontWeight.Bold, fontSize = 16.sp)
                Text(device.manufacturer, fontSize = 12.sp, color = Color.Gray)
                Text(
                    if (device.isActive) "Connected" else "Disconnected",
                    fontSize = 12.sp,
                    color = if (device.isActive) GreenSafe else Color.Gray
                )
            }
            IconButton(onClick = onSync) {
                Icon(Icons.Default.Refresh, "Sync", tint = Navy)
            }
            IconButton(onClick = onUnpair) {
                Icon(Icons.Default.Delete, "Unpair", tint = RedPrimary)
            }
        }
    }
}

@Composable
private fun DiscoveredDeviceCard(
    device: WearableDevice,
    onPair: () -> Unit
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(12.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant)
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Icon(Icons.Default.Bluetooth, "Device", tint = Navy, modifier = Modifier.size(28.dp))
            Spacer(Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(device.name, fontWeight = FontWeight.Medium, fontSize = 15.sp)
                Text(device.manufacturer, fontSize = 12.sp, color = Color.Gray)
            }
            Button(
                onClick = onPair,
                colors = ButtonDefaults.buttonColors(containerColor = Navy)
            ) {
                Text("Pair", color = White)
            }
        }
    }
}
