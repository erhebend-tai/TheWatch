package com.thewatch.app.data.repository

import com.thewatch.app.data.model.MfaEnrollmentChallenge
import com.thewatch.app.data.model.User
import kotlinx.coroutines.flow.Flow

/**
 * Port interface for all authentication operations across the three-tier
 * adapter architecture (Mock -> Native -> Live/Firebase).
 *
 * Write-Ahead Log:
 *   Every implementation MUST log the following WAL entries at a minimum:
 *   - WAL Entry: AUTH_OP_START  -> operation, params, timestamp
 *   - WAL Entry: AUTH_OP_END    -> operation, success/failure, timestamp
 *
 * Example implementation:
 *   // Mock tier (dev/testing):
 *   class MockAuthRepository : AuthRepository { ... }
 *
 *   // Native tier (offline-capable, encrypted local storage):
 *   class NativeAuthRepository(context: Context) : AuthRepository { ... }
 *
 *   // Live tier (Firebase Auth + backend API):
 *   class FirebaseAuthRepository(context: Context) : AuthRepository { ... }
 */
interface AuthRepository {
    suspend fun login(emailOrPhone: String, password: String): Result<User>
    suspend fun signUp(
        name: String,
        email: String,
        phone: String,
        dateOfBirth: String,
        password: String
    ): Result<User>
    suspend fun sendPasswordResetCode(emailOrPhone: String): Result<String>
    suspend fun verifyResetCode(code: String): Result<Boolean>
    suspend fun resetPassword(code: String, newPassword: String): Result<Boolean>
    suspend fun biometricLogin(): Result<User>
    suspend fun logout(): Result<Unit>
    fun getCurrentUser(): Flow<User?>
    suspend fun acceptEULA(userId: String): Result<Unit>

    // ── Email Verification ───────────────────────────────────────────
    /** Send (or resend) the verification email to the currently authenticated user. */
    suspend fun sendEmailVerification(): Result<Unit>

    /**
     * Force-refresh the auth token and return the updated User.
     * After refresh, fields like emailVerified and custom claims are current.
     */
    suspend fun refreshToken(): Result<User>

    // ── Multi-Factor Authentication ──────────────────────────────────
    /**
     * Begin MFA enrollment for the given method.
     * @param method  "totp", "sms", or "backup"
     * @param phoneNumber  Required when method == "sms"; ignored otherwise.
     * @return An [MfaEnrollmentChallenge] containing the session, QR URI, and backup codes.
     */
    suspend fun enrollMfa(method: String, phoneNumber: String? = null): Result<MfaEnrollmentChallenge>

    /**
     * Confirm MFA enrollment by submitting the verification code for the
     * session started by [enrollMfa].
     */
    suspend fun confirmMfaEnrollment(sessionId: String, code: String): Result<Boolean>

    /**
     * Verify an MFA code during the login flow (post-password, pre-session).
     * @param code   The 6-digit TOTP/SMS code, or a backup recovery code.
     * @param method "totp", "sms", or "backup"
     */
    suspend fun verifyMfaCode(code: String, method: String): Result<Boolean>
}
