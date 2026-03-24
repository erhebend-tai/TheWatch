package com.thewatch.app

import android.app.Application
import android.provider.Settings
import com.thewatch.app.data.logging.LogSyncWorker
import com.thewatch.app.data.logging.WatchLogger
import dagger.hilt.android.HiltAndroidApp
import javax.inject.Inject

@HiltAndroidApp
class TheWatchApplication : Application() {

    @Inject lateinit var logger: WatchLogger

    override fun onCreate() {
        super.onCreate()

        // Set stable device ID for log correlation
        logger.deviceId = Settings.Secure.getString(contentResolver, Settings.Secure.ANDROID_ID)

        // Enqueue periodic log sync (15min interval via WorkManager)
        LogSyncWorker.enqueue(this)

        logger.information(
            sourceContext = "TheWatchApplication",
            messageTemplate = "Application started on device {DeviceId}",
            properties = mapOf("DeviceId" to (logger.deviceId ?: "unknown"))
        )
    }

    override fun onTrimMemory(level: Int) {
        super.onTrimMemory(level)
        if (level >= TRIM_MEMORY_BACKGROUND) {
            logger.flush()
        }
    }
}
