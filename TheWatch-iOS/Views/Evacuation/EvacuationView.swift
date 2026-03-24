import SwiftUI
import MapKit

struct EvacuationView: View {
    @State private var selectedTab = 0
    @State private var evacuationRoutes: [EvacuationRoute] = [
        EvacuationRoute(id: "r1", area: "Downtown District", route: "Take Main St to Highway 101 North", estimatedTime: 15, status: .active),
        EvacuationRoute(id: "r2", area: "Harbor Zone", route: "Follow coastal road via Pier Ave", estimatedTime: 22, status: .active),
        EvacuationRoute(id: "r3", area: "Industrial Area", route: "Exit via Industrial Blvd to bypass", estimatedTime: 18, status: .caution)
    ]
    @State private var shelters: [Shelter] = [
        Shelter(id: "s1", name: "Community Center", address: "123 Oak St", capacity: 500, currentOccupancy: 127, distance: 2.3, status: .open),
        Shelter(id: "s2", name: "High School Gym", address: "456 Pine Ave", capacity: 800, currentOccupancy: 342, distance: 3.1, status: .open),
        Shelter(id: "s3", name: "Convention Hall", address: "789 Elm Blvd", capacity: 1200, currentOccupancy: 589, distance: 4.5, status: .open)
    ]
    @Environment(\\.dismiss) var dismiss

    var body: some View {
        ZStack {
            Color(red: 0.97, green: 0.97, blue: 0.97)
                .ignoresSafeArea()

            VStack(spacing: 0) {
                // Header
                HStack {
                    Button(action: { dismiss() }) {
                        HStack(spacing: 4) {
                            Image(systemName: "chevron.left")
                            Text("Back")
                        }
                        .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                    }
                    Spacer()
                }
                .padding(16)
                .background(Color.white)

                Divider()

                // Tab Selector
                Picker("Evacuation Info", selection: $selectedTab) {
                    Text("Routes").tag(0)
                    Text("Shelters").tag(1)
                    Text("Safety").tag(2)
                }
                .pickerStyle(.segmented)
                .padding(12)
                .background(Color.white)

                Divider()

                // Content
                TabView(selection: $selectedTab) {
                    // Routes Tab
                    ScrollView {
                        VStack(spacing: 12) {
                            Text("Evacuation Routes")
                                .font(.headline)
                                .fontWeight(.bold)
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .padding(.horizontal, 16)
                                .padding(.top, 12)

                            ForEach(evacuationRoutes) { route in
                                RouteCard(route: route)
                            }
                            .padding(.horizontal, 16)

                            Spacer()
                                .frame(height: 20)
                        }
                    }
                    .tag(0)

                    // Shelters Tab
                    ScrollView {
                        VStack(spacing: 12) {
                            Text("Emergency Shelters")
                                .font(.headline)
                                .fontWeight(.bold)
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .padding(.horizontal, 16)
                                .padding(.top, 12)

                            ForEach(shelters.sorted { $0.distance < $1.distance }) { shelter in
                                ShelterCard(shelter: shelter)
                            }
                            .padding(.horizontal, 16)

                            Spacer()
                                .frame(height: 20)
                        }
                    }
                    .tag(1)

                    // Safety Info Tab
                    ScrollView {
                        VStack(spacing: 16) {
                            Text("Safety Information")
                                .font(.headline)
                                .fontWeight(.bold)
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .padding(.horizontal, 16)
                                .padding(.top, 12)

                            SafetyInfoCard(
                                title: "Before Evacuation",
                                icon: "list.bullet.circle.fill",
                                points: [
                                    "Gather important documents and medications",
                                    "Put on comfortable, sturdy shoes",
                                    "Check weather conditions and alerts",
                                    "Notify family members of your location",
                                    "Lock doors if safe to do so"
                                ]
                            )

                            SafetyInfoCard(
                                title: "During Evacuation",
                                icon: "figure.walk",
                                points: [
                                    "Stay calm and follow official routes",
                                    "Do not attempt to use your vehicle",
                                    "Stick with your group and count members",
                                    "Use designated evacuation routes",
                                    "Listen for official updates and alerts"
                                ]
                            )

                            SafetyInfoCard(
                                title: "At the Shelter",
                                icon: "house.fill",
                                points: [
                                    "Register with shelter staff immediately",
                                    "Follow all shelter rules and procedures",
                                    "Check in with family members",
                                    "Keep your phone charged and available",
                                    "Report any medical needs to staff"
                                ]
                            )

                            Spacer()
                                .frame(height: 20)
                        }
                    }
                    .tag(2)
                }
                .tabViewStyle(.page(indexDisplayMode: .never))
            }
        }
    }
}

// MARK: - Route Card Component
struct RouteCard: View {
    let route: EvacuationRoute

    var body: some View {
        VStack(spacing: 12) {
            HStack(spacing: 12) {
                Image(systemName: "road.lanes")
                    .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                    .font(.title3)

                VStack(alignment: .leading, spacing: 4) {
                    Text(route.area)
                        .font(.subheadline)
                        .fontWeight(.semibold)
                    Text("Est. time: \(route.estimatedTime) min")
                        .font(.caption)
                        .foregroundColor(.gray)
                }

                Spacer()

                VStack(alignment: .trailing, spacing: 4) {
                    StatusBadge(status: route.status)
                }
            }

            Divider()

            HStack(spacing: 8) {
                Image(systemName: "arrow.right.circle.fill")
                    .foregroundColor(.blue)
                    .font(.caption)
                Text(route.route)
                    .font(.caption)
                    .lineLimit(2)
                Spacer()
            }

            HStack(spacing: 8) {
                Button(action: {}) {
                    HStack(spacing: 6) {
                        Image(systemName: "map.fill")
                        Text("View Map")
                            .font(.caption)
                    }
                    .frame(maxWidth: .infinity)
                    .padding(8)
                    .background(Color(red: 0.9, green: 0.22, blue: 0.27))
                    .foregroundColor(.white)
                    .cornerRadius(6)
                }
                .accessibilityLabel("View evacuation route on map")

                Button(action: {}) {
                    HStack(spacing: 6) {
                        Image(systemName: "square.and.arrow.up")
                        Text("Share")
                            .font(.caption)
                    }
                    .frame(maxWidth: .infinity)
                    .padding(8)
                    .background(Color.gray.opacity(0.1))
                    .foregroundColor(.black)
                    .cornerRadius(6)
                }
                .accessibilityLabel("Share evacuation route")
            }
        }
        .padding(12)
        .background(Color.white)
        .cornerRadius(8)
    }
}

// MARK: - Shelter Card Component
struct ShelterCard: View {
    let shelter: Shelter

    var occupancyPercentage: Double {
        Double(shelter.currentOccupancy) / Double(shelter.capacity) * 100
    }

    var body: some View {
        VStack(spacing: 12) {
            HStack(spacing: 12) {
                Image(systemName: "house.circle.fill")
                    .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                    .font(.title3)

                VStack(alignment: .leading, spacing: 4) {
                    Text(shelter.name)
                        .font(.subheadline)
                        .fontWeight(.semibold)
                    Text(shelter.address)
                        .font(.caption)
                        .foregroundColor(.gray)
                }

                Spacer()

                VStack(alignment: .trailing, spacing: 4) {
                    Text("\(String(format: "%.1f", shelter.distance)) km")
                        .font(.caption)
                        .fontWeight(.semibold)
                    StatusBadge(status: .open)
                }
            }

            Divider()

            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Text("Capacity")
                        .font(.caption)
                        .foregroundColor(.gray)
                    Spacer()
                    Text("\(shelter.currentOccupancy)/\(shelter.capacity)")
                        .font(.caption)
                        .fontWeight(.semibold)
                }

                ProgressView(value: occupancyPercentage / 100)
                    .tint(occupancyPercentage < 60 ? .green : occupancyPercentage < 80 ? .orange : .red)
            }

            HStack(spacing: 8) {
                Button(action: {}) {
                    HStack(spacing: 6) {
                        Image(systemName: "phone.fill")
                        Text("Contact")
                            .font(.caption)
                    }
                    .frame(maxWidth: .infinity)
                    .padding(8)
                    .background(Color(red: 0.9, green: 0.22, blue: 0.27))
                    .foregroundColor(.white)
                    .cornerRadius(6)
                }
                .accessibilityLabel("Contact shelter")

                Button(action: {}) {
                    HStack(spacing: 6) {
                        Image(systemName: "map.fill")
                        Text("Directions")
                            .font(.caption)
                    }
                    .frame(maxWidth: .infinity)
                    .padding(8)
                    .background(Color.gray.opacity(0.1))
                    .foregroundColor(.black)
                    .cornerRadius(6)
                }
                .accessibilityLabel("Get directions to shelter")
            }
        }
        .padding(12)
        .background(Color.white)
        .cornerRadius(8)
    }
}

// MARK: - Safety Info Card Component
struct SafetyInfoCard: View {
    let title: String
    let icon: String
    let points: [String]

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 12) {
                Image(systemName: icon)
                    .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                    .font(.headline)
                Text(title)
                    .font(.subheadline)
                    .fontWeight(.semibold)
                Spacer()
            }

            VStack(alignment: .leading, spacing: 8) {
                ForEach(points, id: \\.self) { point in
                    HStack(spacing: 8) {
                        Circle()
                            .fill(Color(red: 0.9, green: 0.22, blue: 0.27))
                            .frame(width: 6, height: 6)
                        Text(point)
                            .font(.caption)
                            .foregroundColor(.gray)
                        Spacer()
                    }
                }
            }
        }
        .padding(12)
        .background(Color.white)
        .cornerRadius(8)
        .padding(.horizontal, 16)
    }
}

// MARK: - Status Badge Component
struct StatusBadge: View {
    let status: RouteStatus

    var body: some View {
        HStack(spacing: 4) {
            Circle()
                .fill(statusColor)
                .frame(width: 6, height: 6)
            Text(status.rawValue.capitalized)
                .font(.caption2)
        }
        .padding(.horizontal, 6)
        .padding(.vertical, 2)
        .background(statusBackgroundColor)
        .cornerRadius(4)
    }

    private var statusColor: Color {
        switch status {
        case .active:
            return .green
        case .caution:
            return .orange
        case .closed:
            return .red
        }
    }

    private var statusBackgroundColor: Color {
        switch status {
        case .active:
            return Color.green.opacity(0.1)
        case .caution:
            return Color.orange.opacity(0.1)
        case .closed:
            return Color.red.opacity(0.1)
        }
    }
}

#Preview {
    EvacuationView()
}
