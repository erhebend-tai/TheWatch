/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         GuardianConsentRepository.kt                           │
 * │ Purpose:      Port interface (hexagonal) for guardian consent flows.  │
 * │               When a user under 18 signs up, COPPA / GDPR-K / state │
 * │               laws require verifiable parental or guardian consent.   │
 * │               This port defines the contract for requesting,         │
 * │               verifying, and storing guardian consent records.        │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: None (pure domain interface)                           │
 * │                                                                      │
 * │ Adapter tiers:                                                       │
 * │   - Mock:   In-memory consent store with fake verification codes.    │
 * │   - Native: Room + local verification (code-based).                  │
 * │   - Live:   Server-issued consent tokens + email/SMS verification.   │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val repo: GuardianConsentRepository = hiltGet()                    │
 * │   val request = repo.requestConsent(                                 │
 * │       minorUserId = "user_123",                                      │
 * │       guardianName = "Jane Doe",                                     │
 * │       guardianEmail = "jane@example.com",                            │
 * │       guardianPhone = "+1-555-0100",                                 │
 * │       relationship = "Mother"                                        │
 * │   )                                                                  │
 * │   // Guardian receives code, enters it:                              │
 * │   val verified = repo.verifyConsent(request.id, "ABC123")            │
 * │                                                                      │
 * │ Legal references:                                                    │
 * │   - COPPA (Children's Online Privacy Protection Act) 16 CFR 312     │
 * │   - GDPR Article 8 (child consent, <16 in most EU states)           │
 * │   - California CCPA / CPRA minor provisions                         │
 * │   - UK Children's Code (Age Appropriate Design Code)                │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.repository

import kotlinx.coroutines.flow.Flow

/**
 * Port interface for guardian/parental consent — the domain contract.
 *
 * Handles the full lifecycle of consent for users under 18:
 * 1. Request consent (sends verification code to guardian)
 * 2. Verify consent (guardian enters code)
 * 3. Query consent status
 * 4. Revoke consent (guardian withdraws permission)
 */
interface GuardianConsentRepository {

    /**
     * Initiate a consent request for a minor user.
     * Sends a verification code to the guardian via email/SMS.
     *
     * @param minorUserId The user ID of the minor signing up.
     * @param guardianName Full name of the guardian.
     * @param guardianEmail Guardian's email address.
     * @param guardianPhone Guardian's phone number.
     * @param relationship Relationship to the minor (Parent, Legal Guardian, etc.).
     * @return [ConsentRequest] with the generated request ID.
     */
    suspend fun requestConsent(
        minorUserId: String,
        guardianName: String,
        guardianEmail: String,
        guardianPhone: String,
        relationship: String
    ): Result<ConsentRequest>

    /**
     * Verify a consent request using the code sent to the guardian.
     *
     * @param requestId The consent request ID from [requestConsent].
     * @param verificationCode The code entered by the guardian.
     * @return Result containing true if verified, false if code mismatch.
     */
    suspend fun verifyConsent(requestId: String, verificationCode: String): Result<Boolean>

    /**
     * Resend the verification code to the guardian.
     * Rate-limited: max 3 resends per request.
     *
     * @param requestId The consent request ID.
     * @return Result containing the new code (mock) or success indicator.
     */
    suspend fun resendVerificationCode(requestId: String): Result<Unit>

    /**
     * Get the current consent status for a minor user.
     *
     * @param minorUserId The user ID to check.
     * @return [ConsentStatus] indicating the current state.
     */
    suspend fun getConsentStatus(minorUserId: String): ConsentStatus

    /**
     * Observe consent status changes in real time.
     *
     * @param minorUserId The user ID to observe.
     * @return Flow emitting status changes.
     */
    fun observeConsentStatus(minorUserId: String): Flow<ConsentStatus>

    /**
     * Revoke guardian consent (guardian withdraws permission).
     * This should disable the minor's account until new consent is obtained.
     *
     * @param minorUserId The minor's user ID.
     * @param guardianId The guardian who is revoking.
     * @return Result indicating success.
     */
    suspend fun revokeConsent(minorUserId: String, guardianId: String): Result<Unit>

    /**
     * Check whether a date of birth indicates a minor (under 18).
     *
     * @param dateOfBirth Date string in MM/DD/YYYY or YYYY-MM-DD format.
     * @return true if the person is under 18 years old.
     */
    fun isMinor(dateOfBirth: String): Boolean
}

/**
 * Represents a consent request initiated for a minor user.
 */
data class ConsentRequest(
    val id: String,
    val minorUserId: String,
    val guardianName: String,
    val guardianEmail: String,
    val guardianPhone: String,
    val relationship: String,
    val status: ConsentStatus,
    val createdAt: Long = System.currentTimeMillis(),
    val expiresAt: Long = createdAt + (48 * 60 * 60 * 1000) // 48 hours
)

/**
 * Status of a guardian consent request.
 */
enum class ConsentStatus {
    /** No consent request exists for this user. */
    NONE,
    /** Consent request sent, awaiting guardian verification. */
    PENDING,
    /** Guardian has verified and granted consent. */
    GRANTED,
    /** Verification code expired or request timed out. */
    EXPIRED,
    /** Guardian explicitly denied consent. */
    DENIED,
    /** Previously granted consent has been revoked. */
    REVOKED
}
