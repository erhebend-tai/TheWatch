package com.thewatch.app.ui.screens.permissions

import android.app.Activity
import android.content.Intent
import androidx.lifecycle.ViewModel
import com.thewatch.app.util.PermissionManager
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.StateFlow
import javax.inject.Inject

@HiltViewModel
class PermissionsViewModel @Inject constructor(
    private val permissionManager: PermissionManager
) : ViewModel() {

    val fineLocationGranted: StateFlow<Boolean> = permissionManager.fineLocationGranted
    val backgroundLocationGranted: StateFlow<Boolean> = permissionManager.backgroundLocationGranted
    val notificationGranted: StateFlow<Boolean> = permissionManager.notificationGranted
    val cameraGranted: StateFlow<Boolean> = permissionManager.cameraGranted
    val microphoneGranted: StateFlow<Boolean> = permissionManager.microphoneGranted
    val bluetoothGranted: StateFlow<Boolean> = permissionManager.bluetoothGranted
    val bodySensorsGranted: StateFlow<Boolean> = permissionManager.bodySensorsGranted
    val contactsGranted: StateFlow<Boolean> = permissionManager.contactsGranted

    fun isPermissionPermanentlyDenied(activity: Activity, permission: String): Boolean {
        return permissionManager.isPermissionPermanentlyDenied(activity, permission)
    }

    fun getAppSettingsIntent(): Intent {
        return permissionManager.getAppSettingsIntent()
    }

    fun refreshPermissionStates() {
        permissionManager.refreshPermissionStates()
    }
}
