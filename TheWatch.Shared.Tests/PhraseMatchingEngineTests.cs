// PhraseMatchingEngine unit tests — verifies all four matching strategies
// are deterministic and produce expected results for emergency phrase detection.

using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Domain.Services;

namespace TheWatch.Shared.Tests;

public class PhraseMatchingEngineTests
{
    private readonly PhraseMatchingEngine _engine = new();

    // ─────────────────────────────────────────────
    // Helper: create a test phrase
    // ─────────────────────────────────────────────

    private static EmergencyPhrase MakePhrase(
        string text,
        PhraseMatchStrategy strategy = PhraseMatchStrategy.Exact,
        PhraseType type = PhraseType.Duress,
        float threshold = 0.80f)
        => new(
            PhraseId: Guid.NewGuid().ToString(),
            UserId: "test-user",
            PhraseText: text,
            Type: type,
            Strategy: strategy,
            ConfidenceThreshold: threshold,
            IsActive: true,
            CreatedAt: DateTime.UtcNow,
            LastTriggeredAt: null
        );

    // ═════════════════════════════════════════════
    // EXACT MATCH TESTS
    // ═════════════════════════════════════════════

    [Fact]
    public void Exact_IdenticalText_Matches()
    {
        var phrase = MakePhrase("I need to walk the dog", PhraseMatchStrategy.Exact);
        var result = _engine.EvaluateSingle("I need to walk the dog", phrase);

        Assert.True(result.IsMatch);
        Assert.Equal(1.0f, result.Confidence);
        Assert.Equal(PhraseMatchStrategy.Exact, result.StrategyUsed);
    }

    [Fact]
    public void Exact_CaseInsensitive_Matches()
    {
        var phrase = MakePhrase("help me now", PhraseMatchStrategy.Exact);
        var result = _engine.EvaluateSingle("HELP ME NOW", phrase);

        Assert.True(result.IsMatch);
        Assert.Equal(1.0f, result.Confidence);
    }

    [Fact]
    public void Exact_ExtraWhitespace_Matches()
    {
        var phrase = MakePhrase("call the police", PhraseMatchStrategy.Exact);
        var result = _engine.EvaluateSingle("  call   the   police  ", phrase);

        Assert.True(result.IsMatch);
    }

    [Fact]
    public void Exact_DifferentText_NoMatch()
    {
        var phrase = MakePhrase("I need to walk the dog", PhraseMatchStrategy.Exact);
        var result = _engine.EvaluateSingle("The weather is nice today", phrase);

        Assert.False(result.IsMatch);
        Assert.Equal(0f, result.Confidence);
    }

    [Fact]
    public void Exact_PartialText_NoMatch()
    {
        var phrase = MakePhrase("walk the dog", PhraseMatchStrategy.Exact);
        var result = _engine.EvaluateSingle("I need to walk the dog now", phrase);

        Assert.False(result.IsMatch); // Exact means EXACT
    }

    // ═════════════════════════════════════════════
    // FUZZY MATCH TESTS (Levenshtein)
    // ═════════════════════════════════════════════

    [Fact]
    public void Fuzzy_ExactText_PerfectConfidence()
    {
        var phrase = MakePhrase("send help", PhraseMatchStrategy.Fuzzy);
        var result = _engine.EvaluateSingle("send help", phrase);

        Assert.True(result.IsMatch);
        Assert.Equal(1.0f, result.Confidence);
    }

    [Fact]
    public void Fuzzy_MinorTypo_Matches()
    {
        var phrase = MakePhrase("I need to walk the dog", PhraseMatchStrategy.Fuzzy);
        // "wlak" instead of "walk" — 1 transposition
        var result = _engine.EvaluateSingle("I need to wlak the dog", phrase);

        Assert.True(result.IsMatch);
        Assert.True(result.Confidence >= 0.80f);
    }

    [Fact]
    public void Fuzzy_Mumbled_StillMatches()
    {
        var phrase = MakePhrase("call nine one one", PhraseMatchStrategy.Fuzzy);
        // Missing a letter, slight garble
        var result = _engine.EvaluateSingle("cal nine one one", phrase);

        Assert.True(result.IsMatch);
        Assert.True(result.Confidence >= 0.80f);
    }

    [Fact]
    public void Fuzzy_CompletelyDifferent_NoMatch()
    {
        var phrase = MakePhrase("I need to walk the dog", PhraseMatchStrategy.Fuzzy);
        var result = _engine.EvaluateSingle("the sky is blue and bright", phrase);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Fuzzy_PhraseInsideLongerTranscript_Matches()
    {
        var phrase = MakePhrase("walk the dog", PhraseMatchStrategy.Fuzzy);
        // Phrase embedded in longer speech
        var result = _engine.EvaluateSingle("hey I think I need to walk the dog right now", phrase);

        Assert.True(result.IsMatch);
        Assert.True(result.Confidence >= 0.80f);
    }

    // ═════════════════════════════════════════════
    // SUBSTRING MATCH TESTS
    // ═════════════════════════════════════════════

    [Fact]
    public void Substring_PhraseInMiddle_Matches()
    {
        var phrase = MakePhrase("walk the dog", PhraseMatchStrategy.Substring);
        var result = _engine.EvaluateSingle("I really need to walk the dog right now", phrase);

        Assert.True(result.IsMatch);
        Assert.True(result.Confidence >= 0.85f);
    }

    [Fact]
    public void Substring_PhraseAtStart_Matches()
    {
        var phrase = MakePhrase("help me", PhraseMatchStrategy.Substring);
        var result = _engine.EvaluateSingle("help me I am in danger", phrase);

        Assert.True(result.IsMatch);
    }

    [Fact]
    public void Substring_PhraseNotPresent_NoMatch()
    {
        var phrase = MakePhrase("walk the dog", PhraseMatchStrategy.Substring);
        var result = _engine.EvaluateSingle("the weather is lovely today", phrase);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Substring_ExactPhrase_HighConfidence()
    {
        var phrase = MakePhrase("help me", PhraseMatchStrategy.Substring);
        var result = _engine.EvaluateSingle("help me", phrase);

        Assert.True(result.IsMatch);
        Assert.Equal(1.0f, result.Confidence);
    }

    // ═════════════════════════════════════════════
    // PHONETIC MATCH TESTS (Soundex + Metaphone)
    // ═════════════════════════════════════════════

    [Fact]
    public void Phonetic_IdenticalWords_Matches()
    {
        var phrase = MakePhrase("help me now", PhraseMatchStrategy.Phonetic);
        var result = _engine.EvaluateSingle("help me now", phrase);

        Assert.True(result.IsMatch);
        Assert.Equal(1.0f, result.Confidence);
    }

    [Fact]
    public void Phonetic_Homophones_Matches()
    {
        // "there" and "their" are homophones — same Soundex (T600)
        var phrase = MakePhrase("go there now", PhraseMatchStrategy.Phonetic);
        var result = _engine.EvaluateSingle("go their now", phrase);

        Assert.True(result.IsMatch);
    }

    [Fact]
    public void Phonetic_SimilarSounding_Matches()
    {
        // "night" and "nite" should match phonetically
        var phrase = MakePhrase("call at night", PhraseMatchStrategy.Phonetic);
        var result = _engine.EvaluateSingle("call at nite", phrase);

        Assert.True(result.IsMatch);
    }

    [Fact]
    public void Phonetic_CompletelyDifferent_NoMatch()
    {
        var phrase = MakePhrase("walk the dog", PhraseMatchStrategy.Phonetic);
        var result = _engine.EvaluateSingle("buy some milk", phrase);

        Assert.False(result.IsMatch);
    }

    // ═════════════════════════════════════════════
    // MULTI-PHRASE EVALUATION
    // ═════════════════════════════════════════════

    [Fact]
    public void Evaluate_MultiplePhrases_ReturnsBestMatch()
    {
        var phrases = new List<EmergencyPhrase>
        {
            MakePhrase("I need to walk the dog", PhraseMatchStrategy.Exact, PhraseType.Duress),
            MakePhrase("everything is fine", PhraseMatchStrategy.Exact, PhraseType.ClearWord),
            MakePhrase("send help", PhraseMatchStrategy.Substring, PhraseType.Custom),
        };

        var result = _engine.Evaluate("please send help right now", phrases);

        Assert.True(result.IsMatch);
        Assert.Equal(PhraseType.Custom, result.MatchedPhrase!.Type);
        Assert.Equal(PhraseMatchStrategy.Substring, result.StrategyUsed);
    }

    [Fact]
    public void Evaluate_NoPhraseMatches_ReturnsNoMatch()
    {
        var phrases = new List<EmergencyPhrase>
        {
            MakePhrase("walk the dog", PhraseMatchStrategy.Exact),
            MakePhrase("send help", PhraseMatchStrategy.Exact),
        };

        var result = _engine.Evaluate("the weather is nice today", phrases);

        Assert.False(result.IsMatch);
        Assert.Null(result.MatchedPhrase);
    }

    [Fact]
    public void Evaluate_InactivePhraseSkipped()
    {
        var inactive = MakePhrase("walk the dog", PhraseMatchStrategy.Exact) with { IsActive = false };
        var result = _engine.Evaluate("walk the dog", new[] { inactive });

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Evaluate_EmptyTranscript_NoMatch()
    {
        var phrases = new List<EmergencyPhrase> { MakePhrase("help") };
        var result = _engine.Evaluate("", phrases);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Evaluate_EmptyPhraseList_NoMatch()
    {
        var result = _engine.Evaluate("help me", new List<EmergencyPhrase>());
        Assert.False(result.IsMatch);
    }

    // ═════════════════════════════════════════════
    // CONFIDENCE THRESHOLD TESTS
    // ═════════════════════════════════════════════

    [Fact]
    public void Fuzzy_BelowThreshold_NoMatch()
    {
        // High threshold — only very close matches
        var phrase = MakePhrase("I need to walk the dog", PhraseMatchStrategy.Fuzzy, threshold: 0.99f);
        var result = _engine.EvaluateSingle("I need to wlak the dog", phrase);

        // The fuzzy match confidence should be high but below 0.99
        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Fuzzy_LowThreshold_MorePermissive()
    {
        var phrase = MakePhrase("emergency help", PhraseMatchStrategy.Fuzzy, threshold: 0.60f);
        var result = _engine.EvaluateSingle("emergancy halp", phrase);

        // With a lower threshold, slightly garbled speech should still match
        Assert.True(result.Confidence >= 0.60f);
    }

    // ═════════════════════════════════════════════
    // DETERMINISM TESTS
    // ═════════════════════════════════════════════

    [Fact]
    public void Deterministic_SameInputSameOutput_AllStrategies()
    {
        var strategies = new[] { PhraseMatchStrategy.Exact, PhraseMatchStrategy.Fuzzy,
                                  PhraseMatchStrategy.Substring, PhraseMatchStrategy.Phonetic };

        foreach (var strategy in strategies)
        {
            var phrase = MakePhrase("I need to walk the dog", strategy);

            var result1 = _engine.EvaluateSingle("I need to walk the dog", phrase);
            var result2 = _engine.EvaluateSingle("I need to walk the dog", phrase);

            Assert.Equal(result1.IsMatch, result2.IsMatch);
            Assert.Equal(result1.Confidence, result2.Confidence);
            Assert.Equal(result1.StrategyUsed, result2.StrategyUsed);
        }
    }

    // ═════════════════════════════════════════════
    // INTERNAL ALGORITHM TESTS
    // ═════════════════════════════════════════════

    [Theory]
    [InlineData("", "", 0)]
    [InlineData("abc", "", 3)]
    [InlineData("", "abc", 3)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("saturday", "sunday", 3)]
    [InlineData("walk", "wlak", 2)]
    public void Levenshtein_KnownDistances(string a, string b, int expected)
    {
        Assert.Equal(expected, PhraseMatchingEngine.LevenshteinDistance(a, b));
    }

    [Theory]
    [InlineData("Robert", "R163")]
    [InlineData("Rupert", "R163")]
    [InlineData("Smith", "S530")]
    [InlineData("Smythe", "S530")]
    [InlineData("", "0000")]
    public void Soundex_KnownCodes(string input, string expected)
    {
        Assert.Equal(expected, PhraseMatchingEngine.Soundex(input));
    }

    [Theory]
    [InlineData("  hello   world  ", "hello world")]
    [InlineData("HELLO", "hello")]
    [InlineData("don't stop", "don t stop")]
    [InlineData("well-known", "well known")]
    [InlineData("hello!!! world???", "hello world")]
    public void NormalizeText_KnownResults(string input, string expected)
    {
        Assert.Equal(expected, PhraseMatchingEngine.NormalizeText(input));
    }

    // ═════════════════════════════════════════════
    // DURESS vs CLEAR WORD SCENARIO TESTS
    // ═════════════════════════════════════════════

    [Fact]
    public void Scenario_DuressPhrase_TriggersCorrectType()
    {
        var duress = MakePhrase("I need to walk the dog", PhraseMatchStrategy.Substring, PhraseType.Duress);
        var clearWord = MakePhrase("everything is fine", PhraseMatchStrategy.Exact, PhraseType.ClearWord);

        // User says duress phrase embedded in conversation
        var result = _engine.Evaluate(
            "hey can you hold on I need to walk the dog real quick",
            new[] { duress, clearWord });

        Assert.True(result.IsMatch);
        Assert.Equal(PhraseType.Duress, result.MatchedPhrase!.Type);
    }

    [Fact]
    public void Scenario_ClearWord_CancelsAlert()
    {
        var duress = MakePhrase("I need to walk the dog", PhraseMatchStrategy.Exact, PhraseType.Duress);
        var clearWord = MakePhrase("everything is fine", PhraseMatchStrategy.Exact, PhraseType.ClearWord);

        var result = _engine.Evaluate("everything is fine", new[] { duress, clearWord });

        Assert.True(result.IsMatch);
        Assert.Equal(PhraseType.ClearWord, result.MatchedPhrase!.Type);
    }
}
