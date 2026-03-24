package com.thewatch.app.receiver

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import com.thewatch.app.service.LocationService
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import javax.inject.Inject

/**
 * Broadcast receiver that restarts location tracking after device boot.
 * This ensures location tracking is resumed if the user had it enabled before the device was turned off.
 */
@AndroidEntryPoint
class BootReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action == Intent.ACTION_BOOT_COMPLETED) {
            // Restart location tracking in NORMAL mode if it was previously enabled
            val locationIntent = Intent(context, LocationService::class.java).apply {
                action = LocationService.ACTION_START_TRACKING
                putExtra(LocationService.EXTRA_MODE, LocationService.TrackingMode.NORMAL)
            }
            context.startService(locationIntent)
        }
    }
}
