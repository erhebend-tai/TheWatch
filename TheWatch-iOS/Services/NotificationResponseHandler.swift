// ===========================================================================
// NotificationResponseHandler — sends user's Accept/Decline responses to backend.
// ===========================================================================
// Handles outbound responses from notification actions back to the Dashboard API.
// All methods are async and designed to be called from Tasks.
//
// In the current mock implementation, responses are logged.
// Live implementation will POST to the ResponseController endpoints.
// ===========================================================================

import Foundation
import CoreLocation

@Observable
final class NotificationResponseHandler {

    static let shared = NotificationResponseHandler()
    private let locationCoordinator = LocationCoordinator.shared

    private init() {}

    // ── Accept ───────────────────────────────────────────────────────────

    /// Accept an SOS dispatch — "I'm on my way."
    /// Escalates location to emergency mode and sends acknowledgment to backend.
    func sendAccept(
        requestId: String,
        notificationId: String,
        incidentLatitude: Double,
        incidentLongitude: Double
    ) async {
        print("[ResponseHandler] ACCEPT for \(requestId)")

        // Escalate location tracking
        locationCoordinator.escalateToEmergency()

        let location = locationCoordinator.currentLocation
        let distance = calculateDistance(
            from: location,
            toLat: incidentLatitude,
            toLon: incidentLongitude
        )

        // TODO: Replace with actual URLSession POST in live implementation
        // POST /api/response/{requestId}/ack
        print("[ResponseHandler] [MOCK] POST /api/response/\(requestId)/ack — distance=\(String(format: "%.0f", distance))m")
    }

    // ── Decline ──────────────────────────────────────────────────────────

    /// Decline an SOS dispatch — "I can't help right now."
    func sendDecline(requestId: String, notificationId: String) async {
        print("[ResponseHandler] DECLINE for \(requestId)")
        print("[ResponseHandler] [MOCK] Decline recorded")
    }

    // ── I'm OK ───────────────────────────────────────────────────────────

    /// Respond "I'm OK" to a check-in request.
    func sendImOk(requestId: String, notificationId: String) async {
        print("[ResponseHandler] IM_OK for \(requestId)")
        print("[ResponseHandler] [MOCK] I'm OK recorded")
    }

    // ── Need Help ────────────────────────────────────────────────────────

    /// Respond "Need Help" — triggers a secondary SOS.
    func sendNeedHelp(
        requestId: String,
        notificationId: String,
        latitude: Double,
        longitude: Double
    ) async {
        print("[ResponseHandler] NEED HELP for \(requestId) — triggering secondary SOS")

        // Escalate our own location
        locationCoordinator.escalateToEmergency()

        // TODO: POST /api/response/trigger with scope=Neighborhood
        print("[ResponseHandler] [MOCK] Secondary SOS triggered from check-in")
    }

    // ── Call 911 ─────────────────────────────────────────────────────────

    /// Log 911 escalation. Phone dialer is launched by NotificationService.
    func sendCall911(requestId: String, notificationId: String) async {
        print("[ResponseHandler] CALL 911 for \(requestId)")
        print("[ResponseHandler] [MOCK] 911 escalation logged")
    }

    // ── Acknowledge ──────────────────────────────────────────────────────

    /// Acknowledge an evacuation notice.
    func sendAcknowledge(requestId: String, notificationId: String) async {
        print("[ResponseHandler] ACKNOWLEDGE for \(requestId)")
        print("[ResponseHandler] [MOCK] Acknowledgment recorded")
    }

    // ── Device Token Registration ────────────────────────────────────────

    /// Register APNs device token with the backend.
    func registerDeviceToken(_ token: String, platform: String) async {
        print("[ResponseHandler] Registering \(platform) token: \(token.prefix(20))...")
        // TODO: POST /api/response/device/register
        print("[ResponseHandler] [MOCK] Device token registered")
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// Haversine distance calculation.
    private func calculateDistance(
        from location: CLLocationCoordinate2D?,
        toLat: Double,
        toLon: Double
    ) -> Double {
        guard let loc = location else { return 0 }

        let r = 6371000.0 // Earth radius meters
        let dLat = (toLat - loc.latitude) * .pi / 180
        let dLon = (toLon - loc.longitude) * .pi / 180
        let a = sin(dLat / 2) * sin(dLat / 2) +
                cos(loc.latitude * .pi / 180) * cos(toLat * .pi / 180) *
                sin(dLon / 2) * sin(dLon / 2)
        let c = 2 * atan2(sqrt(a), sqrt(1 - a))
        return r * c
    }
}
