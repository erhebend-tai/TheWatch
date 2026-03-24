import SwiftUI
import MapKit

struct HomeView: View {
    @Environment(MockAlertService.self) var alertService
    @Environment(MockVolunteerService.self) var volunteerService
    @State var viewModel: HomeViewModel?
    @State private var position = MapCameraPosition.automatic
    @State private var isSettingsSheet = false

    var body: some View {
        ZStack(alignment: .bottom) {
            // Map
            Map(position: $position) {
                UserAnnotation()

                // Responder annotations
                ForEach(viewModel?.nearbyResponders ?? []) { responder in
                    Annotation("", coordinate: CLLocationCoordinate2D(latitude: responder.latitude, longitude: responder.longitude)) {
                        Image(systemName: "person.fill")
                            .foregroundColor(.green)
                            .padding(6)
                            .background(Color.white)
                            .clipShape(Circle())
                            .shadow(radius: 2)
                    }
                }

                // Community alert annotations
                ForEach(viewModel?.communityAlerts ?? []) { alert in
                    Annotation("", coordinate: CLLocationCoordinate2D(latitude: alert.latitude, longitude: alert.longitude)) {
                        Image(systemName: "exclamationmark.triangle.fill")
                            .foregroundColor(.orange)
                            .padding(6)
                            .background(Color.white)
                            .clipShape(Circle())
                            .shadow(radius: 2)
                    }
                }

                // Proximity rings
                if let activeAlert = viewModel?.activeAlert {
                    MapCircle(center: CLLocationCoordinate2D(latitude: activeAlert.latitude, longitude: activeAlert.longitude), radius: 500)
                        .foregroundStyle(Color.red.opacity(0.2))
                        .stroke(Color.red, lineWidth: 2)

                    MapCircle(center: CLLocationCoordinate2D(latitude: activeAlert.latitude, longitude: activeAlert.longitude), radius: 1000)
                        .foregroundStyle(Color.orange.opacity(0.1))
                        .stroke(Color.orange, lineWidth: 1)

                    MapCircle(center: CLLocationCoordinate2D(latitude: activeAlert.latitude, longitude: activeAlert.longitude), radius: 2000)
                        .foregroundStyle(Color.yellow.opacity(0.05))
                        .stroke(Color.yellow, lineWidth: 1)
                }
            }
            .mapStyle(.standard)
            .ignoresSafeArea()

            VStack(spacing: 0) {
                // Top bar
                HStack(spacing: 12) {
                    Button(action: { viewModel?.toggleNavigationDrawer() }) {
                        Image(systemName: "line.3.horizontal")
                            .font(.headline)
                            .foregroundColor(.black)
                    }
                    .accessibilityLabel("Open menu")

                    TextField("Search location", text: .constant(""))
                        .textFieldStyle(.roundedBorder)
                        .disabled(true)

                    Button(action: {}) {
                        ZStack(alignment: .topTrailing) {
                            Image(systemName: "bell.fill")
                                .font(.headline)
                                .foregroundColor(.black)

                            Circle()
                                .fill(Color(red: 0.9, green: 0.22, blue: 0.27))
                                .frame(width: 18, height: 18)
                                .overlay(
                                    Text("2")
                                        .font(.caption2)
                                        .fontWeight(.bold)
                                        .foregroundColor(.white)
                                )
                        }
                    }
                    .accessibilityLabel("Notifications")
                    .accessibilityValue("2 unread")
                }
                .padding(12)
                .background(Color.white)
                .shadow(radius: 2)

                Spacer()

                // Bottom status panel
                VStack(spacing: 12) {
                    HStack {
                        VStack(alignment: .leading, spacing: 4) {
                            Text("Status: Safe")
                                .font(.subheadline)
                                .fontWeight(.semibold)
                            Text("No active alerts")
                                .font(.caption)
                                .foregroundColor(.gray)
                        }
                        Spacer()
                        Image(systemName: "checkmark.circle.fill")
                            .foregroundColor(.green)
                            .font(.title2)
                    }
                    .padding(12)
                    .background(Color.white)
                    .cornerRadius(8)

                    if let activeAlert = viewModel?.activeAlert {
                        HStack {
                            VStack(alignment: .leading, spacing: 4) {
                                Text("Active Alert: \(activeAlert.type.rawValue)")
                                    .font(.subheadline)
                                    .fontWeight(.semibold)
                                Text("\(activeAlert.responderIds.count) responders responding")
                                    .font(.caption)
                                    .foregroundColor(.gray)
                            }
                            Spacer()
                            Image(systemName: "exclamationmark.circle.fill")
                                .foregroundColor(.red)
                                .font(.title2)
                        }
                        .padding(12)
                        .background(Color(red: 1.0, green: 0.95, blue: 0.95))
                        .cornerRadius(8)
                    }
                }
                .padding(12)
                .background(Color.white)
                .cornerRadius(12)
                .padding(12)
            }

            // SOS Button
            VStack(alignment: .center) {
                Spacer()

                if let vm = viewModel {
                    SOSButton(viewModel: vm)
                }
            }
            .padding(20)

            // Navigation Drawer
            if viewModel?.showNavigationDrawer == true {
                NavigationDrawer(
                    isOpen: .constant(true),
                    onClose: { viewModel?.showNavigationDrawer = false }
                )
                .transition(.move(edge: .leading))
            }
        }
        .sheet(isPresented: $isSettingsSheet) {
            SettingsView()
        }
        .onAppear {
            if viewModel == nil {
                viewModel = HomeViewModel(
                    alertService: alertService,
                    volunteerService: volunteerService
                )
            }

            Task {
                await viewModel?.loadNearbyData()
            }
        }
    }
}

#Preview {
    HomeView()
        .environment(MockAlertService())
        .environment(MockVolunteerService())
}
