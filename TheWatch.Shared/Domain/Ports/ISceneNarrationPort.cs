// =============================================================================
// ISceneNarrationPort — port interface for AI-powered scene narration.
// =============================================================================
// The scene narrator is the heart of the Watch Call de-escalation system.
// It receives periodic frame snapshots from a live video feed and produces
// neutral, factual descriptions of what it observes — no assumptions, no
// identification of individuals, no subjective judgment.
//
// The narrator follows strict guardrails:
//
//   DO describe:
//     - Number of people visible and their general actions
//     - Vehicles (type, color — NOT license plates unless attached to incident)
//     - Objects being carried or manipulated
//     - Environmental conditions (lighting, weather, time indicators)
//     - Spatial relationships ("near the front door", "on the sidewalk")
//     - Sounds if audio is available (footsteps, voices, vehicles)
//
//   DO NOT describe:
//     - Race, ethnicity, skin color, or perceived national origin
//     - Estimated age beyond broad categories ("adult", "child")
//     - Gender unless clearly relevant to identification ("person" preferred)
//     - Clothing brands, logos, or cultural/religious attire
//     - Subjective assessments ("suspicious", "threatening", "sketchy")
//     - Assumptions about intent ("trying to break in", "casing the house")
//     - Facial features, tattoos, scars, or other identifying marks
//
// Example narrations (GOOD):
//   "I see one person standing near the front door of a house. They are
//    holding a rectangular object, possibly a package. The porch light is on."
//
//   "Two people are near a parked vehicle. The vehicle's hood is open.
//    One person appears to be looking at the engine area."
//
//   "A person is walking along the sidewalk at a steady pace. They are
//    carrying a bag. The street has moderate lighting from streetlamps."
//
// Example narrations (BAD — these would be rejected by guardrails):
//   "A suspicious young man is lurking near the house" ← subjective + age
//   "An African-American male in a hoodie" ← race + clothing stereotype
//   "Someone who looks like they don't belong" ← bias + assumption
//   "They appear to be trying to break into the car" ← assumed intent
//
// Implementation:
//   Production: Azure OpenAI GPT-4o vision (gpt-4o is already provisioned)
//     - Frame sent as base64 image in a chat completion request
//     - System prompt enforces the guardrails above
//     - Response is the narration text
//     - Latency target: < 3 seconds per frame
//     - Rate: 1 frame per 2-3 seconds (not every frame — bandwidth + cost)
//
//   Development (Mock):
//     - Returns pre-scripted narrations from a rotation pool
//     - Simulates 1-2 second latency
//     - Useful for UI development without burning API credits
//
// The narration pipeline:
//   1. Video frame captured (client-side, 1 fps)
//   2. Frame sent to server via SignalR binary message
//   3. Server passes frame to ISceneNarrationPort.NarrateFrameAsync()
//   4. Adapter calls GPT-4o vision with the frame + system prompt
//   5. Narration text returned
//   6. Server broadcasts narration to all call participants via SignalR
//   7. Server appends narration to the call's transcript via IWatchCallPort
//   8. Client displays narration as a live caption overlay on the video
//
// WAL: Frame images are processed in-memory and immediately discarded after
//      narration. Only the text narration is persisted (in the call transcript).
//      No facial recognition, no image storage, no biometric data extraction.
// =============================================================================

namespace TheWatch.Shared.Domain.Ports;

// ═══════════════════════════════════════════════════════════════
// Narration Records
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Confidence level of the AI narrator's description.
/// Lower confidence triggers more cautious, hedged language.
/// </summary>
public enum NarrationConfidence
{
    /// <summary>Clear view, well-lit, high-res frame. Confident description.</summary>
    High,

    /// <summary>Partially obscured, moderate lighting. Some hedging ("appears to be").</summary>
    Medium,

    /// <summary>Poor visibility, low-res, heavy motion blur. Very cautious language.</summary>
    Low,

    /// <summary>Frame is too dark/blurred to describe meaningfully.</summary>
    Indeterminate
}

/// <summary>
/// Result of narrating a single video frame.
/// </summary>
public record SceneNarration(
    /// <summary>The narration text describing what the AI sees.</summary>
    string NarrationText,

    /// <summary>Confidence level of the description.</summary>
    NarrationConfidence Confidence,

    /// <summary>
    /// Whether the scene has changed significantly from the previous narration.
    /// If false, the client may choose to suppress this narration to avoid
    /// repetitive descriptions ("same scene, no change").
    /// </summary>
    bool SceneChanged,

    /// <summary>
    /// Number of people detected in the frame (0 if none visible).
    /// Used for dashboard summary without revealing identities.
    /// </summary>
    int PeopleCount,

    /// <summary>
    /// Number of vehicles detected in the frame.
    /// </summary>
    int VehicleCount,

    /// <summary>
    /// Environmental notes (lighting, weather, etc.) extracted from the frame.
    /// </summary>
    string? EnvironmentNote,

    /// <summary>
    /// If the narrator detects something that may warrant escalation (e.g.,
    /// visible weapon, fire, medical emergency), this flag is set.
    /// The system uses this as a HINT to suggest escalation to the caller,
    /// but never auto-escalates from narration alone.
    /// </summary>
    bool EscalationHint,

    /// <summary>Reason for escalation hint, if set.</summary>
    string? EscalationHintReason,

    /// <summary>Processing latency in milliseconds.</summary>
    long LatencyMs,

    /// <summary>Timestamp when the narration was produced.</summary>
    DateTime Timestamp
);

/// <summary>
/// The system prompt template used to instruct the vision model.
/// Exposed so it can be audited, versioned, and tested independently.
/// </summary>
public record NarrationSystemPrompt(
    string PromptId,
    string Version,
    string PromptText,
    DateTime LastUpdated
);

// ═══════════════════════════════════════════════════════════════
// Port Interface
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Port for AI-powered scene narration during Watch Calls.
///
/// Adapters:
///   - MockSceneNarrationAdapter: returns pre-scripted narrations (dev)
///   - AzureOpenAISceneNarrationAdapter: GPT-4o vision for real-time narration (prod)
///   - Future: Google Gemini Pro Vision, Anthropic Claude vision
///
/// The adapter is stateless per-call. Scene change detection is handled by
/// comparing the current narration to the previous one (maintained by the caller).
/// </summary>
public interface ISceneNarrationPort
{
    /// <summary>
    /// The AI model/provider name used for narration.
    /// Examples: "AzureOpenAI-GPT4o", "Mock", "Gemini-ProVision"
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Narrate a single video frame. The frame is a JPEG/PNG image provided
    /// as a byte array. The adapter sends it to the vision model with the
    /// guardrailed system prompt and returns a neutral scene description.
    ///
    /// The previousNarration parameter enables scene-change detection:
    /// if the scene hasn't changed, the adapter can return a lighter response
    /// with SceneChanged=false.
    /// </summary>
    /// <param name="frameData">JPEG or PNG image bytes (single frame).</param>
    /// <param name="callId">The Watch Call this frame belongs to (for logging).</param>
    /// <param name="previousNarration">The last narration text, for scene-change detection. Null if first frame.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A SceneNarration describing the frame contents.</returns>
    Task<SceneNarration> NarrateFrameAsync(
        byte[] frameData,
        string callId,
        string? previousNarration = null,
        CancellationToken ct = default);

    /// <summary>
    /// Narrate a frame with additional audio context (e.g., ambient sounds
    /// captured by the device microphone). The audio provides extra signal
    /// for the narrator ("I hear raised voices" or "The area is quiet").
    /// </summary>
    /// <param name="frameData">JPEG or PNG image bytes.</param>
    /// <param name="audioSnippet">Short audio clip (up to 5 seconds, WAV/OGG).</param>
    /// <param name="callId">The Watch Call this frame belongs to.</param>
    /// <param name="previousNarration">Previous narration for scene-change detection.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<SceneNarration> NarrateFrameWithAudioAsync(
        byte[] frameData,
        byte[] audioSnippet,
        string callId,
        string? previousNarration = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get the current system prompt being used for narration.
    /// Useful for auditing and prompt versioning.
    /// </summary>
    Task<NarrationSystemPrompt> GetCurrentPromptAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Validate that a narration text conforms to the guardrails.
    /// Checks for prohibited language patterns (racial descriptors, subjective
    /// assessments, assumed intent, etc.). Returns null if valid, or a list
    /// of violations if the narration would fail guardrails.
    ///
    /// This is used for:
    ///   1. Post-hoc validation of AI-generated narrations
    ///   2. Testing new prompt versions against a corpus of expected outputs
    ///   3. Auditing historical narration transcripts
    /// </summary>
    Task<IReadOnlyList<string>?> ValidateNarrationAsync(
        string narrationText, CancellationToken ct = default);
}
