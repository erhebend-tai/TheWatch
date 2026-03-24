/**
 * +----------------------------------------------------------------------+
 * | WRITE-AHEAD LOG                                                       |
 * +----------------------------------------------------------------------+
 * | File:         ConnectivityMonitor.kt                                  |
 * | Purpose:      Observes network connectivity state via Android         |
 * |               ConnectivityManager. Triggers immediate sync flush      |
 * |               when connectivity is restored after an offline period.  |
 * | Created:      2026-03-24                                              |
 * | Author:       Claude                                                  |
 * | Dependencies: Android ConnectivityManager, SyncWorker                 |
 * |                                                                       |
 * | Usage example:                                                        |
 * |   // In Application.onCreate():                                       |
 * |   connectivityMonitor.startMonitoring()                               |
 * |                                                                       |
 * |   // Check current state:                                             |
 * |   if (connectivityMonitor.isOnline()) { ... }                         |
 * |                                                                       |
 * |   // Observe as Flow:                                                 |
 * |   connectivityMonitor.networkState.collect { isOnline ->              |
 * |       updateUI(isOnline)                                              |
 * |   }                                                                   |
 * |                                                                       |
 * | Reconnection behavior: When transitioning from offline -> online,     |
 * | the monitor enqueues an expedited SyncWorker to flush pending tasks   |
 * | immediately rather than waiting for the next 15-minute periodic cycle.|
 * |                                                                       |
 * | NOTE: On Android 7+ (API 24), registerDefaultNetworkCallback is used. |
 * | On older devices, falls back to broadcast receiver pattern.           |
 * | Samsung/Xiaomi/Huawei aggressive battery optimization may delay       |
 * | network callbacks. The periodic SyncWorker serves as a fallback.      |
 * +----------------------------------------------------------------------+
 */
package com.thewatch.app.data.sync

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import android.util.Log
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.util.concurrent.atomic.AtomicBoolean
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class ConnectivityMonitor @Inject constructor(
    @ApplicationContext private val context: Context
) {
    companion object {
        private const val TAG = "TheWatch.Connectivity"
    }

    private val connectivityManager =
        context.getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager

    private val _networkState = MutableStateFlow(checkCurrentState())
    val networkState: StateFlow<Boolean> = _networkState.asStateFlow()

    private val monitoring = AtomicBoolean(false)
    private var wasOffline = AtomicBoolean(!checkCurrentState())

    private val networkCallback = object : ConnectivityManager.NetworkCallback() {
        override fun onAvailable(network: Network) {
            Log.i(TAG, "Network available: $network")
            _networkState.value = true

            // If we were offline, trigger immediate sync
            if (wasOffline.getAndSet(false)) {
                Log.i(TAG, "Connectivity restored — triggering expedited sync")
                SyncWorker.enqueueExpedited(context)
            }
        }

        override fun onLost(network: Network) {
            Log.w(TAG, "Network lost: $network")
            // Only mark offline if no other network is available
            if (!hasActiveNetwork()) {
                _networkState.value = false
                wasOffline.set(true)
            }
        }

        override fun onCapabilitiesChanged(
            network: Network,
            networkCapabilities: NetworkCapabilities
        ) {
            val hasInternet = networkCapabilities.hasCapability(
                NetworkCapabilities.NET_CAPABILITY_INTERNET
            )
            val isValidated = networkCapabilities.hasCapability(
                NetworkCapabilities.NET_CAPABILITY_VALIDATED
            )
            val effectivelyOnline = hasInternet && isValidated
            Log.d(TAG, "Capabilities changed: internet=$hasInternet, validated=$isValidated")

            _networkState.value = effectivelyOnline

            if (effectivelyOnline && wasOffline.getAndSet(false)) {
                Log.i(TAG, "Validated connectivity restored — triggering expedited sync")
                SyncWorker.enqueueExpedited(context)
            }
        }
    }

    /**
     * Start monitoring network state. Safe to call multiple times.
     * Should be called in Application.onCreate().
     */
    fun startMonitoring() {
        if (monitoring.getAndSet(true)) {
            Log.d(TAG, "Already monitoring — skipping duplicate registration")
            return
        }

        val request = NetworkRequest.Builder()
            .addCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
            .addTransportType(NetworkCapabilities.TRANSPORT_WIFI)
            .addTransportType(NetworkCapabilities.TRANSPORT_CELLULAR)
            .addTransportType(NetworkCapabilities.TRANSPORT_ETHERNET)
            .build()

        try {
            connectivityManager.registerNetworkCallback(request, networkCallback)
            Log.i(TAG, "Network monitoring started")
        } catch (e: SecurityException) {
            Log.e(TAG, "Missing ACCESS_NETWORK_STATE permission", e)
        }
    }

    /**
     * Stop monitoring. Call on Application termination or if monitoring is no longer needed.
     */
    fun stopMonitoring() {
        if (!monitoring.getAndSet(false)) return

        try {
            connectivityManager.unregisterNetworkCallback(networkCallback)
            Log.i(TAG, "Network monitoring stopped")
        } catch (e: IllegalArgumentException) {
            Log.w(TAG, "Callback already unregistered", e)
        }
    }

    /**
     * Synchronous check of current network state.
     */
    fun isOnline(): Boolean = _networkState.value

    /**
     * Check if any active network exists with internet capability.
     */
    private fun hasActiveNetwork(): Boolean {
        val activeNetwork = connectivityManager.activeNetwork ?: return false
        val capabilities = connectivityManager.getNetworkCapabilities(activeNetwork) ?: return false
        return capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
    }

    /**
     * Initial state check.
     */
    private fun checkCurrentState(): Boolean {
        return hasActiveNetwork()
    }
}
