/**
 * ═══════════════════════════════════════════════════════════════════════════════
 * WRITE-AHEAD LOG — MedicalProfile.kt
 * ═══════════════════════════════════════════════════════════════════════════════
 * Purpose:   HIPAA-protected medical profile data model for TheWatch safety app.
 *            Stores blood type, allergies, medications, medical conditions, and
 *            emergency medical notes. This data is shared with first responders
 *            during an escalation event so they arrive prepared.
 * Date:      2026-03-24
 * Author:    Claude (Anthropic)
 * Deps:      kotlinx.serialization
 * Package:   com.thewatch.app.data.model
 *
 * Usage Example:
 *   val profile = MedicalProfile(
 *       userId = "user_001",
 *       bloodType = BloodType.O_POSITIVE,
 *       allergies = listOf("Penicillin", "Shellfish"),
 *       medications = listOf("Albuterol inhaler", "Lisinopril 10mg"),
 *       medicalConditions = listOf("Asthma", "Hypertension"),
 *       emergencyMedicalNotes = "Carries EpiPen in left pocket at all times"
 *   )
 *
 * HIPAA Compliance Notes:
 *   - Data MUST be encrypted at rest (Room + SQLCipher or EncryptedSharedPreferences)
 *   - Data MUST be encrypted in transit (TLS 1.3 minimum)
 *   - Access MUST be logged (see WatchLogger / LoggingPort)
 *   - Minimum-necessary principle: only share with responders during active escalation
 *   - User MUST consent before any medical data is stored (EULA + explicit opt-in)
 *   - Right to delete: user can clear all medical data at any time
 *   - PHI audit trail required for production (see data/logging/LoggingPort.kt)
 *
 * Related Standards:
 *   - HIPAA Privacy Rule 45 CFR 164.502
 *   - HIPAA Security Rule 45 CFR 164.312 (encryption requirements)
 *   - HITECH Act breach notification (45 CFR 164.404)
 *   - NIST SP 800-66 (HIPAA security implementation guide)
 *   - HL7 FHIR Patient resource (for future interop)
 * ═══════════════════════════════════════════════════════════════════════════════
 */
package com.thewatch.app.data.model

import kotlinx.serialization.Serializable

/**
 * Supported ABO/Rh blood types.
 * Conforms to ISBT 128 coding (International Society of Blood Transfusion).
 *
 * Example:
 *   val bt = BloodType.AB_NEGATIVE
 *   println(bt.displayName) // "AB-"
 */
@Serializable
enum class BloodType(val displayName: String) {
    A_POSITIVE("A+"),
    A_NEGATIVE("A-"),
    B_POSITIVE("B+"),
    B_NEGATIVE("B-"),
    AB_POSITIVE("AB+"),
    AB_NEGATIVE("AB-"),
    O_POSITIVE("O+"),
    O_NEGATIVE("O-"),
    UNKNOWN("Unknown");

    companion object {
        /** Parse from display string, e.g. "O+" -> O_POSITIVE */
        fun fromDisplayName(name: String): BloodType =
            entries.firstOrNull { it.displayName.equals(name, ignoreCase = true) } ?: UNKNOWN

        /** All displayable options for a dropdown */
        val dropdownOptions: List<String> = entries.map { it.displayName }
    }
}

/**
 * HIPAA-protected medical profile attached to a User.
 *
 * This model is stored locally (Room, encrypted) and synced to the backend
 * only during an active emergency escalation or explicit user-initiated sync.
 *
 * Fields:
 *   - userId:                 Foreign key to User.id
 *   - bloodType:              ABO/Rh blood type (dropdown selection)
 *   - allergies:              Known allergies (drugs, food, environmental)
 *   - medications:            Current medications with dosage
 *   - medicalConditions:      Diagnosed conditions (ICD-10 codes in future)
 *   - emergencyMedicalNotes:  Free-text notes for first responders
 *   - organDonor:             Whether the user is a registered organ donor
 *   - physicianName:          Primary care physician name
 *   - physicianPhone:         Primary care physician phone
 *   - insuranceProvider:      Health insurance provider name
 *   - insurancePolicyNumber:  Policy number (encrypted at rest)
 *   - lastUpdated:            Epoch millis of last modification
 *   - consentGiven:           User explicitly consented to store PHI
 *   - consentTimestamp:       When consent was given
 */
@Serializable
data class MedicalProfile(
    val userId: String = "",
    val bloodType: BloodType = BloodType.UNKNOWN,
    val allergies: List<String> = emptyList(),
    val medications: List<String> = emptyList(),
    val medicalConditions: List<String> = emptyList(),
    val emergencyMedicalNotes: String = "",
    val organDonor: Boolean = false,
    val physicianName: String = "",
    val physicianPhone: String = "",
    val insuranceProvider: String = "",
    val insurancePolicyNumber: String = "",
    val lastUpdated: Long = System.currentTimeMillis(),
    val consentGiven: Boolean = false,
    val consentTimestamp: Long = 0L
) {
    /**
     * Returns a summary safe for display to responding volunteers.
     * Excludes insurance details (minimum-necessary principle).
     *
     * Example:
     *   profile.responderSummary()
     *   // "Blood: O+ | Allergies: Penicillin, Shellfish | Meds: Albuterol | Conditions: Asthma"
     */
    fun responderSummary(): String {
        val parts = mutableListOf<String>()
        if (bloodType != BloodType.UNKNOWN) parts.add("Blood: ${bloodType.displayName}")
        if (allergies.isNotEmpty()) parts.add("Allergies: ${allergies.joinToString(", ")}")
        if (medications.isNotEmpty()) parts.add("Meds: ${medications.joinToString(", ")}")
        if (medicalConditions.isNotEmpty()) parts.add("Conditions: ${medicalConditions.joinToString(", ")}")
        if (emergencyMedicalNotes.isNotBlank()) parts.add("Notes: $emergencyMedicalNotes")
        return parts.joinToString(" | ").ifEmpty { "No medical information on file" }
    }

    /** True if the user has entered any medical data at all */
    fun hasData(): Boolean =
        bloodType != BloodType.UNKNOWN ||
        allergies.isNotEmpty() ||
        medications.isNotEmpty() ||
        medicalConditions.isNotEmpty() ||
        emergencyMedicalNotes.isNotBlank()
}
