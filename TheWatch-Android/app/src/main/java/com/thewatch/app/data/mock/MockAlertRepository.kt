package com.thewatch.app.data.mock

import com.thewatch.app.data.model.Alert
import com.thewatch.app.data.model.CommunityAlert
import com.thewatch.app.data.model.Responder
import com.thewatch.app.data.repository.AlertRepository
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.flow
import javax.inject.Inject

class MockAlertRepository @Inject constructor() : AlertRepository {
    private val activeAlertFlow = MutableStateFlow<Alert?>(null)

    override suspend fun activateAlert(alert: Alert): Result<Alert> {
        delay(500)
        activeAlertFlow.value = alert
        return Result.success(alert)
    }

    override suspend fun cancelAlert(alertId: String): Result<Unit> {
        delay(500)
        activeAlertFlow.value = null
        return Result.success(Unit)
    }

    override suspend fun getActiveAlert(userId: String): Flow<Alert?> = activeAlertFlow.asStateFlow()

    override suspend fun getNearbyResponders(
        latitude: Double,
        longitude: Double,
        radiusMeters: Double
    ): Flow<List<Responder>> = flow {
        delay(800)
        emit(
            listOf(
                Responder(
                    id = "responder_001",
                    userId = "user_002",
                    name = "Michael Chen",
                    role = "EMT",
                    latitude = latitude + 0.001,
                    longitude = longitude + 0.001,
                    isAvailable = true,
                    responseTimeMinutes = 3,
                    certifications = listOf("EMT-Basic", "CPR"),
                    distanceMeters = 250.0
                ),
                Responder(
                    id = "responder_002",
                    userId = "user_003",
                    name = "Sarah Anderson",
                    role = "NURSE",
                    latitude = latitude - 0.002,
                    longitude = longitude + 0.002,
                    isAvailable = true,
                    responseTimeMinutes = 5,
                    certifications = listOf("RN", "ACLS", "CPR"),
                    distanceMeters = 450.0
                ),
                Responder(
                    id = "responder_003",
                    userId = "user_004",
                    name = "James Rodriguez",
                    role = "VOLUNTEER",
                    latitude = latitude + 0.003,
                    longitude = longitude - 0.001,
                    isAvailable = true,
                    responseTimeMinutes = 7,
                    certifications = listOf("First Aid", "CPR"),
                    distanceMeters = 680.0
                )
            )
        )
    }

    override suspend fun getNearbyAlerts(
        latitude: Double,
        longitude: Double,
        radiusMeters: Double
    ): Flow<List<CommunityAlert>> = flow {
        delay(600)
        emit(
            listOf(
                CommunityAlert(
                    id = "alert_001",
                    type = "WEATHER",
                    latitude = latitude + 0.005,
                    longitude = longitude + 0.003,
                    radius = 1500.0,
                    description = "Severe thunderstorm warning in effect",
                    severity = "HIGH",
                    timestamp = System.currentTimeMillis() - 600000,
                    expirationTime = System.currentTimeMillis() + 3600000
                ),
                CommunityAlert(
                    id = "alert_002",
                    type = "DANGER",
                    latitude = latitude - 0.003,
                    longitude = longitude - 0.004,
                    radius = 800.0,
                    description = "Active accident on Main Street, avoid area",
                    severity = "MEDIUM",
                    timestamp = System.currentTimeMillis() - 300000,
                    expirationTime = System.currentTimeMillis() + 1800000
                )
            )
        )
    }

    override suspend fun reportResponderMissing(alertId: String, responderId: String): Result<Unit> {
        delay(800)
        return Result.success(Unit)
    }

    override suspend fun confirmAlertResolution(alertId: String): Result<Unit> {
        delay(600)
        activeAlertFlow.value = null
        return Result.success(Unit)
    }
}
