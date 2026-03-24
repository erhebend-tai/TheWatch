// PhraseMatchingEngine — deterministic, platform-independent phrase matching.
// This is the DSL-compliant matching layer. NO ML, NO cloud calls, NO randomness.
// Identical inputs ALWAYS produce identical outputs.
// Used by both Android and iOS native layers after speech-to-text transcription.
// NO database SDK imports allowed in this file.

using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Shared.Domain.Services;

/// <summary>
/// Deterministic phrase matching engine implementing four strategies:
/// Exact, Fuzzy (Levenshtein), Substring, and Phonetic (Soundex + Metaphone).
/// All methods are pure functions — no side effects, no state, no async.
/// </summary>
public sealed class PhraseMatchingEngine : IPhraseMatchingEngine
{
    /// <inheritdoc />
    public PhraseMatchResult Evaluate(string transcribedText, IReadOnlyList<EmergencyPhrase> activePhrases)
    {
        if (string.IsNullOrWhiteSpace(transcribedText) || activePhrases.Count == 0)
        {
            return NoMatch(transcribedText);
        }

        var normalized = NormalizeText(transcribedText);
        PhraseMatchResult? bestMatch = null;

        foreach (var phrase in activePhrases)
        {
            if (!phrase.IsActive) continue;

            var result = MatchPhrase(normalized, transcribedText, phrase);

            if (result.IsMatch && (bestMatch is null || result.Confidence > bestMatch.Confidence))
            {
                bestMatch = result;
            }
        }

        return bestMatch ?? NoMatch(transcribedText, normalized);
    }

    /// <inheritdoc />
    public PhraseMatchResult EvaluateSingle(string transcribedText, EmergencyPhrase phrase)
    {
        if (string.IsNullOrWhiteSpace(transcribedText))
        {
            return NoMatch(transcribedText);
        }

        var normalized = NormalizeText(transcribedText);
        return MatchPhrase(normalized, transcribedText, phrase);
    }

    // ─────────────────────────────────────────────────────────────
    // Core matching dispatcher
    // ─────────────────────────────────────────────────────────────

    private static PhraseMatchResult MatchPhrase(string normalizedTranscript, string rawTranscript, EmergencyPhrase phrase)
    {
        var normalizedPhrase = NormalizeText(phrase.PhraseText);

        var (isMatch, confidence) = phrase.Strategy switch
        {
            PhraseMatchStrategy.Exact     => MatchExact(normalizedTranscript, normalizedPhrase),
            PhraseMatchStrategy.Fuzzy     => MatchFuzzy(normalizedTranscript, normalizedPhrase),
            PhraseMatchStrategy.Substring => MatchSubstring(normalizedTranscript, normalizedPhrase),
            PhraseMatchStrategy.Phonetic  => MatchPhonetic(normalizedTranscript, normalizedPhrase),
            _ => (false, 0f)
        };

        // Apply confidence threshold
        var meetsThreshold = isMatch && confidence >= phrase.ConfidenceThreshold;

        return new PhraseMatchResult(
            IsMatch: meetsThreshold,
            MatchedPhrase: meetsThreshold ? phrase : null,
            Confidence: confidence,
            StrategyUsed: phrase.Strategy,
            TranscribedText: rawTranscript,
            NormalizedTranscript: normalizedTranscript,
            DetectedAt: DateTime.UtcNow
        );
    }

    // ─────────────────────────────────────────────────────────────
    // Strategy: Exact (case-insensitive, whitespace-normalized)
    // ─────────────────────────────────────────────────────────────

    private static (bool isMatch, float confidence) MatchExact(string normalizedTranscript, string normalizedPhrase)
    {
        var match = string.Equals(normalizedTranscript, normalizedPhrase, StringComparison.OrdinalIgnoreCase);
        return (match, match ? 1.0f : 0.0f);
    }

    // ─────────────────────────────────────────────────────────────
    // Strategy: Fuzzy (Levenshtein distance within tolerance)
    // Handles mumbling, slight mispronunciation, accent variation.
    // Tolerance: distance must be ≤ 20% of phrase length.
    // ─────────────────────────────────────────────────────────────

    private static (bool isMatch, float confidence) MatchFuzzy(string normalizedTranscript, string normalizedPhrase)
    {
        // For fuzzy, we check the transcript against the phrase directly,
        // but also check if the phrase appears as a fuzzy substring.
        var distance = LevenshteinDistance(normalizedTranscript, normalizedPhrase);
        var maxLen = Math.Max(normalizedTranscript.Length, normalizedPhrase.Length);

        if (maxLen == 0) return (false, 0f);

        var similarity = 1.0f - ((float)distance / maxLen);

        // Also try sliding window fuzzy match for longer transcripts
        if (normalizedTranscript.Length > normalizedPhrase.Length + 5)
        {
            var windowSimilarity = SlidingWindowFuzzy(normalizedTranscript, normalizedPhrase);
            similarity = Math.Max(similarity, windowSimilarity);
        }

        var isMatch = similarity >= 0.80f; // 80% similarity threshold
        return (isMatch, similarity);
    }

    /// <summary>
    /// Slides a window of phrase length across the transcript,
    /// computing Levenshtein at each position to find the best fuzzy substring match.
    /// </summary>
    private static float SlidingWindowFuzzy(string transcript, string phrase)
    {
        var phraseLen = phrase.Length;
        var bestSimilarity = 0f;

        // Window size: phrase length ± 20%
        var minWindow = Math.Max(1, (int)(phraseLen * 0.8));
        var maxWindow = Math.Min(transcript.Length, (int)(phraseLen * 1.2));

        for (var windowSize = minWindow; windowSize <= maxWindow; windowSize++)
        {
            for (var start = 0; start <= transcript.Length - windowSize; start++)
            {
                var window = transcript.Substring(start, windowSize);
                var distance = LevenshteinDistance(window, phrase);
                var similarity = 1.0f - ((float)distance / Math.Max(windowSize, phraseLen));
                bestSimilarity = Math.Max(bestSimilarity, similarity);

                // Early exit if we found a very good match
                if (bestSimilarity >= 0.95f) return bestSimilarity;
            }
        }

        return bestSimilarity;
    }

    /// <summary>
    /// Standard Levenshtein distance — O(n*m) with single-row optimization.
    /// Deterministic, no allocation beyond the working array.
    /// </summary>
    public static int LevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source)) return target?.Length ?? 0;
        if (string.IsNullOrEmpty(target)) return source.Length;

        var sourceLen = source.Length;
        var targetLen = target.Length;

        // Single-row DP (space-optimized)
        var previousRow = new int[targetLen + 1];
        var currentRow = new int[targetLen + 1];

        for (var j = 0; j <= targetLen; j++)
            previousRow[j] = j;

        for (var i = 1; i <= sourceLen; i++)
        {
            currentRow[0] = i;

            for (var j = 1; j <= targetLen; j++)
            {
                var cost = char.ToLowerInvariant(source[i - 1]) == char.ToLowerInvariant(target[j - 1]) ? 0 : 1;
                currentRow[j] = Math.Min(
                    Math.Min(currentRow[j - 1] + 1, previousRow[j] + 1),
                    previousRow[j - 1] + cost
                );
            }

            // Swap rows
            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[targetLen];
    }

    // ─────────────────────────────────────────────────────────────
    // Strategy: Substring (phrase appears anywhere in transcript)
    // ─────────────────────────────────────────────────────────────

    private static (bool isMatch, float confidence) MatchSubstring(string normalizedTranscript, string normalizedPhrase)
    {
        var contains = normalizedTranscript.Contains(normalizedPhrase, StringComparison.OrdinalIgnoreCase);

        if (!contains) return (false, 0f);

        // Confidence is based on how much of the transcript the phrase covers.
        // Longer surrounding text = slightly lower confidence (more noise).
        var coverage = (float)normalizedPhrase.Length / normalizedTranscript.Length;
        var confidence = 0.85f + (0.15f * coverage); // Range: 0.85–1.0

        return (true, confidence);
    }

    // ─────────────────────────────────────────────────────────────
    // Strategy: Phonetic (Soundex + Double Metaphone)
    // Handles homophones, pronunciation variants, accent.
    // Compares word-by-word phonetic codes.
    // ─────────────────────────────────────────────────────────────

    private static (bool isMatch, float confidence) MatchPhonetic(string normalizedTranscript, string normalizedPhrase)
    {
        var transcriptWords = SplitWords(normalizedTranscript);
        var phraseWords = SplitWords(normalizedPhrase);

        if (phraseWords.Length == 0) return (false, 0f);

        // Generate phonetic codes for phrase words
        var phrasePhonetics = phraseWords.Select(w => (
            soundex: Soundex(w),
            metaphone: DoubleMetaphone(w)
        )).ToArray();

        // Sliding window: find best consecutive match of phrase words in transcript
        var bestMatchCount = 0;
        var bestStartIndex = -1;

        for (var start = 0; start <= transcriptWords.Length - phraseWords.Length; start++)
        {
            var matchCount = 0;

            for (var p = 0; p < phraseWords.Length; p++)
            {
                var tWord = transcriptWords[start + p];
                var tSoundex = Soundex(tWord);
                var tMetaphone = DoubleMetaphone(tWord);

                var soundexMatch = string.Equals(tSoundex, phrasePhonetics[p].soundex, StringComparison.OrdinalIgnoreCase);
                var metaphoneMatch = string.Equals(tMetaphone, phrasePhonetics[p].metaphone, StringComparison.OrdinalIgnoreCase);

                if (soundexMatch || metaphoneMatch)
                    matchCount++;
            }

            if (matchCount > bestMatchCount)
            {
                bestMatchCount = matchCount;
                bestStartIndex = start;

                if (bestMatchCount == phraseWords.Length) break; // Perfect match
            }
        }

        if (bestMatchCount == 0) return (false, 0f);

        var confidence = (float)bestMatchCount / phraseWords.Length;
        var isMatch = confidence >= 0.75f; // At least 75% of words match phonetically

        return (isMatch, confidence);
    }

    /// <summary>
    /// American Soundex algorithm.
    /// Deterministic: same input always produces same 4-character code.
    /// </summary>
    public static string Soundex(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return "0000";

        // Clean to letters only
        var clean = new string(word.Where(char.IsLetter).ToArray()).ToUpperInvariant();
        if (clean.Length == 0) return "0000";

        var result = new char[4];
        result[0] = clean[0];
        var lastCode = SoundexCode(clean[0]);
        var index = 1;

        for (var i = 1; i < clean.Length && index < 4; i++)
        {
            var code = SoundexCode(clean[i]);
            if (code != '0' && code != lastCode)
            {
                result[index++] = code;
            }
            lastCode = code;
        }

        while (index < 4)
            result[index++] = '0';

        return new string(result);
    }

    private static char SoundexCode(char c) => char.ToUpperInvariant(c) switch
    {
        'B' or 'F' or 'P' or 'V' => '1',
        'C' or 'G' or 'J' or 'K' or 'Q' or 'S' or 'X' or 'Z' => '2',
        'D' or 'T' => '3',
        'L' => '4',
        'M' or 'N' => '5',
        'R' => '6',
        _ => '0' // A, E, I, O, U, H, W, Y
    };

    /// <summary>
    /// Simplified Double Metaphone — produces a primary phonetic code.
    /// Deterministic. Handles common English pronunciation patterns.
    /// Full Double Metaphone has ~100 rules; this covers the most impactful ones
    /// for emergency phrase matching (where clarity matters most).
    /// </summary>
    internal static string DoubleMetaphone(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return "";

        var input = word.ToUpperInvariant();
        var result = new List<char>(8);
        var i = 0;

        // Skip silent initial letters
        if (input.Length >= 2)
        {
            var firstTwo = input[..2];
            if (firstTwo is "GN" or "KN" or "PN" or "AE" or "WR")
                i = 1;
        }

        while (i < input.Length && result.Count < 6)
        {
            var c = input[i];

            switch (c)
            {
                case 'A' or 'E' or 'I' or 'O' or 'U':
                    // Vowels only coded at start
                    if (i == 0) result.Add('A');
                    i++;
                    break;

                case 'B':
                    result.Add('P');
                    i += (i + 1 < input.Length && input[i + 1] == 'B') ? 2 : 1;
                    break;

                case 'C':
                    if (i + 1 < input.Length && input[i + 1] is 'E' or 'I' or 'Y')
                    {
                        result.Add('S'); // CE, CI, CY → S
                        i += 2;
                    }
                    else
                    {
                        result.Add('K');
                        i += (i + 1 < input.Length && input[i + 1] == 'C') ? 2 : 1;
                    }
                    break;

                case 'D':
                    if (i + 1 < input.Length && input[i + 1] == 'G')
                    {
                        if (i + 2 < input.Length && input[i + 2] is 'E' or 'I' or 'Y')
                        {
                            result.Add('J'); // DGE, DGI, DGY → J
                            i += 3;
                        }
                        else
                        {
                            result.Add('T');
                            i += 2;
                        }
                    }
                    else
                    {
                        result.Add('T');
                        i += (i + 1 < input.Length && input[i + 1] == 'D') ? 2 : 1;
                    }
                    break;

                case 'F':
                    result.Add('F');
                    i += (i + 1 < input.Length && input[i + 1] == 'F') ? 2 : 1;
                    break;

                case 'G':
                    if (i + 1 < input.Length && input[i + 1] == 'H')
                    {
                        // GH at end or before consonant = silent
                        if (i + 2 >= input.Length || !IsVowel(input[i + 2]))
                        {
                            i += 2;
                        }
                        else
                        {
                            result.Add('K');
                            i += 2;
                        }
                    }
                    else if (i + 1 < input.Length && input[i + 1] is 'E' or 'I' or 'Y')
                    {
                        result.Add('J');
                        i += 2;
                    }
                    else
                    {
                        result.Add('K');
                        i += (i + 1 < input.Length && input[i + 1] == 'G') ? 2 : 1;
                    }
                    break;

                case 'H':
                    // H is coded only if before a vowel and not after a vowel
                    if (i + 1 < input.Length && IsVowel(input[i + 1]))
                    {
                        if (i == 0 || !IsVowel(input[i - 1]))
                            result.Add('H');
                    }
                    i++;
                    break;

                case 'J':
                    result.Add('J');
                    i += (i + 1 < input.Length && input[i + 1] == 'J') ? 2 : 1;
                    break;

                case 'K':
                    result.Add('K');
                    i += (i + 1 < input.Length && input[i + 1] == 'K') ? 2 : 1;
                    break;

                case 'L':
                    result.Add('L');
                    i += (i + 1 < input.Length && input[i + 1] == 'L') ? 2 : 1;
                    break;

                case 'M':
                    result.Add('M');
                    i += (i + 1 < input.Length && input[i + 1] == 'M') ? 2 : 1;
                    break;

                case 'N':
                    result.Add('N');
                    i += (i + 1 < input.Length && input[i + 1] == 'N') ? 2 : 1;
                    break;

                case 'P':
                    if (i + 1 < input.Length && input[i + 1] == 'H')
                    {
                        result.Add('F'); // PH → F
                        i += 2;
                    }
                    else
                    {
                        result.Add('P');
                        i += (i + 1 < input.Length && input[i + 1] == 'P') ? 2 : 1;
                    }
                    break;

                case 'Q':
                    result.Add('K');
                    i += (i + 1 < input.Length && input[i + 1] == 'U') ? 2 : 1;
                    break;

                case 'R':
                    result.Add('R');
                    i += (i + 1 < input.Length && input[i + 1] == 'R') ? 2 : 1;
                    break;

                case 'S':
                    if (i + 1 < input.Length && input[i + 1] == 'H')
                    {
                        result.Add('X'); // SH → X (ʃ)
                        i += 2;
                    }
                    else if (i + 2 < input.Length && input[i..(i + 3)] == "SIO")
                    {
                        result.Add('X');
                        i += 3;
                    }
                    else if (i + 2 < input.Length && input[i..(i + 3)] == "SIA")
                    {
                        result.Add('X');
                        i += 3;
                    }
                    else
                    {
                        result.Add('S');
                        i += (i + 1 < input.Length && input[i + 1] == 'S') ? 2 : 1;
                    }
                    break;

                case 'T':
                    if (i + 1 < input.Length && input[i + 1] == 'H')
                    {
                        result.Add('0'); // TH → 0 (θ)
                        i += 2;
                    }
                    else if (i + 2 < input.Length && input[i..(i + 3)] is "TIO" or "TIA")
                    {
                        result.Add('X');
                        i += 3;
                    }
                    else
                    {
                        result.Add('T');
                        i += (i + 1 < input.Length && input[i + 1] == 'T') ? 2 : 1;
                    }
                    break;

                case 'V':
                    result.Add('F');
                    i += (i + 1 < input.Length && input[i + 1] == 'V') ? 2 : 1;
                    break;

                case 'W':
                    // W before vowel
                    if (i + 1 < input.Length && IsVowel(input[i + 1]))
                        result.Add('W');
                    i++;
                    break;

                case 'X':
                    result.Add('K');
                    result.Add('S');
                    i++;
                    break;

                case 'Y':
                    if (i + 1 < input.Length && IsVowel(input[i + 1]))
                        result.Add('A');
                    i++;
                    break;

                case 'Z':
                    result.Add('S');
                    i += (i + 1 < input.Length && input[i + 1] == 'Z') ? 2 : 1;
                    break;

                default:
                    i++;
                    break;
            }
        }

        return new string(result.ToArray());
    }

    // ─────────────────────────────────────────────────────────────
    // Text normalization
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Normalize text for matching: lowercase, collapse whitespace,
    /// strip punctuation, trim. Deterministic.
    /// </summary>
    public static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var chars = new List<char>(text.Length);
        var lastWasSpace = true; // Suppress leading spaces

        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                chars.Add(char.ToLowerInvariant(c));
                lastWasSpace = false;
            }
            else if (char.IsWhiteSpace(c) || c == '\'' || c == '-')
            {
                // Collapse whitespace and treat apostrophes/hyphens as spaces
                if (!lastWasSpace)
                {
                    chars.Add(' ');
                    lastWasSpace = true;
                }
            }
            // Other punctuation is stripped entirely
        }

        // Remove trailing space
        if (chars.Count > 0 && chars[^1] == ' ')
            chars.RemoveAt(chars.Count - 1);

        return new string(chars.ToArray());
    }

    private static string[] SplitWords(string normalized)
        => normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static bool IsVowel(char c)
        => c is 'A' or 'E' or 'I' or 'O' or 'U';

    private static PhraseMatchResult NoMatch(string transcribedText, string? normalized = null)
        => new(
            IsMatch: false,
            MatchedPhrase: null,
            Confidence: 0f,
            StrategyUsed: PhraseMatchStrategy.Exact,
            TranscribedText: transcribedText,
            NormalizedTranscript: normalized,
            DetectedAt: DateTime.UtcNow
        );
}
