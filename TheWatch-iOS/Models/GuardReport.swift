import Foundation

// ═══════════════════════════════════════════════════════════════
// Guard Reporting Models
// ═══════════════════════════════════════════════════════════════

enum GuardRole: String, Codable, CaseIterable {
    case professionalGuard = "ProfessionalGuard"
    case neighborhoodWatch = "NeighborhoodWatch"
    case propertyManager = "PropertyManager"
    case campusSecurity = "CampusSecurity"
    case eventStaff = "EventStaff"
    case parkRanger = "ParkRanger"
    case transitSecurity = "TransitSecurity"
    case custom = "Custom"
}

enum ReportCategory: String, Codable, CaseIterable {
    case suspiciousActivity = "SuspiciousActivity"
    case trespass = "Trespass"
    case propertyDamage = "PropertyDamage"
    case disturbance = "Disturbance"
    case medicalObservation = "MedicalObservation"
    case fireHazard = "FireHazard"
    case theftBurglary = "TheftBurglary"
    case assault = "Assault"
    case environmentalHazard = "EnvironmentalHazard"
    case vehicleIncident = "VehicleIncident"
    case welfareConcern = "WelfareConcern"
    case routineObservation = "RoutineObservation"
    case infrastructureIssue = "InfrastructureIssue"
    case other = "Other"
}

enum ReportSeverity: String, Codable, CaseIterable {
    case info = "Info"
    case low = "Low"
    case medium = "Medium"
    case high = "High"
    case critical = "Critical"
}

enum ReportStatus: String, Codable, CaseIterable {
    case draft = "Draft"
    case filed = "Filed"
    case underReview = "UnderReview"
    case escalated = "Escalated"
    case resolved = "Resolved"
    case expired = "Expired"
}

/// A structured report filed by a security guard or watch volunteer.
/// Can be UPGRADED to a full Watch call (SOS dispatch) if the situation warrants.
///
/// Example:
///   let report = try await guardService.fileReport(
///       guardUserId: myUserId,
///       category: .suspiciousActivity,
///       severity: .medium,
///       title: "Person trying doors at loading dock",
///       description: "Male, dark hoodie, tried 3 doors...",
///       latitude: 30.2672, longitude: -97.7431
///   )
///
///   // Situation worsens — escalate:
///   let result = try await guardService.escalateToWatchCall(
///       reportId: report.reportId,
///       reason: "Subject now attempting forced entry"
///   )
///   // result.responseRequestId → responders dispatched
struct GuardReport: Codable, Hashable, Identifiable {
    var id: String { reportId }

    let reportId: String
    let guardUserId: String
    let guardName: String
    let guardRole: GuardRole
    let badgeNumber: String?
    let category: ReportCategory
    let severity: ReportSeverity
    let title: String
    let description: String
    let latitude: Double
    let longitude: Double
    let accuracyMeters: Double?
    let locationDescription: String?
    let status: ReportStatus
    let createdAt: Date
    let filedAt: Date?
    let escalatedAt: Date?
    let resolvedAt: Date?
    let escalatedRequestId: String?
    let escalatedScope: String?
    let evidenceIds: [String]?
    let patrolRouteId: String?
    let postId: String?
    let propertyId: String?
    let reviewedBy: String?
    let reviewNotes: String?
    let resolutionNotes: String?
    let resolvedBy: String?
}

/// Guard enrollment profile.
struct GuardProfile: Codable, Hashable {
    let userId: String
    let name: String
    let role: GuardRole
    let badgeNumber: String?
    let licenseNumber: String?
    let organization: String?
    let canFileReports: Bool
    let canEscalateToWatch: Bool
    let canReviewOtherReports: Bool
    let canAccessCCTV: Bool
    let isOnDuty: Bool
    let shiftStartUtc: Date?
    let shiftEndUtc: Date?
    let enrolledAt: Date
    let lastActiveAt: Date
}

/// Result returned when a guard report is escalated to a Watch call.
struct EscalationResult: Codable, Hashable {
    let reportId: String
    let status: String
    let responseRequestId: String
    let scope: String
    let severity: String
    let escalatedAt: Date?
    let message: String
}
