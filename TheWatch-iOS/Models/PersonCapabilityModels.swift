// PersonCapabilityModels.swift
// TheWatch-iOS
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
//   let profile = PersonCapabilityProfile(
//       profileId: UUID().uuidString,
//       userId: "u-456",
//       displayName: "Jane D.",
//       ageCategory: .senior,
//       mobilityStatus: .wheelchair,
//       hearingStatus: .hardOfHearing,
//       medicalConditions: ["Type 2 Diabetes", "Hypertension"],
//       medications: ["Metformin 500mg BID"],
//       allergies: ["Penicillin"]
//   )
//
// Example — C-MIST profile:
//   let cmist = CMISTProfile(
//       userId: "u-456",
//       communicationMethod: .signLanguage,
//       communicationNotes: "Fluent ASL",
//       medicalNeeds: [MedicalNeed(description: "Insulin", isLifeThreatening: true, timeSensitive: true, intervalMinutes: 240)],
//       independenceLevel: .minimalSupervision,
//       transportationNeed: .assistanceNeeded,
//       specialEquipment: ["Wheelchair", "Hearing aids"]
//   )
//
// Write-Ahead Log:
//   WAL-IOS-PC-001: All enums mirrored from C# with raw String values for Codable JSON
//   WAL-IOS-PC-002: All models mirrored from C# as Codable structs
//   WAL-IOS-PC-003: IBCOccupantLoadFactors as static struct constants

import Foundation

// MARK: - Enums (700–799 range mirrors C# values)

/// Mobility status classification per ADA guidelines.
/// Determines egress path weighting and evacuation resource allocation.
enum MobilityStatus: String, Codable, CaseIterable {
    case ambulatory = "Ambulatory"
    case wheelchair = "Wheelchair"
    case walker = "Walker"
    case crutches = "Crutches"
    case cane = "Cane"
    case bedridden = "Bedridden"
    case carriedOnly = "CarriedOnly"
}

/// Vision status classification per ADA guidelines and WHO categories.
/// Determines alert modality (auditory vs. visual vs. tactile).
enum VisionStatus: String, Codable, CaseIterable {
    case normal = "Normal"
    case lowVision = "LowVision"
    case legallyBlind = "LegallyBlind"
    case totallyBlind = "TotallyBlind"
}

/// Hearing status classification per ADA guidelines.
/// Determines visual/vibration alert requirements.
enum HearingStatus: String, Codable, CaseIterable {
    case normal = "Normal"
    case hardOfHearing = "HardOfHearing"
    case deaf = "Deaf"
    case cochlearImplant = "CochlearImplant"
}

/// Cognitive functional assessment for emergency instruction complexity.
enum CognitiveStatus: String, Codable, CaseIterable {
    case normal = "Normal"
    case mildImpairment = "MildImpairment"
    case moderateImpairment = "ModerateImpairment"
    case severeImpairment = "SevereImpairment"
    case nonverbal = "Nonverbal"
}

/// Age category brackets for evacuation priority and medical protocol selection.
enum AgeCategory: String, Codable, CaseIterable {
    /// 0–1 year
    case infant = "Infant"
    /// 1–3 years
    case toddler = "Toddler"
    /// 4–12 years
    case child = "Child"
    /// 13–17 years
    case teenager = "Teenager"
    /// 18–64 years
    case adult = "Adult"
    /// 65+ years
    case senior = "Senior"
}

/// English language proficiency per DOJ LEP guidance (Executive Order 13166).
enum EnglishProficiency: String, Codable, CaseIterable {
    case native = "Native"
    case fluent = "Fluent"
    case intermediate = "Intermediate"
    case basic = "Basic"
    case none = "None"
}

/// Supervision level per C-MIST Independence axis (HHS/ASPR).
enum SupervisionLevel: String, Codable, CaseIterable {
    case independent = "Independent"
    case minimalSupervision = "MinimalSupervision"
    case constantSupervision = "ConstantSupervision"
    case oneToOne = "OneToOne"
    case immobile = "Immobile"
}

/// Transportation need per C-MIST Transportation axis.
enum TransportationNeed: String, Codable, CaseIterable {
    case selfTransport = "SelfTransport"
    case assistanceNeeded = "AssistanceNeeded"
    case stretcherRequired = "StretcherRequired"
    case specialVehicle = "SpecialVehicle"
    case airEvac = "AirEvac"
}

/// Communication method per C-MIST Communication axis.
enum CommunicationMethod: String, Codable, CaseIterable {
    case verbal = "Verbal"
    case signLanguage = "SignLanguage"
    case writtenOnly = "WrittenOnly"
    case pictureBoard = "PictureBoard"
    case assistiveDevice = "AssistiveDevice"
    case interpreter = "Interpreter"
}

// MARK: - Models

/// Person capability and accessibility profile for emergency response planning.
/// Contains ADA-compliant classifications and medical information for safe evacuation.
struct PersonCapabilityProfile: Codable, Hashable, Identifiable {
    var id: String { profileId }

    let profileId: String
    var userId: String
    var displayName: String
    var ageCategory: AgeCategory
    var dateOfBirth: Date?
    var mobilityStatus: MobilityStatus
    var visionStatus: VisionStatus
    var hearingStatus: HearingStatus
    var cognitiveStatus: CognitiveStatus
    var englishProficiency: EnglishProficiency
    var preferredLanguage: String
    var weightKg: Double?
    var heightCm: Double?
    var medicalConditions: [String]
    var medications: [String]
    var allergies: [String]
    var emergencyNotes: String?
    var requiresServiceAnimal: Bool
    var serviceAnimalType: String?
    var lastUpdated: Date
    var correlationId: String

    init(
        profileId: String = UUID().uuidString,
        userId: String = "",
        displayName: String = "",
        ageCategory: AgeCategory = .adult,
        dateOfBirth: Date? = nil,
        mobilityStatus: MobilityStatus = .ambulatory,
        visionStatus: VisionStatus = .normal,
        hearingStatus: HearingStatus = .normal,
        cognitiveStatus: CognitiveStatus = .normal,
        englishProficiency: EnglishProficiency = .native,
        preferredLanguage: String = "en",
        weightKg: Double? = nil,
        heightCm: Double? = nil,
        medicalConditions: [String] = [],
        medications: [String] = [],
        allergies: [String] = [],
        emergencyNotes: String? = nil,
        requiresServiceAnimal: Bool = false,
        serviceAnimalType: String? = nil,
        lastUpdated: Date = Date(),
        correlationId: String = ""
    ) {
        self.profileId = profileId
        self.userId = userId
        self.displayName = displayName
        self.ageCategory = ageCategory
        self.dateOfBirth = dateOfBirth
        self.mobilityStatus = mobilityStatus
        self.visionStatus = visionStatus
        self.hearingStatus = hearingStatus
        self.cognitiveStatus = cognitiveStatus
        self.englishProficiency = englishProficiency
        self.preferredLanguage = preferredLanguage
        self.weightKg = weightKg
        self.heightCm = heightCm
        self.medicalConditions = medicalConditions
        self.medications = medications
        self.allergies = allergies
        self.emergencyNotes = emergencyNotes
        self.requiresServiceAnimal = requiresServiceAnimal
        self.serviceAnimalType = serviceAnimalType
        self.lastUpdated = lastUpdated
        self.correlationId = correlationId
    }

    /// Computed evacuation priority score. Higher = evacuate sooner.
    var evacuationPriorityScore: Int {
        var score = 0
        if mobilityStatus == .bedridden || mobilityStatus == .carriedOnly { score += 100 }
        if ageCategory == .infant || ageCategory == .toddler { score += 90 }
        if mobilityStatus == .wheelchair { score += 70 }
        if visionStatus == .totallyBlind || hearingStatus == .deaf { score += 60 }
        if cognitiveStatus == .severeImpairment { score += 50 }
        if ageCategory == .senior { score += 40 }
        if mobilityStatus == .walker || mobilityStatus == .crutches { score += 20 }
        if visionStatus != .normal || hearingStatus != .normal || cognitiveStatus != .normal { score += 10 }
        return score
    }

    /// Whether this person has any non-Normal accessibility status.
    var hasSpecialNeeds: Bool {
        mobilityStatus != .ambulatory ||
        visionStatus != .normal ||
        hearingStatus != .normal ||
        cognitiveStatus != .normal
    }
}

/// C-MIST functional needs profile per HHS/ASPR framework.
/// C-MIST = Communication, Medical, Independence, Supervision, Transportation.
struct CMISTProfile: Codable, Hashable, Identifiable {
    var id: String { profileId }

    let profileId: String
    var userId: String

    // C: Communication
    var communicationMethod: CommunicationMethod
    var communicationNotes: String

    // M: Medical
    var medicalNeeds: [MedicalNeed]

    // I: Independence
    var independenceLevel: SupervisionLevel

    // S: Supervision
    var supervisionNotes: String?

    // T: Transportation
    var transportationNeed: TransportationNeed
    var transportationNotes: String?

    var specialEquipment: [String]
    var caretakerContactId: String?

    init(
        profileId: String = UUID().uuidString,
        userId: String = "",
        communicationMethod: CommunicationMethod = .verbal,
        communicationNotes: String = "",
        medicalNeeds: [MedicalNeed] = [],
        independenceLevel: SupervisionLevel = .independent,
        supervisionNotes: String? = nil,
        transportationNeed: TransportationNeed = .selfTransport,
        transportationNotes: String? = nil,
        specialEquipment: [String] = [],
        caretakerContactId: String? = nil
    ) {
        self.profileId = profileId
        self.userId = userId
        self.communicationMethod = communicationMethod
        self.communicationNotes = communicationNotes
        self.medicalNeeds = medicalNeeds
        self.independenceLevel = independenceLevel
        self.supervisionNotes = supervisionNotes
        self.transportationNeed = transportationNeed
        self.transportationNotes = transportationNotes
        self.specialEquipment = specialEquipment
        self.caretakerContactId = caretakerContactId
    }

    /// Whether this person requires a caretaker during emergencies.
    var requiresCaretaker: Bool {
        independenceLevel == .constantSupervision ||
        independenceLevel == .oneToOne ||
        independenceLevel == .immobile ||
        caretakerContactId != nil
    }
}

/// A specific medical need with time-sensitivity and equipment requirements.
struct MedicalNeed: Codable, Hashable, Identifiable {
    var id: String { description }

    var description: String
    var isLifeThreatening: Bool
    var requiredMedication: String?
    var requiredEquipment: String?
    var timeSensitive: Bool
    var intervalMinutes: Int?

    init(
        description: String = "",
        isLifeThreatening: Bool = false,
        requiredMedication: String? = nil,
        requiredEquipment: String? = nil,
        timeSensitive: Bool = false,
        intervalMinutes: Int? = nil
    ) {
        self.description = description
        self.isLifeThreatening = isLifeThreatening
        self.requiredMedication = requiredMedication
        self.requiredEquipment = requiredEquipment
        self.timeSensitive = timeSensitive
        self.intervalMinutes = intervalMinutes
    }
}

/// Egress path weight modifier for a specific person's capabilities.
/// WeightMultiplier: 1.0=normal, 2.0=slow, 999.0=impassable.
struct AccessibleEgressWeight: Codable, Hashable, Identifiable {
    var id: String { pathId }

    var pathId: String
    var canUse: Bool
    var weightMultiplier: Double
    var reasonIfBlocked: String?
    var requiresAssistance: Bool
    var assistanceType: String?

    init(
        pathId: String = "",
        canUse: Bool = true,
        weightMultiplier: Double = 1.0,
        reasonIfBlocked: String? = nil,
        requiresAssistance: Bool = false,
        assistanceType: String? = nil
    ) {
        self.pathId = pathId
        self.canUse = canUse
        self.weightMultiplier = weightMultiplier
        self.reasonIfBlocked = reasonIfBlocked
        self.requiresAssistance = requiresAssistance
        self.assistanceType = assistanceType
    }
}

/// Occupant load calculation per IBC 2021 Section 1004.
/// Formula: OccupantLoad = ceiling(AreaSqFt / LoadFactorSqFtPerPerson).
struct OccupantLoadCalculation: Codable, Hashable, Identifiable {
    var id: String { roomId }

    var roomId: String
    var roomType: String
    var areaSqFt: Double
    var occupancyGroup: String
    var loadFactorSqFtPerPerson: Double
    var calculatedOccupantLoad: Int
    var actualOccupants: Int?

    init(
        roomId: String = "",
        roomType: String = "",
        areaSqFt: Double = 0,
        occupancyGroup: String = "",
        loadFactorSqFtPerPerson: Double = 0,
        calculatedOccupantLoad: Int = 0,
        actualOccupants: Int? = nil
    ) {
        self.roomId = roomId
        self.roomType = roomType
        self.areaSqFt = areaSqFt
        self.occupancyGroup = occupancyGroup
        self.loadFactorSqFtPerPerson = loadFactorSqFtPerPerson
        self.calculatedOccupantLoad = calculatedOccupantLoad
        self.actualOccupants = actualOccupants
    }

    /// Whether actual occupants exceed the calculated code maximum.
    var isOverCapacity: Bool {
        guard let actual = actualOccupants else { return false }
        return actual > calculatedOccupantLoad
    }
}

// MARK: - IBC Occupant Load Factors

/// IBC 2021 Table 1004.5 — Maximum Floor Area Allowances Per Occupant.
/// All values in square feet per person.
///
/// Example:
///   let loadFactor = IBCOccupantLoadFactors.business // 100
///   let occupantLoad = Int(ceil(areaSqFt / loadFactor)) // 1500/100 = 15
struct IBCOccupantLoadFactors {
    static let assemblyConcentrated: Double = 7
    static let assemblyStandingSpace: Double = 5
    static let assemblyUnconcentrated: Double = 15
    static let business: Double = 100
    static let educational: Double = 20
    static let factoryIndustrial: Double = 100
    static let highHazard: Double = 100
    static let institutional: Double = 120
    static let mercantileBasement: Double = 30
    static let mercantileGround: Double = 30
    static let mercantileUpper: Double = 60
    static let residential: Double = 200
    static let storage: Double = 300
    static let utility: Double = 300

    /// Look up the load factor for an IBC occupancy group string.
    /// Returns nil for unrecognized groups.
    ///
    /// Example:
    ///   if let factor = IBCOccupantLoadFactors.forOccupancyGroup("B") {
    ///       let load = Int(ceil(1500.0 / factor)) // 15
    ///   }
    static func forOccupancyGroup(_ group: String) -> Double? {
        switch group.uppercased() {
        case "A-1", "A-2", "A":     return assemblyConcentrated
        case "A-STANDING":          return assemblyStandingSpace
        case "A-3", "A-4", "A-5":  return assemblyUnconcentrated
        case "B":                   return business
        case "E":                   return educational
        case "F", "F-1", "F-2":    return factoryIndustrial
        case "H":                   return highHazard
        case "I", "I-1", "I-2", "I-3", "I-4": return institutional
        case "M":                   return mercantileGround
        case "R", "R-1", "R-2", "R-3", "R-4": return residential
        case "S", "S-1", "S-2":    return storage
        case "U":                   return utility
        default:                    return nil
        }
    }

    private init() {} // Non-instantiable
}
