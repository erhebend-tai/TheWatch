import SwiftUI

struct SignUpView: View {
    @Environment(MockAuthService.self) var authService
    @State var router = AppRouter()
    @State var viewModel: SignUpViewModel?

    var body: some View {
        NavigationStack(path: $router.navigationPath) {
            ZStack {
                Color(red: 0.97, green: 0.97, blue: 0.97)
                    .ignoresSafeArea()

                VStack(spacing: 0) {
                    // Progress indicator
                    HStack(spacing: 0) {
                        ForEach(1...3, id: \.self) { step in
                            ZStack {
                                Circle()
                                    .fill(step <= (viewModel?.currentStep ?? 1) ? Color(red: 0.9, green: 0.22, blue: 0.27) : Color.gray.opacity(0.3))
                                Text("\(step)")
                                    .foregroundColor(.white)
                                    .fontWeight(.bold)
                            }
                            .frame(height: 40)

                            if step < 3 {
                                Divider()
                                    .frame(height: 2)
                                    .background(step < (viewModel?.currentStep ?? 1) ? Color(red: 0.9, green: 0.22, blue: 0.27) : Color.gray.opacity(0.3))
                            }
                        }
                    }
                    .padding(.horizontal, 16)
                    .padding(.vertical, 20)

                    // Step content
                    TabView(selection: $viewModel!.currentStep) {
                        Step1View(viewModel: viewModel!)
                            .tag(1)

                        Step2View(viewModel: viewModel!)
                            .tag(2)

                        Step3View(viewModel: viewModel!)
                            .tag(3)
                    }
                    .tabViewStyle(.page(indexDisplayMode: .never))

                    // Navigation buttons
                    HStack(spacing: 12) {
                        if (viewModel?.currentStep ?? 1) > 1 {
                            Button("Back") {
                                viewModel?.previousStep()
                            }
                            .frame(maxWidth: .infinity)
                            .padding(12)
                            .background(Color.gray.opacity(0.2))
                            .foregroundColor(.black)
                            .cornerRadius(8)
                            .accessibilityLabel("Go back to previous step")
                        }

                        Button(action: {
                            if (viewModel?.currentStep ?? 1) < 3 {
                                viewModel?.nextStep()
                            } else {
                                Task {
                                    await viewModel?.completeSignUp()
                                }
                            }
                        }) {
                            if viewModel?.isLoading == true {
                                HStack(spacing: 8) {
                                    ProgressView()
                                        .tint(.white)
                                    Text("Creating...")
                                }
                            } else {
                                Text((viewModel?.currentStep ?? 1) < 3 ? "Next" : "Create Account")
                                    .fontWeight(.semibold)
                            }
                        }
                        .frame(maxWidth: .infinity)
                        .padding(12)
                        .background(Color(red: 0.9, green: 0.22, blue: 0.27))
                        .foregroundColor(.white)
                        .cornerRadius(8)
                        .disabled(viewModel?.isLoading == true)
                        .accessibilityLabel((viewModel?.currentStep ?? 1) < 3 ? "Continue to next step" : "Create account")
                    }
                    .padding(16)
                }
                .navigationDestination(for: AppRouter.Destination.self) { destination in
                    router.view(for: destination)
                }
            }
            .onAppear {
                if viewModel == nil {
                    viewModel = SignUpViewModel(authService: authService)
                }
            }
        }
    }
}

// MARK: - Step 1: Basic Info
struct Step1View: View {
    var viewModel: SignUpViewModel

    var body: some View {
        ScrollView {
            VStack(spacing: 16) {
                Text("Step 1: Basic Information")
                    .font(.headline)
                    .padding(.horizontal)

                VStack(spacing: 12) {
                    TextField("First Name", text: $viewModel.firstName)
                        .textContentType(.givenName)
                        .padding(12)
                        .background(Color.white)
                        .cornerRadius(8)
                        .accessibilityLabel("First name")

                    TextField("Last Name", text: $viewModel.lastName)
                        .textContentType(.familyName)
                        .padding(12)
                        .background(Color.white)
                        .cornerRadius(8)
                        .accessibilityLabel("Last name")

                    TextField("Email", text: $viewModel.email)
                        .textContentType(.emailAddress)
                        .keyboardType(.emailAddress)
                        .padding(12)
                        .background(Color.white)
                        .cornerRadius(8)
                        .accessibilityLabel("Email address")

                    TextField("Phone", text: $viewModel.phone)
                        .textContentType(.telephoneNumber)
                        .keyboardType(.phonePad)
                        .padding(12)
                        .background(Color.white)
                        .cornerRadius(8)
                        .accessibilityLabel("Phone number")

                    DatePicker("Date of Birth", selection: $viewModel.dateOfBirth, displayedComponents: .date)
                        .padding(12)
                        .background(Color.white)
                        .cornerRadius(8)
                        .accessibilityLabel("Date of birth")

                    SecureField("Password", text: $viewModel.password)
                        .padding(12)
                        .background(Color.white)
                        .cornerRadius(8)
                        .accessibilityLabel("Password")

                    PasswordStrengthMeter(strength: viewModel.passwordStrength)

                    SecureField("Confirm Password", text: $viewModel.confirmPassword)
                        .padding(12)
                        .background(Color.white)
                        .cornerRadius(8)
                        .accessibilityLabel("Confirm password")
                }
                .padding(.horizontal)

                Spacer()
            }
            .padding(.vertical)
        }
    }
}

// MARK: - Step 2: Emergency Contacts
struct Step2View: View {
    var viewModel: SignUpViewModel
    @State private var newContactName = ""
    @State private var newContactPhone = ""
    @State private var newContactEmail = ""
    @State private var newContactRelationship: ContactRelationship = .family

    var body: some View {
        ScrollView {
            VStack(spacing: 16) {
                Text("Step 2: Emergency Contacts (up to 3)")
                    .font(.headline)
                    .padding(.horizontal)

                // Add contact form
                VStack(spacing: 12) {
                    TextField("Name", text: $newContactName)
                        .padding(12)
                        .background(Color.white)
                        .cornerRadius(8)

                    TextField("Phone", text: $newContactPhone)
                        .keyboardType(.phonePad)
                        .padding(12)
                        .background(Color.white)
                        .cornerRadius(8)

                    TextField("Email", text: $newContactEmail)
                        .keyboardType(.emailAddress)
                        .padding(12)
                        .background(Color.white)
                        .cornerRadius(8)

                    Picker("Relationship", selection: $newContactRelationship) {
                        ForEach(ContactRelationship.allCases, id: \.self) { relationship in
                            Text(relationship.rawValue).tag(relationship)
                        }
                    }
                    .padding(12)
                    .background(Color.white)
                    .cornerRadius(8)

                    Button(action: {
                        if !newContactName.isEmpty && !newContactPhone.isEmpty {
                            let contact = EmergencyContact(
                                name: newContactName,
                                phone: newContactPhone,
                                email: newContactEmail,
                                relationship: newContactRelationship,
                                priority: viewModel.emergencyContacts.count + 1
                            )
                            viewModel.addEmergencyContact(contact)
                            newContactName = ""
                            newContactPhone = ""
                            newContactEmail = ""
                            newContactRelationship = .family
                        }
                    }) {
                        Text("Add Contact")
                            .frame(maxWidth: .infinity)
                            .padding(12)
                            .background(Color(red: 0.9, green: 0.22, blue: 0.27))
                            .foregroundColor(.white)
                            .cornerRadius(8)
                    }
                    .disabled(viewModel.emergencyContacts.count >= 3 || newContactName.isEmpty)
                }
                .padding(.horizontal)

                // Listed contacts
                ForEach(viewModel.emergencyContacts) { contact in
                    VStack(alignment: .leading, spacing: 8) {
                        HStack {
                            VStack(alignment: .leading, spacing: 4) {
                                Text(contact.name)
                                    .fontWeight(.semibold)
                                Text(contact.relationship.rawValue)
                                    .font(.caption)
                                    .foregroundColor(.gray)
                            }
                            Spacer()
                            Button(action: {
                                viewModel.removeEmergencyContact(contact.id)
                            }) {
                                Image(systemName: "xmark.circle.fill")
                                    .foregroundColor(.red)
                            }
                            .accessibilityLabel("Remove contact")
                        }
                        Text(contact.phone)
                            .font(.caption)
                    }
                    .padding(12)
                    .background(Color.white)
                    .cornerRadius(8)
                    .padding(.horizontal)
                }

                Spacer()
            }
            .padding(.vertical)
        }
    }
}

// MARK: - Step 3: Review & EULA
struct Step3View: View {
    var viewModel: SignUpViewModel

    var body: some View {
        ScrollView {
            VStack(spacing: 16) {
                Text("Step 3: Review & Accept")
                    .font(.headline)
                    .padding(.horizontal)

                // Summary
                VStack(spacing: 12) {
                    SummaryRow(label: "Name", value: "\(viewModel.firstName) \(viewModel.lastName)")
                    SummaryRow(label: "Email", value: viewModel.email)
                    SummaryRow(label: "Phone", value: viewModel.phone)
                    SummaryRow(label: "Contacts", value: "\(viewModel.emergencyContacts.count)")
                }
                .padding(12)
                .background(Color.white)
                .cornerRadius(8)
                .padding(.horizontal)

                // EULA
                VStack(spacing: 12) {
                    ScrollViewReader { reader in
                        ScrollView {
                            VStack(alignment: .leading, spacing: 12) {
                                Text("End User License Agreement")
                                    .fontWeight(.bold)
                                    .id("top")

                                Text("""
                                TheWatch is a life-safety emergency response application. By creating an account, you agree to:

                                1. Provide accurate personal information
                                2. Maintain the confidentiality of your account
                                3. Notify us immediately of unauthorized access
                                4. Use this service lawfully and responsibly
                                5. Not interfere with service operations
                                6. Accept liability limitations

                                We provide this service "as is" and disclaim all warranties. We are not liable for indirect, incidental, special, consequential, or punitive damages.

                                By accepting this agreement, you acknowledge understanding and agreement to all terms.
                                """)
                                .font(.caption)

                                Spacer()
                                    .id("bottom")
                                    .frame(height: 1)
                            }
                            .padding(12)
                        }
                        .frame(height: 200)
                        .border(Color.gray.opacity(0.3))

                        Button(action: {
                            reader.scrollTo("bottom", anchor: .bottom)
                        }) {
                            Text("Scroll to Bottom")
                                .font(.caption)
                                .foregroundColor(.blue)
                        }
                        .padding(.top, 8)
                    }
                }
                .padding(.horizontal)

                // EULA Toggle
                Toggle("I Accept the End User License Agreement", isOn: $viewModel.acceptEULA)
                    .padding(12)
                    .background(Color.white)
                    .cornerRadius(8)
                    .padding(.horizontal)
                    .accessibilityLabel("Accept EULA")

                if let error = viewModel.errorMessage {
                    Text(error)
                        .font(.caption)
                        .foregroundColor(.red)
                        .padding(12)
                        .background(Color.red.opacity(0.1))
                        .cornerRadius(8)
                        .padding(.horizontal)
                }

                Spacer()
            }
            .padding(.vertical)
        }
    }
}

struct SummaryRow: View {
    let label: String
    let value: String

    var body: some View {
        HStack {
            Text(label)
                .fontWeight(.semibold)
            Spacer()
            Text(value)
                .foregroundColor(.gray)
        }
    }
}

#Preview {
    SignUpView()
        .environment(MockAuthService())
}
