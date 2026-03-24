/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         MockSMSFallbackAdapter.kt                              │
 * │ Purpose:      Mock (Tier 1) adapter for SMSFallbackPort. Logs SMS   │
 * │               to Logcat instead of sending real messages. Used for   │
 * │               development, emulator testing, and UI testing.         │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: SMSFallbackPort                                        │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   @Provides fun provideSMSPort(                                      │
 * │       mock: MockSMSFallbackAdapter                                   │
 * │   ): SMSFallbackPort = mock                                          │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.sms.mock

import android.util.Log
import com.thewatch.app.data.model.EmergencyContact
import com.thewatch.app.data.sms.SMSFallbackPort
import com.thewatch.app.data.sms.SMSSendResult
import kotlinx.coroutines.delay
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class MockSMSFallbackAdapter @Inject constructor() : SMSFallbackPort {

    companion object {
        private const val TAG = "TheWatch.MockSMS"
    }

    @Volatile
    var simulateUnavailable: Boolean = false

    /** Record of all "sent" messages for test assertions. */
    val sentMessages: MutableList<Pair<String, String>> = mutableListOf()

    override suspend fun isSMSAvailable(): Boolean {
        val available = !simulateUnavailable
        Log.d(TAG, "isSMSAvailable() -> $available")
        return available
    }

    override suspend fun sendSMS(phoneNumber: String, message: String): SMSSendResult {
        if (simulateUnavailable) return SMSSendResult.Unavailable

        delay(100) // Simulate send delay
        sentMessages.add(phoneNumber to message)
        Log.i(TAG, "Mock SMS to $phoneNumber: $message")
        return SMSSendResult.Sent
    }

    override suspend fun sendSOSToContacts(
        contacts: List<EmergencyContact>,
        latitude: Double?,
        longitude: Double?,
        userName: String,
        customMessage: String?
    ): Map<EmergencyContact, SMSSendResult> {
        Log.i(TAG, "Mock SOS SMS to ${contacts.size} contacts for $userName")
        return contacts.associateWith { contact ->
            sendSMS(contact.phoneNumber, "SOS from $userName at $latitude,$longitude")
        }
    }

    override suspend fun sendCheckInConfirmation(
        contacts: List<EmergencyContact>,
        userName: String
    ): Map<EmergencyContact, SMSSendResult> {
        Log.i(TAG, "Mock check-in SMS to ${contacts.size} contacts for $userName")
        return contacts.associateWith { contact ->
            sendSMS(contact.phoneNumber, "Check-in OK from $userName")
        }
    }
}
