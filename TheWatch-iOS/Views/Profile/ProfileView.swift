import SwiftUI

struct ProfileView: View {
    @Environment(MockUserService.self) var userService
    @State var viewModel: ProfileViewModel?
    @State private var isEditMode = false

    var body: some View {
        NavigationStack {
            ZStack {
                Color(red: 0.97, green: 0.97, blue: 0.97)
                    .ignoresSafeArea()

                VStack(spacing: 0) {
                    // Header
                    HStack {
                        Text("Profile")
                            .font(.headline)
                            .fontWeight(.bold)
                        Spacer()
                        Button(action: { isEditMode.toggle() }) {
                            Text(isEditMode ? "Done" : "Edit")
                                .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                        }
                        .accessibilityLabel(isEditMode ? "Save changes" : "Edit profile")
                    }
                    .padding(16)
                    .background(Color.white)

                    ScrollView {
                        VStack(spacing: 16) {
                            if let vm = viewModel {
                                // Identity Section
                                VStack(spacing: 12) {
                                    HStack {
                                        Text("Identity")
                                            .font(.subheadline)
                                            .fontWeight(.semibold)
                                        Spacer()
                                    }
                                    .padding(.horizontal, 12)

                                    VStack(spacing: 12) {
                                        // Photo placeholder
                                        ZStack(alignment: .bottomTrailing) {
                                            Circle()
                                                .fill(Color.gray.opacity(0.2))
                                                .frame(width: 100, height: 100)
                                                .overlay(
                                                    Image(systemName: "person.fill")
                                                        .font(.system(size: 40))
                                                        .foregroundColor(.gray)
                                                )

                                            if isEditMode {
                                                Circle()
                                                    .fill(Color(red: 0.9, green: 0.22, blue: 0.27))
                                                    .frame(width: 32, height: 32)
                                                    .overlay(
                                                        Image(systemName: "camera.fill")
                                                            .font(.system(size: 14))
                                                            .foregroundColor(.white)
                                                    )
                                            }
                                        }
                                        .frame(height: 100)

                                        VStack(spacing: 12) {
                                            TextField("First Name", text: $vm.firstName)
                                                .disabled(!isEditMode)
                                                .padding(12)
                                                .background(Color.white)
                                                .cornerRadius(8)
                                                .accessibilityLabel("First name")

                                            TextField("Last Name", text: $vm.lastName)
                                                .disabled(!isEditMode)
                                                .padding(12)
                                                .background(Color.white)
                                                .cornerRadius(8)
                                                .accessibilityLabel("Last name")

                                            TextField("Email", text: $vm.email)
                                                .disabled(true)
                                                .keyboardType(.emailAddress)
                                                .padding(12)
                                                .background(Color.gray.opacity(0.1))
                                                .cornerRadius(8)
                                                .accessibilityLabel("Email")

                                            TextField("Phone", text: $vm.phone)
                                                .disabled(!isEditMode)
                                                .keyboardType(.phonePad)
                                                .padding(12)
                                                .background(Color.white)
                                                .cornerRadius(8)
                                                .accessibilityLabel("Phone number")

                                            DatePicker("Date of Birth", selection: $vm.dateOfBirth, displayedComponents: .date)
                                                .disabled(!isEditMode)
                                                .padding(12)
                                                .background(Color.white)
                                                .cornerRadius(8)
                                                .accessibilityLabel("Date of birth")

                                            TextField("Blood Type", text: $vm.bloodType)
                                                .disabled(!isEditMode)
                                                .padding(12)
                                                .background(Color.white)
                                                .cornerRadius(8)
                                                .accessibilityLabel("Blood type")
                                        }
                                    }
                                    .padding(12)
                                    .background(Color.white)
                                    .cornerRadius(8)
                                }

                                // Medical Information Section
                                VStack(spacing: 12) {
                                    HStack {
                                        Text("Medical Information")
                                            .font(.subheadline)
                                            .fontWeight(.semibold)
                                        Spacer()
                                    }
                                    .padding(.horizontal, 12)

                                    VStack(spacing: 12) {
                                        TextField("Medical Conditions", text: $vm.medicalConditions)
                                            .disabled(!isEditMode)
                                            .padding(12)
                                            .background(Color.white)
                                            .cornerRadius(8)
                                            .accessibilityLabel("Medical conditions")

                                        TextField("Current Medications", text: $vm.medications)
                                            .disabled(!isEditMode)
                                            .padding(12)
                                            .background(Color.white)
                                            .cornerRadius(8)
                                            .accessibilityLabel("Current medications")

                                        TextField("Allergies", text: $vm.allergies)
                                            .disabled(!isEditMode)
                                            .padding(12)
                                            .background(Color.white)
                                            .cornerRadius(8)
                                            .accessibilityLabel("Allergies")
                                    }
                                    .padding(12)
                                    .background(Color.white)
                                    .cornerRadius(8)
                                }

                                // Emergency Configuration Section
                                VStack(spacing: 12) {
                                    HStack {
                                        Text("Emergency Configuration")
                                            .font(.subheadline)
                                            .fontWeight(.semibold)
                                        Spacer()
                                    }
                                    .padding(.horizontal, 12)

                                    VStack(spacing: 12) {
                                        VStack(alignment: .leading, spacing: 8) {
                                            Text("Default Alert Severity")
                                                .font(.caption)
                                                .fontWeight(.semibold)
                                            Picker("Severity", selection: $vm.defaultSeverity) {
                                                ForEach(AlertSeverity.allCases, id: \.self) { severity in
                                                    Text(severity.rawValue).tag(severity)
                                                }
                                            }
                                            .disabled(!isEditMode)
                                            .padding(12)
                                            .background(Color.white)
                                            .cornerRadius(8)
                                        }

                                        HStack {
                                            VStack(alignment: .leading, spacing: 4) {
                                                Text("Auto-Escalate to 911")
                                                    .font(.subheadline)
                                                    .fontWeight(.semibold)
                                                Text("After 2 minutes with no responders")
                                                    .font(.caption)
                                                    .foregroundColor(.gray)
                                            }
                                            Spacer()
                                            Toggle("", isOn: $vm.autoEscalate)
                                                .disabled(!isEditMode)
                                                .accessibilityLabel("Auto-escalate to 911")
                                        }
                                        .padding(12)
                                        .background(Color.white)
                                        .cornerRadius(8)

                                        TextField("Duress Code", text: $vm.duressCode)
                                            .disabled(!isEditMode)
                                            .keyboardType(.numberPad)
                                            .padding(12)
                                            .background(Color.white)
                                            .cornerRadius(8)
                                            .accessibilityLabel("Duress code")

                                        TextField("Safe Word", text: $vm.safeWord)
                                            .disabled(!isEditMode)
                                            .padding(12)
                                            .background(Color.white)
                                            .cornerRadius(8)
                                            .accessibilityLabel("Safe word")
                                    }
                                    .padding(12)
                                    .background(Color.white)
                                    .cornerRadius(8)
                                }

                                // Wearable Devices Section
                                VStack(spacing: 12) {
                                    HStack {
                                        Text("Wearable Devices")
                                            .font(.subheadline)
                                            .fontWeight(.semibold)
                                        Spacer()
                                    }
                                    .padding(.horizontal, 12)

                                    VStack(spacing: 12) {
                                        ForEach(vm.wearableDevices) { device in
                                            VStack(alignment: .leading, spacing: 8) {
                                                HStack {
                                                    VStack(alignment: .leading, spacing: 4) {
                                                        Text(device.name)
                                                            .font(.subheadline)
                                                            .fontWeight(.semibold)
                                                        Text(device.deviceType)
                                                            .font(.caption)
                                                            .foregroundColor(.gray)
                                                    }
                                                    Spacer()
                                                    Toggle("", isOn: Binding(
                                                        get: { vm.enabledDevices.contains(device.id) },
                                                        set: { enabled in
                                                            if enabled {
                                                                vm.enabledDevices.insert(device.id)
                                                            } else {
                                                                vm.enabledDevices.remove(device.id)
                                                            }
                                                        }
                                                    ))
                                                    .disabled(!isEditMode)
                                                    .accessibilityLabel("Enable \(device.name)")
                                                }

                                                if device.name == "Apple Watch" || device.name == "Heart Rate Monitor" {
                                                    VStack(spacing: 8) {
                                                        HStack {
                                                            Text("Heart Rate Detection")
                                                                .font(.caption)
                                                            Spacer()
                                                            Toggle("", isOn: $vm.implicitDetectionEnabled)
                                                                .disabled(!isEditMode)
                                                                .accessibilityLabel("Heart rate detection")
                                                        }

                                                        if vm.implicitDetectionEnabled {
                                                            HStack {
                                                                Text("Trigger at BPM >")
                                                                    .font(.caption)
                                                                Spacer()
                                                                TextField("120", text: $vm.heartRateThreshold)
                                                                    .disabled(!isEditMode)
                                                                    .keyboardType(.numberPad)
                                                                    .frame(width: 50)
                                                                    .padding(8)
                                                                    .background(Color.white)
                                                                    .cornerRadius(4)
                                                            }
                                                        }
                                                    }
                                                    .padding(8)
                                                    .background(Color.gray.opacity(0.05))
                                                    .cornerRadius(6)
                                                }
                                            }
                                            .padding(12)
                                            .background(Color.white)
                                            .cornerRadius(8)
                                        }

                                        if vm.wearableDevices.isEmpty {
                                            VStack(spacing: 8) {
                                                Image(systemName: "applewatch")
                                                    .font(.title2)
                                                    .foregroundColor(.gray)
                                                Text("No wearable devices connected")
                                                    .font(.caption)
                                                    .foregroundColor(.gray)
                                            }
                                            .frame(maxWidth: .infinity)
                                            .padding(24)
                                            .background(Color.gray.opacity(0.05))
                                            .cornerRadius(8)
                                        }
                                    }
                                    .padding(12)
                                    .background(Color.white)
                                    .cornerRadius(8)
                                }
                            }

                            Spacer()
                                .frame(height: 32)
                        }
                        .padding(12)
                    }
                }
            }
            .onAppear {
                if viewModel == nil {
                    viewModel = ProfileViewModel(userService: userService)
                    Task {
                        await viewModel?.loadUser()
                    }
                }
            }
        }
    }
}

#Preview {
    ProfileView()
        .environment(MockUserService())
}
