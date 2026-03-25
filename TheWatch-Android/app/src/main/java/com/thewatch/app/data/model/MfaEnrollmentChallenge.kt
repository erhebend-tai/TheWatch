package com.thewatch.app.data.model

import kotlinx.serialization.Serializable

/**
 * MFA enrollment challenge returned by the backend when a user initiates
 * multi-factor authentication setup.
 *
 * Example usage:
 *   val challenge = authRepo.enrollMfa("totp", null).getOrThrow()
 *   // Display QR code from challenge.challengeUri
 *   // User enters TOTP code from authenticator app
 *   authRepo.confirmMfaEnrollment(challenge.sessionId, userCode)
 *
 * Write-Ahead Log:
 *   - WAL Entry: MFA_ENROLL_START  -> sessionId, method, timestamp
 *   - WAL Entry: MFA_ENROLL_CONFIRM -> sessionId, success, timestamp
 *   - WAL Entry: MFA_ENROLL_FAIL   -> sessionId, errorCode, timestamp
 *
 * Supported methods: "totp" (authenticator app), "sms" (text message), "backup" (recovery codes)
 * Backend endpoint: POST /api/account/mfa/enroll
 * Confirm endpoint: POST /api/account/mfa/enroll/confirm
 * Verify endpoint:  POST /api/account/mfa/verify
 */
@Serializable
data class MfaEnrollmentChallenge(
    /** The MFA method being enrolled: "totp", "sms", or "backup" */
    val method: String,

    /**
     * For TOTP: otpauth:// URI to generate a QR code the user scans with
     * Google Authenticator, Authy, 1Password, etc.
     * For SMS: a masked phone number like "+1***5678" confirming the target.
     * For Backup: empty string (codes are in [backupCodes]).
     */
    val challengeUri: String,

    /** Server-issued session identifier that ties enroll-start to enroll-confirm. */
    val sessionId: String,

    /**
     * One-time recovery codes generated during enrollment.
     * Typically 8-10 codes. The user MUST store these offline.
     * Only populated on the initial enrollment response; never returned again.
     */
    val backupCodes: List<String> = emptyList()
)
