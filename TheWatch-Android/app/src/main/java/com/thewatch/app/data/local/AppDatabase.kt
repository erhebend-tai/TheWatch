package com.thewatch.app.data.local

import androidx.room.Database
import androidx.room.RoomDatabase
import com.thewatch.app.data.logging.local.LogEntryDao
import com.thewatch.app.data.logging.local.LogEntryEntity
import com.thewatch.app.data.model.User
import com.thewatch.app.data.sync.SyncTaskDao
import com.thewatch.app.data.sync.SyncTaskEntity

@Database(
    entities = [
        User::class,
        SyncLogEntity::class,
        LogEntryEntity::class,
        SyncTaskEntity::class
    ],
    version = 4,
    exportSchema = false
)
abstract class AppDatabase : RoomDatabase() {
    abstract fun userDao(): UserDao
    abstract fun syncLogDao(): SyncLogDao
    abstract fun logEntryDao(): LogEntryDao
    abstract fun syncTaskDao(): SyncTaskDao
}
