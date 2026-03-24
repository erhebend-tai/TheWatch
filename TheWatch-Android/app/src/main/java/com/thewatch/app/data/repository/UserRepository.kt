package com.thewatch.app.data.repository

import com.thewatch.app.data.model.EmergencyContact
import com.thewatch.app.data.model.User
import com.thewatch.app.data.model.WearableDevice
import kotlinx.coroutines.flow.Flow

interface UserRepository {
    suspend fun updateProfile(user: User): Result<User>
    suspend fun getProfile(userId: String): Flow<User?>
    suspend fun addEmergencyContact(contact: EmergencyContact): Result<EmergencyContact>
    suspend fun updateEmergencyContact(contact: EmergencyContact): Result<EmergencyContact>
    suspend fun deleteEmergencyContact(contactId: String): Result<Unit>
    suspend fun getEmergencyContacts(userId: String): Flow<List<EmergencyContact>>
    suspend fun addWearableDevice(device: WearableDevice): Result<WearableDevice>
    suspend fun removeWearableDevice(deviceId: String): Result<Unit>
    suspend fun getWearableDevices(userId: String): Flow<List<WearableDevice>>
    suspend fun updateProfilePhoto(userId: String, photoUrl: String): Result<String>
}
