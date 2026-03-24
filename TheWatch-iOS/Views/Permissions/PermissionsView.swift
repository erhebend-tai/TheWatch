import SwiftUI

struct PermissionsView: View {
    @State private var permissionManager = PermissionManager.shared
    @State private var expandedPermission: String? = nil
    @Environment(\.dismiss) var dismiss

    var body: some View {
        ZStack {
            Color(red: 0.97, green: 0.97, blue: 0.97)
                .ignoresSafeArea()

            VStack(spacing: 0) {
                // Header
                HStack {
                    Text("App Permissions")
                        .font(.headline)
                        .fontWeight(.bold)
                    Spacer()
                    Button(action: { dismiss() }) {
                        Image(systemName: "xmark.circle.fill")
                            .foregroundColor(.gray)
                    }
                    .accessibilityLabel("Close permissions")
                }
                .padding(16)
                .background(Color.white)

                Divider()

                ScrollView {
                    VStack(spacing: 12) {
                        Text("TheWatch needs several permissions to work effectively. These permissions help us provide you with the best emergency response experience.")
                            .font(.caption)
                            .foregroundColor(.gray)
                            .padding(.horizontal, 16)
                            .padding(.vertical, 12)

                        // Location Permission
                        PermissionCard(
                            title: "Location",
                            icon: "location.fill",
                            isGranted: permissionManager.locationStatus == .authorized,
                            isExpanded: expandedPermission == "location",
                            onToggleExpand: {
                                withAnimation {
                                    expandedPermission = expandedPermission == "location" ? nil : "location"
                                }
                            },
                            onAllow: {
                                Task {
                                    _ = await permissionManager.requestAlwaysLocationPermission()
                                }
                            }
                        ) {
                            VStack(alignment: .leading, spacing: 8) {
                                Text("Why we need this:")
                                    .font(.subheadline)
                                    .fontWeight(.semibold)

                                VStack(alignment: .leading, spacing: 6) {
                                    HStack(spacing: 8) {
                                        Image(systemName: "checkmark.circle.fill")
                                            .foregroundColor(.green)
                                            .font(.caption)
                                        Text("Dispatch responders to your location")
                                            .font(.caption)
                                    }

                                    HStack(spacing: 8) {
                                        Image(systemName: "checkmark.circle.fill")
                                            .foregroundColor(.green)
                                            .font(.caption)
                                        Text("Show nearby emergency responders")
                                            .font(.caption)
                                    }

                                    HStack(spacing: 8) {
                                        Image(systemName: "checkmark.circle.fill")
                                            .foregroundColor(.green)
                                            .font(.caption)
                                        Text("Calculate response times and distances")
                                            .font(.caption)
                                    }
                                }
                                .foregroundColor(.gray)
                            }
                        }
                        .padding(.horizontal, 16)

                        // Notification Permission
                        PermissionCard(
                            title: "Notifications",
                            icon: "bell.fill",
                            isGranted: permissionManager.notificationStatus == .authorized,
                            isExpanded: expandedPermission == "notifications",
                            onToggleExpand: {
                                withAnimation {
                                    expandedPermission = expandedPermission == "notifications" ? nil : "notifications"
                                }
                            },
                            onAllow: {
                                Task {
                                    _ = await permissionManager.requestNotificationPermission()
                                }
                            }
                        ) {
                            VStack(alignment: .leading, spacing: 8) {
                                Text("Why we need this:")
                                    .font(.subheadline)
                                    .fontWeight(.semibold)

                                VStack(alignment: .leading, spacing: 6) {
                                    HStack(spacing: 8) {
                                        Image(systemName: "checkmark.circle.fill")
                                            .foregroundColor(.green)
                                            .font(.caption)
                                        Text("Alert you when responders arrive")
                                            .font(.caption)
                                    }

                                    HStack(spacing: 8) {
                                        Image(systemName: "checkmark.circle.fill")
                                            .foregroundColor(.green)
                                            .font(.caption)
                                        Text("Notify you of community alerts")
                                            .font(.caption)
                                    }

                                    HStack(spacing: 8) {
                                        Image(systemName: "checkmark.circle.fill")
                                            .foregroundColor(.green)
                                            .font(.caption)
                                        Text("Send urgent evacuation notices")
                                            .font(.caption)
                                    }
                                }
                                .foregroundColor(.gray)
                            }
                        }
                        .padding(.horizontal, 16)

                        // Health Kit Permission
                        PermissionCard(
                            title: "Health Data",
                            icon: "heart.fill",
                            isGranted: permissionManager.healthKitStatus == .authorized,
                            isExpanded: expandedPermission == "healthkit",
                            onToggleExpand: {
                                withAnimation {
                                    expandedPermission = expandedPermission == "healthkit" ? nil : "healthkit"
                                }
                            },
                            onAllow: {
                                Task {
                                    _ = await permissionManager.requestHealthKitPermission()
                                }
                            }
                        ) {
                            VStack(alignment: .leading, spacing: 8) {
                                Text("Why we need this:")
                                    .font(.subheadline)
                                    .fontWeight(.semibold)

                                VStack(alignment: .leading, spacing: 6) {
                                    HStack(spacing: 8) {
                                        Image(systemName: "checkmark.circle.fill")
                                            .foregroundColor(.green)
                                            .font(.caption)
                                        Text("Monitor your heart rate for anomalies")
                                            .font(.caption)
                                    }

                                    HStack(spacing: 8) {
                                        Image(systemName: "checkmark.circle.fill")
                                            .foregroundColor(.green)
                                            .font(.caption)
                                        Text("Detect falls and unusual activity")
                                            .font(.caption)
                                    }

                                    HStack(spacing: 8) {
                                        Image(systemName: "checkmark.circle.fill")
                                            .foregroundColor(.green)
                                            .font(.caption)
                                        Text("Provide medical data to responders")
                                            .font(.caption)
                                    }
                                }
                                .foregroundColor(.gray)
                            }
                        }
                        .padding(.horizontal, 16)

                        // Contacts Permission
                        PermissionCard(
                            title: "Contacts",
                            icon: "person.crop.circle.fill",
                            isGranted: permissionManager.contactsStatus == .authorized,
                            isExpanded: expandedPermission == "contacts",
                            onToggleExpand: {
                                withAnimation {
                                    expandedPermission = expandedPermission == "contacts" ? nil : "contacts"
                                }
                            },
                            onAllow: {
                                Task {
                                    _ = await permissionManager.requestContactsPermission()
                                }
                            }
                        ) {
                            VStack(alignment: .leading, spacing: 8) {
                                Text("Why we need this:")
                                    .font(.subheadline)
                                    .fontWeight(.semibold)

                                VStack(alignment: .leading, spacing: 6) {
                                    HStack(spacing: 8) {
                                        Image(systemName: "checkmark.circle.fill")
                                            .foregroundColor(.green)
                                            .font(.caption)
                                        Text("Quickly add emergency contacts")
                                            .font(.caption)
                                    }

                                    HStack(spacing: 8) {
                                        Image(systemName: "checkmark.circle.fill")
                                            .foregroundColor(.green)
                                            .font(.caption)
                                        Text("Auto-fill emergency contact information")
                                            .font(.caption)
                                    }

                                    HStack(spacing: 8) {
                                        Image(systemName: "checkmark.circle.fill")
                                            .foregroundColor(.green)
                                            .font(.caption)
                                        Text("Notify contacts during emergencies")
                                            .font(.caption)
                                    }
                                }
                                .foregroundColor(.gray)
                            }
                        }
                        .padding(.horizontal, 16)

                        Spacer()
                            .frame(height: 20)

                        // Summary
                        HStack(spacing: 8) {
                            Image(systemName: "lock.fill")
                                .foregroundColor(.gray)
                                .font(.caption)
                            Text("All permissions are encrypted and stored securely. You can change these settings anytime in Settings > TheWatch.")
                                .font(.caption2)
                                .foregroundColor(.gray)
                        }
                        .padding(12)
                        .background(Color.white)
                        .cornerRadius(8)
                        .padding(.horizontal, 16)
                    }
                    .padding(.vertical, 16)
                }

                Divider()

                // Action Buttons
                HStack(spacing: 12) {
                    Button(action: { dismiss() }) {
                        Text("Skip")
                            .frame(maxWidth: .infinity)
                            .padding(12)
                            .background(Color.gray.opacity(0.1))
                            .foregroundColor(.black)
                            .cornerRadius(8)
                    }
                    .accessibilityLabel("Skip permissions setup")

                    if permissionManager.allRequiredPermissionsGranted {
                        Button(action: { dismiss() }) {
                            HStack(spacing: 8) {
                                Image(systemName: "checkmark")
                                    .font(.caption)
                                Text("Done")
                                    .fontWeight(.semibold)
                            }
                            .frame(maxWidth: .infinity)
                            .padding(12)
                            .background(Color(red: 0.9, green: 0.22, blue: 0.27))
                            .foregroundColor(.white)
                            .cornerRadius(8)
                        }
                        .accessibilityLabel("Complete permissions setup")
                    } else {
                        Button(action: { permissionManager.openSettings() }) {
                            HStack(spacing: 8) {
                                Image(systemName: "gear")
                                    .font(.caption)
                                Text("Open Settings")
                                    .fontWeight(.semibold)
                            }
                            .frame(maxWidth: .infinity)
                            .padding(12)
                            .background(Color.blue)
                            .foregroundColor(.white)
                            .cornerRadius(8)
                        }
                        .accessibilityLabel("Open system settings to grant permissions")
                    }
                }
                .padding(16)
                .background(Color.white)
            }
        }
    }

    private var allPermissionsGranted: Bool {
        permissionManager.allRequiredPermissionsGranted
    }
}

// MARK: - Permission Card Component
struct PermissionCard<Content: View>: View {
    let title: String
    let icon: String
    let isGranted: Bool
    let isExpanded: Bool
    let onToggleExpand: () -> Void
    let onAllow: () -> Void
    let content: Content

    init(
        title: String,
        icon: String,
        isGranted: Bool,
        isExpanded: Bool,
        onToggleExpand: @escaping () -> Void,
        onAllow: @escaping () -> Void,
        @ViewBuilder content: () -> Content
    ) {
        self.title = title
        self.icon = icon
        self.isGranted = isGranted
        self.isExpanded = isExpanded
        self.onToggleExpand = onToggleExpand
        self.onAllow = onAllow
        self.content = content()
    }

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 12) {
                Image(systemName: icon)
                    .foregroundColor(isGranted ? .green : Color(red: 0.9, green: 0.22, blue: 0.27))
                    .font(.title3)
                    .frame(width: 32)

                VStack(alignment: .leading, spacing: 2) {
                    Text(title)
                        .font(.subheadline)
                        .fontWeight(.semibold)
                    Text(isGranted ? "Granted" : "Required")
                        .font(.caption)
                        .foregroundColor(.gray)
                }

                Spacer()

                if isGranted {
                    Image(systemName: "checkmark.circle.fill")
                        .foregroundColor(.green)
                } else {
                    Button(action: onAllow) {
                        Text("Allow")
                            .font(.caption)
                            .fontWeight(.semibold)
                            .padding(6)
                            .background(Color(red: 0.9, green: 0.22, blue: 0.27))
                            .foregroundColor(.white)
                            .cornerRadius(6)
                    }
                    .accessibilityLabel("Allow \(title) permission")
                }

                Button(action: onToggleExpand) {
                    Image(systemName: isExpanded ? "chevron.up" : "chevron.down")
                        .foregroundColor(.gray)
                        .font(.caption)
                }
                .accessibilityLabel(isExpanded ? "Collapse \(title) details" : "Expand \(title) details")
            }
            .padding(12)

            if isExpanded {
                Divider()
                    .padding(.horizontal, 12)

                content
                    .padding(12)
            }
        }
        .background(Color.white)
        .cornerRadius(8)
    }
}

#Preview {
    PermissionsView()
}
