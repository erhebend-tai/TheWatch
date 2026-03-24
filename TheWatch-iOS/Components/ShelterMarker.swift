// =============================================================================
// WRITE-AHEAD LOG
// =============================================================================
// File:        ShelterMarker.swift
// Purpose:     MapKit Annotation view for rendering emergency shelters on the
//              TheWatch map. Blue house icon with capacity bar showing
//              currentOccupancy / totalCapacity. Color coded: green when under
//              50% full, yellow 50-80%, red over 80%.
// Date:        2026-03-24
// Author:      Claude (Opus 4.6)
// Deps:        SwiftUI, MapKit, Models/Shelter.swift
// iOS Target:  17.0+
//
// Usage Example:
//   Map {
//       ForEach(shelters) { shelter in
//           Annotation("", coordinate: coord) {
//               ShelterMarker(shelter: shelter)
//           }
//       }
//   }
//
// Architecture Notes:
//   - Pure SwiftUI view; driven entirely by Shelter model properties.
//   - Capacity bar is a simple rectangle fill with percentage width.
//   - Color transitions: green (<50%), yellow (50%-80%), red (>80%).
//   - Shows availability badge (beds remaining), services icons, and
//     accessibility indicator.
//   - Closed shelters display a distinct gray-out treatment.
//
// Potential Enhancements (not yet implemented):
//   - Real-time capacity WebSocket subscription.
//   - Tap-to-expand detail card with phone, hours, services list.
//   - Navigation/routing button (MKDirections to shelter).
//   - Pet-friendly / wheelchair-accessible filter badges.
//   - Shelter reservation system integration.
//   - ARC (American Red Cross) Safe & Well integration.
//   - FEMA NSS (National Shelter System) data feed.
//   - Offline availability data via Core Data cache.
// =============================================================================

import SwiftUI
import MapKit

// MARK: - ShelterMarker

/// Blue-themed MapAnnotation pin showing shelter name, capacity bar,
/// and availability status.
///
/// Usage inside `Map {}`:
/// ```swift
/// Annotation("", coordinate: coord) {
///     ShelterMarker(shelter: shelter)
/// }
/// ```
struct ShelterMarker: View {

    let shelter: Shelter

    /// Show full detail (name, services) or compact dot only.
    var compact: Bool = false

    // MARK: - Derived

    /// Occupancy ratio 0.0 ... 1.0.
    private var occupancyRatio: Double {
        shelter.occupancyPercentage
    }

    /// Color coded by occupancy threshold.
    private var capacityColor: Color {
        if !shelter.isOpen { return .gray }
        if occupancyRatio < 0.5 { return .green }
        if occupancyRatio < 0.8 { return .yellow }
        return .red
    }

    /// Primary blue tint for the shelter marker.
    private let shelterBlue = Color(red: 0.15, green: 0.45, blue: 0.85)

    /// Available beds text.
    private var availabilityText: String {
        if !shelter.isOpen { return "Closed" }
        let beds = shelter.availableBeds
        if beds == 0 { return "Full" }
        return "\(beds) beds"
    }

    /// Occupancy percentage display.
    private var occupancyPercentText: String {
        "\(Int(occupancyRatio * 100))%"
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

    private var fullBody: some View {
        VStack(spacing: 0) {
            // --- Callout card ---
            VStack(spacing: 4) {
                // Name
                Text(shelter.name.isEmpty ? "Shelter" : shelter.name)
                    .font(.system(size: 10, weight: .semibold))
                    .foregroundColor(shelter.isOpen ? .primary : .secondary)
                    .lineLimit(1)

                // Capacity bar
                VStack(spacing: 2) {
                    // Bar
                    GeometryReader { geometry in
                        ZStack(alignment: .leading) {
                            // Background track
                            RoundedRectangle(cornerRadius: 2)
                                .fill(Color(.systemGray5))
                                .frame(height: 6)

                            // Fill
                            RoundedRectangle(cornerRadius: 2)
                                .fill(capacityColor)
                                .frame(
                                    width: geometry.size.width * min(occupancyRatio, 1.0),
                                    height: 6
                                )
                        }
                    }
                    .frame(height: 6)
                    .frame(width: 60)

                    // Labels
                    HStack {
                        Text("\(shelter.currentOccupancy)/\(shelter.capacity)")
                            .font(.system(size: 7, weight: .medium))
                            .foregroundColor(.secondary)

                        Spacer()

                        Text(occupancyPercentText)
                            .font(.system(size: 7, weight: .bold))
                            .foregroundColor(capacityColor)
                    }
                    .frame(width: 60)
                }

                // Availability badge
                HStack(spacing: 3) {
                    Circle()
                        .fill(capacityColor)
                        .frame(width: 5, height: 5)

                    Text(availabilityText)
                        .font(.system(size: 8, weight: .medium))
                        .foregroundColor(shelter.isOpen ? .primary : .secondary)
                }

                // Service icons row
                if !shelter.services.isEmpty {
                    HStack(spacing: 3) {
                        ForEach(shelter.services.prefix(4), id: \.self) { service in
                            Image(systemName: serviceIconName(for: service))
                                .font(.system(size: 7))
                                .foregroundColor(shelterBlue.opacity(0.7))
                        }

                        if shelter.services.count > 4 {
                            Text("+\(shelter.services.count - 4)")
                                .font(.system(size: 7, weight: .medium))
                                .foregroundColor(.secondary)
                        }
                    }
                }

                // Accessibility indicators
                HStack(spacing: 4) {
                    if shelter.wheelchairAccessible {
                        Image(systemName: "figure.roll")
                            .font(.system(size: 7))
                            .foregroundColor(shelterBlue)
                    }
                    if shelter.pets {
                        Image(systemName: "pawprint.fill")
                            .font(.system(size: 7))
                            .foregroundColor(shelterBlue)
                    }
                }
            }
            .padding(6)
            .background(Color(.systemBackground))
            .cornerRadius(8)
            .overlay(
                RoundedRectangle(cornerRadius: 8)
                    .stroke(shelterBlue.opacity(shelter.isOpen ? 0.6 : 0.2), lineWidth: 1.5)
            )
            .shadow(color: shelterBlue.opacity(0.15), radius: 3, x: 0, y: 2)
            .opacity(shelter.isOpen ? 1.0 : 0.6)

            // --- Pin stem ---
            Rectangle()
                .fill(shelterBlue.opacity(shelter.isOpen ? 1.0 : 0.4))
                .frame(width: 2, height: 6)

            // --- House icon ---
            ZStack {
                Circle()
                    .fill(shelterBlue.opacity(0.2))
                    .frame(width: 28, height: 28)

                Circle()
                    .fill(shelterBlue.opacity(shelter.isOpen ? 1.0 : 0.4))
                    .frame(width: 20, height: 20)

                Image(systemName: "house.fill")
                    .font(.system(size: 10, weight: .bold))
                    .foregroundColor(.white)
            }
        }
        .accessibilityElement(children: .combine)
        .accessibilityLabel(accessibilityDescription)
    }

    // MARK: - Compact Layout

    private var compactBody: some View {
        ZStack {
            Circle()
                .fill(shelterBlue.opacity(0.25))
                .frame(width: 22, height: 22)

            Circle()
                .fill(shelterBlue.opacity(shelter.isOpen ? 1.0 : 0.4))
                .frame(width: 16, height: 16)

            Image(systemName: "house.fill")
                .font(.system(size: 8, weight: .bold))
                .foregroundColor(.white)

            // Tiny capacity indicator dot
            Circle()
                .fill(capacityColor)
                .frame(width: 6, height: 6)
                .overlay(
                    Circle()
                        .stroke(Color(.systemBackground), lineWidth: 1)
                )
                .offset(x: 8, y: -8)
        }
        .opacity(shelter.isOpen ? 1.0 : 0.5)
        .accessibilityLabel("\(shelter.name), \(availabilityText)")
    }

    // MARK: - Helpers

    /// Map service names to SF Symbols.
    private func serviceIconName(for service: String) -> String {
        let lower = service.lowercased()
        if lower.contains("medical") || lower.contains("first aid") { return "cross.case.fill" }
        if lower.contains("food")    { return "fork.knife" }
        if lower.contains("water")   { return "drop.fill" }
        if lower.contains("blanket") || lower.contains("bedding") { return "bed.double.fill" }
        if lower.contains("shower")  || lower.contains("hygiene")  { return "shower.fill" }
        if lower.contains("phone")   || lower.contains("charging") { return "battery.100.bolt" }
        if lower.contains("child")   || lower.contains("kid")      { return "figure.and.child.holdinghands" }
        if lower.contains("counsel") || lower.contains("mental")   { return "brain.head.profile" }
        return "checkmark.circle.fill"
    }

    private var accessibilityDescription: String {
        var parts: [String] = [shelter.name]
        parts.append(shelter.isOpen ? "Open" : "Closed")
        parts.append("\(shelter.currentOccupancy) of \(shelter.capacity) occupied")
        parts.append("\(shelter.availableBeds) beds available")
        if shelter.wheelchairAccessible { parts.append("Wheelchair accessible") }
        if shelter.pets { parts.append("Pet friendly") }
        return parts.joined(separator: ", ")
    }
}

// MARK: - MapContent Convenience

/// Drop-in `MapContent` that renders a collection of shelters as annotations.
///
/// Usage:
/// ```swift
/// Map {
///     ShelterAnnotations(shelters: viewModel.shelters)
/// }
/// ```
struct ShelterAnnotations: MapContent {
    let shelters: [Shelter]
    var compact: Bool = false

    var body: some MapContent {
        ForEach(shelters) { shelter in
            Annotation(
                "",
                coordinate: CLLocationCoordinate2D(
                    latitude: shelter.latitude,
                    longitude: shelter.longitude
                )
            ) {
                ShelterMarker(shelter: shelter, compact: compact)
            }
        }
    }
}

// MARK: - Preview

#Preview("Shelter - Available") {
    Map {
        Annotation(
            "",
            coordinate: CLLocationCoordinate2D(latitude: 37.7749, longitude: -122.4194)
        ) {
            ShelterMarker(
                shelter: Shelter(
                    id: "shelter-preview-001",
                    name: "Civic Center Shelter",
                    address: "1 Dr Carlton B Goodlett Pl",
                    latitude: 37.7749,
                    longitude: -122.4194,
                    capacity: 200,
                    currentOccupancy: 75,
                    services: ["Medical", "Food", "Water", "Blankets"],
                    phone: "415-555-0100",
                    isOpen: true,
                    pets: true,
                    wheelchairAccessible: true
                )
            )
        }
    }
}

#Preview("Shelter - Nearly Full") {
    Map {
        Annotation(
            "",
            coordinate: CLLocationCoordinate2D(latitude: 37.7849, longitude: -122.4094)
        ) {
            ShelterMarker(
                shelter: Shelter(
                    id: "shelter-preview-002",
                    name: "Moscone Convention Center",
                    address: "747 Howard St",
                    latitude: 37.7849,
                    longitude: -122.4094,
                    capacity: 500,
                    currentOccupancy: 465,
                    services: ["Medical", "Food", "Water", "Blankets", "Shower", "Charging"],
                    isOpen: true,
                    wheelchairAccessible: true
                )
            )
        }
    }
}

#Preview("Shelter - Closed") {
    Map {
        Annotation(
            "",
            coordinate: CLLocationCoordinate2D(latitude: 37.7700, longitude: -122.4250)
        ) {
            ShelterMarker(
                shelter: Shelter(
                    id: "shelter-preview-003",
                    name: "Marina Middle School",
                    address: "3500 Fillmore St",
                    latitude: 37.7700,
                    longitude: -122.4250,
                    capacity: 150,
                    currentOccupancy: 0,
                    isOpen: false
                )
            )
        }
    }
}
