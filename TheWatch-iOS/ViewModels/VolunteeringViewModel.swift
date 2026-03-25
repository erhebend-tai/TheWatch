import Foundation

@Observable
final class VolunteeringViewModel {
    var volunteerProfile: VolunteerProfile?
    var isEnrolled = false
    var selectedRole: ResponderRole = .volunteer
    var selectedSkills: Set<String> = []
    var instantResponseEnabled = false
    var responsibilityRadius: Double = 2000
    var isLoading = false
    var isSaving = false
    var errorMessage: String?

    private let volunteerService: any VolunteerServiceProtocol

    let availableSkills = [
        "First Aid",
        "CPR",
        "Advanced Life Support",
        "Wilderness Rescue",
        "Mental Health Support",
        "Fire Suppression",
        "Rescue Operations",
        "Transportation",
        "Translation Services",
        "Counseling"
    ]

    init(volunteerService: any VolunteerServiceProtocol) {
        self.volunteerService = volunteerService
    }

    func loadProfile(userId: String) async {
        isLoading = true
        errorMessage = nil

        do {
            let profile = try await volunteerService.getVolunteerProfile(userId: userId)
            self.volunteerProfile = profile
            self.isEnrolled = profile.isEnrolled
            self.selectedRole = profile.role
            self.selectedSkills = Set(profile.skills)
            self.instantResponseEnabled = profile.instantResponseEnabled
            self.responsibilityRadius = profile.responsibilityRadius
        } catch {
            errorMessage = "Failed to load volunteer profile"
        }

        isLoading = false
    }

    func enrollAsVolunteer(userId: String) async {
        isSaving = true
        errorMessage = nil

        do {
            let skillsArray = Array(selectedSkills)
            try await volunteerService.enrollAsVolunteer(
                userId: userId,
                role: selectedRole,
                skills: skillsArray,
                radiusMeters: responsibilityRadius
            )
            isEnrolled = true
        } catch {
            errorMessage = "Failed to enroll as volunteer"
        }

        isSaving = false
    }

    func updateAvailability(userId: String) async {
        isSaving = true
        errorMessage = nil

        do {
            try await volunteerService.updateVolunteerAvailability(
                userId: userId,
                isAvailable: isEnrolled,
                radius: responsibilityRadius
            )
        } catch {
            errorMessage = "Failed to update availability"
        }

        isSaving = false
    }

    func toggleSkill(_ skill: String) {
        if selectedSkills.contains(skill) {
            selectedSkills.remove(skill)
        } else {
            selectedSkills.insert(skill)
        }
    }

    func addSkill(_ skill: String, userId: String) async {
        guard !selectedSkills.contains(skill) else { return }
        selectedSkills.insert(skill)

        do {
            try await volunteerService.addSkill(userId: userId, skill: skill)
        } catch {
            selectedSkills.remove(skill)
            errorMessage = "Failed to add skill"
        }
    }

    func removeSkill(_ skill: String, userId: String) async {
        guard selectedSkills.contains(skill) else { return }
        selectedSkills.remove(skill)

        do {
            try await volunteerService.removeSkill(userId: userId, skill: skill)
        } catch {
            selectedSkills.insert(skill)
            errorMessage = "Failed to remove skill"
        }
    }

    var radiusDisplay: String {
        if responsibilityRadius < 1000 {
            return String(format: "%.0f m", responsibilityRadius)
        } else {
            return String(format: "%.1f km", responsibilityRadius / 1000)
        }
    }
}
