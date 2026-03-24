package com.thewatch.app.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.Person
import androidx.compose.material.icons.filled.Phone
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.History
import androidx.compose.material.icons.filled.Logout
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.thewatch.app.ui.theme.GreenSafe
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.White

@Composable
fun NavigationDrawer(
    userName: String = "Alex Rivera",
    userStatus: String = "Safe",
    onHomeClick: () -> Unit = {},
    onProfileClick: () -> Unit = {},
    onContactsClick: () -> Unit = {},
    onHistoryClick: () -> Unit = {},
    onPermissionsClick: () -> Unit = {},
    onVolunteeringClick: () -> Unit = {},
    onEvacuationClick: () -> Unit = {},
    onSettingsClick: () -> Unit = {},
    onSignOutClick: () -> Unit = {}
) {
    Column(
        modifier = Modifier
            .fillMaxHeight()
            .width(280.dp)
            .background(Navy)
            .verticalScroll(rememberScrollState())
    ) {
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .background(color = Navy)
                .padding(16.dp)
        ) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                modifier = Modifier.fillMaxWidth()
            ) {
                Box(
                    modifier = Modifier
                        .size(48.dp)
                        .clip(CircleShape)
                        .background(color = GreenSafe),
                    contentAlignment = Alignment.Center
                ) {
                    Text(
                        text = userName.take(1),
                        color = White,
                        fontWeight = FontWeight.Bold,
                        fontSize = 20.sp
                    )
                }
                Column(
                    modifier = Modifier
                        .padding(start = 12.dp)
                        .weight(1f)
                ) {
                    Text(
                        text = userName,
                        color = White,
                        fontWeight = FontWeight.Bold,
                        fontSize = 16.sp
                    )
                    Box(
                        modifier = Modifier
                            .background(
                                color = GreenSafe,
                                shape = CircleShape
                            )
                            .padding(4.dp, 2.dp)
                    ) {
                        Text(
                            text = userStatus,
                            color = White,
                            fontSize = 10.sp,
                            fontWeight = FontWeight.SemiBold
                        )
                    }
                }
            }
        }

        Spacer(modifier = Modifier.height(16.dp))

        NavigationDrawerItem(
            icon = Icons.Filled.Home,
            label = "Home",
            onClick = onHomeClick
        )
        NavigationDrawerItem(
            icon = Icons.Filled.Person,
            label = "My Profile",
            onClick = onProfileClick
        )
        NavigationDrawerItem(
            icon = Icons.Filled.Phone,
            label = "My Contacts",
            onClick = onContactsClick
        )
        NavigationDrawerItem(
            icon = Icons.Filled.History,
            label = "History",
            onClick = onHistoryClick
        )
        NavigationDrawerItem(
            icon = Icons.Filled.Settings,
            label = "Permissions",
            onClick = onPermissionsClick
        )
        NavigationDrawerItem(
            icon = Icons.Filled.Person,
            label = "Volunteering",
            onClick = onVolunteeringClick
        )
        NavigationDrawerItem(
            icon = Icons.Filled.Home,
            label = "Evacuation Routes",
            onClick = onEvacuationClick
        )
        NavigationDrawerItem(
            icon = Icons.Filled.Settings,
            label = "Settings",
            onClick = onSettingsClick
        )

        Spacer(modifier = Modifier.weight(1f))

        NavigationDrawerItem(
            icon = Icons.Filled.Logout,
            label = "Sign Out",
            onClick = onSignOutClick,
            tint = Color.Red
        )
    }
}

@Composable
private fun NavigationDrawerItem(
    icon: ImageVector,
    label: String,
    onClick: () -> Unit,
    tint: Color = White
) {
    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = Modifier
            .fillMaxWidth()
            .clickable { onClick() }
            .padding(16.dp)
    ) {
        Icon(
            imageVector = icon,
            contentDescription = label,
            tint = tint,
            modifier = Modifier.size(24.dp)
        )
        Text(
            text = label,
            color = tint,
            fontSize = 14.sp,
            modifier = Modifier.padding(start = 16.dp)
        )
    }
}
