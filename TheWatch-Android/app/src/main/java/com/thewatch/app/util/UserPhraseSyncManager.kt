// UserPhraseSyncManager — bridges User model fields (duressCode, personalClearWord)
// into the PhraseDetectionRepository's active phrase list.
// Called whenever user profile is loaded or updated.
// This ensures the phrase detection system always has the latest configured phrases.

package com.thewatch.app.util

import com.thewatch.app.data.model.User
import com.thewatch.app.data.repository.EmergencyPhrase
import com.thewatch.app.data.repository.PhraseDetectionRepository
import com.thewatch.app.data.repository.PhraseMatchStrategy
import com.thewatch.app.data.repository.PhraseType
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Syncs User.duressCode and User.personalClearWord into the
 * PhraseDetectionRepository's active phrase list.
 *
 * Call [syncFromUser] whenever:
 * - User profile is loaded at app startup
 * - User updates their duress code or clear word in Profile settings
 * - User logs in
 *
 * The sync is idempotent — duplicate phrases are replaced, not stacked.
 */
@Singleton
class UserPhraseSyncManager @Inject constructor(
    private val phraseDetectionRepository: PhraseDetectionRepository
) {
    companion object {
        // Fixed IDs for user-configured system phrases — ensures idempotent sync
        private const val DURESS_PHRASE_ID = "system-duress-code"
        private const val CLEAR_WORD_PHRASE_ID = "system-clear-word"
    }

    /**
     * Sync the user's configured duress code and clear word into
     * the phrase detection system. Safe to call multiple times.
     */
    suspend fun syncFromUser(user: User) {
        // Remove existing system phrases first (idempotent)
        phraseDetectionRepository.removePhrase(DURESS_PHRASE_ID)
        phraseDetectionRepository.removePhrase(CLEAR_WORD_PHRASE_ID)

        // Add duress code if configured
        if (!user.duressCode.isNullOrBlank()) {
            phraseDetectionRepository.addPhrase(
                EmergencyPhrase(
                    phraseId = DURESS_PHRASE_ID,
                    userId = user.id,
                    phraseText = user.duressCode,
                    type = PhraseType.DURESS,
                    // Duress codes are typically short numeric codes — use Exact or Substring
                    // Substring because the user might say it within a sentence
                    strategy = PhraseMatchStrategy.SUBSTRING,
                    confidenceThreshold = 0.85f,
                    isActive = true
                )
            )
        }

        // Add clear word if configured
        if (!user.personalClearWord.isNullOrBlank()) {
            phraseDetectionRepository.addPhrase(
                EmergencyPhrase(
                    phraseId = CLEAR_WORD_PHRASE_ID,
                    userId = user.id,
                    phraseText = user.personalClearWord,
                    type = PhraseType.CLEAR_WORD,
                    // Clear words should be fuzzy — user may be stressed, mumbling
                    strategy = PhraseMatchStrategy.FUZZY,
                    confidenceThreshold = 0.80f,
                    isActive = true
                )
            )
        }
    }

    /**
     * Add a custom emergency phrase (beyond the built-in duress code / clear word).
     * Used when the user adds additional phrases from the Profile UI.
     */
    suspend fun addCustomPhrase(
        userId: String,
        phraseText: String,
        strategy: PhraseMatchStrategy = PhraseMatchStrategy.FUZZY,
        confidenceThreshold: Float = 0.80f
    ) {
        phraseDetectionRepository.addPhrase(
            EmergencyPhrase(
                phraseId = "custom-${System.currentTimeMillis()}",
                userId = userId,
                phraseText = phraseText,
                type = PhraseType.CUSTOM,
                strategy = strategy,
                confidenceThreshold = confidenceThreshold,
                isActive = true
            )
        )
    }

    /**
     * Clear all phrases (e.g., on logout).
     */
    suspend fun clearAll() {
        phraseDetectionRepository.removePhrase(DURESS_PHRASE_ID)
        phraseDetectionRepository.removePhrase(CLEAR_WORD_PHRASE_ID)
        // Custom phrases would also need to be cleared — future: iterate and remove
    }
}
