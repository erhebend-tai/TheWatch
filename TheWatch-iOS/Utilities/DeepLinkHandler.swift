// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         DeepLinkHandler.swift
// Purpose:      Handles deep link URL parsing and routing for TheWatch iOS app.
//               Supports the custom URL scheme `thewatch://` for responding to
//               emergency actions, opening specific screens, and handling
//               external triggers (e.g., push notification actions, widget taps,
//               partner app integrations).
// Created:      2026-03-24
// Author:       Claude
// Dependencies: Foundation, AppRouter.swift
// Related:      TheWatchApp.swift (wires .onOpenURL),
//               AppRouter.swift (navigation destinations),
//               NotificationService.swift (notification action deep links)
//
// Usage Example:
//   // In TheWatchApp.swift:
//   .onOpenURL { url in
//       if let destination = DeepLinkHandler.shared.handle(url: url) {
//           router.navigateTo(destination)
//       }
//   }
//
//   // Supported URL Schemes:
//   //   thewatch://response/accept     -> Navigate to active alert acceptance
//   //   thewatch://response/decline    -> Decline alert response
//   //   thewatch://response/checkin    -> Open check-in flow
//   //   thewatch://sos                 -> Trigger SOS (guest or authenticated)
//   //   thewatch://profile             -> Open profile screen
//   //   thewatch://history             -> Open history screen
//   //   thewatch://history/{uuid}      -> Open specific history detail
//   //   thewatch://settings            -> Open settings screen
//   //   thewatch://volunteer           -> Open volunteering screen
//   //   thewatch://consent/{requestId} -> Open guardian consent verification
//   //   thewatch://login               -> Open login screen
//   //   thewatch://signup              -> Open signup screen
//   //   thewatch://reset-password/{email} -> Open password reset with email
//
// Info.plist Configuration Required:
//   <key>CFBundleURLTypes</key>
//   <array>
//       <dict>
//           <key>CFBundleURLName</key>
//           <string>com.thewatch.ios</string>
//           <key>CFBundleURLSchemes</key>
//           <array>
//               <string>thewatch</string>
//           </array>
//       </dict>
//   </array>
//
// Universal Links (future):
//   - Associated Domains: applinks:app.thewatch.com
//   - apple-app-site-association on server
//   - Pattern: https://app.thewatch.com/response/{action}
//
// Potential Additions:
//   - Universal Links (HTTPS) for web-to-app handoff
//   - App Clip invocation URLs
//   - Spotlight/Siri Shortcut integration
//   - Widget deep link targets
//   - Handoff / Continuity deep links
//   - NFC tag URL handling
//   - QR code scanned URL routing
// ============================================================================

import Foundation

// MARK: - Deep Link Action

/// Parsed deep link actions for TheWatch.
enum DeepLinkAction: Equatable, Sendable {
    // Response actions (from notifications or external triggers)
    case responseAccept
    case responseDecline
    case responseCheckIn

    // Direct navigation
    case sos
    case guestSOS
    case profile
    case history
    case historyDetail(UUID)
    case settings
    case volunteering
    case contacts
    case evacuation
    case health
    case notifications
    case permissions

    // Auth flows
    case login
    case signup
    case forgotPassword
    case resetPassword(email: String)
    case guardianConsent(requestId: String)
    case eula

    /// Convert this action to an AppRouter.Destination for navigation.
    var routerDestination: AppRouter.Destination? {
        switch self {
        case .responseAccept, .responseDecline, .responseCheckIn:
            return .home // Response actions route to home where alert handling lives
        case .sos, .guestSOS:
            return .guestSOS
        case .profile:
            return .profile
        case .history:
            return .history
        case .historyDetail(let id):
            return .historyDetail(id)
        case .settings:
            return .settings
        case .volunteering:
            return .volunteering
        case .contacts:
            return .contacts
        case .evacuation:
            return .evacuation
        case .health:
            return .health
        case .notifications:
            return .notifications
        case .permissions:
            return .permissions
        case .login:
            return .login
        case .signup:
            return .signup
        case .forgotPassword:
            return .forgotPassword
        case .resetPassword(let email):
            return .resetPassword(email)
        case .guardianConsent:
            return .guardianConsent
        case .eula:
            return .eula
        }
    }
}

// MARK: - Deep Link Handler

/// Singleton handler for parsing and routing deep link URLs.
/// Thread-safe and Sendable-compliant.
final class DeepLinkHandler: Sendable {

    /// Shared singleton instance
    static let shared = DeepLinkHandler()

    /// The expected URL scheme
    private let scheme = "thewatch"

    private init() {}

    // MARK: - URL Parsing

    /// Parse a deep link URL and return the corresponding action.
    /// - Parameter url: The incoming URL (e.g., thewatch://response/accept)
    /// - Returns: The parsed `DeepLinkAction`, or nil if the URL is unrecognized
    func parse(url: URL) -> DeepLinkAction? {
        guard url.scheme?.lowercased() == scheme else {
            print("[DeepLinkHandler] Unrecognized scheme: \(url.scheme ?? "nil")")
            return nil
        }

        let host = url.host?.lowercased() ?? ""
        let pathComponents = url.pathComponents.filter { $0 != "/" }

        print("[DeepLinkHandler] Parsing URL: \(url.absoluteString)")
        print("[DeepLinkHandler] Host: \(host), Path: \(pathComponents)")

        switch host {
        // ── Response Actions ─────────────────────────────────────────
        case "response":
            guard let action = pathComponents.first?.lowercased() else {
                print("[DeepLinkHandler] Missing response action in URL")
                return nil
            }
            switch action {
            case "accept":
                return .responseAccept
            case "decline":
                return .responseDecline
            case "checkin", "check-in":
                return .responseCheckIn
            default:
                print("[DeepLinkHandler] Unknown response action: \(action)")
                return nil
            }

        // ── SOS ──────────────────────────────────────────────────────
        case "sos":
            return .sos
        case "guest-sos", "guestsos":
            return .guestSOS

        // ── Navigation ───────────────────────────────────────────────
        case "profile":
            return .profile
        case "history":
            if let idString = pathComponents.first, let uuid = UUID(uuidString: idString) {
                return .historyDetail(uuid)
            }
            return .history
        case "settings":
            return .settings
        case "volunteer", "volunteering":
            return .volunteering
        case "contacts":
            return .contacts
        case "evacuation":
            return .evacuation
        case "health":
            return .health
        case "notifications":
            return .notifications
        case "permissions":
            return .permissions

        // ── Auth ─────────────────────────────────────────────────────
        case "login":
            return .login
        case "signup", "sign-up":
            return .signup
        case "forgot-password":
            return .forgotPassword
        case "reset-password":
            if let email = pathComponents.first {
                return .resetPassword(email: email)
            }
            return .forgotPassword
        case "consent", "guardian-consent":
            if let requestId = pathComponents.first {
                return .guardianConsent(requestId: requestId)
            }
            return nil
        case "eula":
            return .eula

        default:
            print("[DeepLinkHandler] Unrecognized deep link host: \(host)")
            return nil
        }
    }

    /// Convenience: Parse URL and return an AppRouter.Destination directly.
    /// - Parameter url: The incoming URL
    /// - Returns: The router destination, or nil if unrecognized
    func handle(url: URL) -> AppRouter.Destination? {
        guard let action = parse(url: url) else { return nil }
        let destination = action.routerDestination
        print("[DeepLinkHandler] Routed to: \(String(describing: destination))")
        return destination
    }

    // MARK: - URL Construction (for outgoing deep links)

    /// Construct a deep link URL for a given action.
    /// Useful for creating notification payloads, widget URLs, etc.
    /// - Parameter action: The action to encode
    /// - Returns: The constructed URL, or nil if invalid
    static func url(for action: DeepLinkAction) -> URL? {
        let base = "thewatch://"
        let path: String

        switch action {
        case .responseAccept:
            path = "response/accept"
        case .responseDecline:
            path = "response/decline"
        case .responseCheckIn:
            path = "response/checkin"
        case .sos:
            path = "sos"
        case .guestSOS:
            path = "guest-sos"
        case .profile:
            path = "profile"
        case .history:
            path = "history"
        case .historyDetail(let id):
            path = "history/\(id.uuidString)"
        case .settings:
            path = "settings"
        case .volunteering:
            path = "volunteering"
        case .contacts:
            path = "contacts"
        case .evacuation:
            path = "evacuation"
        case .health:
            path = "health"
        case .notifications:
            path = "notifications"
        case .permissions:
            path = "permissions"
        case .login:
            path = "login"
        case .signup:
            path = "signup"
        case .forgotPassword:
            path = "forgot-password"
        case .resetPassword(let email):
            path = "reset-password/\(email)"
        case .guardianConsent(let requestId):
            path = "consent/\(requestId)"
        case .eula:
            path = "eula"
        }

        return URL(string: base + path)
    }
}
