package com.thewatch.app.data.adapters

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: AdapterSlot enumerates every swappable adapter in the mobile app.
// Each slot maps to a hexagonal port interface. The AdapterRegistry tracks
// which AdapterTier is active for each slot. The MAUI dashboard's
// AdapterTierController sends SignalR messages keyed by slot name.
//
// When adding a new port/adapter pair to the app:
//   1. Add a new entry here
//   2. Register the Mock/Native/Live implementations in AdapterRegistry
//   3. Update AppModule.kt to resolve through the registry
//
// Example:
// ```kotlin
// val slot = AdapterSlot.fromString("logging") // AdapterSlot.Logging
// val portName = AdapterSlot.SOS.portInterface // "com.thewatch.app.data.emergency.NG911Port"
// ```

/**
 * Enumeration of all adapter slots in the mobile application.
 *
 * Each slot corresponds to a hexagonal port interface. The [AdapterRegistry]
 * maintains a tier mapping for each slot, enabling runtime hot-switching
 * between Mock, Native, Live, and Disabled implementations.
 *
 * Slot names are stable across platforms (Android, iOS, Aspire) so the MAUI
 * dashboard can command tier changes by name via SignalR.
 */
enum class AdapterSlot(
    /** Fully-qualified port interface name for documentation/debugging. */
    val portInterface: String,
    /** Human-readable description for dashboard UI. */
    val description: String
) {
    /** Structured logging — LoggingPort */
    Logging(
        portInterface = "com.thewatch.app.data.logging.LoggingPort",
        description = "Structured logging (Serilog-compatible)"
    ),

    /** Log sync to Firestore — LogSyncPort */
    LogSync(
        portInterface = "com.thewatch.app.data.logging.LogSyncPort",
        description = "Log synchronization to Firestore"
    ),

    /** GPS/location services — LocationRepository */
    Location(
        portInterface = "com.thewatch.app.data.repository.LocationRepository",
        description = "GPS and location services"
    ),

    /** Push notifications — APNs/FCM */
    Notifications(
        portInterface = "com.thewatch.app.data.notifications.NotificationPort",
        description = "Push notification delivery (FCM)"
    ),

    /** SOS/emergency — NG911Port + ImplicitDetectionPort */
    SOS(
        portInterface = "com.thewatch.app.data.emergency.NG911Port",
        description = "SOS alerting and NG911 integration"
    ),

    /** Contact/guardian management */
    Contacts(
        portInterface = "com.thewatch.app.data.repository.UserRepository",
        description = "Contact and guardian management"
    ),

    /** Audio capture for phrase detection + evidence recording */
    Audio(
        portInterface = "com.thewatch.app.data.audio.AudioCapturePort",
        description = "Audio capture (phrase detection, evidence)"
    ),

    /** Telephony — emergency dialing */
    Telephony(
        portInterface = "com.thewatch.app.data.telephony.TelephonyPort",
        description = "Telephony and emergency dialing"
    ),

    /** AI/ML — phrase matching, threat assessment, anomaly detection */
    AI(
        portInterface = "com.thewatch.app.data.ai.InferencePort",
        description = "AI/ML inference (phrase matching, threat detection)"
    ),

    /** Cloud messaging — Firebase Cloud Messaging / SignalR */
    CloudMessaging(
        portInterface = "com.thewatch.app.data.messaging.CloudMessagingPort",
        description = "Cloud messaging (FCM/SignalR)"
    ),

    /** Analytics — usage tracking, crash breadcrumbs */
    Analytics(
        portInterface = "com.thewatch.app.data.analytics.AnalyticsPort",
        description = "Usage analytics and crash breadcrumbs"
    ),

    /** Crash reporting — Crashlytics / Sentry */
    CrashReporting(
        portInterface = "com.thewatch.app.data.crashreporting.CrashReportingPort",
        description = "Crash reporting (Crashlytics)"
    ),

    /** Map service — Google Maps / Mapbox */
    MapService(
        portInterface = "com.thewatch.app.data.maps.MapServicePort",
        description = "Map rendering and geocoding"
    ),

    /** Security — biometric auth, device integrity, cert pinning */
    Security(
        portInterface = "com.thewatch.app.data.security.BiometricAuthPort",
        description = "Biometric auth and device integrity"
    ),

    /** Evidence capture — photos, video, audio recordings, sitreps */
    Evidence(
        portInterface = "com.thewatch.app.data.evidence.EvidencePort",
        description = "Evidence capture and tamper detection"
    ),

    /** BLE — wearable beacon proximity */
    BLE(
        portInterface = "com.thewatch.app.data.ble.BleBeaconPort",
        description = "Bluetooth LE wearable beacons"
    ),

    /** Health/vitals — heart rate, step count from wearables */
    Health(
        portInterface = "com.thewatch.app.data.health.HealthPort",
        description = "Health vitals from wearables"
    ),

    /** Data export — GDPR, evidence bundles */
    DataExport(
        portInterface = "com.thewatch.app.data.export.DataExportPort",
        description = "Data export (GDPR, evidence bundles)"
    );

    companion object {
        /**
         * Parse a slot name from SignalR message or dashboard command.
         * Case-insensitive. Returns null for unrecognized values.
         */
        fun fromString(value: String): AdapterSlot? {
            return entries.firstOrNull { it.name.equals(value, ignoreCase = true) }
        }
    }
}
