package com.thewatch.app.data.model

// PersonCapabilityModels.kt
// TheWatch-Android
//
// Person capability, accessibility, and C-MIST functional needs models
// for TheWatch emergency response system.
//
// Standards referenced:
//   - ADA (Americans with Disabilities Act) — mobility, vision, hearing classifications
//   - C-MIST (HHS/ASPR) — Communication, Medical, Independence, Supervision, Transportation
//   - IBC 2021 Table 1004.5 — occupant load factors
//   - NFPA 101 — life safety egress requirements
//   - ISO 639-1 — language codes
//
// Example — building a capability profile:
//   val profile = PersonCapabilityProfile(
//       profileId = UUID.randomUUID().toString(),
//       userId = "u-456",
//       displayName = "Jane D.",
//       ageCategory = AgeCategory.Senior,
//       mobilityStatus = MobilityStatus.Wheelchair,
//       hearingStatus = HearingStatus.HardOfHearing,
//       medicalConditions = listOf("Type 2 Diabetes", "Hypertension"),
//       medications = listOf("Metformin 500mg BID"),
//       allergies = listOf("Penicillin")
//   )
//
// Example — C-MIST profile:
//   val cmist = CMISTProfile(
//       userId = "u-456",
//       communicationMethod = CommunicationMethod.SignLanguage,
//       communicationNotes = "Fluent ASL",
//       medicalNeeds = listOf(MedicalNeed("Insulin", isLifeThreatening = true, timeSensitive = true, intervalMinutes = 240)),
//       independenceLevel = SupervisionLevel.MinimalSupervision,
//       transportationNeed = TransportationNeed.AssistanceNeeded,
//       specialEquipment = listOf("Wheelchair", "Hearing aids")
//   )
//
// Write-Ahead Log:
//   WAL-AND-PC-001: All enums mirrored from C# as enum class
//   WAL-AND-PC-002: All models mirrored from C# as data class
//   WAL-AND-PC-003: IBCOccupantLoadFactors as object with const vals

import java.util.Date
import java.util.UUID
import kotlin.math.ceil

// ═══════════════════════════════════════════════════════════════
// Enums (700–799 range mirrors C# values)
// ═══════════════════════════════════════════════════════════════

/**
 * Mobility status classification per ADA (Americans with Disabilities Act) guidelines.
 * Determines egress path weighting, elevator dependency, and evacuation resource allocation.
 *
 * Example: MobilityStatus.Wheelchair triggers stair paths to weight=999 (impassable).
 */
enum class MobilityStatus {
    /** Fully ambulatory — can walk, run, and use stairs without assistance. */
    Ambulatory,
    /** Manual or powered wheelchair user — requires ramps, elevators, accessible doorways. */
    Wheelchair,
    /** Walker/rollator user — reduced speed, may need rest stops. */
    Walker,
    /** Crutch user — reduced speed and balance. */
    Crutches,
    /** Cane user — mild mobility impairment. */
    Cane,
    /** Bedridden — cannot self-evacuate, requires stretcher or carry team. */
    Bedridden,
    /** Carried only — infant, small child, or person who must be physically carried. */
    CarriedOnly
}

/**
 * Vision status classification per ADA guidelines and WHO visual impairment categories.
 * Determines tactile/auditory alert requirements and guide assistance during evacuation.
 */
enum class VisionStatus {
    /** Normal or corrected-to-normal vision. */
    Normal,
    /** Low vision (20/70 to 20/200 corrected). */
    LowVision,
    /** Legally blind (20/200 or worse corrected, or visual field <= 20 degrees). */
    LegallyBlind,
    /** Total blindness — no functional vision. */
    TotallyBlind
}

/**
 * Hearing status classification per ADA guidelines.
 * Determines visual/vibration alert requirements and communication method.
 */
enum class HearingStatus {
    /** Normal hearing. */
    Normal,
    /** Hard of hearing — amplified alerts, visual supplements recommended. */
    HardOfHearing,
    /** Profoundly deaf — visual strobe + vibration alerts only. */
    Deaf,
    /** Cochlear implant user — visual/vibration backup required. */
    CochlearImplant
}

/**
 * Cognitive status classification for functional assessment during emergencies.
 * Determines supervision level, communication complexity, and evacuation guidance approach.
 */
enum class CognitiveStatus {
    /** No cognitive impairment. */
    Normal,
    /** Mild impairment — may need simplified instructions. */
    MildImpairment,
    /** Moderate impairment — requires constant supervision, simplified/pictorial instructions. */
    ModerateImpairment,
    /** Severe impairment — requires one-to-one supervision. */
    SevereImpairment,
    /** Nonverbal — cannot communicate verbally; requires AAC device or sign language. */
    Nonverbal
}

/**
 * Age category brackets for emergency response planning.
 * Determines evacuation priority, carry requirements, and medical dosage considerations.
 */
enum class AgeCategory {
    /** 0–1 year — must be carried, highest evacuation priority. */
    Infant,
    /** 1–3 years — must be carried or closely supervised. */
    Toddler,
    /** 4–12 years — can follow simple instructions, may need adult escort. */
    Child,
    /** 13–17 years — can generally self-evacuate. */
    Teenager,
    /** 18–64 years — standard evacuation capability assumed. */
    Adult,
    /** 65+ years — may have reduced mobility/cognition, increased fall risk. */
    Senior
}

/**
 * English language proficiency per DOJ LEP guidance (Executive Order 13166).
 * Determines whether interpreter services or translated alerts are required.
 */
enum class EnglishProficiency {
    /** Native English speaker. */
    Native,
    /** Fluent — fully functional in English. */
    Fluent,
    /** Intermediate — can understand basic emergency instructions. */
    Intermediate,
    /** Basic — limited English, simple commands only. */
    Basic,
    /** No English — requires interpreter or fully translated communication. */
    None
}

/**
 * Supervision level per C-MIST Independence axis (HHS/ASPR).
 * Determines caretaker requirements and evacuation staffing ratios.
 */
enum class SupervisionLevel {
    /** Fully independent. */
    Independent,
    /** Minimal supervision — periodic check-ins needed. */
    MinimalSupervision,
    /** Constant supervision — must be observed at all times. */
    ConstantSupervision,
    /** One-to-one — dedicated caretaker required at all times. */
    OneToOne,
    /** Immobile — cannot move without physical assistance. */
    Immobile
}

/**
 * Transportation need per C-MIST Transportation axis.
 * Determines vehicle type, equipment, and logistics for moving the person.
 */
enum class TransportationNeed {
    /** Can self-transport. */
    SelfTransport,
    /** Assistance needed — can ride in standard vehicle but needs help boarding. */
    AssistanceNeeded,
    /** Stretcher required — must be transported lying down. */
    StretcherRequired,
    /** Special vehicle required — wheelchair-accessible van, bariatric transport. */
    SpecialVehicle,
    /** Air evacuation required — helicopter/air transport. */
    AirEvac
}

/**
 * Communication method per C-MIST Communication axis.
 * Determines how responders should communicate with this person.
 */
enum class CommunicationMethod {
    /** Standard verbal communication. */
    Verbal,
    /** Sign language (ASL, BSL, etc.). */
    SignLanguage,
    /** Written communication only. */
    WrittenOnly,
    /** Picture/symbol communication board (PECS, Bliss). */
    PictureBoard,
    /** Assistive/augmentative communication device (SGD, eye-tracker). */
    AssistiveDevice,
    /** Requires human interpreter. */
    Interpreter
}

// ═══════════════════════════════════════════════════════════════
// Models
// ═══════════════════════════════════════════════════════════════

/**
 * Person capability and accessibility profile for emergency response planning.
 * Contains ADA-compliant classifications and medical information for safe evacuation.
 *
 * HIPAA Note: medicalConditions, medications, and allergies contain PHI
 * and MUST be encrypted at rest (AES-256) and in transit (TLS 1.2+).
 */
data class PersonCapabilityProfile(
    val profileId: String = UUID.randomUUID().toString(),
    val userId: String = "",
    val displayName: String = "",
    val ageCategory: AgeCategory = AgeCategory.Adult,
    val dateOfBirth: Date? = null,
    val mobilityStatus: MobilityStatus = MobilityStatus.Ambulatory,
    val visionStatus: VisionStatus = VisionStatus.Normal,
    val hearingStatus: HearingStatus = HearingStatus.Normal,
    val cognitiveStatus: CognitiveStatus = CognitiveStatus.Normal,
    val englishProficiency: EnglishProficiency = EnglishProficiency.Native,
    val preferredLanguage: String = "en",
    val weightKg: Double? = null,
    val heightCm: Double? = null,
    val medicalConditions: List<String> = emptyList(),
    val medications: List<String> = emptyList(),
    val allergies: List<String> = emptyList(),
    val emergencyNotes: String? = null,
    val requiresServiceAnimal: Boolean = false,
    val serviceAnimalType: String? = null,
    val lastUpdated: Date = Date(),
    val correlationId: String = ""
) {
    /**
     * Computed evacuation priority score. Higher = evacuate sooner.
     *
     * Scoring factors (additive):
     *   +100: Bedridden or CarriedOnly
     *   +90:  Infant or Toddler
     *   +70:  Wheelchair
     *   +60:  Totally blind or deaf
     *   +50:  Severe cognitive impairment
     *   +40:  Senior
     *   +20:  Walker/crutches
     *   +10:  Any other non-Normal status
     */
    val evacuationPriorityScore: Int
        get() {
            var score = 0
            if (mobilityStatus == MobilityStatus.Bedridden || mobilityStatus == MobilityStatus.CarriedOnly) score += 100
            if (ageCategory == AgeCategory.Infant || ageCategory == AgeCategory.Toddler) score += 90
            if (mobilityStatus == MobilityStatus.Wheelchair) score += 70
            if (visionStatus == VisionStatus.TotallyBlind || hearingStatus == HearingStatus.Deaf) score += 60
            if (cognitiveStatus == CognitiveStatus.SevereImpairment) score += 50
            if (ageCategory == AgeCategory.Senior) score += 40
            if (mobilityStatus == MobilityStatus.Walker || mobilityStatus == MobilityStatus.Crutches) score += 20
            if (visionStatus != VisionStatus.Normal || hearingStatus != HearingStatus.Normal || cognitiveStatus != CognitiveStatus.Normal) score += 10
            return score
        }

    /** Whether this person has any non-Normal accessibility status. */
    val hasSpecialNeeds: Boolean
        get() = mobilityStatus != MobilityStatus.Ambulatory ||
                visionStatus != VisionStatus.Normal ||
                hearingStatus != HearingStatus.Normal ||
                cognitiveStatus != CognitiveStatus.Normal
}

/**
 * C-MIST functional needs profile per HHS/ASPR framework.
 * C-MIST = Communication, Medical, Independence, Supervision, Transportation.
 */
data class CMISTProfile(
    val profileId: String = UUID.randomUUID().toString(),
    val userId: String = "",

    // C: Communication
    val communicationMethod: CommunicationMethod = CommunicationMethod.Verbal,
    val communicationNotes: String = "",

    // M: Medical
    val medicalNeeds: List<MedicalNeed> = emptyList(),

    // I: Independence
    val independenceLevel: SupervisionLevel = SupervisionLevel.Independent,

    // S: Supervision
    val supervisionNotes: String? = null,

    // T: Transportation
    val transportationNeed: TransportationNeed = TransportationNeed.SelfTransport,
    val transportationNotes: String? = null,

    val specialEquipment: List<String> = emptyList(),
    val caretakerContactId: String? = null
) {
    /** Whether this person requires a caretaker during emergencies. */
    val requiresCaretaker: Boolean
        get() = independenceLevel == SupervisionLevel.ConstantSupervision ||
                independenceLevel == SupervisionLevel.OneToOne ||
                independenceLevel == SupervisionLevel.Immobile ||
                caretakerContactId != null
}

/**
 * A specific medical need with time-sensitivity and equipment requirements.
 *
 * Example:
 *   MedicalNeed(
 *       description = "Insulin injection",
 *       isLifeThreatening = true,
 *       requiredMedication = "Insulin Lispro 10U",
 *       timeSensitive = true,
 *       intervalMinutes = 240
 *   )
 */
data class MedicalNeed(
    val description: String = "",
    val isLifeThreatening: Boolean = false,
    val requiredMedication: String? = null,
    val requiredEquipment: String? = null,
    val timeSensitive: Boolean = false,
    val intervalMinutes: Int? = null
)

/**
 * Egress path weight modifier for a specific person's capabilities.
 *
 * WeightMultiplier meanings:
 *   1.0 = normal traversal speed
 *   1.5 = moderately slower
 *   2.0 = significantly slower
 *   3.0 = very slow
 *   999.0 = impassable
 */
data class AccessibleEgressWeight(
    val pathId: String = "",
    val canUse: Boolean = true,
    val weightMultiplier: Double = 1.0,
    val reasonIfBlocked: String? = null,
    val requiresAssistance: Boolean = false,
    val assistanceType: String? = null
)

/**
 * Occupant load calculation per IBC 2021 Section 1004.
 * Formula: OccupantLoad = ceiling(AreaSqFt / LoadFactorSqFtPerPerson).
 *
 * Example — 1500 sq ft office (Business):
 *   OccupantLoadCalculation(areaSqFt = 1500.0, occupancyGroup = "B",
 *       loadFactorSqFtPerPerson = 100.0, calculatedOccupantLoad = 15)
 */
data class OccupantLoadCalculation(
    val roomId: String = "",
    val roomType: String = "",
    val areaSqFt: Double = 0.0,
    val occupancyGroup: String = "",
    val loadFactorSqFtPerPerson: Double = 0.0,
    val calculatedOccupantLoad: Int = 0,
    val actualOccupants: Int? = null
) {
    /** Whether actual occupants exceed the calculated code maximum. */
    val isOverCapacity: Boolean
        get() = actualOccupants != null && actualOccupants > calculatedOccupantLoad
}

// ═══════════════════════════════════════════════════════════════
// IBC Occupant Load Factors
// ═══════════════════════════════════════════════════════════════

/**
 * IBC 2021 Table 1004.5 — Maximum Floor Area Allowances Per Occupant.
 * All values in square feet per person.
 *
 * Example:
 *   val loadFactor = IBCOccupantLoadFactors.BUSINESS // 100.0
 *   val occupantLoad = ceil(areaSqFt / loadFactor).toInt() // 1500/100 = 15
 */
object IBCOccupantLoadFactors {
    /** Assembly — concentrated use without fixed seating: 7 net sq ft/person. */
    const val ASSEMBLY_CONCENTRATED: Double = 7.0
    /** Assembly — standing space: 5 net sq ft/person. */
    const val ASSEMBLY_STANDING_SPACE: Double = 5.0
    /** Assembly — unconcentrated use (tables and chairs): 15 net sq ft/person. */
    const val ASSEMBLY_UNCONCENTRATED: Double = 15.0
    /** Business use (offices): 100 gross sq ft/person. */
    const val BUSINESS: Double = 100.0
    /** Educational use (classrooms): 20 net sq ft/person. */
    const val EDUCATIONAL: Double = 20.0
    /** Factory and industrial use: 100 gross sq ft/person. */
    const val FACTORY_INDUSTRIAL: Double = 100.0
    /** High hazard use: 100 gross sq ft/person. */
    const val HIGH_HAZARD: Double = 100.0
    /** Institutional use (hospitals, nursing homes): 120 gross sq ft/person. */
    const val INSTITUTIONAL: Double = 120.0
    /** Mercantile — basement: 30 gross sq ft/person. */
    const val MERCANTILE_BASEMENT: Double = 30.0
    /** Mercantile — ground floor: 30 gross sq ft/person. */
    const val MERCANTILE_GROUND: Double = 30.0
    /** Mercantile — upper floors: 60 gross sq ft/person. */
    const val MERCANTILE_UPPER: Double = 60.0
    /** Residential use: 200 gross sq ft/person. */
    const val RESIDENTIAL: Double = 200.0
    /** Storage use: 300 gross sq ft/person. */
    const val STORAGE: Double = 300.0
    /** Utility and miscellaneous use: 300 gross sq ft/person. */
    const val UTILITY: Double = 300.0

    /**
     * Look up the load factor for an IBC occupancy group string.
     * Returns null for unrecognized groups.
     *
     * Example:
     *   val factor = IBCOccupantLoadFactors.forOccupancyGroup("B") // 100.0
     *   val load = ceil(1500.0 / factor!!).toInt() // 15
     */
    fun forOccupancyGroup(group: String): Double? = when (group.uppercase()) {
        "A-1", "A-2", "A" -> ASSEMBLY_CONCENTRATED
        "A-STANDING" -> ASSEMBLY_STANDING_SPACE
        "A-3", "A-4", "A-5" -> ASSEMBLY_UNCONCENTRATED
        "B" -> BUSINESS
        "E" -> EDUCATIONAL
        "F", "F-1", "F-2" -> FACTORY_INDUSTRIAL
        "H" -> HIGH_HAZARD
        "I", "I-1", "I-2", "I-3", "I-4" -> INSTITUTIONAL
        "M" -> MERCANTILE_GROUND
        "R", "R-1", "R-2", "R-3", "R-4" -> RESIDENTIAL
        "S", "S-1", "S-2" -> STORAGE
        "U" -> UTILITY
        else -> null
    }
}
