/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         MockAccountDeletionAdapter.kt                          │
 * │ Purpose:      Mock adapter for AccountDeletionPort. In-memory state  │
 * │               machine simulating 30-day deletion lifecycle.          │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: AccountDeletionPort, kotlinx.coroutines                │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val mock = MockAccountDeletionAdapter()                            │
 * │   mock.requestDeletion("user-001", "Testing")                        │
 * │   mock.cancelDeletion("user-001")                                    │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.gdpr.mock

import com.thewatch.app.data.gdpr.*
import kotlinx.coroutines.delay

class MockAccountDeletionAdapter : AccountDeletionPort {
    private val requests = mutableMapOf<String, DeletionRequest>()
    private val auditTrail = mutableListOf<DeletionAuditEntry>()
    private val gracePeriodDays = 30
    private val dayMillis = 86_400_000L

    override suspend fun getConfirmationSteps() = listOf(
        DeletionConfirmationStep.OFFER_EXPORT, DeletionConfirmationStep.ACKNOWLEDGE_DATA_LOSS,
        DeletionConfirmationStep.TYPE_CONFIRMATION, DeletionConfirmationStep.RE_AUTHENTICATE
    )

    override suspend fun requestDeletion(userId: String, reason: String?): Result<DeletionRequest> {
        delay(800)
        val now = System.currentTimeMillis()
        val scheduledAt = now + (gracePeriodDays * dayMillis)
        val request = DeletionRequest(userId, now, scheduledAt, reason, DeletionState.PENDING_GRACE_PERIOD, scheduledAt, gracePeriodDays)
        requests[userId] = request
        auditTrail.add(DeletionAuditEntry(now, "DELETION_REQUESTED", userId, userId, "Reason: ${reason ?: "Not provided"}"))
        return Result.success(request)
    }

    override suspend fun getDeletionStatus(userId: String): DeletionRequest? {
        delay(200)
        return requests[userId]?.let { val remaining = ((it.scheduledDeletionAt - System.currentTimeMillis()) / dayMillis).toInt().coerceAtLeast(0); it.copy(daysRemaining = remaining) }
    }

    override suspend fun cancelDeletion(userId: String): Result<Unit> {
        delay(500)
        val req = requests[userId] ?: return Result.failure(IllegalStateException("No pending deletion"))
        if (req.state != DeletionState.PENDING_GRACE_PERIOD) return Result.failure(IllegalStateException("Cannot cancel in state ${req.state}"))
        requests[userId] = req.copy(state = DeletionState.CANCELLED)
        auditTrail.add(DeletionAuditEntry(System.currentTimeMillis(), "DELETION_CANCELLED", userId, userId, "Cancelled during grace period"))
        return Result.success(Unit)
    }

    override suspend fun executeDeletion(userId: String): Result<List<DataCategory>> {
        delay(2000)
        requests[userId] = requests[userId]?.copy(state = DeletionState.COMPLETED) ?: return Result.failure(IllegalStateException("No request"))
        auditTrail.add(DeletionAuditEntry(System.currentTimeMillis(), "DELETION_EXECUTED", userId, "system", "All data erased"))
        return Result.success(DataCategory.entries)
    }

    override suspend fun getGracePeriodDaysRemaining(userId: String): Int {
        val req = requests[userId] ?: return 0
        if (req.state != DeletionState.PENDING_GRACE_PERIOD) return 0
        return ((req.scheduledDeletionAt - System.currentTimeMillis()) / dayMillis).toInt().coerceAtLeast(0)
    }

    override suspend fun getDeletionAuditTrail(userId: String) = auditTrail.filter { it.userId == userId }
}
