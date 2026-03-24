import SwiftUI

struct NavigationDrawer: View {
    @Binding var isOpen: Bool
    let onClose: () -> Void

    var body: some View {
        ZStack(alignment: .leading) {
            // Overlay
            Color.black
                .opacity(0.3)
                .ignoresSafeArea()
                .onTapGesture {
                    onClose()
                }

            // Drawer
            VStack(spacing: 0) {
                // Header
                HStack(spacing: 12) {
                    Circle()
                        .fill(Color(red: 0.9, green: 0.22, blue: 0.27).opacity(0.2))
                        .frame(width: 40, height: 40)
                        .overlay(
                            Text("A")
                                .font(.headline)
                                .fontWeight(.bold)
                                .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                        )

                    VStack(alignment: .leading, spacing: 2) {
                        Text("Alex Chen")
                            .font(.subheadline)
                            .fontWeight(.semibold)
                        Text("Responder")
                            .font(.caption)
                            .foregroundColor(.gray)
                    }

                    Spacer()

                    Button(action: onClose) {
                        Image(systemName: "xmark")
                            .foregroundColor(.gray)
                    }
                    .accessibilityLabel("Close menu")
                }
                .padding(16)
                .background(Color.white)

                Divider()

                // Menu Items
                ScrollView {
                    VStack(spacing: 0) {
                        NavigationDrawerItem(
                            icon: "house.fill",
                            label: "Home",
                            action: onClose
                        )

                        NavigationDrawerItem(
                            icon: "person.fill",
                            label: "Profile",
                            action: onClose
                        )

                        NavigationDrawerItem(
                            icon: "heart.circle.fill",
                            label: "Health",
                            action: onClose
                        )

                        NavigationDrawerItem(
                            icon: "bell.fill",
                            label: "Notifications",
                            action: onClose
                        )

                        NavigationDrawerItem(
                            icon: "clock.fill",
                            label: "History",
                            action: onClose
                        )

                        NavigationDrawerItem(
                            icon: "hands.raised.fill",
                            label: "Volunteering",
                            action: onClose
                        )

                        NavigationDrawerItem(
                            icon: "mappin.circle.fill",
                            label: "Evacuation Info",
                            action: onClose
                        )

                        NavigationDrawerItem(
                            icon: "person.2.fill",
                            label: "Emergency Contacts",
                            action: onClose
                        )

                        NavigationDrawerItem(
                            icon: "lock.fill",
                            label: "Permissions",
                            action: onClose
                        )

                        Divider()
                            .padding(.vertical, 8)

                        NavigationDrawerItem(
                            icon: "gearshape.fill",
                            label: "Settings",
                            action: onClose
                        )

                        NavigationDrawerItem(
                            icon: "questionmark.circle.fill",
                            label: "Help & Support",
                            action: onClose
                        )

                        NavigationDrawerItem(
                            icon: "info.circle.fill",
                            label: "About",
                            action: onClose
                        )
                    }
                }

                Divider()

                // Footer
                VStack(spacing: 12) {
                    Button(action: onClose) {
                        HStack(spacing: 8) {
                            Image(systemName: "iphone.and.arrow.forward")
                                .foregroundColor(.red)
                            Text("Sign Out")
                                .foregroundColor(.red)
                            Spacer()
                        }
                        .font(.subheadline)
                        .padding(12)
                    }
                    .accessibilityLabel("Sign out")
                }
                .padding(16)
                .background(Color.white)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Color.white)
            .ignoresSafeArea()
        }
    }
}

// MARK: - Navigation Drawer Item Component
struct NavigationDrawerItem: View {
    let icon: String
    let label: String
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
            }
            .font(.subheadline)
            .padding(12)
        }
        .accessibilityLabel(label)
    }
}

#Preview {
    NavigationDrawer(isOpen: .constant(true), onClose: {})
}
