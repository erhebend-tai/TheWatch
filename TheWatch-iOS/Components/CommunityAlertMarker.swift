// =============================================================================
// WRITE-AHEAD LOG
// =============================================================================
// File:        CommunityAlertMarker.swift
// Purpose:     MapKit Annotation view for rendering community alerts on the
//              TheWatch emergency map. Displays alert type icon (medical,
//              security, wildfire, flood, etc.), severity-based amber tint, and
//              a confidence / responding-count badge.
// Date:        2026-03-24
// Author:      Claude (Opus 4.6)
// Deps:        SwiftUI, MapKit, Models/CommunityAlert.swift, Models/Alert.swift
// iOS Target:  17.0+
//
// Usage Example:
//   Map {
//       ForEach(communityAlerts) { alert in
//           CommunityAlertMarker(alert: alert)
//       }
//   }
//
// Architecture Notes:
//   - Pure SwiftUI view; stateless, driven entirely by CommunityAlert model.
//   - Alert type mapping is determined by alertType string ("Emergency",
//     "Warning", "Information") and parsed keywords in the title/description
//     for specific icons (medical, fire, flood, security).
//   - Amber color palette: background is amber-tinted; severity modulates
//     opacity and border weight.
//   - Responding count badge shows how many community members are active.
//
// Potential Enhancements (not yet implemented):
//   - Animated ripple effect for high-severity active alerts.
//   - Tap-to-expand detail sheet with description, timeline, reporting user.
//   - Time-decay opacity (older alerts fade).
//   - Integration with CommunityAlert.expiresAt for auto-dismiss.
//   - Real-time WebSocket updates for respondingCount changes.
//   - Clustering for dense alert areas with aggregate severity display.
//   - FEMA IPAWS integration for official government alerts.
//   - CAP (Common Alerting Protocol) icon mapping per OASIS standard.
// =============================================================================

import SwiftUI
import MapKit

// MARK: - Alert Category

/// Semantic alert category derived from CommunityAlert fields.
/// Used to pick the correct SF Symbol and accent color.
enum AlertCategory: String, CaseIterable {
    case medical    = "Medical"
    case security   = "Security"
    case wildfire   = "Wildfire"
    case flood      = "Flood"
    case weather    = "Weather"
    case hazmat     = "HazMat"
    case earthquake = "Earthquake"
    case general    = "General"

    /// SF Symbol name for this category.
    var iconName: String {
        switch self {
        case .medical:    return "cross.case.fill"
        case .security:   return "shield.lefthalf.filled"
        case .wildfire:   return "flame.fill"
        case .flood:      return "drop.triangle.fill"
        case .weather:    return "cloud.bolt.rain.fill"
        case .hazmat:     return "hazardsign.fill"
        case .earthquake: return "waveform.path.ecg"
        case .general:    return "exclamationmark.triangle.fill"
        }
    }

    /// Accent tint overlay; all are amber-family per design spec.
    var tintColor: Color {
        switch self {
        case .medical:    return Color(red: 1.0, green: 0.6, blue: 0.0)
        case .security:   return Color(red: 0.9, green: 0.55, blue: 0.0)
        case .wildfire:   return Color(red: 1.0, green: 0.35, blue: 0.0)
        case .flood:      return Color(red: 0.0, green: 0.6, blue: 0.9)
        case .weather:    return Color(red: 0.5, green: 0.5, blue: 0.7)
        case .hazmat:     return Color(red: 0.85, green: 0.65, blue: 0.0)
        case .earthquake: return Color(red: 0.7, green: 0.4, blue: 0.1)
        case .general:    return Color(red: 1.0, green: 0.75, blue: 0.0) // amber
        }
    }

    /// Infer category from a CommunityAlert by scanning title + description.
    static func infer(from alert: CommunityAlert) -> AlertCategory {
        let haystack = "\(alert.title) \(alert.description)".lowercased()
        if haystack.contains("medical") || haystack.contains("injury") || haystack.contains("ambulance") {
            return .medical
        }
        if haystack.contains("fire") || haystack.contains("wildfire") || haystack.contains("blaze") {
            return .wildfire
        }
        if haystack.contains("flood") || haystack.contains("water level") || haystack.contains("flash flood") {
            return .flood
        }
        if haystack.contains("security") || haystack.contains("theft") || haystack.contains("intruder")
            || haystack.contains("suspicious") {
            return .security
        }
        if haystack.contains("weather") || haystack.contains("tornado") || haystack.contains("hurricane")
            || haystack.contains("storm") {
            return .weather
        }
        if haystack.contains("gas") || haystack.contains("hazmat") || haystack.contains("chemical")
            || haystack.contains("toxic") {
            return .hazmat
        }
        if haystack.contains("earthquake") || haystack.contains("seismic") || haystack.contains("tremor") {
            return .earthquake
        }
        return .general
    }
}

// MARK: - CommunityAlertMarker

/// Amber-tinted MapAnnotation pin for community-sourced alerts.
///
/// Usage inside a `Map` content builder:
/// ```swift
/// Map {
///     ForEach(alerts) { alert in
///         Annotation("", coordinate: coord) {
///             CommunityAlertMarker(alert: alert)
///         }
///     }
/// }
/// ```
struct CommunityAlertMarker: View {

    let alert: CommunityAlert

    /// Optional explicit category override. If nil, category is inferred.
    var categoryOverride: AlertCategory? = nil

    // MARK: - Derived

    private var category: AlertCategory {
        categoryOverride ?? AlertCategory.infer(from: alert)
    }

    /// Severity-based border width: critical/high = thicker.
    private var borderWidth: CGFloat {
        switch alert.severity {
        case .critical: return 3
        case .high:     return 2.5
        case .medium:   return 1.5
        case .low:      return 1
        }
    }

    /// Severity-based opacity for outer glow.
    private var glowOpacity: Double {
        switch alert.severity {
        case .critical: return 0.45
        case .high:     return 0.35
        case .medium:   return 0.2
        case .low:      return 0.1
        }
    }

    /// Confidence badge text (responding count).
    private var respondingText: String {
        if alert.respondingCount > 99 { return "99+" }
        return "\(alert.respondingCount)"
    }

    // MARK: - Body

    var body: some View {
        VStack(spacing: 0) {
            // --- Callout ---
            VStack(spacing: 3) {
                // Title
                Text(alert.title.isEmpty ? category.rawValue : alert.title)
                    .font(.system(size: 10, weight: .semibold))
                    .foregroundColor(.primary)
                    .lineLimit(1)

                // Category + responding badge row
                HStack(spacing: 4) {
                    // Category pill
                    HStack(spacing: 2) {
                        Image(systemName: category.iconName)
                            .font(.system(size: 8))
                        Text(category.rawValue)
                            .font(.system(size: 8, weight: .medium))
                    }
                    .foregroundColor(.white)
                    .padding(.horizontal, 5)
                    .padding(.vertical, 2)
                    .background(category.tintColor)
                    .cornerRadius(3)

                    // Responding count badge
                    if alert.respondingCount > 0 {
                        HStack(spacing: 2) {
                            Image(systemName: "person.2.fill")
                                .font(.system(size: 7))
                            Text(respondingText)
                                .font(.system(size: 8, weight: .bold))
                        }
                        .foregroundColor(category.tintColor)
                        .padding(.horizontal, 4)
                        .padding(.vertical, 2)
                        .background(category.tintColor.opacity(0.15))
                        .cornerRadius(3)
                    }
                }

                // Severity indicator
                HStack(spacing: 3) {
                    Circle()
                        .fill(severityColor)
                        .frame(width: 6, height: 6)
                    Text(alert.severity.rawValue)
                        .font(.system(size: 7, weight: .medium))
                        .foregroundColor(.secondary)
                }
            }
            .padding(6)
            .background(Color(.systemBackground))
            .cornerRadius(8)
            .overlay(
                RoundedRectangle(cornerRadius: 8)
                    .stroke(category.tintColor, lineWidth: borderWidth)
            )
            .shadow(color: category.tintColor.opacity(glowOpacity), radius: 6, x: 0, y: 2)

            // --- Pin stem ---
            Rectangle()
                .fill(category.tintColor)
                .frame(width: 2, height: 8)

            // --- Pin dot ---
            ZStack {
                Circle()
                    .fill(category.tintColor.opacity(0.25))
                    .frame(width: 20, height: 20)

                Circle()
                    .fill(category.tintColor)
                    .frame(width: 14, height: 14)

                Image(systemName: category.iconName)
                    .font(.system(size: 8, weight: .bold))
                    .foregroundColor(.white)
            }
        }
        .accessibilityElement(children: .combine)
        .accessibilityLabel("\(alert.title), \(category.rawValue) alert, severity \(alert.severity.rawValue), \(alert.respondingCount) responding")
    }

    // MARK: - Severity Color

    private var severityColor: Color {
        switch alert.severity {
        case .critical: return .red
        case .high:     return .orange
        case .medium:   return .yellow
        case .low:      return .gray
        }
    }
}

// MARK: - MapContent Convenience

/// Drop-in `MapContent` that renders a collection of community alerts.
///
/// Usage:
/// ```swift
/// Map {
///     CommunityAlertAnnotations(alerts: viewModel.communityAlerts)
/// }
/// ```
struct CommunityAlertAnnotations: MapContent {
    let alerts: [CommunityAlert]

    var body: some MapContent {
        ForEach(alerts) { alert in
            Annotation(
                "",
                coordinate: CLLocationCoordinate2D(
                    latitude: alert.latitude,
                    longitude: alert.longitude
                )
            ) {
                CommunityAlertMarker(alert: alert)
            }
        }
    }
}

// MARK: - Preview

#Preview("Medical Alert") {
    Map {
        Annotation(
            "",
            coordinate: CLLocationCoordinate2D(latitude: 37.7749, longitude: -122.4194)
        ) {
            CommunityAlertMarker(
                alert: CommunityAlert(
                    id: "preview-001",
                    title: "Medical Emergency - Market St",
                    description: "Pedestrian injury reported near intersection",
                    alertType: "Emergency",
                    latitude: 37.7749,
                    longitude: -122.4194,
                    radius: 300,
                    severity: .critical,
                    isActive: true,
                    createdBy: "SF Fire Dept",
                    respondingCount: 5
                )
            )
        }
    }
}

#Preview("Gas Leak Alert") {
    Map {
        Annotation(
            "",
            coordinate: CLLocationCoordinate2D(latitude: 37.7849, longitude: -122.4094)
        ) {
            CommunityAlertMarker(
                alert: CommunityAlert(
                    id: "preview-002",
                    title: "Gas Leak on Market Street",
                    description: "Local utility reports gas leak in 3-block radius",
                    alertType: "Emergency",
                    latitude: 37.7849,
                    longitude: -122.4094,
                    radius: 500,
                    severity: .high,
                    respondingCount: 12
                )
            )
        }
    }
}
