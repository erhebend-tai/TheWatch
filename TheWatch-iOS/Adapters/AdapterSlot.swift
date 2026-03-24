import Foundation

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: AdapterSlot enumerates every swappable adapter in the mobile app.
// Each slot maps to a protocol (port). The AdapterRegistry tracks which
// AdapterTier is active for each slot. The MAUI dashboard's
// AdapterTierController sends SignalR messages keyed by slot name.
//
// When adding a new port/adapter pair to the app:
//   1. Add a new case here
//   2. Register the Mock/Native/Live implementations in AdapterRegistry
//   3. Update TheWatchApp.swift to resolve through the registry
//
// Example:
// ```swift
// let slot = AdapterSlot.fromString("logging") // .logging
// let desc = AdapterSlot.sos.description        // "SOS alerting and NG911 integration"
// ```

/// Enumeration of all adapter slots in the mobile application.
///
/// Each slot corresponds to a protocol (port) interface. The `AdapterRegistry`
/// maintains a tier mapping for each slot, enabling runtime hot-switching
/// between Mock, Native, Live, and Disabled implementations.
///
/// Slot names are stable across platforms (Android, iOS, Aspire) so the MAUI
/// dashboard can command tier changes by name via SignalR.
enum AdapterSlot: String, Codable, CaseIterable, Sendable {
    /// Structured logging — LoggingPort
    case logging = "Logging"
    /// Log sync to Firestore — LogSyncPort
    case logSync = "LogSync"
    /// GPS/location services — LocationManager
    case location = "Location"
    /// Push notifications — APNs
    case notifications = "Notifications"
    /// SOS/emergency — NG911Service + ImplicitDetection
    case sos = "SOS"
    /// Contact/guardian management
    case contacts = "Contacts"
    /// Audio capture for phrase detection + evidence recording
    case audio = "Audio"
    /// Telephony — emergency dialing
    case telephony = "Telephony"
    /// AI/ML — phrase matching, threat assessment, anomaly detection
    case ai = "AI"
    /// Cloud messaging — APNs / SignalR
    case cloudMessaging = "CloudMessaging"
    /// Analytics — usage tracking, crash breadcrumbs
    case analytics = "Analytics"
    /// Crash reporting — Crashlytics / Sentry
    case crashReporting = "CrashReporting"
    /// Map service — MapKit / Mapbox
    case mapService = "MapService"
    /// Security — biometric auth, device integrity, cert pinning
    case security = "Security"
    /// Evidence capture — photos, video, audio recordings, sitreps
    case evidence = "Evidence"
    /// BLE — wearable beacon proximity
    case ble = "BLE"
    /// Health/vitals — HealthKit data from Apple Watch
    case health = "Health"
    /// Data export — GDPR, evidence bundles
    case dataExport = "DataExport"

    /// Human-readable description for dashboard UI.
    var description: String {
        switch self {
        case .logging:         return "Structured logging (Serilog-compatible)"
        case .logSync:         return "Log synchronization to Firestore"
        case .location:        return "GPS and location services"
        case .notifications:   return "Push notification delivery (APNs)"
        case .sos:             return "SOS alerting and NG911 integration"
        case .contacts:        return "Contact and guardian management"
        case .audio:           return "Audio capture (phrase detection, evidence)"
        case .telephony:       return "Telephony and emergency dialing"
        case .ai:              return "AI/ML inference (phrase matching, threat detection)"
        case .cloudMessaging:  return "Cloud messaging (APNs/SignalR)"
        case .analytics:       return "Usage analytics and crash breadcrumbs"
        case .crashReporting:  return "Crash reporting (Crashlytics)"
        case .mapService:      return "Map rendering and geocoding"
        case .security:        return "Biometric auth and device integrity"
        case .evidence:        return "Evidence capture and tamper detection"
        case .ble:             return "Bluetooth LE wearable beacons"
        case .health:          return "Health vitals from Apple Watch"
        case .dataExport:      return "Data export (GDPR, evidence bundles)"
        }
    }

    /// Parse a slot name from SignalR message or dashboard command.
    /// Case-insensitive. Returns nil for unrecognized values.
    static func fromString(_ value: String) -> AdapterSlot? {
        AdapterSlot.allCases.first { $0.rawValue.caseInsensitiveCompare(value) == .orderedSame }
    }
}
