package com.thewatch.app.ui.screens.contacts

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.navigation.NavController
import com.thewatch.app.ui.theme.GreenSafe
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.RedPrimary
import com.thewatch.app.ui.theme.White

data class EmergencyContact(
    val id: String,
    val name: String,
    val phone: String,
    val email: String,
    val relationship: String
)

@Composable
fun ContactsScreen(navController: NavController) {
    val contacts = listOf(
        EmergencyContact(
            id = "1",
            name = "Maria Rivera",
            phone = "+1 (555) 987-6543",
            email = "maria.rivera@example.com",
            relationship = "Sister"
        ),
        EmergencyContact(
            id = "2",
            name = "Dr. James Mitchell",
            phone = "+1 (555) 456-7890",
            email = "j.mitchell@clinic.com",
            relationship = "Primary Doctor"
        ),
        EmergencyContact(
            id = "3",
            name = "Sarah Chen",
            phone = "+1 (555) 234-5678",
            email = "s.chen@workplace.com",
            relationship = "Work Supervisor"
        ),
        EmergencyContact(
            id = "4",
            name = "Local Hospital ER",
            phone = "+1 (555) 111-2222",
            email = "emergency@hospital.com",
            relationship = "Emergency Services"
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
                text = "Emergency Contacts",
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold,
                color = White,
                modifier = Modifier.weight(1f)
            )
        }

        // Contacts List
        LazyColumn(modifier = Modifier.padding(16.dp)) {
            items(contacts) { contact ->
                ContactCard(
                    contact = contact,
                    onEdit = { },
                    onDelete = { }
                )
                Spacer(modifier = Modifier.height(12.dp))
            }
        }
    }
}

@Composable
private fun ContactCard(
    contact: EmergencyContact,
    onEdit: () -> Unit,
    onDelete: () -> Unit
) {
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
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Box(
                    modifier = Modifier
                        .size(40.dp)
                        .clip(CircleShape)
                        .background(GreenSafe),
                    contentAlignment = Alignment.Center
                ) {
                    Text(
                        text = contact.name.take(1),
                        color = White,
                        fontWeight = FontWeight.Bold,
                        fontSize = 16.sp
                    )
                }

                Column(
                    modifier = Modifier
                        .weight(1f)
                        .padding(start = 12.dp)
                ) {
                    Text(
                        text = contact.name,
                        fontSize = 14.sp,
                        fontWeight = FontWeight.Bold,
                        color = Navy
                    )
                    Text(
                        text = contact.relationship,
                        fontSize = 12.sp,
                        color = Color.Gray
                    )
                }

                IconButton(onClick = onEdit, modifier = Modifier.size(32.dp)) {
                    Icon(
                        imageVector = Icons.Filled.Edit,
                        contentDescription = "Edit",
                        tint = Navy,
                        modifier = Modifier.size(16.dp)
                    )
                }

                IconButton(onClick = onDelete, modifier = Modifier.size(32.dp)) {
                    Icon(
                        imageVector = Icons.Filled.Delete,
                        contentDescription = "Delete",
                        tint = RedPrimary,
                        modifier = Modifier.size(16.dp)
                    )
                }
            }

            Spacer(modifier = Modifier.height(8.dp))

            Text(
                text = contact.phone,
                fontSize = 12.sp,
                color = Color.Gray
            )

            Text(
                text = contact.email,
                fontSize = 12.sp,
                color = Color.Gray
            )
        }
    }
}
