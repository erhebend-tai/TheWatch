package com.thewatch.app.ui.screens.settings

import androidx.compose.foundation.background
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
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
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
import androidx.navigation.NavController
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.RedPrimary
import com.thewatch.app.ui.theme.White

@Composable
fun SettingsScreen(navController: NavController) {
    var enableNotifications by remember { mutableStateOf(true) }
    var enableSoundAlerts by remember { mutableStateOf(true) }
    var enableVibration by remember { mutableStateOf(true) }
    var darkMode by remember { mutableStateOf(false) }
    var autoLocationSharing by remember { mutableStateOf(true) }
    var enableBiometric by remember { mutableStateOf(true) }

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
                text = "Settings",
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold,
                color = White,
                modifier = Modifier.weight(1f)
            )
        }

        Column(modifier = Modifier.padding(24.dp)) {
            // Notifications Section
            SettingsSection(title = "Notifications")

            SettingToggle(
                label = "Enable Notifications",
                description = "Receive push notifications",
                isEnabled = enableNotifications,
                onToggle = { enableNotifications = it }
            )

            SettingToggle(
                label = "Sound Alerts",
                description = "Play sound for emergency alerts",
                isEnabled = enableSoundAlerts,
                onToggle = { enableSoundAlerts = it }
            )

            SettingToggle(
                label = "Vibration Feedback",
                description = "Vibrate on emergency alerts",
                isEnabled = enableVibration,
                onToggle = { enableVibration = it }
            )

            Spacer(modifier = Modifier.height(24.dp))

            // Privacy Section
            SettingsSection(title = "Privacy & Security")

            SettingToggle(
                label = "Auto Location Sharing",
                description = "Automatically share location with responders",
                isEnabled = autoLocationSharing,
                onToggle = { autoLocationSharing = it }
            )

            SettingToggle(
                label = "Biometric Authentication",
                description = "Use fingerprint or face ID to unlock",
                isEnabled = enableBiometric,
                onToggle = { enableBiometric = it }
            )

            Spacer(modifier = Modifier.height(24.dp))

            // Display Section
            SettingsSection(title = "Display")

            SettingToggle(
                label = "Dark Mode",
                description = "Use dark theme",
                isEnabled = darkMode,
                onToggle = { darkMode = it }
            )

            Spacer(modifier = Modifier.height(24.dp))

            // About Section
            SettingsSection(title = "About")

            SettingInfo(label = "App Version", value = "1.0.0")
            SettingInfo(label = "Build Number", value = "2024031501")
            SettingInfo(label = "Last Updated", value = "March 15, 2026")

            Spacer(modifier = Modifier.height(24.dp))

            // GDPR & Data Rights
            SettingsSection(title = "GDPR & Data Rights")

            Button(
                onClick = { navController.navigate("data_export") },
                modifier = Modifier.fillMaxWidth(),
                colors = ButtonDefaults.buttonColors(containerColor = Navy)
            ) {
                Text("Export My Data (GDPR Art. 20)", color = White, modifier = Modifier.padding(8.dp))
            }

            Spacer(modifier = Modifier.height(8.dp))

            Button(
                onClick = { navController.navigate("eula_management") },
                modifier = Modifier.fillMaxWidth(),
                colors = ButtonDefaults.buttonColors(containerColor = Navy)
            ) {
                Text("EULA & Terms", color = White, modifier = Modifier.padding(8.dp))
            }

            Spacer(modifier = Modifier.height(8.dp))

            Button(
                onClick = { navController.navigate("log_viewer") },
                modifier = Modifier.fillMaxWidth(),
                colors = ButtonDefaults.buttonColors(containerColor = Navy)
            ) {
                Text("Diagnostics Log Viewer", color = White, modifier = Modifier.padding(8.dp))
            }

            Spacer(modifier = Modifier.height(24.dp))

            // Danger Zone
            Text(
                text = "Danger Zone",
                fontSize = 14.sp,
                fontWeight = FontWeight.Bold,
                color = RedPrimary
            )

            Spacer(modifier = Modifier.height(12.dp))

            Button(
                onClick = { navController.navigate("account_deletion") },
                modifier = Modifier.fillMaxWidth(),
                colors = ButtonDefaults.buttonColors(containerColor = RedPrimary)
            ) {
                Text("Delete Account (GDPR Art. 17)", color = White, modifier = Modifier.padding(8.dp))
            }

            Spacer(modifier = Modifier.height(12.dp))

            Button(
                onClick = { },
                modifier = Modifier.fillMaxWidth(),
                colors = ButtonDefaults.buttonColors(containerColor = RedPrimary)
            ) {
                Text("Clear App Data", color = White, modifier = Modifier.padding(8.dp))
            }

            Spacer(modifier = Modifier.height(12.dp))

            Button(
                onClick = { },
                modifier = Modifier.fillMaxWidth(),
                colors = ButtonDefaults.buttonColors(containerColor = RedPrimary)
            ) {
                Text("Sign Out", color = White, modifier = Modifier.padding(8.dp))
            }

            Spacer(modifier = Modifier.height(32.dp))
        }
    }
}

@Composable
private fun SettingsSection(title: String) {
    Text(
        text = title,
        fontSize = 16.sp,
        fontWeight = FontWeight.Bold,
        color = Navy
    )
    Spacer(modifier = Modifier.height(12.dp))
}

@Composable
private fun SettingToggle(
    label: String,
    description: String,
    isEnabled: Boolean,
    onToggle: (Boolean) -> Unit
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(
                color = Color(0xFFF5F5F5),
                shape = RoundedCornerShape(8.dp)
            )
            .padding(12.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = label,
                fontSize = 14.sp,
                fontWeight = FontWeight.Bold,
                color = Navy
            )
            Text(
                text = description,
                fontSize = 12.sp,
                color = Color.Gray
            )
        }
        Switch(
            checked = isEnabled,
            onCheckedChange = onToggle
        )
    }
    Spacer(modifier = Modifier.height(8.dp))
}

@Composable
private fun SettingInfo(label: String, value: String) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(
                color = Color(0xFFF5F5F5),
                shape = RoundedCornerShape(8.dp)
            )
            .padding(12.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = label,
                fontSize = 14.sp,
                fontWeight = FontWeight.Bold,
                color = Navy
            )
        }
        Text(
            text = value,
            fontSize = 12.sp,
            color = Color.Gray
        )
    }
    Spacer(modifier = Modifier.height(8.dp))
}
