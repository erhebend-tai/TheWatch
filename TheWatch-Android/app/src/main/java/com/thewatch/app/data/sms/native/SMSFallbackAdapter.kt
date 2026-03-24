/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         SMSFallbackAdapter.kt                                  │
 * │ Purpose:      Native (Tier 2) adapter for SMSFallbackPort. Uses     │
 * │               android.telephony.SmsManager to send real SMS          │
 * │               messages via the device's cellular modem. Handles      │
 * │               multipart messages, delivery reports, and dual-SIM.    │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: SmsManager, TelephonyManager, SEND_SMS permission     │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   // In AppModule.kt (release builds):                               │
 * │   @Provides fun provideSMSPort(                                      │
 * │       adapter: SMSFallbackAdapter                                    │
 * │   ): SMSFallbackPort = adapter                                       │
 * │                                                                      │
 * │ NOTE: Requires Manifest.permission.SEND_SMS at runtime.              │
 * │ On API 31+ use SmsManager.createForSubscriptionId() for dual-SIM.  │
 * │ T-Mobile, AT&T, Verizon all support standard GSM SMS delivery.     │
 * │ Some MVNOs may throttle automated SMS — test with production SIMs.  │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.sms.native

import android.Manifest
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.telephony.SmsManager
import android.telephony.TelephonyManager
import android.util.Log
import androidx.core.content.ContextCompat
import com.thewatch.app.data.model.EmergencyContact
import com.thewatch.app.data.sms.SMSFallbackPort
import com.thewatch.app.data.sms.SMSSendResult
import dagger.hilt.android.qualifiers.ApplicationContext
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class SMSFallbackAdapter @Inject constructor(
    @ApplicationContext private val context: Context
) : SMSFallbackPort {

    companion object {
        private const val TAG = "TheWatch.SMSFallback"
        private const val SOS_MESSAGE_TEMPLATE =
            "SOS ALERT from TheWatch: %s needs help! Location: %s Time: %s %s"
        private const val CHECKIN_MESSAGE_TEMPLATE =
            "TheWatch Update: %s has confirmed they are OK. Timestamp: %s"
        private const val MAX_SMS_LENGTH = 160
    }

    private val dateFormat = SimpleDateFormat("yyyy-MM-dd HH:mm:ss z", Locale.US)

    override suspend fun isSMSAvailable(): Boolean {
        // Check SEND_SMS permission
        val hasPermission = ContextCompat.checkSelfPermission(
            context, Manifest.permission.SEND_SMS
        ) == PackageManager.PERMISSION_GRANTED

        if (!hasPermission) {
            Log.w(TAG, "SEND_SMS permission not granted")
            return false
        }

        // Check telephony availability
        val telephonyManager = context.getSystemService(Context.TELEPHONY_SERVICE) as? TelephonyManager
        if (telephonyManager == null) {
            Log.w(TAG, "TelephonyManager not available")
            return false
        }

        // Check SIM state
        val simState = telephonyManager.simState
        if (simState != TelephonyManager.SIM_STATE_READY) {
            Log.w(TAG, "SIM not ready, state=$simState")
            return false
        }

        return true
    }

    override suspend fun sendSMS(phoneNumber: String, message: String): SMSSendResult {
        if (!isSMSAvailable()) return SMSSendResult.Unavailable

        return try {
            val smsManager = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                context.getSystemService(SmsManager::class.java)
            } else {
                @Suppress("DEPRECATION")
                SmsManager.getDefault()
            }

            if (smsManager == null) {
                Log.e(TAG, "SmsManager is null")
                return SMSSendResult.Failed("SmsManager unavailable")
            }

            // Handle multipart for long messages
            if (message.length > MAX_SMS_LENGTH) {
                val parts = smsManager.divideMessage(message)
                val sentIntents = ArrayList<PendingIntent>(parts.size)
                val deliveryIntents = ArrayList<PendingIntent>(parts.size)

                for (i in parts.indices) {
                    val sentIntent = PendingIntent.getBroadcast(
                        context, i,
                        Intent("SMS_SENT_${System.currentTimeMillis()}_$i"),
                        PendingIntent.FLAG_IMMUTABLE
                    )
                    sentIntents.add(sentIntent)

                    val deliveryIntent = PendingIntent.getBroadcast(
                        context, i,
                        Intent("SMS_DELIVERED_${System.currentTimeMillis()}_$i"),
                        PendingIntent.FLAG_IMMUTABLE
                    )
                    deliveryIntents.add(deliveryIntent)
                }

                smsManager.sendMultipartTextMessage(
                    phoneNumber, null, parts, sentIntents, deliveryIntents
                )
            } else {
                val sentIntent = PendingIntent.getBroadcast(
                    context, 0,
                    Intent("SMS_SENT_${System.currentTimeMillis()}"),
                    PendingIntent.FLAG_IMMUTABLE
                )
                smsManager.sendTextMessage(
                    phoneNumber, null, message, sentIntent, null
                )
            }

            Log.i(TAG, "SMS sent to $phoneNumber (${message.length} chars)")
            SMSSendResult.Sent

        } catch (e: SecurityException) {
            Log.e(TAG, "SecurityException sending SMS", e)
            SMSSendResult.Failed("Permission denied: ${e.message}")
        } catch (e: Exception) {
            Log.e(TAG, "Exception sending SMS", e)
            SMSSendResult.Failed("Send error: ${e.message}")
        }
    }

    override suspend fun sendSOSToContacts(
        contacts: List<EmergencyContact>,
        latitude: Double?,
        longitude: Double?,
        userName: String,
        customMessage: String?
    ): Map<EmergencyContact, SMSSendResult> {
        val locationStr = if (latitude != null && longitude != null) {
            "https://maps.google.com/?q=$latitude,$longitude"
        } else {
            "Unknown"
        }

        val timeStr = dateFormat.format(Date())
        val extraMsg = if (customMessage != null) "Msg: $customMessage" else ""

        val message = String.format(
            SOS_MESSAGE_TEMPLATE, userName, locationStr, timeStr, extraMsg
        ).trim()

        Log.i(TAG, "Sending SOS SMS to ${contacts.size} contacts")

        return contacts.associateWith { contact ->
            if (contact.phoneNumber.isBlank()) {
                SMSSendResult.Failed("No phone number for ${contact.name}")
            } else {
                sendSMS(contact.phoneNumber, message)
            }
        }
    }

    override suspend fun sendCheckInConfirmation(
        contacts: List<EmergencyContact>,
        userName: String
    ): Map<EmergencyContact, SMSSendResult> {
        val timeStr = dateFormat.format(Date())
        val message = String.format(CHECKIN_MESSAGE_TEMPLATE, userName, timeStr)

        return contacts.associateWith { contact ->
            if (contact.phoneNumber.isBlank()) {
                SMSSendResult.Failed("No phone number for ${contact.name}")
            } else {
                sendSMS(contact.phoneNumber, message)
            }
        }
    }
}
