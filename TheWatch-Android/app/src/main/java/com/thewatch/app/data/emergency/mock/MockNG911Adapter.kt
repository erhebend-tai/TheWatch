/**
 * ═══════════════════════════════════════════════════════════════════════════════
 * WRITE-AHEAD LOG — MockNG911Adapter.kt
 * ═══════════════════════════════════════════════════════════════════════════════
 * Purpose:   Mock adapter for NG911Port. Simulates 911 call flow for dev/testing.
 *            NEVER dials real 911 — all operations are in-memory.
 *            Production adapter would use Android TelephonyManager + SIP stack.
 * Date:      2026-03-24
 * Author:    Claude (Anthropic)
 * Deps:      NG911Port, kotlinx.coroutines
 * Package:   com.thewatch.app.data.emergency.mock
 *
 * Usage Example:
 *   // Bound via Hilt in AppModule:
 *   @Provides fun provideNG911Port(): NG911Port = MockNG911Adapter()
 * ═══════════════════════════════════════════════════════════════════════════════
 */
package com.thewatch.app.data.emergency.mock

import com.thewatch.app.data.emergency.NG911CallRequest
import com.thewatch.app.data.emergency.NG911Event
import com.thewatch.app.data.emergency.NG911Port
import com.thewatch.app.data.emergency.NG911Session
import com.thewatch.app.data.emergency.NG911SessionStatus
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow
import java.util.UUID
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class MockNG911Adapter @Inject constructor() : NG911Port {

    private val _ng911Events = MutableSharedFlow<NG911Event>(replay = 1)
    override val ng911Events: SharedFlow<NG911Event> = _ng911Events.asSharedFlow()

    private val sessions = mutableMapOf<String, NG911Session>()
    private val userEnabled = mutableMapOf<String, Boolean>()

    override suspend fun isAvailable(lat: Double, lng: Double): Boolean {
        // Mock: NG911 available in continental US coordinates
        return lat in 24.0..49.0 && lng in -125.0..-66.0
    }

    override suspend fun isEnabledForUser(userId: String): Boolean {
        return userEnabled[userId] ?: false
    }

    override suspend fun setEnabled(userId: String, enabled: Boolean): Result<Unit> {
        userEnabled[userId] = enabled
        return Result.success(Unit)
    }

    override suspend fun initiateEmergencyCall(request: NG911CallRequest): Result<NG911Session> {
        delay(1_000) // Simulate connection latency

        val sessionId = "ng911_${UUID.randomUUID()}"
        val session = NG911Session(
            sessionId = sessionId,
            userId = request.userId,
            status = NG911SessionStatus.INITIATING,
            callStartTimestamp = System.currentTimeMillis()
        )
        sessions[sessionId] = session
        _ng911Events.emit(NG911Event.CallInitiated(sessionId, request.userId))

        // Simulate PSAP connection
        delay(2_000)
        val connected = session.copy(
            status = NG911SessionStatus.CONNECTED_TO_PSAP,
            psapId = "PSAP_MOCK_001",
            psapName = "Mock County PSAP"
        )
        sessions[sessionId] = connected
        _ng911Events.emit(NG911Event.ConnectedToPSAP(sessionId, "Mock County PSAP"))

        // Simulate location share
        delay(500)
        _ng911Events.emit(NG911Event.LocationShared(sessionId, request.latitude, request.longitude))

        // Simulate dispatch
        delay(1_000)
        val dispatched = connected.copy(
            status = NG911SessionStatus.DISPATCHED,
            estimatedResponseMinutes = 7
        )
        sessions[sessionId] = dispatched
        _ng911Events.emit(NG911Event.Dispatched(sessionId, 7))

        return Result.success(dispatched)
    }

    override suspend fun cancelCall(sessionId: String): Result<Unit> {
        val session = sessions[sessionId] ?: return Result.failure(
            IllegalStateException("Session $sessionId not found")
        )
        sessions[sessionId] = session.copy(
            status = NG911SessionStatus.CANCELLED,
            callEndTimestamp = System.currentTimeMillis()
        )
        _ng911Events.emit(NG911Event.CallEnded(sessionId, "Cancelled by user"))
        return Result.success(Unit)
    }

    override suspend fun getSession(sessionId: String): NG911Session? {
        return sessions[sessionId]
    }

    override suspend fun updateLocation(sessionId: String, lat: Double, lng: Double): Result<Unit> {
        val session = sessions[sessionId] ?: return Result.failure(
            IllegalStateException("Session $sessionId not found")
        )
        _ng911Events.emit(NG911Event.LocationShared(sessionId, lat, lng))
        return Result.success(Unit)
    }
}
