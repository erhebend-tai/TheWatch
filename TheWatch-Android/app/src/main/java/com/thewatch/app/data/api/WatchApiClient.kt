/**
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                         │
 * │ File:    WatchApiClient.kt                                              │
 * │ Purpose: Centralized HTTP API client for TheWatch Dashboard API.        │
 * │          Wraps all REST endpoints with coroutine-based async methods.   │
 * │          Bearer token auth via FirebaseAuth.currentUser.getIdToken().   │
 * │ Date:    2026-03-24                                                     │
 * │ Author:  Claude                                                         │
 * │ Deps:    OkHttp, Gson, Firebase Auth, Hilt, Coroutines                 │
 * │                                                                         │
 * │ Base URL resolution:                                                    │
 * │   1. Aspire service discovery: https+http://dashboard-api               │
 * │   2. Config override from BuildConfig / local.properties                │
 * │   3. Fallback: http://10.0.2.2:5000 (Android emulator → host)          │
 * │                                                                         │
 * │ Usage Example:                                                          │
 * │   @Inject lateinit var apiClient: WatchApiClient                        │
 * │                                                                         │
 * │   val status = apiClient.getAccountStatus()                             │
 * │   val responses = apiClient.getActiveResponses("user_001")              │
 * │   val participation = apiClient.getParticipation("user_001")            │
 * │   apiClient.updateParticipation(prefs)                                  │
 * │   val evidence = apiClient.getEvidenceForRequest("req_001")             │
 * └──────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.api

import android.util.Log
import com.google.firebase.auth.FirebaseAuth
import com.google.gson.Gson
import com.google.gson.GsonBuilder
import com.google.gson.JsonDeserializationContext
import com.google.gson.JsonDeserializer
import com.google.gson.JsonElement
import com.google.gson.reflect.TypeToken
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.tasks.await
import kotlinx.coroutines.withContext
import java.io.IOException
import java.lang.reflect.Type
import java.net.HttpURLConnection
import java.net.URL
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

// ═══════════════════════════════════════════════════════════════
// API Response DTOs — match the JSON shapes from Dashboard.Api
// ═══════════════════════════════════════════════════════════════

data class AccountStatusResponse(
    val uid: String? = null,
    val email: String? = null,
    val emailVerified: Boolean = false,
    val mfaEnabled: Boolean = false,
    val displayName: String? = null,
    val phoneNumber: String? = null,
    val disabled: Boolean = false
)

data class TriggerResponseDto(
    val requestId: String = "",
    val scope: String = "",
    val strategy: String = "",
    val escalation: String = "",
    val status: String = "",
    val radiusMeters: Double = 0.0,
    val desiredResponderCount: Int = 0,
    val createdAt: String = ""
)

data class ActiveResponseDto(
    val requestId: String = "",
    val userId: String = "",
    val scope: String = "",
    val status: String = "",
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val radiusMeters: Double = 0.0,
    val desiredResponderCount: Int = 0,
    val acknowledgedResponders: List<ResponderAckDto> = emptyList(),
    val createdAt: String = ""
)

data class ResponderAckDto(
    val ackId: String = "",
    val responderId: String = "",
    val responderName: String = "",
    val responderRole: String = "",
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val distanceMeters: Double = 0.0,
    val hasVehicle: Boolean = false,
    val estimatedArrival: String? = null,
    val status: String = ""
)

data class ParticipationPreferencesDto(
    val userId: String = "",
    val isAvailable: Boolean = true,
    val optInScopes: List<String> = emptyList(),
    val certifications: List<String> = emptyList(),
    val hasVehicle: Boolean = false,
    val maxRadiusMeters: Double = 5000.0,
    val quietHoursStart: String? = null,
    val quietHoursEnd: String? = null,
    val weeklySchedule: Map<String, List<String>> = emptyMap()
)

data class EvidenceSubmissionDto(
    val id: String = "",
    val requestId: String? = null,
    val userId: String = "",
    val submitterId: String = "",
    val phase: String = "",
    val submissionType: String = "",
    val title: String? = null,
    val description: String? = null,
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val contentHash: String = "",
    val mimeType: String = "",
    val fileSizeBytes: Long = 0,
    val blobReference: String? = null,
    val thumbnailBlobReference: String? = null,
    val status: String = "",
    val submittedAt: String = ""
)

data class SituationDto(
    val request: ActiveResponseDto? = null,
    val responders: List<ResponderAckDto> = emptyList(),
    val escalationHistory: List<Map<String, Any>> = emptyList()
)

// ═══════════════════════════════════════════════════════════════
// API Client
// ═══════════════════════════════════════════════════════════════

@Singleton
class WatchApiClient @Inject constructor() {

    companion object {
        private const val TAG = "WatchApiClient"

        /**
         * Base URL for the Dashboard API.
         * Resolution order:
         *   1. Environment/BuildConfig override
         *   2. Aspire service discovery (https+http://dashboard-api)
         *   3. Emulator fallback: http://10.0.2.2:5000
         *   4. Physical device fallback: http://localhost:5000
         */
        private const val DEFAULT_BASE_URL = "http://10.0.2.2:5000"
    }

    private var baseUrl: String = DEFAULT_BASE_URL

    private val gson: Gson = GsonBuilder()
        .registerTypeAdapter(LocalDateTime::class.java, LocalDateTimeDeserializer())
        .create()

    // ── Configuration ─────────────────────────────────────────────

    /**
     * Override the base URL at runtime.
     * Called by the app's configuration layer (e.g., Aspire service discovery,
     * settings screen, or BuildConfig injection).
     */
    fun setBaseUrl(url: String) {
        baseUrl = url.trimEnd('/')
        Log.i(TAG, "Base URL set to: $baseUrl")
    }

    // ── Auth Token ────────────────────────────────────────────────

    /**
     * Get the Firebase ID token for the current user.
     * Returns null if no user is logged in.
     * Forces a refresh to ensure the token is not expired.
     */
    private suspend fun getAuthToken(): String? {
        return try {
            FirebaseAuth.getInstance().currentUser?.getIdToken(true)?.await()?.token
        } catch (e: Exception) {
            Log.w(TAG, "Failed to get auth token: ${e.message}")
            null
        }
    }

    // ── HTTP Helpers ──────────────────────────────────────────────

    private suspend fun <T> get(path: String, typeToken: Type): T = withContext(Dispatchers.IO) {
        val url = URL("$baseUrl$path")
        val conn = url.openConnection() as HttpURLConnection
        try {
            conn.requestMethod = "GET"
            conn.setRequestProperty("Content-Type", "application/json")
            conn.setRequestProperty("Accept", "application/json")
            conn.connectTimeout = 15_000
            conn.readTimeout = 30_000

            getAuthToken()?.let { token ->
                conn.setRequestProperty("Authorization", "Bearer $token")
            }

            val responseCode = conn.responseCode
            val body = if (responseCode in 200..299) {
                conn.inputStream.bufferedReader().use { it.readText() }
            } else {
                val error = conn.errorStream?.bufferedReader()?.use { it.readText() } ?: ""
                throw ApiException(responseCode, error)
            }

            gson.fromJson(body, typeToken)
        } finally {
            conn.disconnect()
        }
    }

    private suspend fun <T> post(path: String, requestBody: Any? = null, typeToken: Type): T = withContext(Dispatchers.IO) {
        val url = URL("$baseUrl$path")
        val conn = url.openConnection() as HttpURLConnection
        try {
            conn.requestMethod = "POST"
            conn.setRequestProperty("Content-Type", "application/json")
            conn.setRequestProperty("Accept", "application/json")
            conn.connectTimeout = 15_000
            conn.readTimeout = 30_000
            conn.doOutput = requestBody != null

            getAuthToken()?.let { token ->
                conn.setRequestProperty("Authorization", "Bearer $token")
            }

            if (requestBody != null) {
                val jsonBody = gson.toJson(requestBody)
                conn.outputStream.bufferedWriter().use { it.write(jsonBody) }
            }

            val responseCode = conn.responseCode
            val body = if (responseCode in 200..299) {
                conn.inputStream.bufferedReader().use { it.readText() }
            } else {
                val error = conn.errorStream?.bufferedReader()?.use { it.readText() } ?: ""
                throw ApiException(responseCode, error)
            }

            gson.fromJson(body, typeToken)
        } finally {
            conn.disconnect()
        }
    }

    private suspend fun <T> put(path: String, requestBody: Any, typeToken: Type): T = withContext(Dispatchers.IO) {
        val url = URL("$baseUrl$path")
        val conn = url.openConnection() as HttpURLConnection
        try {
            conn.requestMethod = "PUT"
            conn.setRequestProperty("Content-Type", "application/json")
            conn.setRequestProperty("Accept", "application/json")
            conn.connectTimeout = 15_000
            conn.readTimeout = 30_000
            conn.doOutput = true

            getAuthToken()?.let { token ->
                conn.setRequestProperty("Authorization", "Bearer $token")
            }

            val jsonBody = gson.toJson(requestBody)
            conn.outputStream.bufferedWriter().use { it.write(jsonBody) }

            val responseCode = conn.responseCode
            val body = if (responseCode in 200..299) {
                conn.inputStream.bufferedReader().use { it.readText() }
            } else {
                val error = conn.errorStream?.bufferedReader()?.use { it.readText() } ?: ""
                throw ApiException(responseCode, error)
            }

            gson.fromJson(body, typeToken)
        } finally {
            conn.disconnect()
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Account Endpoints — /api/account
    // ═══════════════════════════════════════════════════════════════

    /** GET /api/account/status — account status (email verified, MFA, etc.) */
    suspend fun getAccountStatus(): AccountStatusResponse {
        return get("/api/account/status", object : TypeToken<AccountStatusResponse>() {}.type)
    }

    /** POST /api/account/verify-email */
    suspend fun sendEmailVerification(): Map<String, Any> {
        return post("/api/account/verify-email", null, object : TypeToken<Map<String, Any>>() {}.type)
    }

    /** POST /api/account/mfa/enroll */
    suspend fun enrollMfa(method: String, phoneNumber: String? = null): Map<String, Any> {
        val body = mapOf("method" to method, "phoneNumber" to phoneNumber)
        return post("/api/account/mfa/enroll", body, object : TypeToken<Map<String, Any>>() {}.type)
    }

    /** POST /api/account/mfa/confirm */
    suspend fun confirmMfaEnrollment(sessionId: String, code: String): Map<String, Any> {
        val body = mapOf("sessionId" to sessionId, "code" to code)
        return post("/api/account/mfa/confirm", body, object : TypeToken<Map<String, Any>>() {}.type)
    }

    /** POST /api/account/mfa/verify */
    suspend fun verifyMfaCode(code: String, method: String = "totp"): Map<String, Any> {
        val body = mapOf("code" to code, "method" to method)
        return post("/api/account/mfa/verify", body, object : TypeToken<Map<String, Any>>() {}.type)
    }

    /** POST /api/account/password-reset (AllowAnonymous) */
    suspend fun sendPasswordReset(email: String): Map<String, Any> {
        val body = mapOf("email" to email)
        return post("/api/account/password-reset", body, object : TypeToken<Map<String, Any>>() {}.type)
    }

    // ═══════════════════════════════════════════════════════════════
    // Response Endpoints — /api/response
    // ═══════════════════════════════════════════════════════════════

    /** POST /api/response/trigger — create a new SOS response */
    suspend fun triggerResponse(
        userId: String,
        scope: String,
        latitude: Double,
        longitude: Double,
        description: String? = null,
        triggerSource: String? = null
    ): TriggerResponseDto {
        val body = mapOf(
            "userId" to userId,
            "scope" to scope,
            "latitude" to latitude,
            "longitude" to longitude,
            "description" to description,
            "triggerSource" to triggerSource
        )
        return post("/api/response/trigger", body, object : TypeToken<TriggerResponseDto>() {}.type)
    }

    /** POST /api/response/{requestId}/ack — responder acknowledgment */
    suspend fun acknowledgeResponse(
        requestId: String,
        responderId: String,
        responderName: String? = null,
        responderRole: String? = null,
        latitude: Double = 0.0,
        longitude: Double = 0.0,
        distanceMeters: Double = 0.0,
        hasVehicle: Boolean = true,
        estimatedArrivalMinutes: Int? = null
    ): Map<String, Any> {
        val body = mapOf(
            "responderId" to responderId,
            "responderName" to responderName,
            "responderRole" to responderRole,
            "latitude" to latitude,
            "longitude" to longitude,
            "distanceMeters" to distanceMeters,
            "hasVehicle" to hasVehicle,
            "estimatedArrivalMinutes" to estimatedArrivalMinutes
        )
        return post("/api/response/$requestId/ack", body, object : TypeToken<Map<String, Any>>() {}.type)
    }

    /** POST /api/response/{requestId}/cancel */
    suspend fun cancelResponse(requestId: String, reason: String? = null): Map<String, Any> {
        val body = mapOf("reason" to reason)
        return post("/api/response/$requestId/cancel", body, object : TypeToken<Map<String, Any>>() {}.type)
    }

    /** POST /api/response/{requestId}/resolve */
    suspend fun resolveResponse(requestId: String, resolvedBy: String? = null): Map<String, Any> {
        val body = mapOf("resolvedBy" to resolvedBy)
        return post("/api/response/$requestId/resolve", body, object : TypeToken<Map<String, Any>>() {}.type)
    }

    /** GET /api/response/{requestId} — full situation (request + acks + escalation) */
    suspend fun getSituation(requestId: String): SituationDto {
        return get("/api/response/$requestId", object : TypeToken<SituationDto>() {}.type)
    }

    /** GET /api/response/active/{userId} — all active responses for a user */
    suspend fun getActiveResponses(userId: String): List<ActiveResponseDto> {
        return get("/api/response/active/$userId", object : TypeToken<List<ActiveResponseDto>>() {}.type)
    }

    /** GET /api/response/participation/{userId} — participation preferences */
    suspend fun getParticipation(userId: String): ParticipationPreferencesDto {
        return get("/api/response/participation/$userId", object : TypeToken<ParticipationPreferencesDto>() {}.type)
    }

    /** PUT /api/response/participation — update participation preferences */
    suspend fun updateParticipation(prefs: ParticipationPreferencesDto): ParticipationPreferencesDto {
        return put("/api/response/participation", prefs, object : TypeToken<ParticipationPreferencesDto>() {}.type)
    }

    /** POST /api/response/participation/{userId}/availability */
    suspend fun setAvailability(userId: String, isAvailable: Boolean, durationMinutes: Int? = null): Map<String, Any> {
        val body = mutableMapOf<String, Any>("isAvailable" to isAvailable)
        durationMinutes?.let { body["duration"] = "00:${it}:00" }
        return post("/api/response/participation/$userId/availability", body, object : TypeToken<Map<String, Any>>() {}.type)
    }

    /** POST /api/response/sos-token — get SOS bypass token */
    suspend fun getSosToken(): Map<String, Any> {
        return post("/api/response/sos-token", null, object : TypeToken<Map<String, Any>>() {}.type)
    }

    /** POST /api/response/{requestId}/messages — send responder message */
    suspend fun sendResponderMessage(
        requestId: String,
        senderId: String,
        senderName: String? = null,
        senderRole: String? = null,
        messageType: String = "Text",
        content: String = "",
        latitude: Double? = null,
        longitude: Double? = null,
        quickResponseCode: String? = null
    ): Map<String, Any> {
        val body = mapOf(
            "senderId" to senderId,
            "senderName" to senderName,
            "senderRole" to senderRole,
            "messageType" to messageType,
            "content" to content,
            "latitude" to latitude,
            "longitude" to longitude,
            "quickResponseCode" to quickResponseCode
        )
        return post("/api/response/$requestId/messages", body, object : TypeToken<Map<String, Any>>() {}.type)
    }

    /** GET /api/response/{requestId}/messages — responder message history */
    suspend fun getResponderMessages(requestId: String, limit: Int = 100, since: String? = null): List<Map<String, Any>> {
        val query = buildString {
            append("?limit=$limit")
            since?.let { append("&since=$it") }
        }
        return get("/api/response/$requestId/messages$query", object : TypeToken<List<Map<String, Any>>>() {}.type)
    }

    /** GET /api/response/quick-responses */
    suspend fun getQuickResponses(): List<Map<String, Any>> {
        return get("/api/response/quick-responses", object : TypeToken<List<Map<String, Any>>>() {}.type)
    }

    // ═══════════════════════════════════════════════════════════════
    // Evidence Endpoints — /api/evidence
    // ═══════════════════════════════════════════════════════════════

    /** GET /api/evidence/request/{requestId} — all evidence for an incident */
    suspend fun getEvidenceForRequest(requestId: String): List<EvidenceSubmissionDto> {
        return get("/api/evidence/request/$requestId", object : TypeToken<List<EvidenceSubmissionDto>>() {}.type)
    }

    /** GET /api/evidence/{id} — single submission */
    suspend fun getEvidence(id: String): EvidenceSubmissionDto {
        return get("/api/evidence/$id", object : TypeToken<EvidenceSubmissionDto>() {}.type)
    }

    /** GET /api/evidence/user/{userId} — all evidence for a user */
    suspend fun getEvidenceForUser(userId: String, phase: String? = null): List<EvidenceSubmissionDto> {
        val query = phase?.let { "?phase=$it" } ?: ""
        return get("/api/evidence/user/$userId$query", object : TypeToken<List<EvidenceSubmissionDto>>() {}.type)
    }

    // ═══════════════════════════════════════════════════════════════
    // Notification / Mobile Log Endpoints
    // ═══════════════════════════════════════════════════════════════

    /** POST /api/mobilelog — submit mobile device logs */
    suspend fun submitMobileLog(logs: List<Map<String, Any>>): Map<String, Any> {
        return post("/api/mobilelog", logs, object : TypeToken<Map<String, Any>>() {}.type)
    }

    /** GET /api/health — API health check */
    suspend fun healthCheck(): Map<String, Any> {
        return get("/api/health", object : TypeToken<Map<String, Any>>() {}.type)
    }
}

// ═══════════════════════════════════════════════════════════════
// Exception
// ═══════════════════════════════════════════════════════════════

class ApiException(val statusCode: Int, val errorBody: String) :
    IOException("API error $statusCode: $errorBody")

// ═══════════════════════════════════════════════════════════════
// Gson Adapter for LocalDateTime
// ═══════════════════════════════════════════════════════════════

private class LocalDateTimeDeserializer : JsonDeserializer<LocalDateTime> {
    override fun deserialize(json: JsonElement, typeOfT: Type, context: JsonDeserializationContext): LocalDateTime {
        return try {
            LocalDateTime.parse(json.asString, DateTimeFormatter.ISO_DATE_TIME)
        } catch (e: Exception) {
            LocalDateTime.now()
        }
    }
}
