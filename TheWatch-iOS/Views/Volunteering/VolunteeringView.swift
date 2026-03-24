import SwiftUI

struct VolunteeringView: View {
    @Environment(MockVolunteerService.self) var volunteerService
    @State var viewModel: VolunteeringViewModel?
    @State private var isEditMode = false

    var body: some View {
        NavigationStack {
            ZStack {
                Color(red: 0.97, green: 0.97, blue: 0.97)
                    .ignoresSafeArea()

                VStack(spacing: 0) {
                    // Header
                    HStack {
                        Text("Volunteering")
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
                                // Volunteer Status
                                VStack(spacing: 12) {
                                    HStack {
                                        VStack(alignment: .leading, spacing: 4) {
                                            Text("Volunteer Status")
                                                .font(.subheadline)
                                                .fontWeight(.semibold)
                                            Text(vm.isVolunteer ? "Active" : "Inactive")
                                                .font(.caption)
                                                .foregroundColor(.gray)
                                        }
                                        Spacer()
                                        Toggle("", isOn: $vm.isVolunteer)
                                            .disabled(!isEditMode)
                                            .accessibilityLabel("Toggle volunteer status")
                                    }
                                    .padding(12)
                                    .background(Color.white)
                                    .cornerRadius(8)
                                }

                                if vm.isVolunteer {
                                    // Role Selection
                                    VStack(spacing: 12) {
                                        HStack {
                                            Text("Role")
                                                .font(.subheadline)
                                                .fontWeight(.semibold)
                                            Spacer()
                                        }

                                        Picker("Role", selection: $vm.selectedRole) {
                                            ForEach(ResponderRole.allCases, id: \.self) { role in
                                                Text(role.rawValue).tag(role)
                                            }
                                        }
                                        .disabled(!isEditMode)
                                        .padding(12)
                                        .background(Color.white)
                                        .cornerRadius(8)
                                    }

                                    // Skills Section
                                    VStack(spacing: 12) {
                                        HStack {
                                            Text("Skills & Certifications")
                                                .font(.subheadline)
                                                .fontWeight(.semibold)
                                            Spacer()
                                        }

                                        VStack(spacing: 8) {
                                            ForEach(vm.skills) { skill in
                                                HStack {
                                                    VStack(alignment: .leading, spacing: 2) {
                                                        Text(skill)
                                                            .font(.subheadline)
                                                            .fontWeight(.semibold)
                                                    }
                                                    Spacer()
                                                    if isEditMode {
                                                        Button(action: { vm.removeSkill(skill) }) {
                                                            Image(systemName: "xmark.circle.fill")
                                                                .foregroundColor(.red)
                                                        }
                                                        .accessibilityLabel("Remove \(skill)")
                                                    }
                                                }
                                                .padding(12)
                                                .background(Color.white)
                                                .cornerRadius(8)
                                            }

                                            if vm.skills.isEmpty {
                                                Text("No skills added yet")
                                                    .font(.caption)
                                                    .foregroundColor(.gray)
                                                    .padding(12)
                                                    .background(Color.white)
                                                    .cornerRadius(8)
                                            }

                                            if isEditMode {
                                                HStack(spacing: 8) {
                                                    Picker("Add Skill", selection: $vm.newSkill) {
                                                        Text("CPR Certified").tag("CPR Certified")
                                                        Text("First Aid").tag("First Aid")
                                                        Text("EMT License").tag("EMT License")
                                                        Text("Trauma Training").tag("Trauma Training")
                                                        Text("Mental Health Support").tag("Mental Health Support")
                                                    }
                                                    .frame(maxWidth: .infinity)

                                                    Button(action: { vm.addSkill(vm.newSkill) }) {
                                                        Image(systemName: "plus.circle.fill")
                                                            .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                                                    }
                                                    .disabled(vm.skills.contains(vm.newSkill))
                                                    .accessibilityLabel("Add skill")
                                                }
                                                .padding(12)
                                                .background(Color.white)
                                                .cornerRadius(8)
                                            }
                                        }
                                    }

                                    // Availability Section
                                    VStack(spacing: 12) {
                                        HStack {
                                            Text("Availability")
                                                .font(.subheadline)
                                                .fontWeight(.semibold)
                                            Spacer()
                                        }

                                        VStack(spacing: 12) {
                                            HStack {
                                                VStack(alignment: .leading, spacing: 4) {
                                                    Text("Available Now")
                                                        .font(.subheadline)
                                                        .fontWeight(.semibold)
                                                    Text(vm.isAvailable ? "Ready to respond" : "Currently unavailable")
                                                        .font(.caption)
                                                        .foregroundColor(.gray)
                                                }
                                                Spacer()
                                                Toggle("", isOn: $vm.isAvailable)
                                                    .disabled(!isEditMode)
                                                    .accessibilityLabel("Toggle availability")
                                            }
                                            .padding(12)
                                            .background(Color.white)
                                            .cornerRadius(8)

                                            HStack {
                                                VStack(alignment: .leading, spacing: 4) {
                                                    Text("Response Radius")
                                                        .font(.subheadline)
                                                        .fontWeight(.semibold)
                                                    Text("\(vm.responseRadius) km")
                                                        .font(.caption)
                                                        .foregroundColor(.gray)
                                                }
                                                Spacer()
                                                Slider(value: $vm.responseRadius, in: 1...50, step: 1)
                                                    .disabled(!isEditMode)
                                                    .frame(maxWidth: 120)
                                                    .accessibilityLabel("Response radius")
                                            }
                                            .padding(12)
                                            .background(Color.white)
                                            .cornerRadius(8)

                                            HStack {
                                                VStack(alignment: .leading, spacing: 4) {
                                                    Text("Max Distance to Travel")
                                                        .font(.subheadline)
                                                        .fontWeight(.semibold)
                                                    Text("\(vm.maxDistance) km")
                                                        .font(.caption)
                                                        .foregroundColor(.gray)
                                                }
                                                Spacer()
                                                Slider(value: $vm.maxDistance, in: 5...100, step: 5)
                                                    .disabled(!isEditMode)
                                                    .frame(maxWidth: 120)
                                                    .accessibilityLabel("Maximum travel distance")
                                            }
                                            .padding(12)
                                            .background(Color.white)
                                            .cornerRadius(8)
                                        }
                                    }

                                    // Statistics Section
                                    VStack(spacing: 12) {
                                        HStack {
                                            Text("Statistics")
                                                .font(.subheadline)
                                                .fontWeight(.semibold)
                                            Spacer()
                                        }

                                        HStack(spacing: 12) {
                                            StatCard(
                                                label: "Responses",
                                                value: "\(vm.totalResponses)",
                                                icon: "person.fill"
                                            )

                                            StatCard(
                                                label: "Avg Time",
                                                value: "\(vm.averageResponseTime)m",
                                                icon: "clock.fill"
                                            )

                                            StatCard(
                                                label: "Rating",
                                                value: String(format: "%.1f", vm.rating),
                                                icon: "star.fill"
                                            )
                                        }
                                    }
                                }

                                Spacer()
                                    .frame(height: 32)
                            }
                        }
                        .padding(12)
                    }
                }
            }
            .onAppear {
                if viewModel == nil {
                    viewModel = VolunteeringViewModel(volunteerService: volunteerService)
                    Task {
                        await viewModel?.loadProfile()
                    }
                }
            }
        }
    }
}

struct StatCard: View {
    let label: String
    let value: String
    let icon: String

    var body: some View {
        VStack(spacing: 8) {
            Image(systemName: icon)
                .font(.headline)
                .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))

            Text(value)
                .font(.headline)
                .fontWeight(.semibold)

            Text(label)
                .font(.caption)
                .foregroundColor(.gray)
        }
        .frame(maxWidth: .infinity)
        .padding(12)
        .background(Color.white)
        .cornerRadius(8)
    }
}

#Preview {
    VolunteeringView()
        .environment(MockVolunteerService())
}
