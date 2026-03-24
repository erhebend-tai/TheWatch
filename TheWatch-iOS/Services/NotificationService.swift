// ===========================================================================
// NotificationService — APNs push notification handler for TheWatch iOS.
// ===========================================================================
// Handles inbound push notifications and presents actionable notifications
// with Accept/Decline, I'm OK/Need Help, or Acknowledge/Need Assistance.
//
// Notification categories (matching C# NotificationCategory enum):
//   SOS_DISPATCH       → Accept / Decline
//   ESCALATION_ALERT   → Accept / Decline / Call 911
//   CHECK_IN_REQUEST   → I'm OK / Need Help
//   EVACUATION_NOTICE  → Acknowledged / Need Assistance
//   SOS_CANCELLED      → Dismiss
//   SOS_RESOLVED       → View Summary
//
// Response flow:
//   User taps action → UNNotificationAction
//     → NotificationService.handleActionResponse()
//     → NotificationResponseHandler.sendAccept/Decline/etc.
//     → POST to Dashboard API
//
// IMPORTANT: Audio NEVER leaves the device. Notifications contain zero audio data.
// ===========================================================================

import Foundation
import UserNotifications
import UIKit
import Combine

@Observable
final class NotificationService: NSObject {

    static let shared = NotificationService()

    // ── Notification Categories ──────────────────────────────────────────
    static let categorySosDispatch = "SOS_DISPATCH"
    static let categoryEscalation = "ESCALATION_ALERT"
    static let categoryCheckIn = "CHECK_IN_REQUEST"
    static let categoryEvacuation = "EVACUATION_NOTICE"
    static let categorySosCancelled = "SOS_CANCELLED"
    static let categorySosResolved = "SOS_RESOLVED"

    // ── Action Identifiers ───────────────────────────────────────────────
    static let actionAccept = "ACCEPT"
    static let actionDecline = "DECLINE"
    static let actionImOk = "IM_OK"
    static let actionNeedHelp = "NEED_HELP"
    static let actionCall911 = "CALL_911"
    static let actionAcknowledge = "ACKNOWLEDGE"

    // ── State ────────────────────────────────────────────────────────────
    private(set) var deviceToken: String?
    private(set) var isRegistered = false
    private(set) var lastNotificationPayload: [String: Any]?

    private let responseHandler = NotificationResponseHandler.shared

    // Publishers for downstream consumers
    let sosDispatchReceived = PassthroughSubject<SOSNotificationPayload, Never>()
    let notificationResponseSent = PassthroughSubject<(String, String), Never>() // (requestId, action)

    private override init() {
        super.init()
    }

    // ── Setup ────────────────────────────────────────────────────────────

    /// Call once from AppDelegate.didFinishLaunchingWithOptions or TheWatchApp.init
    func configure() {
        UNUserNotificationCenter.current().delegate = self
        registerNotificationCategories()
        requestAuthorization()
    }

    private func requestAuthorization() {
        UNUserNotificationCenter.current().requestAuthorization(
            options: [.alert, .badge, .sound, .criticalAlert, .providesAppNotificationSettings]
        ) { granted, error in
            if granted {
                DispatchQueue.main.async {
                    UIApplication.shared.registerForRemoteNotifications()
                }
                print("[NotifService] Push authorization granted")
            } else {
                print("[NotifService] Push authorization denied: \(error?.localizedDescription ?? "unknown")")
            }
        }
    }

    // ── Category Registration ────────────────────────────────────────────

    private func registerNotificationCategories() {

        // SOS Dispatch: Accept / Decline
        let sosDispatch = UNNotificationCategory(
            identifier: Self.categorySosDispatch,
            actions: [
                UNNotificationAction(
                    identifier: Self.actionAccept,
                    title: "Accept",
                    options: [.foreground]
                ),
                UNNotificationAction(
                    identifier: Self.actionDecline,
                    title: "Decline",
                    options: [.destructive]
                )
            ],
            intentIdentifiers: [],
            options: [.customDismissAction]
        )

        // Escalation: Accept / Decline / Call 911
        let escalation = UNNotificationCategory(
            identifier: Self.categoryEscalation,
            actions: [
                UNNotificationAction(
                    identifier: Self.actionAccept,
                    title: "Accept",
                    options: [.foreground]
                ),
                UNNotificationAction(
                    identifier: Self.actionDecline,
                    title: "Decline",
                    options: [.destructive]
                ),
                UNNotificationAction(
                    identifier: Self.actionCall911,
                    title: "Call 911",
                    options: [.foreground]
                )
            ],
            intentIdentifiers: [],
            options: [.customDismissAction]
        )

        // Check-In: I'm OK / Need Help
        let checkIn = UNNotificationCategory(
            identifier: Self.categoryCheckIn,
            actions: [
                UNNotificationAction(
                    identifier: Self.actionImOk,
                    title: "I'm OK",
                    options: []
                ),
                UNNotificationAction(
                    identifier: Self.actionNeedHelp,
                    title: "Need Help",
                    options: [.foreground, .destructive]
                )
            ],
            intentIdentifiers: [],
            options: [.customDismissAction]
        )

        // Evacuation: Acknowledged / Need Assistance
        let evacuation = UNNotificationCategory(
            identifier: Self.categoryEvacuation,
            actions: [
                UNNotificationAction(
                    identifier: Self.actionAcknowledge,
                    title: "Acknowledged",
                    options: []
                ),
                UNNotificationAction(
                    identifier: Self.actionNeedHelp,
                    title: "Need Assistance",
                    options: [.foreground, .destructive]
                )
            ],
            intentIdentifiers: [],
            options: [.customDismissAction]
        )

        UNUserNotificationCenter.current().setNotificationCategories([
            sosDispatch, escalation, checkIn, evacuation
        ])
    }

    // ── Token Management ─────────────────────────────────────────────────

    func didRegisterForRemoteNotifications(deviceToken: Data) {
        let token = deviceToken.map { String(format: "%02.2hhx", $0) }.joined()
        self.deviceToken = token
        self.isRegistered = true
        print("[NotifService] APNs token: \(token.prefix(20))...")

        // Register with backend
        Task {
            await responseHandler.registerDeviceToken(token, platform: "iOS")
        }
    }

    func didFailToRegisterForRemoteNotifications(error: Error) {
        print("[NotifService] Failed to register for APNs: \(error.localizedDescription)")
        self.isRegistered = false
    }

    // ── Handle Action Responses ──────────────────────────────────────────

    private func handleActionResponse(
        actionIdentifier: String,
        userInfo: [AnyHashable: Any]
    ) {
        guard let requestId = userInfo["request_id"] as? String else {
            print("[NotifService] No request_id in notification payload")
            return
        }

        let notificationId = userInfo["notification_id"] as? String ?? requestId
        let latitude = userInfo["latitude"] as? Double ?? 0.0
        let longitude = userInfo["longitude"] as? Double ?? 0.0

        print("[NotifService] Action: \(actionIdentifier) for request: \(requestId)")

        Task {
            switch actionIdentifier {
            case Self.actionAccept:
                await responseHandler.sendAccept(
                    requestId: requestId,
                    notificationId: notificationId,
                    incidentLatitude: latitude,
                    incidentLongitude: longitude
                )
                notificationResponseSent.send((requestId, "ACCEPT"))

            case Self.actionDecline:
                await responseHandler.sendDecline(
                    requestId: requestId,
                    notificationId: notificationId
                )
                notificationResponseSent.send((requestId, "DECLINE"))

            case Self.actionImOk:
                await responseHandler.sendImOk(
                    requestId: requestId,
                    notificationId: notificationId
                )
                notificationResponseSent.send((requestId, "IM_OK"))

            case Self.actionNeedHelp:
                await responseHandler.sendNeedHelp(
                    requestId: requestId,
                    notificationId: notificationId,
                    latitude: latitude,
                    longitude: longitude
                )
                notificationResponseSent.send((requestId, "NEED_HELP"))

            case Self.actionCall911:
                await responseHandler.sendCall911(
                    requestId: requestId,
                    notificationId: notificationId
                )
                notificationResponseSent.send((requestId, "CALL_911"))
                // Open phone dialer
                if let url = URL(string: "tel://911") {
                    await UIApplication.shared.open(url)
                }

            case Self.actionAcknowledge:
                await responseHandler.sendAcknowledge(
                    requestId: requestId,
                    notificationId: notificationId
                )
                notificationResponseSent.send((requestId, "ACKNOWLEDGE"))

            default:
                print("[NotifService] Unknown action: \(actionIdentifier)")
            }
        }
    }
}

// ═══════════════════════════════════════════════════════════════
// UNUserNotificationCenterDelegate
// ═══════════════════════════════════════════════════════════════

extension NotificationService: UNUserNotificationCenterDelegate {

    /// Called when notification is received while app is in foreground.
    /// Show it as a banner — don't suppress it.
    func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        willPresent notification: UNNotification,
        withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void
    ) {
        let userInfo = notification.request.content.userInfo
        lastNotificationPayload = userInfo as? [String: Any]

        let category = notification.request.content.categoryIdentifier
        print("[NotifService] Foreground notification: \(category)")

        // Parse and publish SOS dispatch for in-app handling
        if category == Self.categorySosDispatch || category == Self.categoryEscalation {
            if let payload = parseSOSPayload(userInfo) {
                sosDispatchReceived.send(payload)
            }
        }

        // Show as banner + sound even when app is in foreground
        completionHandler([.banner, .sound, .badge, .list])
    }

    /// Called when user taps on a notification or an action button.
    func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        didReceive response: UNNotificationResponse,
        withCompletionHandler completionHandler: @escaping () -> Void
    ) {
        let actionIdentifier = response.actionIdentifier
        let userInfo = response.notification.request.content.userInfo

        if actionIdentifier == UNNotificationDefaultActionIdentifier {
            // User tapped the notification itself — open response detail
            if let requestId = userInfo["request_id"] as? String {
                print("[NotifService] Tap-to-open for request: \(requestId)")
                // Navigate to response detail via deep link
                NotificationCenter.default.post(
                    name: .navigateToResponse,
                    object: nil,
                    userInfo: ["requestId": requestId]
                )
            }
        } else if actionIdentifier != UNNotificationDismissActionIdentifier {
            // User tapped an action button
            handleActionResponse(
                actionIdentifier: actionIdentifier,
                userInfo: userInfo
            )
        }

        completionHandler()
    }

    private func parseSOSPayload(_ userInfo: [AnyHashable: Any]) -> SOSNotificationPayload? {
        guard let requestId = userInfo["request_id"] as? String else { return nil }

        return SOSNotificationPayload(
            requestId: requestId,
            requestorName: userInfo["requestor_name"] as? String,
            scope: userInfo["scope"] as? String,
            latitude: userInfo["latitude"] as? Double ?? 0.0,
            longitude: userInfo["longitude"] as? Double ?? 0.0,
            distanceMeters: userInfo["distance_meters"] as? Double,
            title: userInfo["title"] as? String ?? "Emergency Alert",
            body: userInfo["body"] as? String ?? "Someone needs help"
        )
    }
}

// ═══════════════════════════════════════════════════════════════
// Supporting Types
// ═══════════════════════════════════════════════════════════════

struct SOSNotificationPayload {
    let requestId: String
    let requestorName: String?
    let scope: String?
    let latitude: Double
    let longitude: Double
    let distanceMeters: Double?
    let title: String
    let body: String
}

extension Notification.Name {
    static let navigateToResponse = Notification.Name("navigateToResponse")
}
