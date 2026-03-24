package com.thewatch.app.data.repository

import com.thewatch.app.data.model.Alert
import com.thewatch.app.data.model.CommunityAlert
import com.thewatch.app.data.model.Responder
import kotlinx.coroutines.flow.Flow

interface AlertRepository {
    suspend fun activateAlert(alert: Alert): Result<Alert>
    suspend fun cancelAlert(alertId: String): Result<Unit>
    suspend fun getActiveAlert(userId: String): Flow<Alert?>
    suspend fun getNearbyResponders(latitude: Double, longitude: Double, radiusMeters: Double): Flow<List<Responder>>
    suspend fun getNearbyAlerts(latitude: Double, longitude: Double, radiusMeters: Double): Flow<List<CommunityAlert>>
    suspend fun reportResponderMissing(alertId: String, responderId: String): Result<Unit>
    suspend fun confirmAlertResolution(alertId: String): Result<Unit>
}
