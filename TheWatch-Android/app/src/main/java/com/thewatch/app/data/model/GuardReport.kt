package com.thewatch.app.data.model

/**
 * Guard roles for security reporting.
 */
enum class GuardRole {
    ProfessionalGuard,
    NeighborhoodWatch,
    PropertyManager,
    CampusSecurity,
    EventStaff,
    ParkRanger,
    TransitSecurity,
    Custom
}

/**
 * Report categories — determines severity suggestions and supervisor routing.
 */
enum class ReportCategory {
    SuspiciousActivity,
    Trespass,
    PropertyDamage,
    Disturbance,
    MedicalObservation,
    FireHazard,
    TheftBurglary,
    Assault,
    EnvironmentalHazard,
    VehicleIncident,
    WelfareConcern,
    RoutineObservation,
    InfrastructureIssue,
    Other
}

enum class ReportSeverity { Info, Low, Medium, High, Critical }

enum class ReportStatus { Draft, Filed, UnderReview, Escalated, Resolved, Expired }

/**
 * A structured report filed by a security guard.
 * Can be UPGRADED to a full Watch call (SOS dispatch) via the escalate endpoint.
 *
 * Example — file a report then escalate:
 *   val report = guardRepository.fileReport(
 *       guardUserId = myUserId,
 *       category = ReportCategory.SuspiciousActivity,
 *       severity = ReportSeverity.Medium,
 *       title = "Person trying doors at loading dock",
 *       description = "Male, dark hoodie, tried 3 doors...",
 *       latitude = 30.2672,
 *       longitude = -97.7431
 *   )
 *
 *   // Situation worsens — escalate to Watch call
 *   val escalation = guardRepository.escalateToWatchCall(
 *       reportId = report.reportId,
 *       reason = "Subject now attempting forced entry"
 *   )
 *   // escalation.responseRequestId → responders dispatched
 */
data class GuardReport(
    val reportId: String = "",
    val guardUserId: String = "",
    val guardName: String = "",
    val guardRole: String = "NeighborhoodWatch",
    val badgeNumber: String? = null,
    val category: String = "Other",
    val severity: String = "Low",
    val title: String = "",
    val description: String = "",
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val accuracyMeters: Double? = null,
    val locationDescription: String? = null,
    val status: String = "Draft",
    val createdAt: String = "",
    val filedAt: String? = null,
    val escalatedAt: String? = null,
    val resolvedAt: String? = null,
    val escalatedRequestId: String? = null,
    val escalatedScope: String? = null,
    val evidenceIds: List<String>? = null,
    val patrolRouteId: String? = null,
    val postId: String? = null,
    val propertyId: String? = null,
    val reviewedBy: String? = null,
    val reviewNotes: String? = null,
    val resolutionNotes: String? = null,
    val resolvedBy: String? = null
)

/**
 * Guard enrollment profile.
 */
data class GuardProfile(
    val userId: String = "",
    val name: String = "",
    val role: String = "NeighborhoodWatch",
    val badgeNumber: String? = null,
    val licenseNumber: String? = null,
    val organization: String? = null,
    val canFileReports: Boolean = true,
    val canEscalateToWatch: Boolean = true,
    val canReviewOtherReports: Boolean = false,
    val canAccessCCTV: Boolean = false,
    val isOnDuty: Boolean = false,
    val shiftStartUtc: String? = null,
    val shiftEndUtc: String? = null
)

/**
 * Result of escalating a report to a Watch call.
 */
data class EscalationResult(
    val reportId: String = "",
    val status: String = "Escalated",
    val responseRequestId: String = "",
    val scope: String = "Neighborhood",
    val severity: String = "High",
    val escalatedAt: String? = null,
    val message: String = ""
)
