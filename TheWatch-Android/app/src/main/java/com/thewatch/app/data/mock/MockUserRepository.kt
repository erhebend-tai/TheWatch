package com.thewatch.app.data.mock

import com.thewatch.app.data.model.EmergencyContact
import com.thewatch.app.data.model.User
import com.thewatch.app.data.model.WearableDevice
import com.thewatch.app.data.repository.UserRepository
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow
import javax.inject.Inject

class MockUserRepository @Inject constructor() : UserRepository {

    private val mockUser = User(
        id = "user_001",
        name = "Alex Rivera",
        email = "alex.rivera@example.com",
        phone = "+1-555-0123",
        dateOfBirth = "1990-05-15",
        photoUrl = null,
        bloodType = "O+",
        medicalConditions = "Asthma, Seasonal Allergies",
        medications = "Albuterol inhaler, Cetirizine",
        defaultSeverity = "HIGH",
        autoEscalationTimerMinutes = 30,
        autoEscalateTo911 = false,
        duressCode = "911911",
        personalClearWord = "SAFE",
        checkInScheduleMinutes = 240,
        isAuthenticated = true
    )

    private val mockContacts = listOf(
        EmergencyContact(
            id = "contact_001",
            userId = "user_001",
            name = "Maria Rivera",
            phone = "+1-555-0124",
            email = "maria.rivera@example.com",
            relationship = "Sister",
            priority = 1,
            notificationEnabled = true
        ),
        EmergencyContact(
            id = "contact_002",
            userId = "user_001",
            name = "Dr. James Mitchell",
            phone = "+1-555-0125",
            email = "dr.mitchell@hospital.com",
            relationship = "Personal Doctor",
            priority = 2,
            notificationEnabled = true
        ),
        EmergencyContact(
            id = "contact_003",
            userId = "user_001",
            name = "Jordan Lee",
            phone = "+1-555-0126",
            email = "jordan.lee@example.com",
            relationship = "Friend",
            priority = 3,
            notificationEnabled = true
        )
    )

    private val mockDevices = listOf(
        WearableDevice(
            id = "device_001",
            name = "Apple Watch Series 8",
            type = "SMARTWATCH",
            isConnected = true,
            batteryPercent = 85
        ),
        WearableDevice(
            id = "device_002",
            name = "Fitbit Charge 5",
            type = "FITNESS_BAND",
            isConnected = true,
            batteryPercent = 72
        )
    )

    override suspend fun updateProfile(user: User): Result<User> {
        delay(1000)
        return Result.success(user)
    }

    override suspend fun getProfile(userId: String): Flow<User?> = flow {
        delay(500)
        emit(mockUser)
    }

    override suspend fun addEmergencyContact(contact: EmergencyContact): Result<EmergencyContact> {
        delay(800)
        return Result.success(contact)
    }

    override suspend fun updateEmergencyContact(contact: EmergencyContact): Result<EmergencyContact> {
        delay(800)
        return Result.success(contact)
    }

    override suspend fun deleteEmergencyContact(contactId: String): Result<Unit> {
        delay(600)
        return Result.success(Unit)
    }

    override suspend fun getEmergencyContacts(userId: String): Flow<List<EmergencyContact>> = flow {
        delay(500)
        emit(mockContacts)
    }

    override suspend fun addWearableDevice(device: WearableDevice): Result<WearableDevice> {
        delay(800)
        return Result.success(device)
    }

    override suspend fun removeWearableDevice(deviceId: String): Result<Unit> {
        delay(600)
        return Result.success(Unit)
    }

    override suspend fun getWearableDevices(userId: String): Flow<List<WearableDevice>> = flow {
        delay(500)
        emit(mockDevices)
    }

    override suspend fun updateProfilePhoto(userId: String, photoUrl: String): Result<String> {
        delay(1500)
        return Result.success(photoUrl)
    }
}
