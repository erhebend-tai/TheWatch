package com.thewatch.app.data.model

import androidx.room.Entity
import androidx.room.PrimaryKey
import kotlinx.serialization.Serializable

@Serializable
@Entity(tableName = "users")
data class User(
    @PrimaryKey
    val id: String,
    val name: String,
    val email: String,
    val phone: String,
    val dateOfBirth: String,
    val photoUrl: String? = null,
    val bloodType: String? = null,
    val medicalConditions: String? = null,
    val medications: String? = null,
    val defaultSeverity: String = "HIGH",
    val autoEscalationTimerMinutes: Int = 30,
    val autoEscalateTo911: Boolean = false,
    val duressCode: String? = null,
    val personalClearWord: String? = null,
    val checkInScheduleMinutes: Int? = null,
    val isAuthenticated: Boolean = false,
    val lastUpdated: Long = System.currentTimeMillis()
)

@Serializable
data class EmergencyContact(
    val id: String,
    val userId: String,
    val name: String,
    val phone: String,
    val email: String,
    val relationship: String,
    val priority: Int = 1,
    val notificationEnabled: Boolean = true
)

@Serializable
data class Alert(
    val id: String,
    val userId: String,
    val severity: String, // LOW, MEDIUM, HIGH, CRITICAL
    val type: String, // FALL, MEDICAL, ATTACK, OTHER
    val latitude: Double,
    val longitude: Double,
    val timestamp: Long,
    val status: String, // ACTIVE, RESOLVED, CANCELLED, ESCALATED
    val description: String? = null,
    val triggeredBy: String? = null, // USER, IMPLICIT_DETECTION, WEARABLE
    val responderAssignedId: String? = null,
    val confidence: Float = 1.0f
)

@Serializable
data class HistoryEvent(
    val id: String,
    val userId: String,
    val type: String, // ALERT_ACTIVATED, ALERT_RESOLVED, CONTACT_NOTIFIED, etc.
    val severity: String? = null,
    val status: String? = null,
    val timestamp: Long,
    val latitude: Double? = null,
    val longitude: Double? = null,
    val triggerSource: String? = null,
    val confidenceScore: Float? = null,
    val description: String? = null,
    val escalationCount: Int = 0
)

@Serializable
data class Responder(
    val id: String,
    val userId: String,
    val name: String,
    val role: String, // EMT, POLICE, FIRE, VOLUNTEER, NURSE
    val latitude: Double,
    val longitude: Double,
    val isAvailable: Boolean = true,
    val responseTimeMinutes: Int? = null,
    val certifications: List<String> = emptyList(),
    val distanceMeters: Double? = null
)

@Serializable
data class CommunityAlert(
    val id: String,
    val type: String, // WEATHER, DANGER, EVACUATION, etc.
    val latitude: Double,
    val longitude: Double,
    val radius: Double,
    val description: String,
    val severity: String, // LOW, MEDIUM, HIGH, CRITICAL
    val timestamp: Long,
    val expirationTime: Long
)

@Serializable
data class EvacuationRoute(
    val id: String,
    val name: String,
    val description: String,
    val waypoints: List<LatLng>,
    val estimatedTimeMinutes: Int,
    val difficulty: String // EASY, MODERATE, DIFFICULT
)

@Serializable
data class LatLng(
    val latitude: Double,
    val longitude: Double
)

@Serializable
data class Shelter(
    val id: String,
    val name: String,
    val address: String,
    val latitude: Double,
    val longitude: Double,
    val capacity: Int,
    val availableSpaces: Int,
    val amenities: List<String> = emptyList(),
    val phone: String? = null
)

@Serializable
data class WearableDevice(
    val id: String,
    val name: String,
    val type: String, // SMARTWATCH, FITNESS_BAND, PHONE_SENSOR
    val isConnected: Boolean,
    val batteryPercent: Int
)
