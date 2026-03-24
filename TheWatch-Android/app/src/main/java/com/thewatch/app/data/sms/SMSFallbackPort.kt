/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         SMSFallbackPort.kt                                     │
 * │ Purpose:      Hexagonal port interface for SMS fallback during SOS.  │
 * │               When the device is offline (no data) but has cellular  │
 * │               signal, SOS alerts are sent via SMS to emergency       │
 * │               contacts as a last-resort communication channel.       │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: EmergencyContact model                                 │
 * │                                                                      │
 * │ Adapter tiers:                                                       │
 * │   - Mock:   Logs SMS to console. Dev/test/emulator.                  │
 * │   - Native: Uses android.telephony.SmsManager to send real SMS.     │
 * │   - Live:   Native + server-side SMS gateway (Twilio/etc) (future). │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val port: SMSFallbackPort = hiltGet()                              │
 * │   if (port.isSMSAvailable()) {                                       │
 * │       val results = port.sendSOSToContacts(                          │
 * │           contacts = emergencyContacts,                              │
 * │           location = lastKnownLocation,                              │
 * │           userName = "John Doe"                                      │
 * │       )                                                              │
 * │       results.forEach { (contact, success) ->                        │
 * │           Log.d("SMS", "${contact.name}: ${if (success) "sent" else "failed"}") │
 * │       }                                                              │
 * │   }                                                                  │
 * │                                                                      │
 * │ NOTE: SEND_SMS permission is DANGEROUS and must be requested at      │
 * │ runtime. Dual-SIM devices may use a non-default SIM — the native    │
 * │ adapter should use SmsManager.getDefault() or allow user to pick.   │
 * │ SMS is limited to 160 chars (GSM-7) or 70 chars (UCS-2/Unicode).   │
 * │ The SOS message should be kept under 160 chars ASCII for reliability.│
 * │ Some carriers block automated SMS — test with target carriers.      │
 * │ RCS/iMessage are NOT supported; this is pure GSM SMS.               │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.sms

import com.thewatch.app.data.model.EmergencyContact

/**
 * Result of an SMS send attempt.
 */
sealed class SMSSendResult {
    /** SMS accepted by the telephony stack for delivery. */
    object Sent : SMSSendResult()
    /** SMS delivered (delivery report received). Not all carriers support this. */
    object Delivered : SMSSendResult()
    /** Send failed. */
    data class Failed(val reason: String, val errorCode: Int = -1) : SMSSendResult()
    /** SMS capability not available (no SIM, airplane mode, etc.). */
    object Unavailable : SMSSendResult()
}

/**
 * Port interface for SMS fallback during offline SOS alerts.
 *
 * Three-tier implementations:
 * - **Mock**: Simulates SMS send to console. For dev/test/emulator.
 * - **Native**: Real SMS via android.telephony.SmsManager.
 * - **Live**: Native + server-side SMS gateway for redundancy (future).
 */
interface SMSFallbackPort {

    /**
     * Check whether SMS sending is available on this device.
     * Returns false if no SIM, airplane mode, or permission not granted.
     */
    suspend fun isSMSAvailable(): Boolean

    /**
     * Send a single SMS message to a phone number.
     *
     * @param phoneNumber Recipient phone number in E.164 format (e.g., +15551234567).
     * @param message Message body (max 160 chars ASCII recommended).
     * @return [SMSSendResult] indicating the outcome.
     */
    suspend fun sendSMS(phoneNumber: String, message: String): SMSSendResult

    /**
     * Send SOS alert SMS to all emergency contacts.
     *
     * Constructs a standardized SOS message with user name, location coordinates,
     * and timestamp, then sends to each contact's phone number.
     *
     * @param contacts List of emergency contacts to notify.
     * @param latitude Last known latitude (null if unknown).
     * @param longitude Last known longitude (null if unknown).
     * @param userName Name of the user in distress.
     * @param customMessage Optional additional message from the user.
     * @return Map of contact to send result.
     */
    suspend fun sendSOSToContacts(
        contacts: List<EmergencyContact>,
        latitude: Double? = null,
        longitude: Double? = null,
        userName: String = "Unknown User",
        customMessage: String? = null
    ): Map<EmergencyContact, SMSSendResult>

    /**
     * Send a check-in confirmation SMS (e.g., "I'm OK" message).
     *
     * @param contacts Contacts who were previously alerted.
     * @param userName Name of the user confirming safety.
     * @return Map of contact to send result.
     */
    suspend fun sendCheckInConfirmation(
        contacts: List<EmergencyContact>,
        userName: String = "Unknown User"
    ): Map<EmergencyContact, SMSSendResult>
}
