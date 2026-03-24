// =============================================================================
// WRITE-AHEAD LOG
// =============================================================================
// File:        ResponderMapMarker.swift
// Purpose:     MapKit Annotation view for rendering nearby responders on the
//              TheWatch emergency map. Displays responder name, ETA badge, and
//              a status icon (available / responding / off-duty). Green-tinted
//              marker body so responders are visually distinct from alerts and
//              shelters.
// Date:        2026-03-24
// Author:      Claude (Opus 4.6)
// Deps:        SwiftUI, MapKit, Models/Responder.swift
// iOS Target:  17.0+
//
// Usage Example:
//   Map {
//       ForEach(responders) { responder in
//           ResponderMapMarker(responder: responder)
//       }
//   }
//
// Architecture Notes:
//   - Pure SwiftUI view, no external state dependencies.
//   - ETA is derived from Responder.responseTime (seconds).
//   - Color intensity changes with ResponderStatus:
//       .available  -> bright green
//       .onCall     -> amber/yellow (actively responding)
//       .offDuty    -> gray
//   - Accessibility labels include name, status, and ETA.
//
// Potential Enhancements (not yet implemented):
//   - Animated pulsing ring when status == .onCall (responding).
//   - Tap-to-expand detail card with skills list.
//   - Real-time ETA recalculation via MKDirections.
//   - Clustering via MKClusterAnnotation for dense responder areas.
//   - Integration with Apple Watch complication for nearest responder.
// =============================================================================

import SwiftUI
import MapKit

// MARK: - ResponderMapMarker

/// A MapKit `Annotation` content view that renders a single nearby responder
/// with name label, ETA badge, and status icon.
///
/// Drop this inside a `Map { }` content builder using the `Annotation` wrapper:
/// ```swift
/// Annotation("", coordinate: coord) {
///     ResponderMapMarker(responder: responder)
/// }
/// ```
/// Or use the convenience `MapContent` builder below.
struct ResponderMapMarker: View {

    // MARK: - Properties

    let responder: Responder

    /// Whether the marker is in a compact mode (zoomed-out map).
    /// When compact, the name label and ETA are hidden; only the icon dot shows.
    var compact: Bool = false

    // MARK: - Derived

    /// Human-readable ETA string, e.g. "3 min", "< 1 min".
    private var etaText: String {
        guard let seconds = responder.responseTime else { return "N/A" }
        let minutes = Int(seconds / 60)
        if minutes < 1 { return "< 1 min" }
        if minutes < 60 { return "\(minutes) min" }
        let hours = minutes / 60
        let remainingMin = minutes % 60
        return "\(hours)h \(remainingMin)m"
    }

    /// Primary tint color driven by responder status.
    private var statusColor: Color {
        switch responder.status {
        case .available:
            return .green
        case .onCall:
            return Color(red: 1.0, green: 0.75, blue: 0.0) // amber
        case .offDuty:
            return .gray
        }
    }

    /// SF Symbol name for the status icon overlay.
    private var statusIconName: String {
        switch responder.status {
        case .available:
            return "checkmark.circle.fill"
        case .onCall:
            return "arrow.triangle.turn.up.right.circle.fill"
        case .offDuty:
            return "moon.circle.fill"
        }
    }

    /// SF Symbol for the responder role badge.
    private var roleIconName: String {
        switch responder.role {
        case .volunteer:
            return "person.fill"
        case .emt:
            return "cross.case.fill"
        case .firefighter:
            return "flame.fill"
        case .police:
            return "shield.lefthalf.filled"
        case .medical:
            return "stethoscope"
        }
    }

    // MARK: - Body

    var body: some View {
        if compact {
            compactBody
        } else {
            fullBody
        }
    }

    // MARK: - Full Layout

    /// Full annotation: icon + name + ETA badge.
    private var fullBody: some View {
        VStack(spacing: 0) {
            // --- Callout bubble ---
            VStack(spacing: 4) {
                // Name row
                Text(responder.name.isEmpty ? "Responder" : responder.name)
                    .font(.caption2)
                    .fontWeight(.semibold)
                    .foregroundColor(.primary)
                    .lineLimit(1)

                // ETA badge
                HStack(spacing: 3) {
                    Image(systemName: "clock.fill")
                        .font(.system(size: 8))
                    Text(etaText)
                        .font(.system(size: 9, weight: .medium))
                }
                .foregroundColor(.white)
                .padding(.horizontal, 6)
                .padding(.vertical, 2)
                .background(statusColor)
                .cornerRadius(4)
            }
            .padding(6)
            .background(Color(.systemBackground))
            .cornerRadius(8)
            .shadow(color: .black.opacity(0.15), radius: 3, x: 0, y: 2)

            // --- Pin triangle ---
            Triangle()
                .fill(Color(.systemBackground))
                .frame(width: 12, height: 6)
                .shadow(color: .black.opacity(0.08), radius: 1, x: 0, y: 1)

            // --- Marker circle ---
            ZStack {
                Circle()
                    .fill(statusColor.opacity(0.2))
                    .frame(width: 32, height: 32)

                Circle()
                    .fill(statusColor)
                    .frame(width: 24, height: 24)

                Image(systemName: roleIconName)
                    .font(.system(size: 12, weight: .bold))
                    .foregroundColor(.white)

                // Status badge (bottom-right)
                Image(systemName: statusIconName)
                    .font(.system(size: 10))
                    .foregroundColor(statusColor)
                    .background(
                        Circle()
                            .fill(Color(.systemBackground))
                            .frame(width: 14, height: 14)
                    )
                    .offset(x: 10, y: 10)
            }
        }
        .accessibilityElement(children: .combine)
        .accessibilityLabel("\(responder.name), \(responder.role.rawValue), \(responder.status.rawValue), ETA \(etaText)")
    }

    // MARK: - Compact Layout

    /// Minimal dot marker for zoomed-out display.
    private var compactBody: some View {
        ZStack {
            Circle()
                .fill(statusColor.opacity(0.3))
                .frame(width: 18, height: 18)

            Circle()
                .fill(statusColor)
                .frame(width: 12, height: 12)

            Image(systemName: roleIconName)
                .font(.system(size: 7, weight: .bold))
                .foregroundColor(.white)
        }
        .accessibilityLabel("\(responder.name), \(responder.status.rawValue)")
    }
}

// MARK: - Triangle Shape

/// Tiny downward-pointing triangle used as the callout pin tip.
private struct Triangle: Shape {
    func path(in rect: CGRect) -> Path {
        var path = Path()
        path.move(to: CGPoint(x: rect.midX, y: rect.maxY))
        path.addLine(to: CGPoint(x: rect.minX, y: rect.minY))
        path.addLine(to: CGPoint(x: rect.maxX, y: rect.minY))
        path.closeSubpath()
        return path
    }
}

// MARK: - MapContent Convenience

/// Drop-in `MapContent` that renders a collection of responders as annotations.
///
/// Usage:
/// ```swift
/// Map {
///     ResponderAnnotations(responders: viewModel.nearbyResponders)
/// }
/// ```
struct ResponderAnnotations: MapContent {
    let responders: [Responder]
    var compact: Bool = false

    var body: some MapContent {
        ForEach(responders) { responder in
            Annotation(
                "",
                coordinate: CLLocationCoordinate2D(
                    latitude: responder.latitude,
                    longitude: responder.longitude
                )
            ) {
                ResponderMapMarker(responder: responder, compact: compact)
            }
        }
    }
}

// MARK: - Preview

#Preview("Full Marker") {
    Map {
        Annotation(
            "",
            coordinate: CLLocationCoordinate2D(latitude: 37.7749, longitude: -122.4194)
        ) {
            ResponderMapMarker(
                responder: Responder(
                    id: "resp-preview-001",
                    name: "Sarah Chen",
                    role: .emt,
                    latitude: 37.7749,
                    longitude: -122.4194,
                    distance: 350,
                    skills: ["CPR", "AED", "First Aid"],
                    isVerified: true,
                    responseTime: 180,
                    status: .available,
                    availability: .available
                )
            )
        }
    }
}

#Preview("Compact Marker") {
    Map {
        Annotation(
            "",
            coordinate: CLLocationCoordinate2D(latitude: 37.7749, longitude: -122.4194)
        ) {
            ResponderMapMarker(
                responder: Responder(
                    id: "resp-preview-002",
                    name: "Marcus Lee",
                    role: .firefighter,
                    latitude: 37.7749,
                    longitude: -122.4194,
                    distance: 800,
                    responseTime: 420,
                    status: .onCall
                ),
                compact: true
            )
        }
    }
}
