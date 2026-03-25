# Merge Brief: Cowork Changes (March 24, 2026)

**From:** Cowork/Claude (Opus 4.6)
**To:** Claude Code — for use when merging the 10 feature branches
**Purpose:** Files modified by Cowork that may conflict with feature branch work

---

## HIGH-RISK MERGE CONFLICTS (files touched by both Cowork and likely touched by agents)

### 1. Android: `AppModule.kt`
**Path:** `TheWatch-Android/app/src/main/java/com/thewatch/app/di/AppModule.kt`
**What Cowork added:** Logging DI providers (lines 45-60):
```kotlin
// ── Logging (Mock tier — swap to Native tier for Firestore) ──
fun provideLoggingPort(mock: MockLoggingAdapter): LoggingPort = mock
fun provideLogSyncPort(mock: MockLogSyncAdapter): LogSyncPort = mock
fun provideLogEntryDao(db: AppDatabase): LogEntryDao = db.logEntryDao()
fun provideWatchLogger(port: LoggingPort): WatchLogger = WatchLogger(port)
```
**Agents likely to conflict:** Android Auth+Security (#1), Android Profile+Settings (#5), Android Evidence+Media (#7), Android Offline+Health (#9) — all may add their own DI providers here.
**Resolution:** Keep both. Cowork's logging providers are independent of any repository providers.

### 2. Android: `AppDatabase.kt`
**Path:** `TheWatch-Android/app/src/main/java/com/thewatch/app/data/local/AppDatabase.kt`
**What Cowork changed:** Added `LogEntryEntity` to entities array, bumped version to 2, added `logEntryDao()`, added `fallbackToDestructiveMigration()`.
**Agents likely to conflict:** Android Offline+Health (#9) may add Health entities, Android Evidence+Media (#7) may add Evidence entities.
**Resolution:** Merge all entities into the array, use highest version number, keep `fallbackToDestructiveMigration()` for dev.

### 3. Android: `TheWatchApplication.kt`
**Path:** `TheWatch-Android/app/src/main/java/com/thewatch/app/TheWatchApplication.kt`
**What Cowork changed:** Was a 2-line stub. Now injects `WatchLogger`, sets `deviceId`, enqueues `LogSyncWorker`, logs on startup, flushes on trim memory.
**Agents likely to conflict:** Android Auth+Security (#1) may add biometric init, Android Offline+Health (#9) may add Health Connect init.
**Resolution:** Keep Cowork's logger init (it should run first), then add agent initializations after. Logger should be available for other components to use.

### 4. Android: `SyncLogEntity.kt`
**Path:** `TheWatch-Android/app/src/main/java/com/thewatch/app/data/local/SyncLogEntity.kt`
**What happened:** Claude Code (not Cowork) already expanded this significantly with `SyncAction` enum, `SyncStatus` enum, indices, priority field, and WAL header. The file was modified via linter/external edit.
**Agents likely to conflict:** Android Offline+Health (#9) will almost certainly touch this.
**Resolution:** Use the expanded version (already committed). It has backward-compat fields.

### 5. iOS: `TheWatchApp.swift`
**Path:** `TheWatch-iOS/TheWatchApp.swift`
**What Cowork added:** `WatchLogger.shared` property, `init()` block that configures MockLoggingAdapter, `logger.flush()` in `.background`, `logger.information()` in `.active`.
**Agents likely to conflict:** iOS Auth+Security (#2) may add biometric config, iOS Profile+Settings (#6) may add dark mode init.
**Resolution:** Keep Cowork's init block (logger setup must happen first). Add agent code after the logger configuration.

---

## MEDIUM-RISK (new files in same directories)

### 6. Android: `data/logging/` package (ALL NEW)
**Path:** `TheWatch-Android/app/src/main/java/com/thewatch/app/data/logging/`
**New files:**
- `LogLevel.kt`, `LogEntry.kt`, `LoggingPort.kt`, `WatchLogger.kt`, `LogSyncWorker.kt`
- `mock/MockLoggingAdapter.kt`
- `local/LogEntryEntity.kt`, `local/LogEntryDao.kt`
- `native/NativeLoggingAdapter.kt`, `native/NativeLogSyncAdapter.kt`

**No conflict expected** — entirely new package. But agents SHOULD use `WatchLogger` for their logging instead of raw `Log.d()`.

### 7. iOS: `Logging/` group (ALL NEW)
**Path:** `TheWatch-iOS/Logging/`
**New files:**
- `LogLevel.swift`, `LogEntry.swift`, `LoggingPort.swift`, `WatchLogger.swift`
- `Mock/MockLoggingAdapter.swift`
- `Local/LogEntryModel.swift`
- `Native/NativeLoggingAdapter.swift`, `Native/NativeLogSyncAdapter.swift`

**No conflict expected** — entirely new directory.

---

## LOW-RISK (Aspire backend — agents don't touch these)

### 8. Aspire: New controllers + services
- `Dashboard.Api/Controllers/AdapterTierController.cs` (NEW)
- `Dashboard.Api/Controllers/TestOrchestratorController.cs` (NEW)
- `Dashboard.Api/Controllers/MobileLogController.cs` (NEW)
- `Dashboard.Api/Services/ITestOrchestratorService.cs` (NEW)
- `Dashboard.Api/Services/TestOrchestratorService.cs` (NEW)
- `Dashboard.Api/Program.cs` — added `ITestOrchestratorService` registration (line 93)

### 9. MAUI: New pages + ViewModels
- `TheWatch.Maui/ViewModels/AdapterTierViewModel.cs` (NEW)
- `TheWatch.Maui/ViewModels/TestDashboardViewModel.cs` (NEW)
- `TheWatch.Maui/Views/AdapterTierPage.xaml` + `.xaml.cs` (NEW)
- `TheWatch.Maui/Views/TestDashboardPage.xaml` + `.xaml.cs` (NEW)
- `TheWatch.Maui/MauiProgram.cs` — added 2 ViewModels + 2 Pages to DI

### 10. AdapterRegistry.cs
**Path:** `TheWatch.Shared/Configuration/AdapterRegistry.cs`
**What happened:** Claude Code added `BuildOutput` property (line 68). Cowork's `AdapterTierController` reads all existing properties. No conflict, but the controller's `GetTierForSlot`/`SetTierForSlot` switch should add a `"buildoutput"` case if Claude Code wants it switchable at runtime.

---

## RECOMMENDED MERGE ORDER

1. Merge Cowork's logging infrastructure first (new packages, no conflicts)
2. Then merge the 10 feature branches in any order
3. Resolve `AppModule.kt` / `AppDatabase.kt` / `TheWatchApp.swift` conflicts by keeping both sides
4. After all merges: bump `AppDatabase` version to whatever's highest + 1

---

## KEY PATTERN: How agents should use WatchLogger

### Android
```kotlin
@Inject lateinit var logger: WatchLogger

logger.information(
    sourceContext = "EvidenceCaptureService",
    messageTemplate = "Photo captured for incident {IncidentId}",
    properties = mapOf("IncidentId" to incidentId),
    correlationId = activeAlertId
)
```

### iOS
```swift
WatchLogger.shared.information(
    source: "EvidenceCaptureService",
    template: "Photo captured for incident {IncidentId}",
    properties: ["IncidentId": incidentId],
    correlationId: activeAlertId
)
```
