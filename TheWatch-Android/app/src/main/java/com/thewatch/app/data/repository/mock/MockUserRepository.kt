package com.thewatch.app.data.repository.mock

import com.thewatch.app.data.model.EmergencyContact
import com.thewatch.app.data.model.User
import com.thewatch.app.data.model.WearableDevice
import com.thewatch.app.data.repository.UserRepository
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flowOf
import java.time.LocalDate
import javax.inject.Inject

class MockUserRepository @Inject constructor() : UserRepository {

    private var currentUser: User? = null
    private val emergencyContacts = mutableListOf<EmergencyContact>()
    private val wearableDevices = mutableListOf<WearableDevice>()

    init {
        currentUser = User(
            id = "user_001",
            fullName = "Alex Rivera",
            email = "alex.rivera@example.com",
            phoneNumber = "+1-555-0123",
            dateOfBirth = LocalDate.of(1990, 5, 15),
            bloodType = "O+",
            medicalConditions = listOf("Asthma", "Hypertension"),
            photoUrl = "https://example.com/avatar.jpg"
        )

        emergencyContacts.addAll(
            listOf(
                EmergencyContact(
                    id = "contact_001",
                    userId = "user_001",
                    name = "Maria Rivera",
                    relationship = "Sister",
                    phoneNumber = "+1-555-0124",
                    email = "maria.rivera@example.com"
                ),
                EmergencyContact(
                    id = "contact_002",
                    userId = "user_001",
                    name = "Dr. James Mitchell",
                    relationship = "Primary Doctor",
                    phoneNumber = "+1-555-0125",
                    email = "j.mitchell@medicalcenter.com"
                ),
                EmergencyContact(
                    id = "contact_003",
                    userId = "user_001",
                    name = "Sarah Chen",
                    relationship = "Work Supervisor",
                    phoneNumber = "+1-555-0126",
                    email = "s.chen@company.com"
                ),
                EmergencyContact(
                    id = "contact_004",
                    userId = "user_001",
                    name = "Local Hospital ER",
                    relationship = "Emergency Services",
                    phoneNumber = "+1-555-0911",
                    email = "er@localhospital.com"
                )
            )
        )

        wearableDevices.addAll(
            listOf(
                WearableDevice(
                    id = "device_001",
                    userId = "user_001",
                    name = "Smartwatch Pro",
                    manufacturer = "TechWatch",
                    isActive = true
                )
            )
        )
    }

    override suspend fun updateProfile(user: User): Result<User> {
        delay(800)
        currentUser = user
        return Result.success(user)
    }

    override suspend fun getProfile(userId: String): Flow<User?> {
        return flowOf(currentUser)
    }

    override suspend fun addEmergencyContact(contact: EmergencyContact): Result<EmergencyContact> {
        delay(600)
        emergencyContacts.add(contact)
        return Result.success(contact)
    }

    override suspend fun updateEmergencyContact(contact: EmergencyContact): Result<EmergencyContact> {
        delay(600)
        val index = emergencyContacts.indexOfFirst { it.id == contact.id }
        if (index >= 0) {
            emergencyContacts[index] = contact
        }
        return Result.success(contact)
    }

    override suspend fun deleteEmergencyContact(contactId: String): Result<Unit> {
        delay(500)
        emergencyContacts.removeIf { it.id == contactId }
        return Result.success(Unit)
    }

    override suspend fun getEmergencyContacts(userId: String): Flow<List<EmergencyContact>> {
        return flowOf(emergencyContacts.filter { it.userId == userId })
    }

    override suspend fun addWearableDevice(device: WearableDevice): Result<WearableDevice> {
        delay(700)
        wearableDevices.add(device)
        return Result.success(device)
    }

    override suspend fun removeWearableDevice(deviceId: String): Result<Unit> {
        delay(600)
        wearableDevices.removeIf { it.id == deviceId }
        return Result.success(Unit)
    }

    override suspend fun getWearableDevices(userId: String): Flow<List<WearableDevice>> {
        return flowOf(wearableDevices.filter { it.userId == userId })
    }

    override suspend fun updateProfilePhoto(userId: String, photoUrl: String): Result<String> {
        delay(800)
        currentUser = currentUser?.copy(photoUrl = photoUrl)
        return Result.success(photoUrl)
    }
}
