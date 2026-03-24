// PhraseDetectionRepository — bridges native SpeechRecognizer transcription
// and the deterministic phrase matching engine.
// Collects transcription events from PhraseDetectionService,
// runs them through matching logic, and emits match results.

package com.thewatch.app.data.repository

import android.content.Context
import com.thewatch.app.service.PhraseDetectionService
import com.thewatch.app.service.TranscriptionEvent
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Emergency phrase configuration — mirrors the C# EmergencyPhrase record.
 * Stored locally on device. Never sent to cloud.
 */
data class EmergencyPhrase(
    val phraseId: String,
    val userId: String,
    val phraseText: String,
    val type: PhraseType,
    val strategy: PhraseMatchStrategy,
    val confidenceThreshold: Float = 0.80f,
    val isActive: Boolean = true,
    val createdAt: Long = System.currentTimeMillis(),
    val lastTriggeredAt: Long? = null
)

enum class PhraseType {
    /** Duress code — triggers silent SOS without visible alert. */
    DURESS,
    /** Clear word — cancels an active alert, confirms user is safe. */
    CLEAR_WORD,
    /** Custom emergency trigger — triggers standard SOS. */
    CUSTOM
}

enum class PhraseMatchStrategy {
    /** Exact string match (case-insensitive, whitespace-normalized). */
    EXACT,
    /** Levenshtein distance within tolerance. */
    FUZZY,
    /** Phrase appears anywhere in transcribed speech. */
    SUBSTRING,
    /** Soundex/Metaphone phonetic comparison. */
    PHONETIC
}

/**
 * Result of matching transcribed speech against emergency phrases.
 */
data class PhraseMatchResult(
    val isMatch: Boolean,
    val matchedPhrase: EmergencyPhrase? = null,
    val confidence: Float = 0f,
    val strategyUsed: PhraseMatchStrategy = PhraseMatchStrategy.EXACT,
    val transcribedText: String = "",
    val timestamp: Long = System.currentTimeMillis()
)

/**
 * Repository interface for phrase detection.
 * Exposes flows for UI/ViewModel consumption.
 */
interface PhraseDetectionRepository {
    /** Whether the service is currently listening. */
    val isListening: StateFlow<Boolean>

    /** Whether on-device speech recognition is available. */
    val isAvailable: StateFlow<Boolean>

    /** Flow of transcription events (for debug UI). */
    val transcriptions: SharedFlow<TranscriptionEvent>

    /** Flow of phrase match results (for SOS trigger pipeline). */
    val matchResults: SharedFlow<PhraseMatchResult>

    /** Currently configured emergency phrases. */
    val activePhrases: StateFlow<List<EmergencyPhrase>>

    /** Start listening for emergency phrases. */
    fun startListening()

    /** Stop listening. */
    fun stopListening()

    /** Add a new emergency phrase. */
    suspend fun addPhrase(phrase: EmergencyPhrase)

    /** Remove a phrase by ID. */
    suspend fun removePhrase(phraseId: String)

    /** Update an existing phrase. */
    suspend fun updatePhrase(phrase: EmergencyPhrase)

    /** Get all phrases for a user. */
    suspend fun getPhrasesForUser(userId: String): List<EmergencyPhrase>
}

/**
 * Implementation that:
 * 1. Controls PhraseDetectionService lifecycle
 * 2. Collects transcription events
 * 3. Runs deterministic matching (port from C# PhraseMatchingEngine)
 * 4. Emits match results to the SOS pipeline
 */
@Singleton
class PhraseDetectionRepositoryImpl @Inject constructor(
    @ApplicationContext private val context: Context
) : PhraseDetectionRepository {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    // Phrase storage (in-memory for now — will be backed by Room)
    private val _activePhrases = MutableStateFlow<List<EmergencyPhrase>>(emptyList())
    override val activePhrases: StateFlow<List<EmergencyPhrase>> = _activePhrases.asStateFlow()

    // Match results
    private val _matchResults = MutableSharedFlow<PhraseMatchResult>(
        replay = 0,
        extraBufferCapacity = 8
    )
    override val matchResults: SharedFlow<PhraseMatchResult> = _matchResults.asSharedFlow()

    override val isListening: StateFlow<Boolean> = PhraseDetectionService.isListening
    override val isAvailable: StateFlow<Boolean> = PhraseDetectionService.isAvailable
    override val transcriptions: SharedFlow<TranscriptionEvent> = PhraseDetectionService.transcriptionFlow

    private val matcher = DeterministicPhraseMatcher()

    init {
        // Collect transcription events and run matching
        scope.launch {
            PhraseDetectionService.transcriptionFlow.collect { event ->
                val phrases = _activePhrases.value.filter { it.isActive }
                if (phrases.isEmpty()) return@collect

                // Only match on final results to avoid false triggers on partial speech
                if (!event.isFinal) return@collect

                val result = matcher.evaluate(event.text, phrases)
                if (result.isMatch) {
                    _matchResults.emit(result)
                }
            }
        }
    }

    override fun startListening() {
        PhraseDetectionService.start(context)
    }

    override fun stopListening() {
        PhraseDetectionService.stop(context)
    }

    override suspend fun addPhrase(phrase: EmergencyPhrase) {
        _activePhrases.value = _activePhrases.value + phrase
    }

    override suspend fun removePhrase(phraseId: String) {
        _activePhrases.value = _activePhrases.value.filter { it.phraseId != phraseId }
    }

    override suspend fun updatePhrase(phrase: EmergencyPhrase) {
        _activePhrases.value = _activePhrases.value.map {
            if (it.phraseId == phrase.phraseId) phrase else it
        }
    }

    override suspend fun getPhrasesForUser(userId: String): List<EmergencyPhrase> {
        return _activePhrases.value.filter { it.userId == userId }
    }
}

/**
 * Deterministic phrase matching — Kotlin port of the C# PhraseMatchingEngine.
 * Pure functions, no side effects, no ML. Identical inputs → identical outputs.
 */
internal class DeterministicPhraseMatcher {

    fun evaluate(transcribedText: String, activePhrases: List<EmergencyPhrase>): PhraseMatchResult {
        if (transcribedText.isBlank() || activePhrases.isEmpty()) {
            return PhraseMatchResult(isMatch = false, transcribedText = transcribedText)
        }

        val normalized = normalizeText(transcribedText)
        var bestMatch: PhraseMatchResult? = null

        for (phrase in activePhrases) {
            if (!phrase.isActive) continue

            val result = matchPhrase(normalized, transcribedText, phrase)
            if (result.isMatch && (bestMatch == null || result.confidence > bestMatch.confidence)) {
                bestMatch = result
            }
        }

        return bestMatch ?: PhraseMatchResult(isMatch = false, transcribedText = transcribedText)
    }

    private fun matchPhrase(
        normalizedTranscript: String,
        rawTranscript: String,
        phrase: EmergencyPhrase
    ): PhraseMatchResult {
        val normalizedPhrase = normalizeText(phrase.phraseText)

        val (isMatch, confidence) = when (phrase.strategy) {
            PhraseMatchStrategy.EXACT -> matchExact(normalizedTranscript, normalizedPhrase)
            PhraseMatchStrategy.FUZZY -> matchFuzzy(normalizedTranscript, normalizedPhrase)
            PhraseMatchStrategy.SUBSTRING -> matchSubstring(normalizedTranscript, normalizedPhrase)
            PhraseMatchStrategy.PHONETIC -> matchPhonetic(normalizedTranscript, normalizedPhrase)
        }

        val meetsThreshold = isMatch && confidence >= phrase.confidenceThreshold

        return PhraseMatchResult(
            isMatch = meetsThreshold,
            matchedPhrase = if (meetsThreshold) phrase else null,
            confidence = confidence,
            strategyUsed = phrase.strategy,
            transcribedText = rawTranscript
        )
    }

    // ── Exact match ──

    private fun matchExact(transcript: String, phrase: String): Pair<Boolean, Float> {
        val match = transcript.equals(phrase, ignoreCase = true)
        return match to if (match) 1.0f else 0.0f
    }

    // ── Fuzzy match (Levenshtein) ──

    private fun matchFuzzy(transcript: String, phrase: String): Pair<Boolean, Float> {
        val distance = levenshteinDistance(transcript, phrase)
        val maxLen = maxOf(transcript.length, phrase.length)
        if (maxLen == 0) return false to 0f

        var similarity = 1.0f - (distance.toFloat() / maxLen)

        // Sliding window for phrase inside longer transcript
        if (transcript.length > phrase.length + 5) {
            val windowSim = slidingWindowFuzzy(transcript, phrase)
            similarity = maxOf(similarity, windowSim)
        }

        return (similarity >= 0.80f) to similarity
    }

    private fun slidingWindowFuzzy(transcript: String, phrase: String): Float {
        val phraseLen = phrase.length
        var best = 0f
        val minWindow = maxOf(1, (phraseLen * 0.8).toInt())
        val maxWindow = minOf(transcript.length, (phraseLen * 1.2).toInt())

        for (windowSize in minWindow..maxWindow) {
            for (start in 0..(transcript.length - windowSize)) {
                val window = transcript.substring(start, start + windowSize)
                val distance = levenshteinDistance(window, phrase)
                val sim = 1.0f - (distance.toFloat() / maxOf(windowSize, phraseLen))
                best = maxOf(best, sim)
                if (best >= 0.95f) return best
            }
        }
        return best
    }

    internal fun levenshteinDistance(source: String, target: String): Int {
        if (source.isEmpty()) return target.length
        if (target.isEmpty()) return source.length

        var prev = IntArray(target.length + 1) { it }
        var curr = IntArray(target.length + 1)

        for (i in 1..source.length) {
            curr[0] = i
            for (j in 1..target.length) {
                val cost = if (source[i - 1].lowercaseChar() == target[j - 1].lowercaseChar()) 0 else 1
                curr[j] = minOf(curr[j - 1] + 1, prev[j] + 1, prev[j - 1] + cost)
            }
            val tmp = prev; prev = curr; curr = tmp
        }
        return prev[target.length]
    }

    // ── Substring match ──

    private fun matchSubstring(transcript: String, phrase: String): Pair<Boolean, Float> {
        val contains = transcript.contains(phrase, ignoreCase = true)
        if (!contains) return false to 0f
        val coverage = phrase.length.toFloat() / transcript.length
        val confidence = 0.85f + (0.15f * coverage)
        return true to confidence
    }

    // ── Phonetic match (Soundex) ──

    private fun matchPhonetic(transcript: String, phrase: String): Pair<Boolean, Float> {
        val transcriptWords = transcript.split(" ").filter { it.isNotBlank() }
        val phraseWords = phrase.split(" ").filter { it.isNotBlank() }
        if (phraseWords.isEmpty()) return false to 0f

        val phraseSoundex = phraseWords.map { soundex(it) }
        var bestCount = 0

        for (start in 0..(transcriptWords.size - phraseWords.size)) {
            var count = 0
            for (p in phraseWords.indices) {
                val tSoundex = soundex(transcriptWords[start + p])
                if (tSoundex == phraseSoundex[p]) count++
            }
            bestCount = maxOf(bestCount, count)
            if (bestCount == phraseWords.size) break
        }

        if (bestCount == 0) return false to 0f
        val confidence = bestCount.toFloat() / phraseWords.size
        return (confidence >= 0.75f) to confidence
    }

    internal fun soundex(word: String): String {
        val clean = word.filter { it.isLetter() }.uppercase()
        if (clean.isEmpty()) return "0000"

        val result = CharArray(4)
        result[0] = clean[0]
        var lastCode = soundexCode(clean[0])
        var idx = 1

        for (i in 1 until clean.length) {
            if (idx >= 4) break
            val code = soundexCode(clean[i])
            if (code != '0' && code != lastCode) {
                result[idx++] = code
            }
            lastCode = code
        }
        while (idx < 4) result[idx++] = '0'
        return String(result)
    }

    private fun soundexCode(c: Char): Char = when (c.uppercaseChar()) {
        'B', 'F', 'P', 'V' -> '1'
        'C', 'G', 'J', 'K', 'Q', 'S', 'X', 'Z' -> '2'
        'D', 'T' -> '3'
        'L' -> '4'
        'M', 'N' -> '5'
        'R' -> '6'
        else -> '0'
    }

    // ── Text normalization ──

    internal fun normalizeText(text: String): String {
        val sb = StringBuilder(text.length)
        var lastWasSpace = true

        for (c in text) {
            when {
                c.isLetterOrDigit() -> {
                    sb.append(c.lowercaseChar())
                    lastWasSpace = false
                }
                c.isWhitespace() || c == '\'' || c == '-' -> {
                    if (!lastWasSpace) {
                        sb.append(' ')
                        lastWasSpace = true
                    }
                }
                // Other punctuation stripped
            }
        }

        if (sb.isNotEmpty() && sb.last() == ' ') sb.deleteCharAt(sb.length - 1)
        return sb.toString()
    }
}
