package com.thewatch.app.data.repository.mock

import com.thewatch.app.data.model.Alert
import com.thewatch.app.data.model.CommunityAlert
import com.thewatch.app.data.model.Responder
import com.thewatch.app.data.repository.AlertRepository
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flowOf
import java.time.LocalDateTime
import javax.inject.Inject

class MockAlertRepository @Inject constructor() : AlertRepository {

    private var activeAlert: Alert? = null

    override suspend fun activateAlert(alert: Alert): Result<Alert> {
        delay(800)
        activeAlert = alert
        return Result.success(alert)
    }

    override suspend fun cancelAlert(alertId: String): Result<Unit> {
        delay(600)
        if (activeAlert?.id == alertId) {
            activeAlert = null
        }
        return Result.success(Unit)
    }

    override suspend fun getActiveAlert(userId: String): Flow<Alert?> {
        return flowOf(activeAlert)
    }

    override suspend fun getNearbyResponders(
        latitude: Double,
        longitude: Double,
        radiusMeters: Double
    ): Flow<List<Responder>> {
        delay(500)
        val responders = listOf(
            Responder(
                id = "responder_001",
                name = "John Martinez",
                type = "Emergency Medical Technician",
                distance = 250.0,
                latitude = latitude + 0.002,
                longitude = longitude + 0.002,
                eta = 3
            ),
            Responder(
                id = "responder_002",
                name = "Lisa Chen",
                type = "Paramedic",
                distance = 450.0,
                latitude = latitude - 0.001,
                longitude = longitude + 0.003,
                eta = 5
            ),
            Responder(
                id = "responder_003",
                name = "David Thompson",
                type = "Police Officer",
                distance = 600.0,
                latitude = latitude + 0.003,
                longitude = longitude - 0.002,
                eta = 7
            )
        )
        return flowOf(responders)
    }

    override suspend fun getNearbyAlerts(
        latitude: Double,
        longitude: Double,
        radiusMeters: Double
    ): Flow<List<CommunityAlert>> {
        delay(500)
        val alerts = listOf(
            CommunityAlert(
                id = "alert_001",
                type = "Medical Emergency",
                latitude = latitude + 0.005,
                longitude = longitude + 0.005,
                description = "Person collapsed near Central Park entrance",
                timestamp = LocalDateTime.now().minusMinutes(15),
                severity = "High",
                respondersCount = 2
            ),
            CommunityAlert(
                id = "alert_002",
                type = "Traffic Incident",
                latitude = latitude - 0.003,
                longitude = longitude - 0.003,
                description = "Multi-vehicle accident on 5th Avenue",
                timestamp = LocalDateTime.now().minusMinutes(25),
                severity = "Medium",
                respondersCount = 1
            )
        )
        return flowOf(alerts)
    }

    override suspend fun reportResponderMissing(alertId: String, responderId: String): Result<Unit> {
        delay(700)
        return Result.success(Unit)
    }

    override suspend fun confirmAlertResolution(alertId: String): Result<Unit> {
        delay(600)
        return Result.success(Unit)
    }
}
