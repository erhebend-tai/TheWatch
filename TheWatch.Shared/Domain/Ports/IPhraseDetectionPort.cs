// IPhraseDetectionPort — domain port for voice-activated emergency phrase detection.
// The speech-to-text engine MAY use ML (that's just transcription).
// The phrase MATCHING against transcribed text MUST be deterministic (DSL constraint).
// All audio processing MUST be on-device — never send safety phrases to cloud APIs.
// NO database SDK imports allowed in this file.

namespace TheWatch.Shared.Domain.Ports;

/// <summary>
/// Represents a user-configured emergency phrase or duress code.
/// </summary>
public record EmergencyPhrase(
    string PhraseId,
    string UserId,
    string PhraseText,            // The actual phrase, e.g., "I need to walk the dog"
    PhraseType Type,              // Duress, ClearWord, or Custom
    PhraseMatchStrategy Strategy, // How to match: Exact, Fuzzy, Substring, Phonetic
    float ConfidenceThreshold,    // 0.0–1.0, minimum confidence to trigger (default 0.80)
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastTriggeredAt
);

public enum PhraseType
{
    /// <summary>Duress code — triggers silent SOS without visible alert on screen.</summary>
    Duress,
    /// <summary>Clear word — cancels an active alert, confirms user is safe.</summary>
    ClearWord,
    /// <summary>Custom emergency trigger — triggers standard SOS.</summary>
    Custom
}

public enum PhraseMatchStrategy
{
    /// <summary>Exact string match (case-insensitive, whitespace-normalized).</summary>
    Exact,
    /// <summary>Levenshtein distance within tolerance (handles mumbling, accent).</summary>
    Fuzzy,
    /// <summary>Phrase appears anywhere in transcribed speech.</summary>
    Substring,
    /// <summary>Soundex/Metaphone comparison (handles homophones, pronunciation).</summary>
    Phonetic
}

/// <summary>
/// Result of a phrase match attempt against transcribed speech.
/// </summary>
public record PhraseMatchResult(
    bool IsMatch,
    EmergencyPhrase? MatchedPhrase,
    float Confidence,              // 0.0–1.0
    PhraseMatchStrategy StrategyUsed,
    string TranscribedText,        // What the engine heard
    string? NormalizedTranscript,   // After normalization
    DateTime DetectedAt
);

/// <summary>
/// Port for managing user-configured emergency phrases (CRUD).
/// Implemented by storage adapters.
/// </summary>
public interface IPhraseProgrammingPort
{
    Task<List<EmergencyPhrase>> GetPhrasesForUserAsync(string userId, CancellationToken ct = default);
    Task<EmergencyPhrase> AddPhraseAsync(EmergencyPhrase phrase, CancellationToken ct = default);
    Task<EmergencyPhrase> UpdatePhraseAsync(EmergencyPhrase phrase, CancellationToken ct = default);
    Task<bool> RemovePhraseAsync(string phraseId, CancellationToken ct = default);
    Task<EmergencyPhrase?> GetPhraseByIdAsync(string phraseId, CancellationToken ct = default);
    Task<bool> ValidatePhraseUniqueness(string userId, string phraseText, CancellationToken ct = default);
}

/// <summary>
/// Port for the deterministic phrase matching engine.
/// This is the DSL-compliant matching layer — NO ML/AI here.
/// Takes transcribed text and checks it against registered phrases.
/// Platform-independent: same algorithm on Android, iOS, and server.
/// </summary>
public interface IPhraseMatchingEngine
{
    /// <summary>
    /// Check transcribed speech against all active phrases for a user.
    /// Returns the best match (highest confidence above threshold), or no match.
    /// DETERMINISTIC: identical inputs always produce identical outputs.
    /// </summary>
    PhraseMatchResult Evaluate(string transcribedText, IReadOnlyList<EmergencyPhrase> activePhrases);

    /// <summary>
    /// Check transcribed speech against a single phrase.
    /// Used for testing/validation in the Profile UI.
    /// </summary>
    PhraseMatchResult EvaluateSingle(string transcribedText, EmergencyPhrase phrase);
}

/// <summary>
/// Port for the native speech-to-text listener.
/// Implemented per-platform: SFSpeechRecognizer (iOS), SpeechRecognizer/Vosk (Android).
/// This layer MAY use ML — it's just transcription, not decision-making.
/// </summary>
public interface IPhraseDetectionPort
{
    /// <summary>Is the listener currently active and processing audio?</summary>
    bool IsListening { get; }

    /// <summary>Is on-device speech recognition available on this device?</summary>
    bool IsAvailable { get; }

    /// <summary>Start continuous listening. Audio stays on-device.</summary>
    Task<bool> StartListeningAsync(CancellationToken ct = default);

    /// <summary>Stop listening and release audio resources.</summary>
    Task StopListeningAsync(CancellationToken ct = default);

    /// <summary>
    /// Event raised when speech is transcribed.
    /// The native layer transcribes; the PhraseMatchingEngine evaluates.
    /// Separation of concerns: transcription (ML-ok) vs matching (deterministic).
    /// </summary>
    event EventHandler<SpeechTranscriptionEventArgs>? OnSpeechTranscribed;

    /// <summary>
    /// Event raised when a phrase match is detected.
    /// Fired after PhraseMatchingEngine.Evaluate returns a match.
    /// </summary>
    event EventHandler<PhraseMatchResult>? OnPhraseDetected;
}

public class SpeechTranscriptionEventArgs : EventArgs
{
    public required string TranscribedText { get; init; }
    public required float Confidence { get; init; }
    public required bool IsFinal { get; init; }        // false = partial/interim result
    public required DateTime Timestamp { get; init; }
}
