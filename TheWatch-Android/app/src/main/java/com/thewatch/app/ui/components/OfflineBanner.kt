/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         OfflineBanner.kt                                       │
 * │ Purpose:      Yellow offline-status banner with wifi-off icon and    │
 * │               smooth slide-in/out animation. Observes device         │
 * │               connectivity via ConnectivityManager callback.         │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: Jetpack Compose Animation, Material 3, Material Icons, │
 * │               ConnectivityManager (android.net)                      │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val connectivity = rememberConnectivityState()                     │
 * │   val isOnline by connectivity                                       │
 * │   OfflineBanner(isOnline = isOnline)                                 │
 * │                                                                      │
 * │ NOTE: On Samsung OneUI / Xiaomi MIUI, ConnectivityManager callbacks  │
 * │ may be killed aggressively. The banner re-checks on recomposition.  │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.ui.components

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.expandVertically
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.shrinkVertically
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.WifiOff
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.State
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.thewatch.app.ui.theme.YellowWarning

/**
 * Observes real-time network connectivity via ConnectivityManager.
 * Returns a State<Boolean> that is true when device has validated internet.
 */
@Composable
fun rememberConnectivityState(): State<Boolean> {
    val context = LocalContext.current
    val connectivityManager = remember {
        context.getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager
    }
    val isOnline = remember { mutableStateOf(checkCurrentConnectivity(connectivityManager)) }

    DisposableEffect(connectivityManager) {
        val callback = object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) {
                isOnline.value = true
            }
            override fun onLost(network: Network) {
                isOnline.value = false
            }
            override fun onCapabilitiesChanged(network: Network, caps: NetworkCapabilities) {
                isOnline.value = caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET) &&
                        caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED)
            }
        }
        val request = NetworkRequest.Builder()
            .addCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
            .build()
        connectivityManager.registerNetworkCallback(request, callback)
        onDispose { connectivityManager.unregisterNetworkCallback(callback) }
    }
    return isOnline
}

private fun checkCurrentConnectivity(cm: ConnectivityManager): Boolean {
    val network = cm.activeNetwork ?: return false
    val caps = cm.getNetworkCapabilities(network) ?: return false
    return caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET) &&
            caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED)
}

/**
 * Yellow offline banner with wifi-off icon. Slides in/out with animation.
 *
 * @param isOnline Current connectivity state. Pass true = banner hidden.
 * @param modifier Modifier for layout.
 */
@Composable
fun OfflineBanner(
    isOnline: Boolean = true,
    modifier: Modifier = Modifier
) {
    AnimatedVisibility(
        visible = !isOnline,
        enter = slideInVertically(initialOffsetY = { -it }) +
                expandVertically(expandFrom = Alignment.Top) +
                fadeIn(),
        exit = slideOutVertically(targetOffsetY = { -it }) +
                shrinkVertically(shrinkTowards = Alignment.Top) +
                fadeOut()
    ) {
        Row(
            modifier = modifier
                .fillMaxWidth()
                .background(YellowWarning)
                .padding(horizontal = 16.dp, vertical = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.Center
        ) {
            Icon(
                imageVector = Icons.Default.WifiOff,
                contentDescription = "No connection",
                tint = Color.Black,
                modifier = Modifier.size(18.dp)
            )
            Spacer(modifier = Modifier.width(8.dp))
            Text(
                text = "No connection \u2014 data will sync when online",
                fontSize = 13.sp,
                fontWeight = FontWeight.Medium,
                color = Color.Black
            )
        }
    }
}
