// IPersonCapabilityPort — domain port for person accessibility, functional needs assessment,
// and ADA/IBC-compliant evacuation planning in TheWatch emergency response system.
//
// Architecture:
//   ┌────────────────────┐     ┌─────────────────────────┐     ┌──────────────────────────┐
//   │ Dashboard / Mobile  │────▶│ IPersonCapabilityPort   │────▶│ Adapter                  │
//   │ (profile CRUD,      │     │ .GetProfileAsync()      │     │ (SQL, Cosmos, Firebase,  │
//   │  evacuation calc)   │     │ .ComputeEgressWeights() │     │  Mock)                   │
//   └────────────────────┘     └─────────────────────────┘     └──────────────────────────┘
//                                       │
//                              Feeds into evacuation routing engine:
//                              - Egress path weighting per person's mobility
//                              - Evacuation priority scoring (infants, bedridden first)
//                              - Multi-language announcement generation
//                              - C-MIST caretaker notification
//
// Standards:
//   - ADA — accessibility classifications for mobility, vision, hearing
//   - C-MIST (HHS/ASPR) — functional needs framework for emergency management
//   - IBC 2021 Section 1004 — occupant load calculations
//   - NFPA 101 — life safety egress requirements
//   - HIPAA — all medical data encrypted at rest and in transit
//
// Example — full evacuation planning flow:
//   // 1. Get all occupants with special needs in a building
//   var specialNeeds = await port.GetOccupantsWithSpecialNeedsAsync("bldg-42", ct);
//
//   // 2. For each, compute accessible egress paths
//   foreach (var person in specialNeeds)
//   {
//       var weights = await port.ComputeAccessibleEgressWeightsAsync(person.UserId, "bldg-42", ct);
//       var priority = await port.GetEvacuationPriorityAsync(person.UserId, ct);
//       var needsCaretaker = await port.RequiresCaretakerAsync(person.UserId, ct);
//       // Route person through best accessible path, assign caretaker if needed
//   }
//
//   // 3. Generate multi-language announcements
//   var languages = await port.GetLanguagesInStructureAsync("bldg-42", ct);
//   // languages: ["en", "es", "zh", "ar"] → generate announcements in all four
//
// Example — occupant load check:
//   var load = await port.ComputeOccupantLoadAsync("room-101", "Conference Room", 1500, "B", ct);
//   // load.CalculatedOccupantLoad = 15 (1500 / 100 per IBC Business)
//   if (load.ActualOccupants > load.CalculatedOccupantLoad)
//       // FIRE CODE VIOLATION — alert building manager
//
// Write-Ahead Log:
//   WAL-PCP-001: IPersonCapabilityPort interface created — 10 methods
//   WAL-PCP-002: Profile CRUD — GetProfileAsync, UpdateProfileAsync
//   WAL-PCP-003: C-MIST CRUD — GetCMISTProfileAsync, UpdateCMISTProfileAsync
//   WAL-PCP-004: Egress weighting — ComputeAccessibleEgressWeightsAsync
//   WAL-PCP-005: Occupant load — ComputeOccupantLoadAsync (IBC Table 1004.5)
//   WAL-PCP-006: Special needs query — GetOccupantsWithSpecialNeedsAsync
//   WAL-PCP-007: Evacuation priority — GetEvacuationPriorityAsync
//   WAL-PCP-008: Caretaker check — RequiresCaretakerAsync
//   WAL-PCP-009: Language survey — GetLanguagesInStructureAsync

using TheWatch.Shared.Domain.Models;

namespace TheWatch.Shared.Domain.Ports;

/// <summary>
/// Domain port for person capability and accessibility management.
/// Provides profile CRUD, C-MIST functional needs assessment,
/// ADA-compliant egress path weighting, IBC occupant load calculations,
/// and evacuation priority scoring.
/// </summary>
public interface IPersonCapabilityPort
{
    // ── Profile CRUD ──────────────────────────────────────────────

    /// <summary>
    /// Retrieve the capability profile for a user.
    /// Returns null if no profile has been created yet.
    /// </summary>
    /// <param name="userId">User identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The user's capability profile, or null if not found.</returns>
    Task<PersonCapabilityProfile?> GetProfileAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Create or update a user's capability profile.
    /// Sets <see cref="PersonCapabilityProfile.LastUpdated"/> to UTC now.
    /// Triggers re-computation of egress weights and evacuation priority for any
    /// structures the user is registered in.
    /// </summary>
    /// <param name="profile">The profile to upsert.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted profile with updated timestamps.</returns>
    Task<PersonCapabilityProfile> UpdateProfileAsync(PersonCapabilityProfile profile, CancellationToken ct = default);

    // ── C-MIST Functional Needs ───────────────────────────────────

    /// <summary>
    /// Retrieve the C-MIST (Communication, Medical, Independence, Supervision, Transportation)
    /// functional needs profile for a user. Returns null if not created.
    /// </summary>
    /// <param name="userId">User identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The user's C-MIST profile, or null if not found.</returns>
    Task<CMISTProfile?> GetCMISTProfileAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Create or update a user's C-MIST functional needs profile.
    /// Validates that SpecialEquipment entries are non-empty strings and
    /// MedicalNeeds are ordered by IsLifeThreatening descending.
    /// </summary>
    /// <param name="profile">The C-MIST profile to upsert.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted C-MIST profile.</returns>
    Task<CMISTProfile> UpdateCMISTProfileAsync(CMISTProfile profile, CancellationToken ct = default);

    // ── Egress & Evacuation ───────────────────────────────────────

    /// <summary>
    /// Compute accessible egress path weights for a specific user in a structure.
    /// Evaluates every egress path in the structure against the user's mobility,
    /// vision, and other capabilities to produce a weight multiplier for each path.
    /// <para>
    /// Weight multiplier meanings:
    ///   1.0 = normal speed, 1.5 = moderately slower, 2.0 = significantly slower,
    ///   3.0 = very slow, 999.0 = impassable for this person.
    /// </para>
    /// </summary>
    /// <param name="userId">User whose capabilities determine the weights.</param>
    /// <param name="structureId">Building/structure containing the egress paths.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Weighted egress path list — one entry per path in the structure.</returns>
    Task<IReadOnlyList<AccessibleEgressWeight>> ComputeAccessibleEgressWeightsAsync(
        string userId, string structureId, CancellationToken ct = default);

    /// <summary>
    /// Compute the IBC Table 1004.5 occupant load for a room/space.
    /// Formula: OccupantLoad = ceiling(AreaSqFt / LoadFactorSqFtPerPerson).
    /// The LoadFactor is looked up from <see cref="IBCOccupantLoadFactors"/> based on occupancy group.
    /// </summary>
    /// <param name="roomId">Room identifier.</param>
    /// <param name="roomType">Human-readable room type (e.g., "Conference Room").</param>
    /// <param name="areaSqFt">Floor area in square feet.</param>
    /// <param name="occupancyGroup">IBC occupancy group (A, B, E, F, H, I, M, R, S, U).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Computed occupant load calculation with all input/output fields populated.</returns>
    Task<OccupantLoadCalculation> ComputeOccupantLoadAsync(
        string roomId, string roomType, double areaSqFt, string occupancyGroup, CancellationToken ct = default);

    /// <summary>
    /// Get all occupants in a structure who have non-Normal mobility, vision, hearing,
    /// or cognitive status. These are the people who need special evacuation consideration.
    /// Results ordered by evacuation priority descending (highest priority first).
    /// </summary>
    /// <param name="structureId">Building/structure identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of capability profiles for occupants with special needs.</returns>
    Task<IReadOnlyList<PersonCapabilityProfile>> GetOccupantsWithSpecialNeedsAsync(
        string structureId, CancellationToken ct = default);

    /// <summary>
    /// Compute evacuation priority score for a user. Higher score = evacuate sooner.
    /// <para>
    /// Scoring factors (additive):
    ///   +100: Bedridden or CarriedOnly mobility
    ///   +90:  Infant or Toddler age category
    ///   +80:  Life-threatening medical need (oxygen, insulin, dialysis)
    ///   +70:  Wheelchair mobility
    ///   +60:  Totally blind or deaf
    ///   +50:  Severe cognitive impairment
    ///   +40:  Senior age category
    ///   +30:  Requires one-to-one supervision
    ///   +20:  Walker/crutches mobility
    ///   +10:  Any other non-Normal status
    /// </para>
    /// </summary>
    /// <param name="userId">User identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Priority score — higher values should be evacuated first.</returns>
    Task<int> GetEvacuationPriorityAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Check whether a user requires a caretaker to be present during emergencies.
    /// Returns true if the C-MIST profile indicates ConstantSupervision, OneToOne, or Immobile,
    /// or if the person has a CaretakerContactId set, or if cognitive status is Severe/Nonverbal
    /// and age category is Child or younger.
    /// </summary>
    /// <param name="userId">User identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if caretaker is required for this user.</returns>
    Task<bool> RequiresCaretakerAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Get all distinct preferred languages for occupants in a structure.
    /// Used to generate multi-language emergency announcements per DOJ LEP guidance.
    /// Returns ISO 639-1 codes sorted alphabetically (e.g., ["ar", "en", "es", "zh"]).
    /// </summary>
    /// <param name="structureId">Building/structure identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Sorted list of distinct ISO 639-1 language codes.</returns>
    Task<IReadOnlyList<string>> GetLanguagesInStructureAsync(
        string structureId, CancellationToken ct = default);
}
