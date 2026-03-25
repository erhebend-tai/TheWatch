import Foundation

@Observable
final class ProfileViewModel {
    var user: User?
    var isLoading = false
    var isSaving = false
    var errorMessage: String?
    var successMessage: String?

    // Edit state
    var isEditing = false
    var editedUser: User?

    private let userService: any UserServiceProtocol

    init(userService: any UserServiceProtocol) {
        self.userService = userService
    }

    func loadUser(userId: String) async {
        isLoading = true
        errorMessage = nil

        do {
            self.user = try await userService.getUser(userId: userId)
        } catch {
            errorMessage = "Failed to load user profile"
        }

        isLoading = false
    }

    func startEditing() {
        editedUser = user
        isEditing = true
    }

    func cancelEditing() {
        editedUser = nil
        isEditing = false
    }

    func saveChanges() async {
        guard let editedUser = editedUser else { return }

        isSaving = true
        errorMessage = nil
        successMessage = nil

        do {
            self.user = try await userService.updateUser(editedUser)
            self.editedUser = nil
            self.isEditing = false
            successMessage = "Profile updated successfully"

            DispatchQueue.main.asyncAfter(deadline: .now() + 2) {
                self.successMessage = nil
            }
        } catch {
            errorMessage = "Failed to save changes"
        }

        isSaving = false
    }

    func addWearableDevice(_ device: WearableDevice) {
        editedUser?.wearableDevices.append(device)
    }

    func removeWearableDevice(id: String) {
        editedUser?.wearableDevices.removeAll { $0.id == id }
    }

    func toggleWearableDevice(id: String) {
        if let index = editedUser?.wearableDevices.firstIndex(where: { $0.id == id }) {
            editedUser?.wearableDevices[index].isActive.toggle()
        }
    }
}
