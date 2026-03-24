import SwiftUI

struct SettingsView: View {
    @State private var notificationsEnabled = true
    @State private var soundEnabled = true
    @State private var vibrationEnabled = true
    @State private var autoSOSEnabled = false
    @State private var autoSOSDelay = 30.0
    @State private var locationTrackingEnabled = true
    @State private var dataCollection = false
    @State private var expandedSection: String? = nil
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

                ScrollView {
                    VStack(spacing: 20) {
                        // Notifications Section
                        VStack(spacing: 0) {
                            SectionHeader(
                                title: "Notifications",
                                icon: "bell.fill",
                                isExpanded: expandedSection == "notifications",
                                onTap: {
                                    withAnimation {
                                        expandedSection = expandedSection == "notifications" ? nil : "notifications"
                                    }
                                }
                            )

                            if expandedSection == "notifications" {
                                VStack(spacing: 12) {
                                    SettingToggle(
                                        label: "All Notifications",
                                        description: "Receive all emergency and community alerts",
                                        isEnabled: $notificationsEnabled
                                    )

                                    SettingToggle(
                                        label: "Sound",
                                        description: "Play sound for incoming alerts",
                                        isEnabled: $soundEnabled
                                    )

                                    SettingToggle(
                                        label: "Vibration",
                                        description: "Vibrate for incoming alerts",
                                        isEnabled: $vibrationEnabled
                                    )
                                }
                                .padding(16)
                                .background(Color.white)
                            }
                        }
                        .background(Color.white)
                        .cornerRadius(8)
                        .padding(.horizontal, 16)

                        // Emergency Settings
                        VStack(spacing: 0) {
                            SectionHeader(
                                title: "Emergency",
                                icon: "exclamationmark.triangle.fill",
                                isExpanded: expandedSection == "emergency",
                                onTap: {
                                    withAnimation {
                                        expandedSection = expandedSection == "emergency" ? nil : "emergency"
                                    }
                                }
                            )

                            if expandedSection == "emergency" {
                                VStack(spacing: 16) {
                                    SettingToggle(
                                        label: "Auto SOS",
                                        description: "Automatically trigger SOS if unresponded after delay",
                                        isEnabled: $autoSOSEnabled
                                    )

                                    if autoSOSEnabled {
                                        VStack(alignment: .leading, spacing: 8) {
                                            Text("Auto SOS Delay")
                                                .font(.subheadline)
                                                .fontWeight(.semibold)

                                            HStack(spacing: 12) {
                                                Slider(value: $autoSOSDelay, in: 10...120, step: 5)
                                                Text("\(Int(autoSOSDelay))s")
                                                    .font(.caption)
                                                    .fontWeight(.semibold)
                                                    .frame(width: 40)
                                            }
                                        }
                                    }

                                    SettingToggle(
                                        label: "Location Tracking",
                                        description: "Allow continuous location tracking during emergencies",
                                        isEnabled: $locationTrackingEnabled
                                    )
                                }
                                .padding(16)
                                .background(Color.white)
                            }
                        }
                        .background(Color.white)
                        .cornerRadius(8)
                        .padding(.horizontal, 16)

                        // Privacy Section
                        VStack(spacing: 0) {
                            SectionHeader(
                                title: "Privacy",
                                icon: "lock.fill",
                                isExpanded: expandedSection == "privacy",
                                onTap: {
                                    withAnimation {
                                        expandedSection = expandedSection == "privacy" ? nil : "privacy"
                                    }
                                }
                            )

                            if expandedSection == "privacy" {
                                VStack(spacing: 12) {
                                    SettingToggle(
                                        label: "Analytics",
                                        description: "Share anonymized data to improve TheWatch",
                                        isEnabled: $dataCollection
                                    )

                                    SettingButton(
                                        label: "Privacy Policy",
                                        icon: "doc.fill",
                                        action: {}
                                    )

                                    SettingButton(
                                        label: "Terms of Service",
                                        icon: "doc.text.fill",
                                        action: {}
                                    )
                                }
                                .padding(16)
                                .background(Color.white)
                            }
                        }
                        .background(Color.white)
                        .cornerRadius(8)
                        .padding(.horizontal, 16)

                        // GDPR & Data Rights Section
                        VStack(spacing: 0) {
                            SectionHeader(
                                title: "GDPR & Data Rights",
                                icon: "shield.fill",
                                isExpanded: expandedSection == "gdpr",
                                onTap: {
                                    withAnimation {
                                        expandedSection = expandedSection == "gdpr" ? nil : "gdpr"
                                    }
                                }
                            )

                            if expandedSection == "gdpr" {
                                VStack(spacing: 12) {
                                    NavigationLink(destination: DataExportView()) {
                                        HStack(spacing: 12) {
                                            Image(systemName: "square.and.arrow.up")
                                                .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                                                .frame(width: 24)
                                            Text("Export My Data (Art. 20)")
                                                .foregroundColor(.black)
                                            Spacer()
                                            Image(systemName: "chevron.right")
                                                .foregroundColor(.gray)
                                                .font(.caption)
                                        }
                                        .padding(12)
                                    }
                                    .accessibilityLabel("Export your data under GDPR Article 20")

                                    NavigationLink(destination: EULAManagementView()) {
                                        HStack(spacing: 12) {
                                            Image(systemName: "doc.text.fill")
                                                .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                                                .frame(width: 24)
                                            Text("EULA & Terms")
                                                .foregroundColor(.black)
                                            Spacer()
                                            Image(systemName: "chevron.right")
                                                .foregroundColor(.gray)
                                                .font(.caption)
                                        }
                                        .padding(12)
                                    }
                                    .accessibilityLabel("View and manage EULA")

                                    NavigationLink(destination: LogViewerView()) {
                                        HStack(spacing: 12) {
                                            Image(systemName: "list.bullet.rectangle")
                                                .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                                                .frame(width: 24)
                                            Text("Diagnostics Log Viewer")
                                                .foregroundColor(.black)
                                            Spacer()
                                            Image(systemName: "chevron.right")
                                                .foregroundColor(.gray)
                                                .font(.caption)
                                        }
                                        .padding(12)
                                    }
                                    .accessibilityLabel("View diagnostic logs")

                                    Divider()

                                    NavigationLink(destination: AccountDeletionView()) {
                                        HStack(spacing: 12) {
                                            Image(systemName: "trash.fill")
                                                .foregroundColor(.red)
                                                .frame(width: 24)
                                            Text("Delete Account (Art. 17)")
                                                .foregroundColor(.red)
                                            Spacer()
                                            Image(systemName: "chevron.right")
                                                .foregroundColor(.gray)
                                                .font(.caption)
                                        }
                                        .padding(12)
                                    }
                                    .accessibilityLabel("Delete your account under GDPR Article 17")
                                }
                                .padding(16)
                                .background(Color.white)
                            }
                        }
                        .background(Color.white)
                        .cornerRadius(8)
                        .padding(.horizontal, 16)

                        // Account Section
                        VStack(spacing: 0) {
                            SectionHeader(
                                title: "Account",
                                icon: "person.fill",
                                isExpanded: expandedSection == "account",
                                onTap: {
                                    withAnimation {
                                        expandedSection = expandedSection == "account" ? nil : "account"
                                    }
                                }
                            )

                            if expandedSection == "account" {
                                VStack(spacing: 12) {
                                    SettingButton(
                                        label: "Change Password",
                                        icon: "key.fill",
                                        action: {}
                                    )

                                    SettingButton(
                                        label: "Connected Devices",
                                        icon: "applewatch",
                                        action: {}
                                    )

                                    SettingButton(
                                        label: "Two-Factor Authentication",
                                        icon: "checkmark.shield.fill",
                                        action: {}
                                    )

                                    Divider()

                                    Button(action: {}) {
                                        HStack(spacing: 12) {
                                            Image(systemName: "iphone.and.arrow.forward")
                                                .foregroundColor(.red)
                                                .frame(width: 24)
                                            Text("Sign Out")
                                                .foregroundColor(.red)
                                            Spacer()
                                            Image(systemName: "chevron.right")
                                                .foregroundColor(.gray)
                                                .font(.caption)
                                        }
                                        .frame(maxWidth: .infinity, alignment: .leading)
                                        .padding(12)
                                    }
                                    .accessibilityLabel("Sign out of account")
                                }
                                .padding(16)
                                .background(Color.white)
                            }
                        }
                        .background(Color.white)
                        .cornerRadius(8)
                        .padding(.horizontal, 16)

                        // About Section
                        VStack(spacing: 0) {
                            SectionHeader(
                                title: "About",
                                icon: "info.circle.fill",
                                isExpanded: expandedSection == "about",
                                onTap: {
                                    withAnimation {
                                        expandedSection = expandedSection == "about" ? nil : "about"
                                    }
                                }
                            )

                            if expandedSection == "about" {
                                VStack(spacing: 12) {
                                    HStack {
                                        Text("App Version")
                                            .font(.subheadline)
                                        Spacer()
                                        Text("1.0.0")
                                            .font(.subheadline)
                                            .foregroundColor(.gray)
                                    }
                                    .padding(12)

                                    Divider()

                                    HStack {
                                        Text("Build Number")
                                            .font(.subheadline)
                                        Spacer()
                                        Text("42")
                                            .font(.subheadline)
                                            .foregroundColor(.gray)
                                    }
                                    .padding(12)

                                    Divider()

                                    Button(action: {}) {
                                        Text("Check for Updates")
                                            .frame(maxWidth: .infinity)
                                            .padding(12)
                                            .background(Color(red: 0.9, green: 0.22, blue: 0.27).opacity(0.1))
                                            .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                                            .cornerRadius(8)
                                    }
                                    .accessibilityLabel("Check for app updates")
                                }
                                .padding(16)
                                .background(Color.white)
                            }
                        }
                        .background(Color.white)
                        .cornerRadius(8)
                        .padding(.horizontal, 16)

                        Spacer()
                            .frame(height: 20)
                    }
                    .padding(.vertical, 16)
                }
            }
        }
    }
}

// MARK: - Section Header Component
struct SectionHeader: View {
    let title: String
    let icon: String
    let isExpanded: Bool
    let onTap: () -> Void

    var body: some View {
        Button(action: onTap) {
            HStack(spacing: 12) {
                Image(systemName: icon)
                    .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                    .frame(width: 24)

                Text(title)
                    .font(.subheadline)
                    .fontWeight(.semibold)
                    .foregroundColor(.black)

                Spacer()

                Image(systemName: isExpanded ? "chevron.up" : "chevron.down")
                    .foregroundColor(.gray)
                    .font(.caption)
            }
            .padding(12)
            .background(Color.white)
        }
        .accessibilityLabel("Toggle \(title) section")
    }
}

// MARK: - Setting Toggle Component
struct SettingToggle: View {
    let label: String
    let description: String
    @Binding var isEnabled: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 12) {
                VStack(alignment: .leading, spacing: 4) {
                    Text(label)
                        .font(.subheadline)
                        .fontWeight(.semibold)
                    Text(description)
                        .font(.caption)
                        .foregroundColor(.gray)
                }
                Spacer()
                Toggle("", isOn: $isEnabled)
                    .accessibilityLabel(label)
            }
        }
    }
}

// MARK: - Setting Button Component
struct SettingButton: View {
    let label: String
    let icon: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 12) {
                Image(systemName: icon)
                    .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                    .frame(width: 24)
                Text(label)
                    .foregroundColor(.black)
                Spacer()
                Image(systemName: "chevron.right")
                    .foregroundColor(.gray)
                    .font(.caption)
            }
            .padding(12)
        }
        .accessibilityLabel(label)
    }
}

#Preview {
    SettingsView()
}
