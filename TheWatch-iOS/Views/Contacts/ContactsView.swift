import SwiftUI

struct ContactsView: View {
    @State private var contacts: [EmergencyContact] = [
        EmergencyContact(name: "Sarah Johnson", phone: "555-0101", email: "sarah@example.com", relationship: .family, priority: 1),
        EmergencyContact(name: "John Smith", phone: "555-0102", email: "john@example.com", relationship: .friend, priority: 2),
        EmergencyContact(name: "Dr. Michael Lee", phone: "555-0103", email: "m.lee@hospital.com", relationship: .medical, priority: 3)
    ]
    @State private var showAddContact = false
    @State private var editingContact: EmergencyContact? = nil
    @State private var newContactName = ""
    @State private var newContactPhone = ""
    @State private var newContactEmail = ""
    @State private var newContactRelationship: ContactRelationship = .family
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
                    Button(action: { showAddContact = true }) {
                        Image(systemName: "plus.circle.fill")
                            .font(.title3)
                            .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                    }
                    .accessibilityLabel("Add emergency contact")
                }
                .padding(16)
                .background(Color.white)

                Divider()

                ScrollView {
                    VStack(spacing: 12) {
                        Text("Emergency Contacts")
                            .font(.headline)
                            .fontWeight(.bold)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(.horizontal, 16)
                            .padding(.vertical, 12)

                        if contacts.isEmpty {
                            VStack(spacing: 12) {
                                Image(systemName: "person.crop.circle.badge.questionmark")
                                    .font(.system(size: 40))
                                    .foregroundColor(.gray)
                                Text("No Emergency Contacts")
                                    .font(.subheadline)
                                    .fontWeight(.semibold)
                                Text("Add at least one emergency contact to receive notifications during emergencies")
                                    .font(.caption)
                                    .foregroundColor(.gray)
                                    .multilineTextAlignment(.center)
                                Button(action: { showAddContact = true }) {
                                    Text("Add Contact")
                                        .frame(maxWidth: .infinity)
                                        .padding(12)
                                        .background(Color(red: 0.9, green: 0.22, blue: 0.27))
                                        .foregroundColor(.white)
                                        .cornerRadius(8)
                                }
                                .accessibilityLabel("Add emergency contact")
                            }
                            .padding(16)
                            .frame(maxWidth: .infinity)
                            .background(Color.white)
                            .cornerRadius(8)
                            .padding(.horizontal, 16)
                        } else {
                            ForEach($contacts) { $contact in
                                ContactRow(contact: $contact, onEdit: { editingContact = contact }, onDelete: {
                                    contacts.removeAll { $0.id == contact.id }
                                })
                            }
                            .padding(.horizontal, 16)
                        }

                        Spacer()
                            .frame(height: 20)
                    }
                    .padding(.vertical, 12)
                }
            }
        }
        .sheet(isPresented: $showAddContact) {
            AddContactSheet(
                isPresented: $showAddContact,
                name: $newContactName,
                phone: $newContactPhone,
                email: $newContactEmail,
                relationship: $newContactRelationship,
                onAdd: {
                    let newContact = EmergencyContact(
                        name: newContactName,
                        phone: newContactPhone,
                        email: newContactEmail,
                        relationship: newContactRelationship,
                        priority: contacts.count + 1
                    )
                    contacts.append(newContact)
                    resetForm()
                    showAddContact = false
                }
            )
        }
    }

    private func resetForm() {
        newContactName = ""
        newContactPhone = ""
        newContactEmail = ""
        newContactRelationship = .family
    }
}

// MARK: - Contact Row Component
struct ContactRow: View {
    @Binding var contact: EmergencyContact
    let onEdit: () -> Void
    let onDelete: () -> Void
    @State private var showDeleteConfirmation = false

    var body: some View {
        VStack(spacing: 12) {
            HStack(spacing: 12) {
                VStack(alignment: .center, spacing: 0) {
                    Circle()
                        .fill(Color(red: 0.9, green: 0.22, blue: 0.27).opacity(0.2))
                        .frame(width: 48, height: 48)
                        .overlay(
                            Text(contact.initials)
                                .font(.headline)
                                .fontWeight(.bold)
                                .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                        )
                }

                VStack(alignment: .leading, spacing: 4) {
                    HStack(spacing: 8) {
                        Text(contact.name)
                            .font(.subheadline)
                            .fontWeight(.semibold)
                        Text(contact.relationship.rawValue)
                            .font(.caption)
                            .foregroundColor(.white)
                            .padding(.horizontal, 6)
                            .padding(.vertical, 2)
                            .background(Color.gray.opacity(0.4))
                            .cornerRadius(4)
                    }
                    HStack(spacing: 12) {
                        HStack(spacing: 4) {
                            Image(systemName: "phone.fill")
                                .font(.caption)
                            Text(contact.phone)
                                .font(.caption)
                        }
                        .foregroundColor(.gray)

                        if !contact.email.isEmpty {
                            HStack(spacing: 4) {
                                Image(systemName: "envelope.fill")
                                    .font(.caption)
                                Text(contact.email)
                                    .font(.caption)
                            }
                            .foregroundColor(.gray)
                        }
                    }
                }

                Spacer()

                HStack(spacing: 8) {
                    Menu {
                        Button(action: onEdit) {
                            HStack(spacing: 8) {
                                Image(systemName: "pencil")
                                Text("Edit")
                            }
                        }
                        .accessibilityLabel("Edit contact")

                        Button(action: { showDeleteConfirmation = true }) {
                            HStack(spacing: 8) {
                                Image(systemName: "trash")
                                Text("Delete")
                            }
                        }
                        .accessibilityLabel("Delete contact")
                    } label: {
                        Image(systemName: "ellipsis")
                            .foregroundColor(.gray)
                    }
                    .accessibilityLabel("Contact options")
                }
            }
            .padding(12)

            if contact.priority == 1 {
                HStack(spacing: 6) {
                    Image(systemName: "star.fill")
                        .font(.caption2)
                        .foregroundColor(.orange)
                    Text("Primary contact")
                        .font(.caption2)
                        .foregroundColor(.gray)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(8)
                .background(Color.orange.opacity(0.1))
                .cornerRadius(4)
            }
        }
        .background(Color.white)
        .cornerRadius(8)
        .confirmationDialog("Delete Contact", isPresented: $showDeleteConfirmation, actions: {
            Button("Delete", role: .destructive) {
                onDelete()
            }
            Button("Cancel", role: .cancel) {}
        }, message: {
            Text("Are you sure you want to remove \(contact.name) from your emergency contacts?")
        })
    }
}

// MARK: - Add Contact Sheet
struct AddContactSheet: View {
    @Binding var isPresented: Bool
    @Binding var name: String
    @Binding var phone: String
    @Binding var email: String
    @Binding var relationship: ContactRelationship
    let onAdd: () -> Void

    var body: some View {
        ZStack {
            Color(red: 0.97, green: 0.97, blue: 0.97)
                .ignoresSafeArea()

            VStack(spacing: 0) {
                HStack {
                    Text("Add Emergency Contact")
                        .font(.headline)
                        .fontWeight(.bold)
                    Spacer()
                    Button(action: { isPresented = false }) {
                        Image(systemName: "xmark.circle.fill")
                            .foregroundColor(.gray)
                    }
                    .accessibilityLabel("Close add contact sheet")
                }
                .padding(16)
                .background(Color.white)

                Divider()

                ScrollView {
                    VStack(spacing: 16) {
                        VStack(alignment: .leading, spacing: 8) {
                            Text("Name")
                                .font(.subheadline)
                                .fontWeight(.semibold)
                            TextField("Full name", text: $name)
                                .padding(12)
                                .background(Color.white)
                                .cornerRadius(8)
                                .accessibilityLabel("Contact name")
                        }

                        VStack(alignment: .leading, spacing: 8) {
                            Text("Phone")
                                .font(.subheadline)
                                .fontWeight(.semibold)
                            TextField("Phone number", text: $phone)
                                .keyboardType(.phonePad)
                                .padding(12)
                                .background(Color.white)
                                .cornerRadius(8)
                                .accessibilityLabel("Contact phone")
                        }

                        VStack(alignment: .leading, spacing: 8) {
                            Text("Email")
                                .font(.subheadline)
                                .fontWeight(.semibold)
                            TextField("Email (optional)", text: $email)
                                .keyboardType(.emailAddress)
                                .padding(12)
                                .background(Color.white)
                                .cornerRadius(8)
                                .accessibilityLabel("Contact email")
                        }

                        VStack(alignment: .leading, spacing: 8) {
                            Text("Relationship")
                                .font(.subheadline)
                                .fontWeight(.semibold)
                            Picker("Relationship", selection: $relationship) {
                                ForEach(ContactRelationship.allCases, id: \\.self) { rel in
                                    Text(rel.rawValue).tag(rel)
                                }
                            }
                            .padding(12)
                            .background(Color.white)
                            .cornerRadius(8)
                            .accessibilityLabel("Contact relationship")
                        }

                        Spacer()
                    }
                    .padding(16)
                }

                Divider()

                HStack(spacing: 12) {
                    Button(action: { isPresented = false }) {
                        Text("Cancel")
                            .frame(maxWidth: .infinity)
                            .padding(12)
                            .background(Color.gray.opacity(0.1))
                            .foregroundColor(.black)
                            .cornerRadius(8)
                    }
                    .accessibilityLabel("Cancel adding contact")

                    Button(action: onAdd) {
                        Text("Add Contact")
                            .frame(maxWidth: .infinity)
                            .padding(12)
                            .background(Color(red: 0.9, green: 0.22, blue: 0.27))
                            .foregroundColor(.white)
                            .cornerRadius(8)
                    }
                    .disabled(name.isEmpty || phone.isEmpty)
                    .opacity(name.isEmpty || phone.isEmpty ? 0.6 : 1.0)
                    .accessibilityLabel("Add emergency contact")
                }
                .padding(16)
                .background(Color.white)
            }
        }
    }
}

// MARK: - Extension for Contact Initials
extension EmergencyContact {
    var initials: String {
        let components = name.split(separator: " ")
        if components.count >= 2 {
            return String(components[0].prefix(1)) + String(components[1].prefix(1))
        } else {
            return String(name.prefix(1))
        }
    }
}

#Preview {
    ContactsView()
}
