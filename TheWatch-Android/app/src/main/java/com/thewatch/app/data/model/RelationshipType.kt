/**
 * ═══════════════════════════════════════════════════════════════════════════════
 * WRITE-AHEAD LOG — RelationshipType.kt
 * ═══════════════════════════════════════════════════════════════════════════════
 * Purpose:   Defines relationship types and associated trust levels for
 *            emergency contacts. Trust levels determine notification priority
 *            and data access during escalation events.
 * Date:      2026-03-24
 * Author:    Claude (Anthropic)
 * Deps:      kotlinx.serialization
 * Package:   com.thewatch.app.data.model
 *
 * Usage Example:
 *   val rel = RelationshipType.FAMILY
 *   println(rel.trustLevel)     // 5
 *   println(rel.displayName)    // "Family"
 *   println(rel.badgeColor)     // used for UI trust badge
 *
 * Trust Level Scale (1-5):
 *   5 = Family      — Full PHI access, first notified, can override duress
 *   4 = Medical     — Full PHI access, notified for medical events
 *   3 = Friend      — Limited PHI (blood type, allergies only), general alerts
 *   2 = Neighbor    — Location only, welfare check capability
 *   1 = Other       — Location only during active SOS, minimal data
 *
 * Related:
 *   - EmergencyContact model (data/model/User.kt)
 *   - ContactsScreen (ui/screens/contacts/ContactsScreen.kt)
 *   - NIST SP 800-63 Digital Identity Guidelines (trust assurance levels)
 * ═══════════════════════════════════════════════════════════════════════════════
 */
package com.thewatch.app.data.model

import kotlinx.serialization.Serializable

/**
 * Relationship types with inherent trust levels for emergency contact classification.
 *
 * Trust levels map to:
 *   - Notification priority order (higher trust = earlier notification)
 *   - PHI data exposure scope (higher trust = more medical data shared)
 *   - Escalation authority (Family can acknowledge on user's behalf)
 *
 * Example:
 *   val contact = EmergencyContact(
 *       ...,
 *       relationshipType = RelationshipType.MEDICAL,
 *       ...
 *   )
 *   if (contact.relationshipType.trustLevel >= 4) {
 *       // Share full medical profile with this contact
 *   }
 */
@Serializable
enum class RelationshipType(
    val trustLevel: Int,
    val displayName: String,
    val description: String
) {
    FAMILY(
        trustLevel = 5,
        displayName = "Family",
        description = "Immediate family member — full access, first notified"
    ),
    MEDICAL(
        trustLevel = 4,
        displayName = "Medical Professional",
        description = "Doctor, nurse, therapist — full PHI access for medical events"
    ),
    FRIEND(
        trustLevel = 3,
        displayName = "Friend",
        description = "Trusted friend — limited PHI, general emergency alerts"
    ),
    NEIGHBOR(
        trustLevel = 2,
        displayName = "Neighbor",
        description = "Nearby contact — location sharing, welfare checks"
    ),
    OTHER(
        trustLevel = 1,
        displayName = "Other",
        description = "Other contact — minimal data during active SOS only"
    );

    companion object {
        /** Parse from display name, fallback to OTHER */
        fun fromDisplayName(name: String): RelationshipType =
            entries.firstOrNull { it.displayName.equals(name, ignoreCase = true) } ?: OTHER

        /** Parse from legacy free-text relationship strings */
        fun fromLegacyString(legacy: String): RelationshipType {
            val lower = legacy.lowercase()
            return when {
                lower.contains("family") || lower.contains("sister") ||
                lower.contains("brother") || lower.contains("mother") ||
                lower.contains("father") || lower.contains("parent") ||
                lower.contains("spouse") || lower.contains("wife") ||
                lower.contains("husband") -> FAMILY

                lower.contains("doctor") || lower.contains("physician") ||
                lower.contains("nurse") || lower.contains("medical") ||
                lower.contains("therapist") || lower.contains("emergency services") -> MEDICAL

                lower.contains("friend") || lower.contains("colleague") ||
                lower.contains("coworker") || lower.contains("supervisor") -> FRIEND

                lower.contains("neighbor") -> NEIGHBOR

                else -> OTHER
            }
        }

        /** All options for dropdown UI */
        val dropdownOptions: List<String> = entries.map { it.displayName }
    }
}
