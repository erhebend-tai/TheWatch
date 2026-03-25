// PersonCapabilityModels — domain models for person accessibility, functional needs,
// and occupant load calculations in TheWatch emergency response system.
//
// These models support:
//   1. PersonCapabilityProfile — ADA-compliant personal capability assessment
//   2. CMISTProfile — HHS/CDC C-MIST functional needs framework
//   3. MedicalNeed — time-sensitive medical requirements (insulin, dialysis, oxygen)
//   4. AccessibleEgressWeight — per-path evacuation weight based on person's capabilities
//   5. OccupantLoadCalculation — IBC Table 1004.5 occupant load factor computation
//   6. IBCOccupantLoadFactors — static constants from International Building Code
//
// Standards referenced:
//   - ADA (Americans with Disabilities Act) — accessibility classifications
//   - C-MIST (HHS/ASPR) — Communication, Medical, Independence, Supervision, Transportation
//   - IBC 2021 Table 1004.5 — Maximum Floor Area Allowances Per Occupant
//   - NFPA 101 Life Safety Code — egress capacity and evacuation planning
//   - ISO 639-1 — language codes (PreferredLanguage field)
//   - HIPAA — medical data fields require encryption at rest and in transit
//
// Example — building a capability profile:
//   var profile = new PersonCapabilityProfile
//   {
//       ProfileId = Guid.NewGuid().ToString(),
//       UserId = "u-456",
//       DisplayName = "Jane Doe",
//       AgeCategory = AgeCategory.Senior,
//       MobilityStatus = MobilityStatus.Wheelchair,
//       VisionStatus = VisionStatus.Normal,
//       HearingStatus = HearingStatus.HardOfHearing,
//       CognitiveStatus = CognitiveStatus.Normal,
//       EnglishProficiency = EnglishProficiency.Native,
//       WeightKg = 68.0,
//       MedicalConditions = new List<string> { "Type 2 Diabetes", "Hypertension" },
//       Medications = new List<string> { "Metformin 500mg BID", "Lisinopril 10mg QD" },
//       Allergies = new List<string> { "Penicillin", "Sulfa" },
//       RequiresServiceAnimal = false,
//       CorrelationId = "corr-abc123"
//   };
//
// Example — computing occupant load per IBC:
//   var load = new OccupantLoadCalculation
//   {
//       RoomId = "room-101",
//       RoomType = "Conference Room",
//       AreaSqFt = 1500,
//       OccupancyGroup = "B",
//       LoadFactorSqFtPerPerson = IBCOccupantLoadFactors.Business, // 100
//       CalculatedOccupantLoad = (int)Math.Ceiling(1500.0 / 100) // = 15
//   };
//
// Write-Ahead Log:
//   WAL-PCM-001: PersonCapabilityProfile model created — ADA fields + medical/allergy
//   WAL-PCM-002: CMISTProfile model created — HHS/CDC C-MIST framework fields
//   WAL-PCM-003: MedicalNeed model created — time-sensitive medical requirements
//   WAL-PCM-004: AccessibleEgressWeight model created — per-path evacuation weighting
//   WAL-PCM-005: OccupantLoadCalculation model created — IBC Table 1004.5 computation
//   WAL-PCM-006: IBCOccupantLoadFactors static class created — IBC constants

using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Models;

/// <summary>
/// Person capability and accessibility profile for emergency response planning.
/// Combines ADA-compliant capability classifications with medical information
/// needed for safe evacuation and emergency care.
/// <para>
/// HIPAA Note: MedicalConditions, Medications, and Allergies fields contain PHI
/// and MUST be encrypted at rest (AES-256) and in transit (TLS 1.2+).
/// </para>
/// </summary>
public class PersonCapabilityProfile
{
    /// <summary>Unique profile identifier (GUID).</summary>
    public string ProfileId { get; set; } = string.Empty;

    /// <summary>User ID this profile belongs to — foreign key to the user/account system.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Display name for emergency responder identification (first name + last initial recommended).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Age bracket classification — determines pediatric/geriatric protocols and evacuation priority.</summary>
    public AgeCategory AgeCategory { get; set; } = AgeCategory.Adult;

    /// <summary>
    /// Date of birth for precise age calculation. Nullable for privacy — AgeCategory is sufficient
    /// for emergency planning if exact DOB is not provided.
    /// </summary>
    public DateTime? DateOfBirth { get; set; }

    /// <summary>Mobility classification per ADA — determines egress path weighting and transport needs.</summary>
    public MobilityStatus MobilityStatus { get; set; } = MobilityStatus.Ambulatory;

    /// <summary>Vision classification per ADA — determines alert modality (auditory vs. visual vs. tactile).</summary>
    public VisionStatus VisionStatus { get; set; } = VisionStatus.Normal;

    /// <summary>Hearing classification per ADA — determines alert modality (visual strobe, vibration).</summary>
    public HearingStatus HearingStatus { get; set; } = HearingStatus.Normal;

    /// <summary>Cognitive functional assessment — determines supervision level and instruction complexity.</summary>
    public CognitiveStatus CognitiveStatus { get; set; } = CognitiveStatus.Normal;

    /// <summary>English language proficiency per DOJ LEP guidance — determines translation/interpreter needs.</summary>
    public EnglishProficiency EnglishProficiency { get; set; } = EnglishProficiency.Native;

    /// <summary>
    /// Preferred language as ISO 639-1 two-letter code (e.g., "en", "es", "zh", "ar").
    /// Used for multi-language emergency announcements and interpreter dispatch.
    /// </summary>
    public string PreferredLanguage { get; set; } = "en";

    /// <summary>Body weight in kilograms — used for stretcher team sizing and medical dosage reference. Nullable for privacy.</summary>
    public double? WeightKg { get; set; }

    /// <summary>Height in centimeters — used for stretcher/transport sizing. Nullable for privacy.</summary>
    public double? HeightCm { get; set; }

    /// <summary>
    /// Active medical conditions (e.g., "Type 2 Diabetes", "Epilepsy", "Asthma").
    /// HIPAA PHI — encrypt at rest. Used by first responders for triage.
    /// </summary>
    public List<string> MedicalConditions { get; set; } = new();

    /// <summary>
    /// Current medications with dosage (e.g., "Insulin Lispro 10U before meals", "Albuterol PRN").
    /// HIPAA PHI — encrypt at rest. Critical for EMS handoff.
    /// </summary>
    public List<string> Medications { get; set; } = new();

    /// <summary>
    /// Known allergies — drug, food, and environmental (e.g., "Penicillin - anaphylaxis", "Latex", "Bee stings").
    /// HIPAA PHI — encrypt at rest. Critical for EMS to avoid contraindicated treatments.
    /// </summary>
    public List<string> Allergies { get; set; } = new();

    /// <summary>
    /// Free-text emergency notes visible to responders (e.g., "Patient has a port on left chest",
    /// "Do not restrain — PTSD trigger", "Epi-pen in left jacket pocket").
    /// </summary>
    public string? EmergencyNotes { get; set; }

    /// <summary>Whether person relies on a service animal that must be evacuated together.</summary>
    public bool RequiresServiceAnimal { get; set; }

    /// <summary>Type of service animal if applicable (e.g., "Guide dog", "Hearing dog", "Psychiatric service dog").</summary>
    public string? ServiceAnimalType { get; set; }

    /// <summary>UTC timestamp of last profile update — triggers re-computation of egress weights and evacuation priority.</summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>Correlation ID linking this profile to the audit trail for change tracking.</summary>
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// C-MIST functional needs profile per HHS/ASPR and CDC emergency preparedness framework.
/// C-MIST = Communication, Medical, Independence, Supervision, Transportation.
/// <para>
/// This is the standard framework used by FEMA, Red Cross, and state/local emergency managers
/// to plan for people with access and functional needs (AFN) in disasters.
/// </para>
/// <para>
/// Example:
///   var cmist = new CMISTProfile
///   {
///       UserId = "u-456",
///       CommunicationMethod = CommunicationMethod.SignLanguage,
///       CommunicationNotes = "Fluent ASL, can lip-read slowly",
///       IndependenceLevel = SupervisionLevel.MinimalSupervision,
///       TransportationNeed = TransportationNeed.AssistanceNeeded,
///       SpecialEquipment = new List&lt;string&gt; { "Wheelchair", "Hearing aids" },
///       MedicalNeeds = new List&lt;MedicalNeed&gt;
///       {
///           new MedicalNeed
///           {
///               Description = "Insulin injection",
///               IsLifeThreatening = true,
///               RequiredMedication = "Insulin Lispro",
///               TimeSensitive = true,
///               IntervalMinutes = 240
///           }
///       }
///   };
/// </para>
/// </summary>
public class CMISTProfile
{
    /// <summary>Unique profile identifier (GUID).</summary>
    public string ProfileId { get; set; } = string.Empty;

    /// <summary>User ID this C-MIST profile belongs to.</summary>
    public string UserId { get; set; } = string.Empty;

    // ── C: Communication ──────────────────────────────────────────

    /// <summary>Primary communication method — determines responder interaction approach.</summary>
    public CommunicationMethod CommunicationMethod { get; set; } = CommunicationMethod.Verbal;

    /// <summary>
    /// Additional communication notes (e.g., "Uses Tobii eye-tracker SGD",
    /// "Speaks Mandarin and basic English", "Can write but very slowly").
    /// </summary>
    public string CommunicationNotes { get; set; } = string.Empty;

    // ── M: Medical ────────────────────────────────────────────────

    /// <summary>
    /// List of medical needs with time-sensitivity and equipment requirements.
    /// Ordered by criticality — life-threatening needs first.
    /// </summary>
    public List<MedicalNeed> MedicalNeeds { get; set; } = new();

    // ── I: Independence ───────────────────────────────────────────

    /// <summary>Level of independence / supervision required per C-MIST Independence axis.</summary>
    public SupervisionLevel IndependenceLevel { get; set; } = SupervisionLevel.Independent;

    // ── S: Supervision ────────────────────────────────────────────

    /// <summary>
    /// Additional supervision notes (e.g., "Wanders if not watched",
    /// "Becomes agitated in crowds", "Needs familiar caretaker Maria — phone 555-0123").
    /// </summary>
    public string? SupervisionNotes { get; set; }

    // ── T: Transportation ─────────────────────────────────────────

    /// <summary>Transportation need classification for evacuation planning.</summary>
    public TransportationNeed TransportationNeed { get; set; } = TransportationNeed.SelfTransport;

    /// <summary>
    /// Additional transportation notes (e.g., "Wheelchair does not fold — needs ramp van",
    /// "Oxygen tank must remain upright", "Service dog must ride with patient").
    /// </summary>
    public string? TransportationNotes { get; set; }

    /// <summary>
    /// Special equipment that must accompany the person during evacuation.
    /// Examples: "Wheelchair", "Portable oxygen concentrator", "Feeding tube pump",
    /// "CPAP machine", "Suction device", "IV pole", "Communication board".
    /// </summary>
    public List<string> SpecialEquipment { get; set; } = new();

    /// <summary>
    /// User ID of the designated caretaker/guardian contact — notified automatically during emergencies.
    /// Nullable if person is independent.
    /// </summary>
    public string? CaretakerContactId { get; set; }
}

/// <summary>
/// A specific medical need with time-sensitivity and equipment requirements.
/// Used in CMISTProfile.MedicalNeeds to detail each condition's emergency implications.
/// <para>
/// Example:
///   new MedicalNeed
///   {
///       Description = "Peritoneal dialysis",
///       IsLifeThreatening = true,
///       RequiredEquipment = "Dialysis cycler, dialysate bags",
///       TimeSensitive = true,
///       IntervalMinutes = 480 // every 8 hours
///   };
/// </para>
/// </summary>
public class MedicalNeed
{
    /// <summary>Human-readable description of the medical need (e.g., "Insulin injection", "Nebulizer treatment").</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>True if missing this treatment could be fatal or cause permanent harm (e.g., insulin, dialysis, ventilator).</summary>
    public bool IsLifeThreatening { get; set; }

    /// <summary>Medication required, if any (e.g., "Insulin Lispro 10U", "Albuterol 2.5mg/3mL"). Null if equipment-only.</summary>
    public string? RequiredMedication { get; set; }

    /// <summary>Equipment required, if any (e.g., "Nebulizer", "Portable oxygen concentrator"). Null if medication-only.</summary>
    public string? RequiredEquipment { get; set; }

    /// <summary>True if the treatment must be administered on a strict schedule (missed window = medical emergency).</summary>
    public bool TimeSensitive { get; set; }

    /// <summary>
    /// Interval in minutes between treatments, if time-sensitive.
    /// Examples: insulin=240 (4h), dialysis=480 (8h), seizure med=720 (12h).
    /// Null if not on a recurring schedule.
    /// </summary>
    public int? IntervalMinutes { get; set; }
}

/// <summary>
/// Egress path weight modifier for a specific person's capabilities.
/// Generated by <see cref="TheWatch.Shared.Domain.Ports.IPersonCapabilityPort.ComputeAccessibleEgressWeightsAsync"/>.
/// <para>
/// WeightMultiplier meanings:
///   1.0 = normal traversal speed (ambulatory adult on level ground)
///   1.5 = moderately slower (cane user, senior)
///   2.0 = significantly slower (walker user on ramp)
///   3.0 = very slow (assisted wheelchair on ramp)
///   999.0 = impassable (wheelchair user + stairs with no elevator)
/// </para>
/// <para>
/// Example:
///   // Wheelchair user, stair-only egress path
///   new AccessibleEgressWeight
///   {
///       PathId = "path-stairwell-A",
///       CanUse = false,
///       WeightMultiplier = 999.0,
///       ReasonIfBlocked = "Stair-only path, no elevator — wheelchair cannot traverse",
///       RequiresAssistance = true,
///       AssistanceType = "Carry team (2+ persons) or area-of-rescue-assistance wait"
///   };
/// </para>
/// </summary>
public class AccessibleEgressWeight
{
    /// <summary>Egress path identifier (references the building/structure path graph).</summary>
    public string PathId { get; set; } = string.Empty;

    /// <summary>Whether this person can use this egress path at all.</summary>
    public bool CanUse { get; set; } = true;

    /// <summary>
    /// Multiplier applied to base traversal time. 1.0 = normal, 2.0 = twice as slow, 999.0 = impassable.
    /// Used by the evacuation routing algorithm to find optimal accessible paths.
    /// </summary>
    public double WeightMultiplier { get; set; } = 1.0;

    /// <summary>Human-readable reason if path is blocked or significantly impeded. Null if CanUse=true and weight near 1.0.</summary>
    public string? ReasonIfBlocked { get; set; }

    /// <summary>Whether this person needs physical assistance to traverse this path.</summary>
    public bool RequiresAssistance { get; set; }

    /// <summary>Type of assistance needed (e.g., "Sighted guide", "Carry team 2+", "Wheelchair push assist"). Null if no assistance needed.</summary>
    public string? AssistanceType { get; set; }
}

/// <summary>
/// Occupant load calculation per IBC (International Building Code) 2021 Section 1004.
/// Computes maximum occupant load for a room/space based on occupancy group and floor area.
/// <para>
/// Formula: OccupantLoad = ceiling(AreaSqFt / LoadFactorSqFtPerPerson)
/// </para>
/// <para>
/// Example — 1500 sq ft conference room (Business occupancy):
///   CalculatedOccupantLoad = ceiling(1500 / 100) = 15 persons
/// </para>
/// <para>
/// Example — 5000 sq ft assembly hall with fixed seating:
///   CalculatedOccupantLoad = ceiling(5000 / 7) = 715 persons
/// </para>
/// </summary>
public class OccupantLoadCalculation
{
    /// <summary>Room/space identifier in the structure model.</summary>
    public string RoomId { get; set; } = string.Empty;

    /// <summary>Human-readable room type (e.g., "Conference Room", "Cafeteria", "Warehouse").</summary>
    public string RoomType { get; set; } = string.Empty;

    /// <summary>Net or gross floor area in square feet (per IBC 1004.1.1 — gross vs net depends on occupancy type).</summary>
    public double AreaSqFt { get; set; }

    /// <summary>
    /// IBC occupancy group classification letter(s).
    /// A=Assembly, B=Business, E=Educational, F=Factory/Industrial,
    /// H=High Hazard, I=Institutional, M=Mercantile, R=Residential,
    /// S=Storage, U=Utility.
    /// </summary>
    public string OccupancyGroup { get; set; } = string.Empty;

    /// <summary>
    /// Square feet per person from IBC Table 1004.5 for the given occupancy type.
    /// Use <see cref="IBCOccupantLoadFactors"/> constants for standard values.
    /// </summary>
    public double LoadFactorSqFtPerPerson { get; set; }

    /// <summary>
    /// Calculated maximum occupant load = ceiling(AreaSqFt / LoadFactorSqFtPerPerson).
    /// This is the code-required egress capacity the exits must support.
    /// </summary>
    public int CalculatedOccupantLoad { get; set; }

    /// <summary>
    /// Actual number of occupants currently in the space, if known (from sensors, badge-in, headcount).
    /// Null if real-time count not available — use CalculatedOccupantLoad as worst-case.
    /// </summary>
    public int? ActualOccupants { get; set; }
}

/// <summary>
/// IBC (International Building Code) 2021 Table 1004.5 — Maximum Floor Area Allowances Per Occupant.
/// All values in square feet per person (gross or net as specified by IBC for each use).
/// <para>
/// Usage:
///   double loadFactor = IBCOccupantLoadFactors.Business; // 100 sq ft/person
///   int occupantLoad = (int)Math.Ceiling(areaSqFt / loadFactor);
/// </para>
/// <para>
/// Note: These are CODE MINIMUMS. Actual egress capacity must meet or exceed these.
/// Local amendments may modify these values — check AHJ (Authority Having Jurisdiction).
/// </para>
/// </summary>
public static class IBCOccupantLoadFactors
{
    /// <summary>Assembly — concentrated use without fixed seating (chairs, tables): 7 net sq ft/person. IBC Table 1004.5.</summary>
    public const double Assembly_Concentrated = 7;

    /// <summary>Assembly — standing space: 5 net sq ft/person. IBC Table 1004.5.</summary>
    public const double Assembly_StandingSpace = 5;

    /// <summary>Assembly — unconcentrated use (tables and chairs, conference): 15 net sq ft/person. IBC Table 1004.5.</summary>
    public const double Assembly_Unconcentrated = 15;

    /// <summary>Business use (offices, professional services): 100 gross sq ft/person. IBC Table 1004.5.</summary>
    public const double Business = 100;

    /// <summary>Educational use (classrooms): 20 net sq ft/person. IBC Table 1004.5.</summary>
    public const double Educational = 20;

    /// <summary>Factory and industrial use: 100 gross sq ft/person. IBC Table 1004.5.</summary>
    public const double Factory_Industrial = 100;

    /// <summary>High hazard use: 100 gross sq ft/person. IBC Table 1004.5.</summary>
    public const double HighHazard = 100;

    /// <summary>Institutional use (hospitals, nursing homes, jails): 120 gross sq ft/person. IBC Table 1004.5.</summary>
    public const double Institutional = 120;

    /// <summary>Mercantile — basement and ground floor: 30 gross sq ft/person. IBC Table 1004.5.</summary>
    public const double Mercantile_Basement = 30;

    /// <summary>Mercantile — ground floor: 30 gross sq ft/person. IBC Table 1004.5.</summary>
    public const double Mercantile_Ground = 30;

    /// <summary>Mercantile — upper floors: 60 gross sq ft/person. IBC Table 1004.5.</summary>
    public const double Mercantile_Upper = 60;

    /// <summary>Residential use: 200 gross sq ft/person. IBC Table 1004.5.</summary>
    public const double Residential = 200;

    /// <summary>Storage use: 300 gross sq ft/person. IBC Table 1004.5.</summary>
    public const double Storage = 300;

    /// <summary>Utility and miscellaneous use: 300 gross sq ft/person. IBC Table 1004.5.</summary>
    public const double Utility = 300;
}
