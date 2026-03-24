/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         MockGuardianConsentRepository.kt                       │
 * │ Purpose:      Mock (Tier 1) adapter for GuardianConsentRepository.   │
 * │               Stores consent requests in-memory with a static        │
 * │               verification code "WATCH123". Used for development     │
 * │               and UI testing of the guardian consent flow.            │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: GuardianConsentRepository                              │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   // In AppModule.kt:                                                │
 * │   @Provides fun provideGuardianConsentRepository(                    │
 * │       mock: MockGuardianConsentRepository                            │
 * │   ): GuardianConsentRepository = mock                                │
 * │                                                                      │
 * │   // Then in signup flow:                                            │
 * │   val result = repo.requestConsent(...)                              │
 * │   // Use code "WATCH123" to verify in mock mode                      │
 * │   repo.verifyConsent(result.id, "WATCH123")                          │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.repository.mock

import android.util.Log
import com.thewatch.app.data.repository.ConsentRequest
import com.thewatch.app.data.repository.ConsentStatus
import com.thewatch.app.data.repository.GuardianConsentRepository
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.map
import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Locale
import java.util.UUID
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class MockGuardianConsentRepository @Inject constructor() : GuardianConsentRepository {

    companion object {
        private const val TAG = "TheWatch.MockConsent"
        /** Static verification code for mock tier. */
        const val MOCK_VERIFICATION_CODE = "WATCH123"
    }

    /** In-memory store of consent requests keyed by request ID. */
    private val requests = mutableMapOf<String, ConsentRequest>()

    /** In-memory store of consent status keyed by minor user ID. */
    private val statusFlows = mutableMapOf<String, MutableStateFlow<ConsentStatus>>()

    /** Resend count per request ID (rate limiting). */
    private val resendCounts = mutableMapOf<String, Int>()

    override suspend fun requestConsent(
        minorUserId: String,
        guardianName: String,
        guardianEmail: String,
        guardianPhone: String,
        relationship: String
    ): Result<ConsentRequest> {
        delay(1000) // Simulate network
        Log.i(TAG, "requestConsent() for minor=$minorUserId, guardian=$guardianName")

        val requestId = UUID.randomUUID().toString()
        val request = ConsentRequest(
            id = requestId,
            minorUserId = minorUserId,
            guardianName = guardianName,
            guardianEmail = guardianEmail,
            guardianPhone = guardianPhone,
            relationship = relationship,
            status = ConsentStatus.PENDING
        )

        requests[requestId] = request
        getOrCreateStatusFlow(minorUserId).value = ConsentStatus.PENDING
        resendCounts[requestId] = 0

        Log.i(TAG, "Consent request created: $requestId (code=$MOCK_VERIFICATION_CODE)")
        return Result.success(request)
    }

    override suspend fun verifyConsent(requestId: String, verificationCode: String): Result<Boolean> {
        delay(800) // Simulate network
        val request = requests[requestId]
            ?: return Result.failure(Exception("Consent request not found: $requestId"))

        return if (verificationCode == MOCK_VERIFICATION_CODE) {
            requests[requestId] = request.copy(status = ConsentStatus.GRANTED)
            getOrCreateStatusFlow(request.minorUserId).value = ConsentStatus.GRANTED
            Log.i(TAG, "Consent GRANTED for minor=${request.minorUserId}")
            Result.success(true)
        } else {
            Log.w(TAG, "Invalid verification code for request=$requestId")
            Result.success(false)
        }
    }

    override suspend fun resendVerificationCode(requestId: String): Result<Unit> {
        delay(500)
        val count = resendCounts.getOrDefault(requestId, 0)
        if (count >= 3) {
            return Result.failure(Exception("Maximum resend attempts reached (3)"))
        }
        resendCounts[requestId] = count + 1
        Log.i(TAG, "Verification code resent for request=$requestId (attempt ${count + 1}/3)")
        return Result.success(Unit)
    }

    override suspend fun getConsentStatus(minorUserId: String): ConsentStatus {
        return getOrCreateStatusFlow(minorUserId).value
    }

    override fun observeConsentStatus(minorUserId: String): Flow<ConsentStatus> {
        return getOrCreateStatusFlow(minorUserId)
    }

    override suspend fun revokeConsent(minorUserId: String, guardianId: String): Result<Unit> {
        delay(500)
        getOrCreateStatusFlow(minorUserId).value = ConsentStatus.REVOKED
        Log.i(TAG, "Consent REVOKED for minor=$minorUserId by guardian=$guardianId")
        return Result.success(Unit)
    }

    override fun isMinor(dateOfBirth: String): Boolean {
        return try {
            val format = if (dateOfBirth.contains("/")) {
                SimpleDateFormat("MM/dd/yyyy", Locale.US)
            } else {
                SimpleDateFormat("yyyy-MM-dd", Locale.US)
            }
            val dob = format.parse(dateOfBirth) ?: return false
            val today = Calendar.getInstance()
            val birth = Calendar.getInstance().apply { time = dob }

            var age = today.get(Calendar.YEAR) - birth.get(Calendar.YEAR)
            if (today.get(Calendar.DAY_OF_YEAR) < birth.get(Calendar.DAY_OF_YEAR)) {
                age--
            }
            Log.d(TAG, "isMinor($dateOfBirth) -> age=$age, minor=${age < 18}")
            age < 18
        } catch (e: Exception) {
            Log.w(TAG, "Failed to parse DOB: $dateOfBirth — defaulting to false")
            false
        }
    }

    private fun getOrCreateStatusFlow(minorUserId: String): MutableStateFlow<ConsentStatus> {
        return statusFlows.getOrPut(minorUserId) {
            MutableStateFlow(ConsentStatus.NONE)
        }
    }
}
