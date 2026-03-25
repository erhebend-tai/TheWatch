// =============================================================================
// Mock Scene Narration Adapter — simulates AI vision narration for development.
// =============================================================================
// Returns pre-scripted neutral narrations from a rotation pool, simulating
// 1-2 second processing latency. Useful for UI development and testing the
// narration display pipeline without burning vision API credits.
//
// The mock narrations follow the same guardrails as production:
//   - Factual, neutral descriptions only
//   - No identification of individuals by protected characteristics
//   - No subjective assessments or assumed intent
//
// WAL: No actual image processing occurs. Frame data is accepted but discarded.
// =============================================================================

using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Mock;

public class MockSceneNarrationAdapter : ISceneNarrationPort
{
    private readonly ILogger<MockSceneNarrationAdapter> _logger;
    private readonly Random _rng = new();
    private int _callCounter;

    // Pre-scripted narrations that rotate based on call count
    private static readonly string[] NarrationPool =
    {
        "I see one person standing near the sidewalk. They appear to be looking at a handheld device. The area is well-lit by a nearby streetlamp.",
        "Two people are visible near the entrance of a building. They are standing and appear to be having a conversation. The scene is calm.",
        "I see a person walking along a path at a normal pace. They are carrying a bag. No other people are visible in the frame.",
        "A vehicle is parked along the curb. No one is visible inside or near the vehicle. The surrounding area appears quiet.",
        "One person is visible near a residential driveway. They appear to be approaching a door. A porch light is on.",
        "I see two people walking together on a sidewalk. They are moving at a relaxed pace in the same direction. The street has moderate traffic.",
        "A person is standing near a parked bicycle. They appear to be adjusting something on the bicycle. The area is a residential street.",
        "I see one person sitting on a bench. They are looking at a handheld device. The surrounding area is quiet with no other people visible.",
        "A vehicle has stopped near an intersection. A person is exiting the vehicle from the passenger side. They are carrying a small bag.",
        "I see a person near a mailbox. They appear to be retrieving items from it. This appears to be a residential area with houses on both sides."
    };

    private static readonly string[] EnvironmentNotes =
    {
        "Street lighting is adequate.",
        "The area appears to be a quiet residential neighborhood.",
        "Moderate ambient lighting from nearby buildings.",
        "The scene is well-lit. Clear visibility.",
        "Low ambient light. Streetlamp provides primary illumination.",
        null!  // Sometimes no environment note
    };

    // Narrations that include escalation hints (for testing the escalation flow)
    private static readonly (string Narration, string Reason)[] EscalationNarrations =
    {
        ("I see what appears to be smoke coming from a window of a building. One person is visible outside, appearing to move quickly away from the area.",
         "Possible smoke detected from building"),
        ("I see a person on the ground who does not appear to be moving. Another person is nearby and appears to be calling for assistance.",
         "Person on ground, possible medical situation")
    };

    public MockSceneNarrationAdapter(ILogger<MockSceneNarrationAdapter> logger)
    {
        _logger = logger;
    }

    public string ProviderName => "Mock";

    public async Task<SceneNarration> NarrateFrameAsync(
        byte[] frameData, string callId, string? previousNarration = null,
        CancellationToken ct = default)
    {
        // Simulate processing latency (800-2000ms)
        var latency = _rng.Next(800, 2000);
        await Task.Delay(latency, ct);

        var counter = Interlocked.Increment(ref _callCounter);

        // Every 15th call returns an escalation hint narration (for testing)
        if (counter % 15 == 0)
        {
            var (escalationText, reason) = EscalationNarrations[counter % EscalationNarrations.Length];
            _logger.LogInformation("[WAL-NARRATION-MOCK] Call {CallId}: Returning escalation-hint narration", callId);

            return new SceneNarration(
                NarrationText: escalationText,
                Confidence: NarrationConfidence.High,
                SceneChanged: true,
                PeopleCount: _rng.Next(1, 3),
                VehicleCount: 0,
                EnvironmentNote: "Visibility is clear.",
                EscalationHint: true,
                EscalationHintReason: reason,
                LatencyMs: latency,
                Timestamp: DateTime.UtcNow);
        }

        // Normal narration from rotation pool
        var narrationIndex = counter % NarrationPool.Length;
        var narrationText = NarrationPool[narrationIndex];

        // Determine if scene changed from previous
        var sceneChanged = previousNarration == null || previousNarration != narrationText;

        // Vary confidence based on mock "frame quality"
        var confidence = counter % 10 == 0
            ? NarrationConfidence.Low
            : counter % 5 == 0
                ? NarrationConfidence.Medium
                : NarrationConfidence.High;

        var envIndex = counter % EnvironmentNotes.Length;
        var envNote = EnvironmentNotes[envIndex];

        _logger.LogDebug("[WAL-NARRATION-MOCK] Call {CallId}: Frame {Counter}, latency {Latency}ms, confidence {Confidence}",
            callId, counter, latency, confidence);

        return new SceneNarration(
            NarrationText: narrationText,
            Confidence: confidence,
            SceneChanged: sceneChanged,
            PeopleCount: _rng.Next(0, 4),
            VehicleCount: _rng.Next(0, 3),
            EnvironmentNote: envNote,
            EscalationHint: false,
            EscalationHintReason: null,
            LatencyMs: latency,
            Timestamp: DateTime.UtcNow);
    }

    public async Task<SceneNarration> NarrateFrameWithAudioAsync(
        byte[] frameData, byte[] audioSnippet, string callId,
        string? previousNarration = null, CancellationToken ct = default)
    {
        // Mock: audio doesn't change the narration, just adds a note
        var narration = await NarrateFrameAsync(frameData, callId, previousNarration, ct);

        var audioNotes = new[]
        {
            " The ambient audio is quiet.",
            " I can hear distant traffic sounds.",
            " There are muffled voices audible but not distinct.",
            " The area sounds quiet with occasional ambient noise."
        };

        var audioNote = audioNotes[_rng.Next(audioNotes.Length)];
        return narration with { NarrationText = narration.NarrationText + audioNote };
    }

    public Task<NarrationSystemPrompt> GetCurrentPromptAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new NarrationSystemPrompt(
            PromptId: "mock-prompt-v1",
            Version: "1.0.0-mock",
            PromptText: @"You are a neutral scene narrator for a community safety system called TheWatch.
You will receive a single frame from a live video feed. Describe ONLY what you see
in plain, factual language.

RULES:
- Describe actions, objects, vehicles, and environmental conditions.
- Use ""person"" or ""people"" — never describe race, ethnicity, age, or gender.
- Never use subjective words like ""suspicious"", ""threatening"", or ""sketchy"".
- Never assume intent. Describe actions, not motivations.
- If visibility is poor, say so. Do not guess.
- Keep descriptions to 1-3 sentences.
- If nothing notable is visible, say ""The scene appears quiet with no notable activity.""",
            LastUpdated: DateTime.UtcNow.AddDays(-7)));
    }

    public Task<IReadOnlyList<string>?> ValidateNarrationAsync(string narrationText, CancellationToken ct = default)
    {
        var violations = new List<string>();

        // Check for prohibited subjective language
        var prohibitedTerms = new[]
        {
            "suspicious", "sketchy", "threatening", "menacing", "shady",
            "lurking", "prowling", "casing", "doesn't belong", "out of place",
            "thug", "criminal", "gangster", "illegal"
        };

        var lower = narrationText.ToLowerInvariant();
        foreach (var term in prohibitedTerms)
        {
            if (lower.Contains(term))
                violations.Add($"Prohibited subjective term detected: \"{term}\"");
        }

        // Check for racial/ethnic descriptors
        var racialTerms = new[]
        {
            "african", "asian", "hispanic", "latino", "caucasian", "white male",
            "black male", "white female", "black female", "middle eastern", "indian"
        };

        foreach (var term in racialTerms)
        {
            if (lower.Contains(term))
                violations.Add($"Prohibited racial/ethnic descriptor detected: \"{term}\"");
        }

        // Check for assumed intent language
        var intentPatterns = new[]
        {
            "trying to break", "attempting to steal", "about to",
            "planning to", "wants to", "intends to", "going to rob"
        };

        foreach (var pattern in intentPatterns)
        {
            if (lower.Contains(pattern))
                violations.Add($"Prohibited assumed-intent language detected: \"{pattern}\"");
        }

        return Task.FromResult(violations.Count > 0
            ? (IReadOnlyList<string>?)violations
            : null);
    }
}
