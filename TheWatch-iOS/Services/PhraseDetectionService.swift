// PhraseDetectionService — on-device speech recognition + deterministic phrase matching.
// Uses SFSpeechRecognizer with on-device mode (iOS 13+) — audio NEVER leaves device.
// AVAudioEngine captures audio; SFSpeechRecognizer transcribes; DeterministicPhraseMatcher evaluates.
// This service owns the full pipeline: audio capture → transcription → matching → notification.

import Foundation
import Speech
import AVFoundation
import Combine

// MARK: - Models (mirrors C# EmergencyPhrase record)

enum PhraseType: String, Codable {
    case duress     // Silent SOS, no visible alert
    case clearWord  // Cancels active alert
    case custom     // Standard SOS trigger
}

enum PhraseMatchStrategy: String, Codable {
    case exact      // Case-insensitive, whitespace-normalized
    case fuzzy      // Levenshtein distance within tolerance
    case substring  // Phrase appears anywhere in transcript
    case phonetic   // Soundex comparison
}

struct EmergencyPhrase: Identifiable, Codable, Equatable {
    let id: String
    let userId: String
    let phraseText: String
    let type: PhraseType
    let strategy: PhraseMatchStrategy
    var confidenceThreshold: Float
    var isActive: Bool
    let createdAt: Date
    var lastTriggeredAt: Date?

    init(
        id: String = UUID().uuidString,
        userId: String,
        phraseText: String,
        type: PhraseType,
        strategy: PhraseMatchStrategy,
        confidenceThreshold: Float = 0.80,
        isActive: Bool = true,
        createdAt: Date = Date(),
        lastTriggeredAt: Date? = nil
    ) {
        self.id = id
        self.userId = userId
        self.phraseText = phraseText
        self.type = type
        self.strategy = strategy
        self.confidenceThreshold = confidenceThreshold
        self.isActive = isActive
        self.createdAt = createdAt
        self.lastTriggeredAt = lastTriggeredAt
    }
}

struct PhraseMatchResult {
    let isMatch: Bool
    let matchedPhrase: EmergencyPhrase?
    let confidence: Float
    let strategyUsed: PhraseMatchStrategy
    let transcribedText: String
    let normalizedTranscript: String?
    let detectedAt: Date
}

struct TranscriptionEvent {
    let text: String
    let confidence: Float
    let isFinal: Bool
    let timestamp: Date
}

// MARK: - PhraseDetectionService

@Observable
final class PhraseDetectionService {
    static let shared = PhraseDetectionService()

    // Published state
    private(set) var isListening = false
    private(set) var isAvailable = false
    private(set) var lastTranscription: String = ""

    // Phrase storage (in-memory, persisted via UserDefaults or CoreData later)
    var activePhrases: [EmergencyPhrase] = []

    // Combine publishers for downstream consumption
    let matchResultPublisher = PassthroughSubject<PhraseMatchResult, Never>()
    let transcriptionPublisher = PassthroughSubject<TranscriptionEvent, Never>()

    // Audio pipeline
    private let audioEngine = AVAudioEngine()
    private var speechRecognizer: SFSpeechRecognizer?
    private var recognitionRequest: SFSpeechAudioBufferRecognitionRequest?
    private var recognitionTask: SFSpeechRecognitionTask?
    private let matcher = DeterministicPhraseMatcher()

    // Restart management
    private var shouldBeListening = false
    private var restartTimer: Timer?
    private var consecutiveErrors = 0
    private let maxConsecutiveErrors = 5

    private init() {
        speechRecognizer = SFSpeechRecognizer(locale: Locale(identifier: "en-US"))
        isAvailable = speechRecognizer?.isAvailable ?? false

        // Monitor availability changes
        speechRecognizer?.delegate = AvailabilityDelegate { [weak self] available in
            self?.isAvailable = available
            if !available && self?.isListening == true {
                self?.stopListening()
            }
        }
    }

    // MARK: - Authorization

    func requestAuthorization() async -> Bool {
        // Speech recognition permission
        let speechStatus = await withCheckedContinuation { continuation in
            SFSpeechRecognizer.requestAuthorization { status in
                continuation.resume(returning: status)
            }
        }
        guard speechStatus == .authorized else { return false }

        // Microphone permission
        if #available(iOS 17.0, *) {
            let micGranted = await AVAudioApplication.requestRecordPermission()
            return micGranted
        } else {
            let micGranted = await withCheckedContinuation { continuation in
                AVAudioSession.sharedInstance().requestRecordPermission { granted in
                    continuation.resume(returning: granted)
                }
            }
            return micGranted
        }
    }

    // MARK: - Listening Lifecycle

    func startListening() {
        guard !isListening else { return }
        guard isAvailable else {
            print("[PhraseDetection] Speech recognition not available")
            return
        }

        shouldBeListening = true
        startRecognition()
    }

    func stopListening() {
        shouldBeListening = false
        stopRecognition()
        restartTimer?.invalidate()
        restartTimer = nil
    }

    private func startRecognition() {
        // Clean up any existing task
        stopRecognition()

        guard let recognizer = speechRecognizer, recognizer.isAvailable else {
            scheduleRestart(delay: 2.0)
            return
        }

        do {
            // Configure audio session for background recording
            let audioSession = AVAudioSession.sharedInstance()
            try audioSession.setCategory(.record, mode: .measurement, options: .duckOthers)
            try audioSession.setActive(true, options: .notifyOthersOnDeactivation)

            // Create recognition request
            let request = SFSpeechAudioBufferRecognitionRequest()

            // CRITICAL: Force on-device recognition — audio never leaves device
            if #available(iOS 13.0, *) {
                request.requiresOnDeviceRecognition = true
            }

            request.shouldReportPartialResults = true

            // Limit task duration to avoid memory buildup (restart after each result)
            if #available(iOS 16.0, *) {
                request.addsPunctuation = false
            }

            recognitionRequest = request

            // Start recognition task
            recognitionTask = recognizer.recognitionTask(with: request) { [weak self] result, error in
                guard let self else { return }

                if let result {
                    let text = result.bestTranscription.formattedString
                    let isFinal = result.isFinal

                    self.lastTranscription = text

                    let event = TranscriptionEvent(
                        text: text,
                        confidence: 0.5, // SFSpeechRecognizer doesn't give per-result confidence easily
                        isFinal: isFinal,
                        timestamp: Date()
                    )
                    self.transcriptionPublisher.send(event)

                    // Only match on final results
                    if isFinal {
                        self.evaluateTranscription(text)
                        self.consecutiveErrors = 0
                        // Restart for continuous listening
                        self.scheduleRestart(delay: 0.3)
                    }
                }

                if let error {
                    let nsError = error as NSError
                    print("[PhraseDetection] Error: \(nsError.localizedDescription) (code: \(nsError.code))")

                    // Error code 1 = "No speech detected" — normal, just restart
                    // Error code 216 = Recognition request was canceled — normal on restart
                    if nsError.code == 1 || nsError.code == 216 {
                        self.consecutiveErrors = 0
                        self.scheduleRestart(delay: 0.5)
                    } else {
                        self.consecutiveErrors += 1
                        let delay = self.consecutiveErrors >= self.maxConsecutiveErrors ? 10.0 : 2.0
                        if self.consecutiveErrors >= self.maxConsecutiveErrors {
                            self.consecutiveErrors = 0
                        }
                        self.scheduleRestart(delay: delay)
                    }
                }
            }

            // Install audio tap
            let inputNode = audioEngine.inputNode
            let recordingFormat = inputNode.outputFormat(forBus: 0)

            inputNode.installTap(onBus: 0, bufferSize: 1024, format: recordingFormat) { [weak self] buffer, _ in
                self?.recognitionRequest?.append(buffer)
            }

            audioEngine.prepare()
            try audioEngine.start()

            isListening = true
            print("[PhraseDetection] Started listening (on-device)")

        } catch {
            print("[PhraseDetection] Failed to start: \(error)")
            scheduleRestart(delay: 2.0)
        }
    }

    private func stopRecognition() {
        recognitionTask?.cancel()
        recognitionTask = nil

        recognitionRequest?.endAudio()
        recognitionRequest = nil

        if audioEngine.isRunning {
            audioEngine.stop()
            audioEngine.inputNode.removeTap(onBus: 0)
        }

        isListening = false
    }

    private func scheduleRestart(delay: TimeInterval) {
        guard shouldBeListening else { return }

        restartTimer?.invalidate()
        restartTimer = Timer.scheduledTimer(withTimeInterval: delay, repeats: false) { [weak self] _ in
            guard let self, self.shouldBeListening else { return }
            self.startRecognition()
        }
    }

    // MARK: - Phrase Evaluation

    private func evaluateTranscription(_ text: String) {
        let active = activePhrases.filter { $0.isActive }
        guard !active.isEmpty else { return }

        let result = matcher.evaluate(transcribedText: text, activePhrases: active)
        if result.isMatch {
            print("[PhraseDetection] MATCH: \(result.matchedPhrase?.phraseText ?? "?") " +
                  "(type: \(result.matchedPhrase?.type.rawValue ?? "?"), " +
                  "confidence: \(result.confidence))")
            matchResultPublisher.send(result)
        }
    }

    // MARK: - Phrase CRUD

    func addPhrase(_ phrase: EmergencyPhrase) {
        activePhrases.append(phrase)
    }

    func removePhrase(id: String) {
        activePhrases.removeAll { $0.id == id }
    }

    func updatePhrase(_ phrase: EmergencyPhrase) {
        if let index = activePhrases.firstIndex(where: { $0.id == phrase.id }) {
            activePhrases[index] = phrase
        }
    }
}

// MARK: - SFSpeechRecognizer Delegate

private class AvailabilityDelegate: NSObject, SFSpeechRecognizerDelegate {
    let onChange: (Bool) -> Void

    init(onChange: @escaping (Bool) -> Void) {
        self.onChange = onChange
    }

    func speechRecognizer(_ speechRecognizer: SFSpeechRecognizer, availabilityDidChange available: Bool) {
        onChange(available)
    }
}

// MARK: - Deterministic Phrase Matcher (Swift port of C# PhraseMatchingEngine)

struct DeterministicPhraseMatcher {

    func evaluate(transcribedText: String, activePhrases: [EmergencyPhrase]) -> PhraseMatchResult {
        guard !transcribedText.trimmingCharacters(in: .whitespaces).isEmpty,
              !activePhrases.isEmpty else {
            return noMatch(transcribedText)
        }

        let normalized = normalizeText(transcribedText)
        var bestMatch: PhraseMatchResult?

        for phrase in activePhrases where phrase.isActive {
            let result = matchPhrase(normalizedTranscript: normalized, rawTranscript: transcribedText, phrase: phrase)
            if result.isMatch, bestMatch == nil || result.confidence > (bestMatch?.confidence ?? 0) {
                bestMatch = result
            }
        }

        return bestMatch ?? noMatch(transcribedText, normalized: normalized)
    }

    func evaluateSingle(transcribedText: String, phrase: EmergencyPhrase) -> PhraseMatchResult {
        guard !transcribedText.trimmingCharacters(in: .whitespaces).isEmpty else {
            return noMatch(transcribedText)
        }
        let normalized = normalizeText(transcribedText)
        return matchPhrase(normalizedTranscript: normalized, rawTranscript: transcribedText, phrase: phrase)
    }

    // MARK: - Core Matching

    private func matchPhrase(normalizedTranscript: String, rawTranscript: String, phrase: EmergencyPhrase) -> PhraseMatchResult {
        let normalizedPhrase = normalizeText(phrase.phraseText)

        let (isMatch, confidence): (Bool, Float)
        switch phrase.strategy {
        case .exact:     (isMatch, confidence) = matchExact(normalizedTranscript, normalizedPhrase)
        case .fuzzy:     (isMatch, confidence) = matchFuzzy(normalizedTranscript, normalizedPhrase)
        case .substring: (isMatch, confidence) = matchSubstring(normalizedTranscript, normalizedPhrase)
        case .phonetic:  (isMatch, confidence) = matchPhonetic(normalizedTranscript, normalizedPhrase)
        }

        let meetsThreshold = isMatch && confidence >= phrase.confidenceThreshold

        return PhraseMatchResult(
            isMatch: meetsThreshold,
            matchedPhrase: meetsThreshold ? phrase : nil,
            confidence: confidence,
            strategyUsed: phrase.strategy,
            transcribedText: rawTranscript,
            normalizedTranscript: normalizedTranscript,
            detectedAt: Date()
        )
    }

    // MARK: - Exact

    private func matchExact(_ transcript: String, _ phrase: String) -> (Bool, Float) {
        let match = transcript.caseInsensitiveCompare(phrase) == .orderedSame
        return (match, match ? 1.0 : 0.0)
    }

    // MARK: - Fuzzy (Levenshtein)

    private func matchFuzzy(_ transcript: String, _ phrase: String) -> (Bool, Float) {
        let distance = levenshteinDistance(transcript, phrase)
        let maxLen = max(transcript.count, phrase.count)
        guard maxLen > 0 else { return (false, 0) }

        var similarity: Float = 1.0 - (Float(distance) / Float(maxLen))

        if transcript.count > phrase.count + 5 {
            let windowSim = slidingWindowFuzzy(transcript, phrase)
            similarity = max(similarity, windowSim)
        }

        return (similarity >= 0.80, similarity)
    }

    private func slidingWindowFuzzy(_ transcript: String, _ phrase: String) -> Float {
        let phraseLen = phrase.count
        var best: Float = 0
        let minWindow = max(1, Int(Float(phraseLen) * 0.8))
        let maxWindow = min(transcript.count, Int(Float(phraseLen) * 1.2))
        let transcriptChars = Array(transcript)

        for windowSize in minWindow...maxWindow {
            for start in 0...(transcriptChars.count - windowSize) {
                let window = String(transcriptChars[start..<(start + windowSize)])
                let distance = levenshteinDistance(window, phrase)
                let sim: Float = 1.0 - (Float(distance) / Float(max(windowSize, phraseLen)))
                best = max(best, sim)
                if best >= 0.95 { return best }
            }
        }
        return best
    }

    func levenshteinDistance(_ source: String, _ target: String) -> Int {
        let s = Array(source.lowercased())
        let t = Array(target.lowercased())
        if s.isEmpty { return t.count }
        if t.isEmpty { return s.count }

        var prev = Array(0...t.count)
        var curr = Array(repeating: 0, count: t.count + 1)

        for i in 1...s.count {
            curr[0] = i
            for j in 1...t.count {
                let cost = s[i - 1] == t[j - 1] ? 0 : 1
                curr[j] = min(curr[j - 1] + 1, min(prev[j] + 1, prev[j - 1] + cost))
            }
            swap(&prev, &curr)
        }
        return prev[t.count]
    }

    // MARK: - Substring

    private func matchSubstring(_ transcript: String, _ phrase: String) -> (Bool, Float) {
        guard transcript.localizedCaseInsensitiveContains(phrase) else { return (false, 0) }
        let coverage = Float(phrase.count) / Float(transcript.count)
        return (true, 0.85 + 0.15 * coverage)
    }

    // MARK: - Phonetic (Soundex)

    private func matchPhonetic(_ transcript: String, _ phrase: String) -> (Bool, Float) {
        let transcriptWords = transcript.split(separator: " ").map(String.init)
        let phraseWords = phrase.split(separator: " ").map(String.init)
        guard !phraseWords.isEmpty else { return (false, 0) }
        guard transcriptWords.count >= phraseWords.count else { return (false, 0) }

        let phraseSoundex = phraseWords.map { soundex($0) }
        var bestCount = 0

        for start in 0...(transcriptWords.count - phraseWords.count) {
            var count = 0
            for p in phraseWords.indices {
                if soundex(transcriptWords[start + p]) == phraseSoundex[p] {
                    count += 1
                }
            }
            bestCount = max(bestCount, count)
            if bestCount == phraseWords.count { break }
        }

        guard bestCount > 0 else { return (false, 0) }
        let confidence = Float(bestCount) / Float(phraseWords.count)
        return (confidence >= 0.75, confidence)
    }

    func soundex(_ word: String) -> String {
        let clean = word.filter { $0.isLetter }.uppercased()
        guard let first = clean.first else { return "0000" }

        var result: [Character] = [first]
        var lastCode = soundexCode(first)

        for char in clean.dropFirst() {
            guard result.count < 4 else { break }
            let code = soundexCode(char)
            if code != "0" && code != lastCode {
                result.append(code)
            }
            lastCode = code
        }

        while result.count < 4 { result.append("0") }
        return String(result)
    }

    private func soundexCode(_ c: Character) -> Character {
        switch c.uppercased().first! {
        case "B", "F", "P", "V": return "1"
        case "C", "G", "J", "K", "Q", "S", "X", "Z": return "2"
        case "D", "T": return "3"
        case "L": return "4"
        case "M", "N": return "5"
        case "R": return "6"
        default: return "0"
        }
    }

    // MARK: - Normalization

    func normalizeText(_ text: String) -> String {
        var result: [Character] = []
        var lastWasSpace = true

        for c in text {
            if c.isLetter || c.isNumber {
                result.append(Character(c.lowercased()))
                lastWasSpace = false
            } else if c.isWhitespace || c == "'" || c == "-" {
                if !lastWasSpace {
                    result.append(" ")
                    lastWasSpace = true
                }
            }
        }

        if let last = result.last, last == " " {
            result.removeLast()
        }

        return String(result)
    }

    private func noMatch(_ text: String, normalized: String? = nil) -> PhraseMatchResult {
        PhraseMatchResult(
            isMatch: false,
            matchedPhrase: nil,
            confidence: 0,
            strategyUsed: .exact,
            transcribedText: text,
            normalizedTranscript: normalized,
            detectedAt: Date()
        )
    }
}
