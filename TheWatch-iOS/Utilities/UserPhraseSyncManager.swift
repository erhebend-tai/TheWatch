// UserPhraseSyncManager — bridges User model fields (duressCode, personalClearWord)
// into the PhraseDetectionService's active phrase list.
// Called whenever user profile is loaded or updated.
// This ensures the phrase detection system always has the latest configured phrases.

import Foundation

/// Syncs User.duressCode and User.personalClearWord into the
/// PhraseDetectionService's active phrase list.
///
/// Call `syncFromUser(_:)` whenever:
/// - User profile is loaded at app startup
/// - User updates their duress code or clear word in Profile settings
/// - User logs in
///
/// The sync is idempotent — duplicate phrases are replaced, not stacked.
final class UserPhraseSyncManager {
    static let shared = UserPhraseSyncManager()

    private let phraseDetectionService = PhraseDetectionService.shared

    // Fixed IDs for user-configured system phrases — ensures idempotent sync
    private let duressId = "system-duress-code"
    private let clearWordId = "system-clear-word"

    private init() {}

    /// Sync the user's configured duress code and clear word into
    /// the phrase detection system. Safe to call multiple times.
    func syncFromUser(_ user: User) {
        // Remove existing system phrases first (idempotent)
        phraseDetectionService.removePhrase(id: duressId)
        phraseDetectionService.removePhrase(id: clearWordId)

        // Add duress code if configured
        if let duressCode = user.duressCode, !duressCode.isEmpty {
            phraseDetectionService.addPhrase(
                EmergencyPhrase(
                    id: duressId,
                    userId: user.id,
                    phraseText: duressCode,
                    type: .duress,
                    // Substring because the user might say the code within a sentence
                    strategy: .substring,
                    confidenceThreshold: 0.85
                )
            )
        }

        // Add clear word if configured
        if let clearWord = user.personalClearWord, !clearWord.isEmpty {
            phraseDetectionService.addPhrase(
                EmergencyPhrase(
                    id: clearWordId,
                    userId: user.id,
                    phraseText: clearWord,
                    type: .clearWord,
                    // Fuzzy because user may be stressed, mumbling
                    strategy: .fuzzy,
                    confidenceThreshold: 0.80
                )
            )
        }
    }

    /// Add a custom emergency phrase (beyond the built-in duress code / clear word).
    /// Used when the user adds additional phrases from the Profile UI.
    func addCustomPhrase(
        userId: String,
        phraseText: String,
        strategy: PhraseMatchStrategy = .fuzzy,
        confidenceThreshold: Float = 0.80
    ) {
        phraseDetectionService.addPhrase(
            EmergencyPhrase(
                id: "custom-\(UUID().uuidString)",
                userId: userId,
                phraseText: phraseText,
                type: .custom,
                strategy: strategy,
                confidenceThreshold: confidenceThreshold
            )
        )
    }

    /// Clear all phrases (e.g., on logout).
    func clearAll() {
        phraseDetectionService.removePhrase(id: duressId)
        phraseDetectionService.removePhrase(id: clearWordId)
        // Custom phrases: clear the entire list
        phraseDetectionService.activePhrases.removeAll()
    }
}
