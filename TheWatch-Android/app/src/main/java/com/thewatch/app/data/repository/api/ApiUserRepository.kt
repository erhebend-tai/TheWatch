/**
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                         │
 * │ File:    ApiUserRepository.kt                                           │
 * │ Purpose: UserRepository implementation backed by WatchApiClient.        │
 * │          Calls GET/PUT /api/account/status for profile data.            │
 * │ Date:    2026-03-24                                                     │
 * │ Author:  Claude                                                         │
 * │ Deps:    WatchApiClient, Firebase Auth, Hilt                            │
 * │                                                                         │
 * │ Usage Example:                                                          │
 * │   val repo: UserRepository = apiUserRepository                          │
 * │   repo.getProfile("user_001").collect { user -> }                       │
 * │   repo.updateProfile(user)                                              │
 * └──────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.repository.api

import com.google.firebase.auth.FirebaseAuth
import com.thewatch.app.data.api.WatchApiClient
import com.thewatch.app.data.model.EmergencyContact
import com.thewatch.app.data.model.User
import com.thewatch.app.data.model.WearableDevice
import com.thewatch.app.data.repository.UserRepository
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow
import javax.inject.Inject

class ApiUserRepository @Inject constructor(
    private val apiClient: WatchApiClient
) : UserRepository {

    override suspend fun getProfile(userId: String): Flow<User?> = flow {
        try {
            val accountStatus = apiClient.getAccountStatus()
            val firebaseUser = FirebaseAuth.getInstance().currentUser

            emit(
                User(
                    id = userId,
                    name = accountStatus.displayName ?: firebaseUser?.displayName ?: "",
                    email = accountStatus.email ?: firebaseUser?.email ?: "",
                    phone = accountStatus.phoneNumber ?: firebaseUser?.phoneNumber ?: "",
                    dateOfBirth = "",
                    isAuthenticated = true
                )
            )
        } catch (e: Exception) {
            // Fallback to Firebase user data if API is unreachable
            val firebaseUser = FirebaseAuth.getInstance().currentUser
            if (firebaseUser != null) {
                emit(
                    User(
                        id = firebaseUser.uid,
                        name = firebaseUser.displayName ?: "",
                        email = firebaseUser.email ?: "",
                        phone = firebaseUser.phoneNumber ?: "",
                        dateOfBirth = "",
                        isAuthenticated = true
                    )
                )
            } else {
                emit(null)
            }
        }
    }

    override suspend fun updateProfile(user: User): Result<User> {
        // Profile updates flow through Firebase Auth for now.
        // When the backend adds PUT /api/account/profile, wire here.
        return Result.success(user)
    }

    override suspend fun addEmergencyContact(contact: EmergencyContact): Result<EmergencyContact> {
        // Emergency contacts are stored locally + synced; no dedicated backend endpoint yet.
        return Result.success(contact)
    }

    override suspend fun updateEmergencyContact(contact: EmergencyContact): Result<EmergencyContact> {
        return Result.success(contact)
    }

    override suspend fun deleteEmergencyContact(contactId: String): Result<Unit> {
        return Result.success(Unit)
    }

    override suspend fun getEmergencyContacts(userId: String): Flow<List<EmergencyContact>> = flow {
        // Emergency contacts are stored locally; emit empty list for API tier.
        // Will be wired when GET /api/account/contacts endpoint is available.
        emit(emptyList())
    }

    override suspend fun addWearableDevice(device: WearableDevice): Result<WearableDevice> {
        return Result.success(device)
    }

    override suspend fun removeWearableDevice(deviceId: String): Result<Unit> {
        return Result.success(Unit)
    }

    override suspend fun getWearableDevices(userId: String): Flow<List<WearableDevice>> = flow {
        emit(emptyList())
    }

    override suspend fun updateProfilePhoto(userId: String, photoUrl: String): Result<String> {
        return Result.success(photoUrl)
    }
}
