import SwiftUI
import MapKit

struct HomeView: View {
    @Environment(MockAlertService.self) var alertService
    @Environment(MockVolunteerService.self) var volunteerService
    @State var viewModel: HomeViewModel?
    @State private var position = MapCameraPosition.automatic
    @State private var isSettingsSheet = false
    @State private var sosViewModel = SosCountdownViewModel()

    var body: some View {
        ZStack(alignment: .bottom) {
            // Map
            Map(position: $position) {
                UserAnnotation()

                // Responder annotations with ETA labels
                ForEach(viewModel?.nearbyResponders ?? []) { responder in
                    Annotation("", coordinate: CLLocationCoordinate2D(latitude: responder.latitude, longitude: responder.longitude)) {
                        VStack(spacing: 2) {
                            // Role icon
                            Image(systemName: responder.hasVehicle ? "car.fill" : "person.fill")
                                .foregroundColor(responder.status == .onCall ? .blue : .green)
                                .padding(6)
                                .background(Color.white)
                                .clipShape(Circle())
                                .shadow(radius: 2)

                            // Name + ETA label
                            VStack(spacing: 0) {
                                Text(responder.name.isEmpty ? responder.role.rawValue : responder.name)
                                    .font(.caption2)
                                    .fontWeight(.semibold)
                                    .foregroundColor(Color(red: 0.2, green: 0.2, blue: 0.4))

                                HStack(spacing: 2) {
                                    Text(responder.distanceDisplay)
                                        .font(.caption2)
                                        .foregroundColor(.secondary)
                                    if let eta = responder.responseTime {
                                        Text("ETA \(Int(eta / 60))m")
                                            .font(.caption2)
                                            .fontWeight(.semibold)
                                            .foregroundColor(.blue)
                                    }
                                }
                            }
                            .padding(.horizontal, 6)
                            .padding(.vertical, 2)
                            .background(Color.white.opacity(0.9))
                            .cornerRadius(4)
                            .shadow(radius: 1)
                        }
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

                // Proximity scope rings at response radii
                if let activeAlert = viewModel?.activeAlert {
                    let center = CLLocationCoordinate2D(latitude: activeAlert.latitude, longitude: activeAlert.longitude)

                    // 1km ring — CheckIn scope (innermost, most urgent)
                    MapCircle(center: center, radius: 1000)
                        .foregroundStyle(Color.red.opacity(0.08))
                        .stroke(Color.red.opacity(0.6), lineWidth: 3)

                    // 3km ring — Emergency scope
                    MapCircle(center: center, radius: 3000)
                        .foregroundStyle(Color.orange.opacity(0.04))
                        .stroke(Color.orange.opacity(0.4), lineWidth: 2)

                    // 10km ring — CommunityWatch scope (outermost)
                    MapCircle(center: center, radius: 10000)
                        .foregroundStyle(Color.yellow.opacity(0.02))
                        .stroke(Color.yellow.opacity(0.3), lineWidth: 1)
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

            // SOS Button — tapping opens full-screen SOS countdown overlay
            VStack(alignment: .center) {
                Spacer()

                Button(action: {
                    sosViewModel.startSOS(source: .manualButton)
                }) {
                    VStack(spacing: 8) {
                        ZStack {
                            Circle()
                                .fill(Color(red: 0.9, green: 0.22, blue: 0.27))
                                .frame(width: 140, height: 140)
                                .shadow(color: Color(red: 0.9, green: 0.22, blue: 0.27).opacity(0.4), radius: 8)

                            VStack(spacing: 8) {
                                Image(systemName: "exclamationmark")
                                    .font(.system(size: 40, weight: .bold))
                                    .foregroundColor(.white)
                                Text("SOS")
                                    .font(.headline)
                                    .fontWeight(.bold)
                                    .foregroundColor(.white)
                            }
                        }
                        .frame(width: 140, height: 140)

                        Text("Tap to activate")
                            .font(.caption2)
                            .foregroundColor(.gray)
                    }
                }
                .accessibilityLabel("SOS button")
                .accessibilityValue("Tap to activate emergency response")
            }
            .padding(20)
            .fullScreenCover(isPresented: $sosViewModel.isPresented) {
                SosCountdownView(viewModel: sosViewModel)
            }

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
