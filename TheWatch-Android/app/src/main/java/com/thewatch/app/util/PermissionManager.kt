package com.thewatch.app.util

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.core.content.ContextCompat
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Centralized permission manager for TheWatch.
 * Handles location permission flow with special case for Android 10+ background location.
 */
@Singleton
class PermissionManager @Inject constructor(
    @ApplicationContext private val context: Context
) {

    private val _fineLocationGranted = MutableStateFlow(checkFineLocationPermission())
    val fineLocationGranted: StateFlow<Boolean> = _fineLocationGranted.asStateFlow()

    private val _backgroundLocationGranted = MutableStateFlow(checkBackgroundLocationPermission())
    val backgroundLocationGranted: StateFlow<Boolean> = _backgroundLocationGranted.asStateFlow()

    private val _notificationGranted = MutableStateFlow(checkNotificationPermission())
    val notificationGranted: StateFlow<Boolean> = _notificationGranted.asStateFlow()

    private val _cameraGranted = MutableStateFlow(checkPermission(Manifest.permission.CAMERA))
    val cameraGranted: StateFlow<Boolean> = _cameraGranted.asStateFlow()

    private val _microphoneGranted = MutableStateFlow(checkPermission(Manifest.permission.RECORD_AUDIO))
    val microphoneGranted: StateFlow<Boolean> = _microphoneGranted.asStateFlow()

    private val _bluetoothGranted = MutableStateFlow(checkBluetoothPermissions())
    val bluetoothGranted: StateFlow<Boolean> = _bluetoothGranted.asStateFlow()

    private val _bodySensorsGranted = MutableStateFlow(checkPermission(Manifest.permission.BODY_SENSORS))
    val bodySensorsGranted: StateFlow<Boolean> = _bodySensorsGranted.asStateFlow()

    private val _contactsGranted = MutableStateFlow(checkPermission(Manifest.permission.READ_CONTACTS))
    val contactsGranted: StateFlow<Boolean> = _contactsGranted.asStateFlow()

    /**
     * Check if fine location permission is granted.
     */
    fun checkFineLocationPermission(): Boolean =
        ContextCompat.checkSelfPermission(
            context,
            Manifest.permission.ACCESS_FINE_LOCATION
        ) == PackageManager.PERMISSION_GRANTED

    /**
     * Check if background location permission is granted.
     * Only applies to Android 10+.
     */
    fun checkBackgroundLocationPermission(): Boolean {
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            ContextCompat.checkSelfPermission(
                context,
                Manifest.permission.ACCESS_BACKGROUND_LOCATION
            ) == PackageManager.PERMISSION_GRANTED
        } else {
            true // Pre-Android 10, background location is granted with foreground
        }
    }

    /**
     * Check notification permission (Android 13+).
     */
    fun checkNotificationPermission(): Boolean {
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            ContextCompat.checkSelfPermission(
                context,
                Manifest.permission.POST_NOTIFICATIONS
            ) == PackageManager.PERMISSION_GRANTED
        } else {
            true // Pre-Android 13, notifications are always allowed
        }
    }

    /**
     * Check Bluetooth permissions (varies by Android version).
     */
    private fun checkBluetoothPermissions(): Boolean {
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            ContextCompat.checkSelfPermission(
                context,
                Manifest.permission.BLUETOOTH_SCAN
            ) == PackageManager.PERMISSION_GRANTED &&
                    ContextCompat.checkSelfPermission(
                        context,
                        Manifest.permission.BLUETOOTH_CONNECT
                    ) == PackageManager.PERMISSION_GRANTED
        } else {
            ContextCompat.checkSelfPermission(
                context,
                Manifest.permission.BLUETOOTH
            ) == PackageManager.PERMISSION_GRANTED
        }
    }

    /**
     * Generic permission check.
     */
    private fun checkPermission(permission: String): Boolean =
        ContextCompat.checkSelfPermission(context, permission) == PackageManager.PERMISSION_GRANTED

    /**
     * Get permissions needed for location tracking.
     * Returns foreground location first, then background if on Android 10+.
     */
    fun getLocationPermissionsToRequest(): Array<String> {
        val permissions = mutableListOf<String>()

        if (!checkFineLocationPermission()) {
            permissions.add(Manifest.permission.ACCESS_FINE_LOCATION)
            permissions.add(Manifest.permission.ACCESS_COARSE_LOCATION)
        }

        // Only request background location if foreground is already granted or about to be granted
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q && checkFineLocationPermission()) {
            if (!checkBackgroundLocationPermission()) {
                permissions.add(Manifest.permission.ACCESS_BACKGROUND_LOCATION)
            }
        }

        return permissions.toTypedArray()
    }

    /**
     * Get all critical permissions for TheWatch app.
     */
    fun getCriticalPermissionsToRequest(): Array<String> {
        val permissions = mutableListOf<String>()

        // Location (foreground always needed for emergency response)
        if (!checkFineLocationPermission()) {
            permissions.add(Manifest.permission.ACCESS_FINE_LOCATION)
            permissions.add(Manifest.permission.ACCESS_COARSE_LOCATION)
        }

        // Notifications (critical for alert delivery)
        if (!checkNotificationPermission()) {
            permissions.add(Manifest.permission.POST_NOTIFICATIONS)
        }

        return permissions.toTypedArray()
    }

    /**
     * Check if a permission has been permanently denied (Don't Ask Again).
     * This requires checking with shouldShowRequestPermissionRationale.
     */
    fun isPermissionPermanentlyDenied(activity: android.app.Activity, permission: String): Boolean {
        return !checkPermission(permission) &&
                !activity.shouldShowRequestPermissionRationale(permission)
    }

    /**
     * Get deep link to app Settings page.
     * User can manually enable permissions there.
     */
    fun getAppSettingsIntent(): Intent {
        return Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS).apply {
            data = Uri.fromParts("package", context.packageName, null)
        }
    }

    /**
     * Update permission states after requesting.
     */
    fun refreshPermissionStates() {
        _fineLocationGranted.value = checkFineLocationPermission()
        _backgroundLocationGranted.value = checkBackgroundLocationPermission()
        _notificationGranted.value = checkNotificationPermission()
        _cameraGranted.value = checkPermission(Manifest.permission.CAMERA)
        _microphoneGranted.value = checkPermission(Manifest.permission.RECORD_AUDIO)
        _bluetoothGranted.value = checkBluetoothPermissions()
        _bodySensorsGranted.value = checkPermission(Manifest.permission.BODY_SENSORS)
        _contactsGranted.value = checkPermission(Manifest.permission.READ_CONTACTS)
    }

    /**
     * Check if all critical permissions for emergency response are granted.
     */
    fun areCriticalPermissionsGranted(): Boolean {
        return checkFineLocationPermission() && checkNotificationPermission()
    }
}
