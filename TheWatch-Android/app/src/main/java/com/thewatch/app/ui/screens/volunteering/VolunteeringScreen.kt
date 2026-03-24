package com.thewatch.app.ui.screens.volunteering

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
import com.thewatch.app.ui.theme.GreenSafe
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.White

@Composable
fun VolunteeringScreen(
    navController: NavController,
    viewModel: VolunteeringViewModel = hiltViewModel()
) {
    val isEnrolled by viewModel.isEnrolled.collectAsState()
    val selectedRole by viewModel.selectedRole.collectAsState()
    val responseHistory by viewModel.responseHistory.collectAsState()

    var selectedSkills by remember { mutableStateOf(setOf("CPR", "First Aid")) }
    val availableSkills = listOf("CPR", "First Aid", "AED", "Basic Life Support", "Trauma Response", "Crisis Counseling")

    var mondayAvailable by remember { mutableStateOf(true) }
    var tuesdayAvailable by remember { mutableStateOf(true) }
    var wednesdayAvailable by remember { mutableStateOf(false) }
    var thursdayAvailable by remember { mutableStateOf(true) }
    var fridayAvailable by remember { mutableStateOf(true) }
    var saturdayAvailable by remember { mutableStateOf(false) }
    var sundayAvailable by remember { mutableStateOf(false) }

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
                text = "Volunteering",
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold,
                color = White,
                modifier = Modifier.weight(1f)
            )
        }

        Column(modifier = Modifier.padding(24.dp)) {
            // Enrollment Toggle
            Text(
                text = "Volunteer Status",
                fontSize = 16.sp,
                fontWeight = FontWeight.Bold,
                color = Navy
            )

            Spacer(modifier = Modifier.height(12.dp))

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
                        text = "Enrolled as Volunteer",
                        fontSize = 14.sp,
                        fontWeight = FontWeight.Bold
                    )
                    Text(
                        text = if (isEnrolled) "You are enrolled and available" else "Not currently enrolled",
                        fontSize = 12.sp,
                        color = Color.Gray
                    )
                }
                Switch(
                    checked = isEnrolled,
                    onCheckedChange = { viewModel.updateEnrollmentStatus(it) }
                )
            }

            if (isEnrolled) {
                Spacer(modifier = Modifier.height(24.dp))

                // Role Selection
                Text(
                    text = "Primary Role",
                    fontSize = 16.sp,
                    fontWeight = FontWeight.Bold,
                    color = Navy
                )

                Spacer(modifier = Modifier.height(12.dp))

                listOf("First Responder", "Counselor", "Logistics", "Communications").forEach { role ->
                    RoleButton(
                        label = role,
                        isSelected = selectedRole == role,
                        onClick = { viewModel.updateRole(role) }
                    )
                    Spacer(modifier = Modifier.height(8.dp))
                }

                Spacer(modifier = Modifier.height(24.dp))

                // Skills
                Text(
                    text = "Skills & Certifications",
                    fontSize = 16.sp,
                    fontWeight = FontWeight.Bold,
                    color = Navy
                )

                Spacer(modifier = Modifier.height(12.dp))

                Row(modifier = Modifier.fillMaxWidth()) {
                    availableSkills.take(3).forEach { skill ->
                        SkillChip(
                            label = skill,
                            isSelected = skill in selectedSkills,
                            onClick = {
                                selectedSkills = if (skill in selectedSkills) {
                                    selectedSkills - skill
                                } else {
                                    selectedSkills + skill
                                }
                                viewModel.updateSkills(selectedSkills.toList())
                            }
                        )
                        Spacer(modifier = Modifier.width(8.dp))
                    }
                }

                Spacer(modifier = Modifier.height(8.dp))

                Row(modifier = Modifier.fillMaxWidth()) {
                    availableSkills.drop(3).forEach { skill ->
                        SkillChip(
                            label = skill,
                            isSelected = skill in selectedSkills,
                            onClick = {
                                selectedSkills = if (skill in selectedSkills) {
                                    selectedSkills - skill
                                } else {
                                    selectedSkills + skill
                                }
                                viewModel.updateSkills(selectedSkills.toList())
                            }
                        )
                        Spacer(modifier = Modifier.width(8.dp))
                    }
                }

                Spacer(modifier = Modifier.height(24.dp))

                // Weekly Schedule
                Text(
                    text = "Weekly Availability",
                    fontSize = 16.sp,
                    fontWeight = FontWeight.Bold,
                    color = Navy
                )

                Spacer(modifier = Modifier.height(12.dp))

                ScheduleDay("Monday", mondayAvailable) { mondayAvailable = it }
                ScheduleDay("Tuesday", tuesdayAvailable) { tuesdayAvailable = it }
                ScheduleDay("Wednesday", wednesdayAvailable) { wednesdayAvailable = it }
                ScheduleDay("Thursday", thursdayAvailable) { thursdayAvailable = it }
                ScheduleDay("Friday", fridayAvailable) { fridayAvailable = it }
                ScheduleDay("Saturday", saturdayAvailable) { saturdayAvailable = it }
                ScheduleDay("Sunday", sundayAvailable) { sundayAvailable = it }

                Spacer(modifier = Modifier.height(24.dp))

                // Response History
                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .background(
                            color = GreenSafe.copy(alpha = 0.1f),
                            shape = RoundedCornerShape(8.dp)
                        )
                        .padding(16.dp)
                ) {
                    Column {
                        Text(
                            text = "Response History",
                            fontSize = 14.sp,
                            fontWeight = FontWeight.Bold,
                            color = Navy
                        )
                        Spacer(modifier = Modifier.height(8.dp))
                        Text(
                            text = "$responseHistory responses this month",
                            fontSize = 12.sp,
                            color = Color.Gray
                        )
                    }
                }
            }

            Spacer(modifier = Modifier.height(32.dp))
        }
    }
}

@Composable
private fun RoleButton(
    label: String,
    isSelected: Boolean,
    onClick: () -> Unit
) {
    Button(
        onClick = onClick,
        modifier = Modifier.fillMaxWidth(),
        colors = ButtonDefaults.buttonColors(
            containerColor = if (isSelected) Navy else Color(0xFFE0E0E0)
        ),
        shape = RoundedCornerShape(8.dp)
    ) {
        Text(
            label,
            color = if (isSelected) White else Navy,
            modifier = Modifier.padding(8.dp)
        )
    }
}

@Composable
private fun SkillChip(
    label: String,
    isSelected: Boolean,
    onClick: () -> Unit
) {
    Button(
        onClick = onClick,
        modifier = Modifier.height(36.dp),
        colors = ButtonDefaults.buttonColors(
            containerColor = if (isSelected) GreenSafe else Color(0xFFE0E0E0)
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
private fun ScheduleDay(
    day: String,
    isAvailable: Boolean,
    onAvailabilityChange: (Boolean) -> Unit
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
        Text(
            text = day,
            fontSize = 14.sp,
            modifier = Modifier.weight(1f)
        )
        Switch(checked = isAvailable, onCheckedChange = onAvailabilityChange)
    }

    Spacer(modifier = Modifier.height(8.dp))
}
