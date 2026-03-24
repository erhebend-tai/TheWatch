/**
 * ═══════════════════════════════════════════════════════════════════════════════
 * WRITE-AHEAD LOG — NG911Port.kt
 * ═══════════════════════════════════════════════════════════════════════════════
 * Purpose:   Hexagonal port for Next-Generation 911 (NG911) integration.
 *            NG911 replaces legacy PSAP analog systems with IP-based emergency
 *            communication. This port abstracts the call initiation, location
 *            sharing (PIDF-LO), and session management required for automated
 *            911 dispatch when escalation timer expires.
 * Date:      2026-03-24
 * Author:    Claude (Anthropic)
 * Deps:      kotlinx.coroutines.flow
 * Package:   com.thewatch.app.data.emergency
 *
 * Usage Example:
 *   @Inject lateinit var ng911Port: NG911Port
 *
 *   // Check availability in user's area
 *   val available = ng911Port.isAvailable(lat = 40.7128, lng = -74.0060)
 *
 *   // Initiate emergency call with location + medical data
 *   val session = ng911Port.initiateEmergencyCall(
 *       NG911CallRequest(
 *           userId = "user_001",
 *           latitude = 40.7128,
 *           longitude = -74.0060,
 *           callType = NG911CallType.AUTOMATIC_ESCALATION,
 *           medicalSummary = "Blood: O+ | Allergies: Penicillin | Conditions: Asthma",
 *           callerName = "Alex Rivera",
 *           callerPhone = "+15550123"
 *       )
 *   )
 *
 * NG911 Standards Reference:
 *   - NENA i3 Standard (NENA-STA-010.3)
 *   - RFC 5222 (LoST — Location-to-Service Translation)
 *   - RFC 4119 (PIDF-LO — Presence Information Data Format Location Object)
 *   - RFC 6443 (SIP — Session Initiation Protocol for emergency calls)
 *   - FCC 47 CFR 9.10 (NG911 requirements for wireless carriers)
 *   - APCO ANS 1.101.3 (Core Services NG911)
 *
 * IMPORTANT LEGAL NOTE:
 *   - Auto-dialing 911 without user consent may violate state laws
 *   - User MUST explicitly opt-in via SettingsScreen toggle
 *   - False call penalties: 18 USC 1038 (false information / hoax)
 *   - App must clearly indicate when 911 auto-dial is armed
 *   - All 911 calls MUST be logged with timestamp, location, reason
 * ═══════════════════════════════════════════════════════════════════════════════
 */
package com.thewatch.app.data.emergency

import kotlinx.coroutines.flow.SharedFlow

/**
 * Type of 911 call being placed.
 */
enum class NG911CallType {
    /** User manually pressed SOS and confirmed 911 */
    USER_INITIATED,
    /** Auto-escalation timer expired with no user response */
    AUTOMATIC_ESCALATION,
    /** Wearable detected critical health event (fall, cardiac arrest) */
    WEARABLE_TRIGGERED,
    /** Duress code detected in speech */
    DURESS_DETECTED
}

/**
 * Request payload for initiating an NG911 call.
 */
data class NG911CallRequest(
    val userId: String,
    val latitude: Double,
    val longitude: Double,
    val altitude: Double? = null,
    val horizontalAccuracyMeters: Double? = null,
    val callType: NG911CallType,
    val medicalSummary: String = "",
    val callerName: String = "",
    val callerPhone: String = "",
    val additionalNotes: String = "",
    val timestamp: Long = System.currentTimeMillis()
)

/**
 * Session returned after NG911 call initiation.
 */
data class NG911Session(
    val sessionId: String,
    val userId: String,
    val status: NG911SessionStatus,
    val psapId: String? = null,
    val psapName: String? = null,
    val estimatedResponseMinutes: Int? = null,
    val callStartTimestamp: Long,
    val callEndTimestamp: Long? = null
)

enum class NG911SessionStatus {
    INITIATING,
    CONNECTED_TO_PSAP,
    LOCATION_SHARED,
    DISPATCHED,
    COMPLETED,
    FAILED,
    CANCELLED
}

/**
 * Events emitted during NG911 session lifecycle.
 */
sealed class NG911Event {
    data class CallInitiated(val sessionId: String, val userId: String) : NG911Event()
    data class ConnectedToPSAP(val sessionId: String, val psapName: String) : NG911Event()
    data class LocationShared(val sessionId: String, val lat: Double, val lng: Double) : NG911Event()
    data class Dispatched(val sessionId: String, val eta: Int?) : NG911Event()
    data class CallEnded(val sessionId: String, val reason: String) : NG911Event()
    data class CallFailed(val sessionId: String, val error: String) : NG911Event()
}

/**
 * Hexagonal port for NG911 emergency call integration.
 *
 * Adapters:
 *   - MockNG911Adapter       — simulated for dev/testing
 *   - (Future) SIPng911Adapter — real SIP-based NG911 via SRTP
 *   - (Future) TelephonyNG911Adapter — Android Telephony fallback (legacy 911)
 */
interface NG911Port {

    /** Observable stream of NG911 session events */
    val ng911Events: SharedFlow<NG911Event>

    /** Check if NG911 service is available at the given coordinates */
    suspend fun isAvailable(lat: Double, lng: Double): Boolean

    /** Check if the user has enabled and consented to auto-911 */
    suspend fun isEnabledForUser(userId: String): Boolean

    /** Enable or disable auto-911 for a user (requires explicit consent) */
    suspend fun setEnabled(userId: String, enabled: Boolean): Result<Unit>

    /** Initiate an emergency call to 911/PSAP */
    suspend fun initiateEmergencyCall(request: NG911CallRequest): Result<NG911Session>

    /** Cancel an active NG911 session (if possible — some PSAPs require callback) */
    suspend fun cancelCall(sessionId: String): Result<Unit>

    /** Get the current session status */
    suspend fun getSession(sessionId: String): NG911Session?

    /** Update location during an active session (PIDF-LO refresh) */
    suspend fun updateLocation(sessionId: String, lat: Double, lng: Double): Result<Unit>
}
