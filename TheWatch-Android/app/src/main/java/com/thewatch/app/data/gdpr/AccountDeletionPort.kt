/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         AccountDeletionPort.kt                                 │
 * │ Purpose:      Hexagonal port for GDPR Article 17 right to erasure.   │
 * │               30-day grace period, multi-step confirmation, audit.    │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: kotlinx.coroutines                                     │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val port: AccountDeletionPort = hiltGet()                          │
 * │   port.requestDeletion("user-001", "No longer using")                │
 * │   // 30 days later: port.executeDeletion("user-001")                 │
 * │                                                                      │
 * │ Regulatory: GDPR Art.17, CCPA 1798.105, Apple 5.1.1(v), Google Play  │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.gdpr

enum class DeletionState { NONE, PENDING_GRACE_PERIOD, EXECUTING, COMPLETED, CANCELLED, FAILED }

data class DeletionRequest(
    val userId: String, val requestedAt: Long, val scheduledDeletionAt: Long,
    val reason: String?, val state: DeletionState, val cancellationDeadline: Long, val daysRemaining: Int
)

enum class DeletionConfirmationStep { RE_AUTHENTICATE, TYPE_CONFIRMATION, ACKNOWLEDGE_DATA_LOSS, OFFER_EXPORT }

data class DeletionAuditEntry(val timestamp: Long, val action: String, val userId: String, val performedBy: String, val details: String)

interface AccountDeletionPort {
    suspend fun getConfirmationSteps(): List<DeletionConfirmationStep>
    suspend fun requestDeletion(userId: String, reason: String? = null): Result<DeletionRequest>
    suspend fun getDeletionStatus(userId: String): DeletionRequest?
    suspend fun cancelDeletion(userId: String): Result<Unit>
    suspend fun executeDeletion(userId: String): Result<List<DataCategory>>
    suspend fun getGracePeriodDaysRemaining(userId: String): Int
    suspend fun getDeletionAuditTrail(userId: String): List<DeletionAuditEntry>
}
