// PersonCapabilityType — enumerates person accessibility and capability classifications
// for TheWatch emergency response system.
//
// These enums drive:
//   1. Evacuation priority scoring — bedridden, infant, oxygen-dependent evacuate first
//   2. Accessible egress path weighting — wheelchair users cannot use stairs
//   3. Multi-language emergency announcements — based on EnglishProficiency + PreferredLanguage
//   4. C-MIST profile generation (HHS/CDC framework for functional needs in emergencies)
//   5. Occupant load calculations per IBC Table 1004.5
//
// Standards referenced:
//   - ADA (Americans with Disabilities Act) — mobility, vision, hearing classifications
//   - C-MIST (Communication, Medical, Independence, Supervision, Transportation)
//     per HHS/ASPR and CDC emergency preparedness guidance
//   - IBC (International Building Code) — occupancy groups, egress requirements
//   - NFPA 101 Life Safety Code — evacuation planning for persons with disabilities
//   - ISO 639-1 — language codes for PreferredLanguage field
//
// Enum value range: 700–799 (reserved block for Person Capability domain)
//
// Example: A wheelchair user (MobilityStatus.Wheelchair) in a 3-story building
//   triggers egress weight = 999 (impassable) for stair-only paths and
//   requires elevator or area-of-rescue-assistance routing.
//
// Example: A deaf resident (HearingStatus.Deaf) triggers visual/vibration alert
//   instead of audible alarm during SOS dispatch.
//
// Write-Ahead Log:
//   WAL-PC-001: MobilityStatus enum created — ADA compliant classifications
//   WAL-PC-002: VisionStatus enum created — ADA compliant classifications
//   WAL-PC-003: HearingStatus enum created — ADA compliant classifications
//   WAL-PC-004: CognitiveStatus enum created — functional assessment levels
//   WAL-PC-005: AgeCategory enum created — age bracket classifications
//   WAL-PC-006: EnglishProficiency enum created — LEP (Limited English Proficiency) levels
//   WAL-PC-007: SupervisionLevel enum created — C-MIST Independence axis
//   WAL-PC-008: TransportationNeed enum created — C-MIST Transportation axis
//   WAL-PC-009: CommunicationMethod enum created — C-MIST Communication axis

namespace TheWatch.Shared.Enums;

/// <summary>
/// Mobility status classification per ADA (Americans with Disabilities Act) guidelines.
/// Determines egress path weighting, elevator dependency, and evacuation resource allocation.
/// <para>
/// Example: MobilityStatus.Wheelchair triggers stair paths to weight=999 (impassable),
/// elevator paths to weight=1.5 (slower than ambulatory), and requires area-of-rescue-assistance.
/// </para>
/// </summary>
public enum MobilityStatus
{
    /// <summary>Fully ambulatory — can walk, run, and use stairs without assistance (ADA: no mobility impairment).</summary>
    Ambulatory = 700,

    /// <summary>Manual or powered wheelchair user — requires ramps, elevators, accessible doorways min 32" clear width (ADA 404.2.3).</summary>
    Wheelchair = 701,

    /// <summary>Walker/rollator user — reduced speed, may need rest stops, can negotiate shallow stairs with assistance.</summary>
    Walker = 702,

    /// <summary>Crutch user — reduced speed and balance, can negotiate stairs slowly with handrail.</summary>
    Crutches = 703,

    /// <summary>Cane user — mild mobility impairment, can negotiate most paths at reduced speed.</summary>
    Cane = 704,

    /// <summary>Bedridden — cannot self-evacuate, requires stretcher or carry team, highest evacuation priority.</summary>
    Bedridden = 705,

    /// <summary>Carried only — infant, small child, or person who must be physically carried (no self-mobility).</summary>
    CarriedOnly = 706
}

/// <summary>
/// Vision status classification per ADA guidelines and WHO visual impairment categories.
/// Determines tactile/auditory alert requirements and guide assistance during evacuation.
/// <para>
/// Example: VisionStatus.TotallyBlind triggers auditory-only alerts, requires sighted guide
/// or tactile egress path markings, and voice-based navigation instructions.
/// </para>
/// </summary>
public enum VisionStatus
{
    /// <summary>Normal or corrected-to-normal vision — no special accommodations needed.</summary>
    Normal = 710,

    /// <summary>Low vision (20/70 to 20/200 corrected) — large print, high contrast, magnification aids (ADA compliant signage required).</summary>
    LowVision = 711,

    /// <summary>Legally blind (20/200 or worse corrected, or visual field &lt;= 20 degrees) — tactile + auditory alerts, sighted guide recommended.</summary>
    LegallyBlind = 712,

    /// <summary>Total blindness — no functional vision, requires auditory alerts, tactile paths, and sighted guide for evacuation.</summary>
    TotallyBlind = 713
}

/// <summary>
/// Hearing status classification per ADA guidelines.
/// Determines visual/vibration alert requirements and communication method during emergencies.
/// <para>
/// Example: HearingStatus.Deaf triggers visual strobe alerts (NFPA 72 18.5),
/// vibration-based mobile notifications, and text/ASL-based communication.
/// </para>
/// </summary>
public enum HearingStatus
{
    /// <summary>Normal hearing — standard audible alerts effective.</summary>
    Normal = 720,

    /// <summary>Hard of hearing — amplified alerts, visual supplements recommended (ADA: assistive listening required in assembly areas).</summary>
    HardOfHearing = 721,

    /// <summary>Profoundly deaf — visual strobe + vibration alerts only, ASL or text communication (NFPA 72 18.5 visible notification).</summary>
    Deaf = 722,

    /// <summary>Cochlear implant user — may perceive some sounds, but visual/vibration backup required; magnetic interference near some equipment.</summary>
    CochlearImplant = 723
}

/// <summary>
/// Cognitive status classification for functional assessment during emergencies.
/// Determines supervision level, communication complexity, and evacuation guidance approach.
/// <para>
/// Example: CognitiveStatus.SevereImpairment triggers one-to-one supervision requirement,
/// simple pictorial instructions, and caretaker must be co-located during evacuation.
/// </para>
/// </summary>
public enum CognitiveStatus
{
    /// <summary>No cognitive impairment — can understand and follow complex verbal/written emergency instructions independently.</summary>
    Normal = 730,

    /// <summary>Mild impairment — may need simplified instructions, repeated prompts, or extra time to process emergency directions.</summary>
    MildImpairment = 731,

    /// <summary>Moderate impairment — requires constant supervision, simplified/pictorial instructions, familiar caretaker preferred.</summary>
    ModerateImpairment = 732,

    /// <summary>Severe impairment — requires one-to-one supervision, cannot process verbal instructions, caretaker must be present.</summary>
    SevereImpairment = 733,

    /// <summary>Nonverbal — cannot communicate verbally; requires AAC device, picture board, or sign language; may have any cognitive level.</summary>
    Nonverbal = 734
}

/// <summary>
/// Age category brackets for emergency response planning.
/// Determines evacuation priority, carry requirements, and medical dosage considerations.
/// <para>
/// Example: AgeCategory.Infant (0–1 year) sets evacuation priority to maximum,
/// requires carried evacuation, and flags pediatric medical protocols.
/// </para>
/// </summary>
public enum AgeCategory
{
    /// <summary>Infant: 0–1 year — must be carried, highest evacuation priority, pediatric protocols, cannot self-report.</summary>
    Infant = 740,

    /// <summary>Toddler: 1–3 years — must be carried or closely supervised, cannot follow complex instructions, pediatric protocols.</summary>
    Toddler = 741,

    /// <summary>Child: 4–12 years — can follow simple instructions, may need adult escort, pediatric medical dosages.</summary>
    Child = 742,

    /// <summary>Teenager: 13–17 years — can generally self-evacuate, may assist others, adult medical dosages for older teens.</summary>
    Teenager = 743,

    /// <summary>Adult: 18–64 years — standard evacuation capability assumed unless other conditions noted.</summary>
    Adult = 744,

    /// <summary>Senior: 65+ years — may have reduced mobility/cognition, increased fall risk, polypharmacy considerations, check-in priority.</summary>
    Senior = 745
}

/// <summary>
/// English language proficiency for emergency communication planning.
/// Per DOJ LEP (Limited English Proficiency) guidance and Executive Order 13166.
/// Determines whether interpreter services or translated alerts are required.
/// <para>
/// Example: EnglishProficiency.None with PreferredLanguage="es" triggers Spanish-language
/// emergency announcements and dispatches Spanish-speaking responder if available.
/// </para>
/// </summary>
public enum EnglishProficiency
{
    /// <summary>Native English speaker — all English emergency communications effective.</summary>
    Native = 750,

    /// <summary>Fluent — fully functional in English for emergency communication, may prefer another language.</summary>
    Fluent = 751,

    /// <summary>Intermediate — can understand basic emergency instructions, complex medical/legal terms may be missed.</summary>
    Intermediate = 752,

    /// <summary>Basic — limited English, can understand simple commands ("go", "stop", "help"), requires translated materials.</summary>
    Basic = 753,

    /// <summary>No English — requires interpreter or fully translated communication, per DOJ LEP guidance (EO 13166).</summary>
    None = 754
}

/// <summary>
/// Supervision level per C-MIST (Communication, Medical, Independence, Supervision, Transportation)
/// framework from HHS/ASPR for functional needs assessment in emergencies.
/// Determines caretaker requirements and evacuation staffing ratios.
/// <para>
/// Example: SupervisionLevel.OneToOne means one dedicated caretaker must be assigned
/// to this person during evacuation — they cannot be left unattended.
/// </para>
/// </summary>
public enum SupervisionLevel
{
    /// <summary>Fully independent — can self-evacuate, self-medicate, and make decisions without assistance.</summary>
    Independent = 760,

    /// <summary>Minimal supervision — periodic check-ins needed, can function alone for short periods, verbal prompts may be required.</summary>
    MinimalSupervision = 761,

    /// <summary>Constant supervision — must be observed at all times, may wander or become disoriented, but can ambulate with guidance.</summary>
    ConstantSupervision = 762,

    /// <summary>One-to-one — dedicated caretaker required at all times, cannot be left alone, highest staffing ratio.</summary>
    OneToOne = 763,

    /// <summary>Immobile — cannot move without physical assistance, requires carry team or stretcher, may need medical equipment transport.</summary>
    Immobile = 764
}

/// <summary>
/// Transportation need per C-MIST framework for evacuation and transport planning.
/// Determines vehicle type, equipment, and logistics for moving the person.
/// <para>
/// Example: TransportationNeed.StretcherRequired triggers ambulance or medical transport
/// allocation and excludes standard passenger vehicle options.
/// </para>
/// </summary>
public enum TransportationNeed
{
    /// <summary>Can self-transport — has own vehicle or can walk/use public transit independently.</summary>
    SelfTransport = 770,

    /// <summary>Assistance needed — can ride in standard vehicle but needs help boarding/deboarding (wheelchair transfer, walker stow).</summary>
    AssistanceNeeded = 771,

    /// <summary>Stretcher required — must be transported lying down, requires ambulance or medical transport vehicle.</summary>
    StretcherRequired = 772,

    /// <summary>Special vehicle required — wheelchair-accessible van, bariatric transport, or vehicle with medical equipment hookups.</summary>
    SpecialVehicle = 773,

    /// <summary>Air evacuation required — remote location, critical medical condition, or impassable ground routes require helicopter/air transport.</summary>
    AirEvac = 774
}

/// <summary>
/// Communication method per C-MIST framework for emergency interaction planning.
/// Determines how responders should communicate with this person during an incident.
/// <para>
/// Example: CommunicationMethod.SignLanguage triggers ASL-capable responder dispatch
/// or video remote interpreting (VRI) service activation.
/// </para>
/// </summary>
public enum CommunicationMethod
{
    /// <summary>Standard verbal communication — can speak and understand spoken language.</summary>
    Verbal = 780,

    /// <summary>Sign language (ASL, BSL, etc.) — requires sign-fluent responder or video remote interpreting (VRI).</summary>
    SignLanguage = 781,

    /// <summary>Written communication only — can read/write but cannot speak or hear; text-based interaction required.</summary>
    WrittenOnly = 782,

    /// <summary>Picture/symbol communication board — uses PECS, Bliss symbols, or similar AAC picture system.</summary>
    PictureBoard = 783,

    /// <summary>Assistive/augmentative communication device — speech-generating device (SGD), eye-tracking communicator, etc.</summary>
    AssistiveDevice = 784,

    /// <summary>Requires human interpreter — foreign language or specialized communication need requiring live interpreter on scene.</summary>
    Interpreter = 785
}
