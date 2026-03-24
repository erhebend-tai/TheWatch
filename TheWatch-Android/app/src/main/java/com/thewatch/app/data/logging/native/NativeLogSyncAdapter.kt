package com.thewatch.app.data.logging.native

import android.util.Log
import com.google.firebase.firestore.FirebaseFirestore
import com.google.firebase.firestore.Query
import com.thewatch.app.data.logging.LogEntry
import com.thewatch.app.data.logging.LogLevel
import com.thewatch.app.data.logging.LogSyncPort
import com.thewatch.app.data.logging.local.LogEntryDao
import com.thewatch.app.data.logging.local.LogEntryEntity
import kotlinx.coroutines.tasks.await
import java.time.Instant
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Native Firestore sync adapter — pushes local Room logs to Firestore
 * and pulls cross-device entries.
 *
 * Firestore collection structure:
 * ```
 * thewatch-logs/
 *   {deviceId}/
 *     entries/
 *       {logEntryId} → { timestamp, level, sourceContext, ... }
 * ```
 *
 * Batched writes (max 500 per Firestore batch) for efficiency.
 * Called by WorkManager periodic sync + app foregrounding.
 */
@Singleton
class NativeLogSyncAdapter @Inject constructor(
    private val dao: LogEntryDao,
    private val firestore: FirebaseFirestore
) : LogSyncPort {

    companion object {
        private const val TAG = "TheWatch"
        private const val COLLECTION = "thewatch-logs"
        private const val SUBCOLLECTION = "entries"
        private const val BATCH_SIZE = 500
    }

    override suspend fun syncToFirestore(): Int {
        val unsynced = dao.getUnsynced(BATCH_SIZE)
        if (unsynced.isEmpty()) return 0

        return try {
            // Group by device for collection routing
            val byDevice = unsynced.groupBy { it.deviceId ?: "unknown" }

            var totalSynced = 0
            for ((deviceId, batch) in byDevice) {
                val firestoreBatch = firestore.batch()
                for (entity in batch) {
                    val docRef = firestore
                        .collection(COLLECTION)
                        .document(deviceId)
                        .collection(SUBCOLLECTION)
                        .document(entity.id)

                    firestoreBatch.set(docRef, entity.toFirestoreMap())
                }
                firestoreBatch.commit().await()

                // Mark as synced in Room
                dao.markSynced(batch.map { it.id })
                totalSynced += batch.size
            }

            Log.i(TAG, "[LogSync] Synced $totalSynced entries to Firestore")
            totalSynced
        } catch (e: Exception) {
            Log.e(TAG, "[LogSync] Firestore sync failed: ${e.message}", e)
            0
        }
    }

    override suspend fun pullFromFirestore(limit: Int): List<LogEntry> {
        return try {
            // Pull from all devices for cross-device visibility
            val snapshot = firestore.collectionGroup(SUBCOLLECTION)
                .orderBy("timestamp", Query.Direction.DESCENDING)
                .limit(limit.toLong())
                .get()
                .await()

            snapshot.documents.mapNotNull { doc ->
                try {
                    LogEntry(
                        id = doc.id,
                        timestamp = Instant.ofEpochMilli(doc.getLong("timestamp") ?: 0),
                        level = LogLevel.entries.getOrElse(
                            (doc.getLong("level") ?: 2).toInt()
                        ) { LogLevel.Information },
                        sourceContext = doc.getString("sourceContext") ?: "Unknown",
                        messageTemplate = doc.getString("messageTemplate") ?: "",
                        properties = parseFirestoreProperties(doc.get("properties")),
                        exception = doc.getString("exception"),
                        correlationId = doc.getString("correlationId"),
                        userId = doc.getString("userId"),
                        deviceId = doc.getString("deviceId"),
                        synced = true
                    )
                } catch (e: Exception) {
                    Log.w(TAG, "[LogSync] Failed to parse Firestore doc ${doc.id}: ${e.message}")
                    null
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "[LogSync] Firestore pull failed: ${e.message}", e)
            emptyList()
        }
    }

    override suspend fun isSyncAvailable(): Boolean {
        return try {
            // Quick Firestore connectivity check
            firestore.collection(COLLECTION).limit(1).get().await()
            true
        } catch (e: Exception) {
            false
        }
    }

    @Suppress("UNCHECKED_CAST")
    private fun parseFirestoreProperties(raw: Any?): Map<String, String> {
        return when (raw) {
            is Map<*, *> -> (raw as? Map<String, Any>)?.mapValues { it.value.toString() } ?: emptyMap()
            else -> emptyMap()
        }
    }

    private fun LogEntryEntity.toFirestoreMap(): Map<String, Any?> = mapOf(
        "timestamp" to timestampEpochMs,
        "level" to level,
        "sourceContext" to sourceContext,
        "messageTemplate" to messageTemplate,
        "renderedMessage" to renderedMessage,
        "properties" to LogEntryEntity.fromDomain(toDomain()).propertiesJson, // reuse serializer
        "exception" to exception,
        "correlationId" to correlationId,
        "userId" to userId,
        "deviceId" to deviceId
    )
}
