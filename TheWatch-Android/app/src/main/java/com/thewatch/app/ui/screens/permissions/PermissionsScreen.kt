package com.thewatch.app.ui.screens.permissions

import android.app.Activity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.navigation.NavController
import com.thewatch.app.ui.screens.permissions.PermissionsViewModel
import com.thewatch.app.ui.theme.GreenSafe
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.RedPrimary
import com.thewatch.app.ui.theme.White

data class PermissionItem(
    val name: String,
    val description: String,
    val isGranted: Boolean,
    val isPermanentlyDenied: Boolean = false
)

@Composable
fun PermissionsScreen(
    navController: NavController,
    viewModel: PermissionsViewModel = hiltViewModel()
) {
    val context = LocalContext.current as? Activity
    val fineLocationGranted by viewModel.fineLocationGranted.collectAsState()
    val backgroundLocationGranted by viewModel.backgroundLocationGranted.collectAsState()
    val notificationsGranted by viewModel.notificationGranted.collectAsState()
    val cameraGranted by viewModel.cameraGranted.collectAsState()
    val microphoneGranted by viewModel.microphoneGranted.collectAsState()
    val bluetoothGranted by viewModel.bluetoothGranted.collectAsState()
    val bodySensorsGranted by viewModel.bodySensorsGranted.collectAsState()
    val contactsGranted by viewModel.contactsGranted.collectAsState()

    var showRationale by remember { mutableStateOf<String?>(null) }
    var permissionsPermanentlyDenied by remember { mutableStateOf(false) }

    // Launcher for requesting permissions
    val multiplePermissionsLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { permissions ->
        val denied = permissions.filterValues { !it }.keys
        if (denied.isNotEmpty()) {
            // Check if any are permanently denied
            val anyPermanentlyDenied = denied.any { permission ->
                context?.let { viewModel.isPermissionPermanentlyDenied(it, permission) } ?: false
            }
            if (anyPermanentlyDenied) {
                permissionsPermanentlyDenied = true
            }
        }
        viewModel.refreshPermissionStates()
    }

    // Check permission state on composition
    LaunchedEffect(Unit) {
        viewModel.refreshPermissionStates()
    }

    val permissions = listOf(
        PermissionItem(
            "Location",
            "Precise location for emergency response (always needed)",
            fineLocationGranted,
            context?.let { viewModel.isPermissionPermanentlyDenied(it, android.Manifest.permission.ACCESS_FINE_LOCATION) } ?: false
        ),
        PermissionItem(
            "Background Location",
            "Location tracking even when app is closed (Android 10+)",
            backgroundLocationGranted,
            context?.let { viewModel.isPermissionPermanentlyDenied(it, android.Manifest.permission.ACCESS_BACKGROUND_LOCATION) } ?: false
        ),
        PermissionItem(
            "Notifications",
            "Receive emergency alerts and updates (critical)",
            notificationsGranted,
            context?.let { viewModel.isPermissionPermanentlyDenied(it, android.Manifest.permission.POST_NOTIFICATIONS) } ?: false
        ),
        PermissionItem(
            "Microphone",
            "Voice-based SOS activation",
            microphoneGranted,
            context?.let { viewModel.isPermissionPermanentlyDenied(it, android.Manifest.permission.RECORD_AUDIO) } ?: false
        ),
        PermissionItem(
            "Camera",
            "Optional for emergency documentation",
            cameraGranted,
            context?.let { viewModel.isPermissionPermanentlyDenied(it, android.Manifest.permission.CAMERA) } ?: false
        ),
        PermissionItem(
            "Health Data",
            "Monitor vital signs from wearables",
            bodySensorsGranted,
            context?.let { viewModel.isPermissionPermanentlyDenied(it, android.Manifest.permission.BODY_SENSORS) } ?: false
        ),
        PermissionItem(
            "Bluetooth",
            "Connect to wearable devices",
            bluetoothGranted,
            context?.let { viewModel.isPermissionPermanentlyDenied(it, android.Manifest.permission.BLUETOOTH_SCAN) } ?: false
        ),
        PermissionItem(
            "Contacts",
            "Quick access to emergency contacts",
            contactsGranted,
            context?.let { viewModel.isPermissionPermanentlyDenied(it, android.Manifest.permission.READ_CONTACTS) } ?: false
        )
    )

    if (permissionsPermanentlyDenied) {
        AlertDialog(
            onDismissRequest = { permissionsPermanentlyDenied = false },
            title = { Text("Permission Required") },
            text = { Text("Some permissions were permanently denied. Please enable them in app settings.") },
            confirmButton = {
                Button(
                    onClick = {
                        val intent = viewModel.getAppSettingsIntent()
                        context?.startActivity(intent)
                        permissionsPermanentlyDenied = false
                    }
                ) {
                    Text("Open Settings")
                }
            }
        )
    }

    if (showRationale != null) {
        AlertDialog(
            onDismissRequest = { showRationale = null },
            title = { Text("${showRationale ?: "Permission"} Needed") },
            text = { Text("This permission is important for TheWatch to function properly.") },
            confirmButton = {
                Button(onClick = { showRationale = null }) {
                    Text("OK")
                }
            }
        )
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(White)
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
                text = "Permissions",
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold,
                color = White,
                modifier = Modifier.weight(1f)
            )
        }

        Column(modifier = Modifier.padding(16.dp)) {
            Spacer(modifier = Modifier.height(12.dp))

            permissions.forEachIndexed { index, permission ->
                PermissionRow(
                    name = permission.name,
                    description = permission.description,
                    isGranted = permission.isGranted,
                    isPermanentlyDenied = permission.isPermanentlyDenied,
                    onGrantClick = {
                        val permissionsToRequest = when (index) {
                            0 -> arrayOf(android.Manifest.permission.ACCESS_FINE_LOCATION, android.Manifest.permission.ACCESS_COARSE_LOCATION)
                            1 -> arrayOf(android.Manifest.permission.ACCESS_BACKGROUND_LOCATION)
                            2 -> arrayOf(android.Manifest.permission.POST_NOTIFICATIONS)
                            3 -> arrayOf(android.Manifest.permission.RECORD_AUDIO)
                            4 -> arrayOf(android.Manifest.permission.CAMERA)
                            5 -> arrayOf(android.Manifest.permission.BODY_SENSORS)
                            6 -> if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.S) {
                                arrayOf(android.Manifest.permission.BLUETOOTH_SCAN, android.Manifest.permission.BLUETOOTH_CONNECT)
                            } else {
                                arrayOf(android.Manifest.permission.BLUETOOTH)
                            }
                            7 -> arrayOf(android.Manifest.permission.READ_CONTACTS)
                            else -> emptyArray()
                        }
                        multiplePermissionsLauncher.launch(permissionsToRequest)
                    },
                    onSettingsClick = {
                        val intent = viewModel.getAppSettingsIntent()
                        context?.startActivity(intent)
                    }
                )
                Spacer(modifier = Modifier.height(16.dp))
            }
        }
    }
}

@Composable
private fun PermissionRow(
    name: String,
    description: String,
    isGranted: Boolean,
    isPermanentlyDenied: Boolean = false,
    onGrantClick: () -> Unit,
    onSettingsClick: () -> Unit = {}
) {
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .background(
                color = when {
                    isGranted -> Color(0xFFE8F5E9)
                    isPermanentlyDenied -> Color(0xFFFFCDD2)
                    else -> Color(0xFFFFF3E0)
                },
                shape = RoundedCornerShape(8.dp)
            )
            .padding(12.dp)
    ) {
        Column(modifier = Modifier.fillMaxWidth()) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = name,
                        fontSize = 14.sp,
                        fontWeight = FontWeight.Bold,
                        color = Navy
                    )
                    Spacer(modifier = Modifier.height(4.dp))
                    Text(
                        text = description,
                        fontSize = 12.sp,
                        color = Color.Gray
                    )
                }
            }

            Spacer(modifier = Modifier.height(12.dp))

            if (isGranted) {
                Box(
                    modifier = Modifier
                        .background(GreenSafe, shape = RoundedCornerShape(4.dp))
                        .padding(8.dp, 4.dp),
                    contentAlignment = Alignment.Center
                ) {
                    Text(
                        text = "Granted",
                        fontSize = 12.sp,
                        color = White,
                        fontWeight = FontWeight.Bold
                    )
                }
            } else if (isPermanentlyDenied) {
                Button(
                    onClick = onSettingsClick,
                    colors = ButtonDefaults.buttonColors(containerColor = Color(0xFFC62828)),
                    modifier = Modifier.fillMaxWidth()
                ) {
                    Text("Open Settings", fontSize = 12.sp)
                }
            } else {
                Button(
                    onClick = onGrantClick,
                    colors = ButtonDefaults.buttonColors(containerColor = RedPrimary),
                    modifier = Modifier.fillMaxWidth()
                ) {
                    Text("Grant Permission", fontSize = 12.sp)
                }
            }
        }
    }
}
