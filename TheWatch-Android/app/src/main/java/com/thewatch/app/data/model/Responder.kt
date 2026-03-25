package com.thewatch.app.data.model

data class Responder(
    val id: String = "",
    val name: String = "",
    val type: String = "",
    val distance: Double = 0.0,
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val eta: Int = 0,
    val certifications: List<String> = emptyList(),
    val hasVehicle: Boolean = false
)

/**
 * Navigation directions returned by the API after a responder acknowledges an incident.
 * Contains deep links to launch turn-by-turn navigation on the responder's device.
 *
 * Example usage:
 *   val directions = ackResponse.directions
 *   // Launch Google Maps with driving/walking directions
 *   val intent = Intent(Intent.ACTION_VIEW, Uri.parse(directions.googleMapsUrl))
 *   startActivity(intent)
 */
data class NavigationDirections(
    val travelMode: String = "driving",       // "driving" or "walking"
    val distanceMeters: Double = 0.0,
    val estimatedTravelTimeMinutes: Double? = null,
    val googleMapsUrl: String = "",
    val appleMapsUrl: String = "",
    val wazeUrl: String = ""
)

/**
 * Response returned when a responder acknowledges an incident via POST /api/response/{id}/ack.
 * Includes the ack confirmation AND navigation directions to the incident.
 */
data class AcknowledgmentResponse(
    val ackId: String = "",
    val requestId: String = "",
    val responderId: String = "",
    val status: String = "",
    val estimatedArrival: String? = null,
    val directions: NavigationDirections = NavigationDirections()
)

// ═══════════════════════════════════════════════════════════════
// Responder Communication Models
// ═══════════════════════════════════════════════════════════════

/**
 * Message types for incident-scoped responder communication.
 */
enum class ResponderMessageType {
    Text,
    LocationShare,
    StatusUpdate,
    Image,
    QuickResponse
}

/**
 * Server-side guardrails verdict applied to each message.
 * All messages route through the server for safety filtering.
 */
enum class GuardrailsVerdict {
    Approved,     // Message delivered as-is
    Redacted,     // PII was removed, redacted version delivered
    Blocked,      // Message not delivered (profanity, threats)
    RateLimited   // Too many messages, try again later
}

/**
 * A message in an incident's responder communication channel.
 * Every message passes through server guardrails before delivery.
 *
 * Example usage:
 *   // Send a text message
 *   val result = commRepository.sendMessage(
 *       requestId = incidentId,
 *       senderId = myUserId,
 *       content = "I can see the location, approaching from the north"
 *   )
 *   when (result.verdict) {
 *       GuardrailsVerdict.Approved -> // Message sent successfully
 *       GuardrailsVerdict.Blocked -> showWarning(result.reason)
 *       GuardrailsVerdict.Redacted -> showInfo("Some info was redacted for privacy")
 *       GuardrailsVerdict.RateLimited -> showInfo("Slow down — too many messages")
 *   }
 */
data class ResponderChatMessage(
    val messageId: String = "",
    val requestId: String = "",
    val senderId: String = "",
    val senderName: String = "",
    val senderRole: String? = null,
    val messageType: String = "Text",
    val content: String = "",
    val latitude: Double? = null,
    val longitude: Double? = null,
    val quickResponseCode: String? = null,
    val verdict: String = "Approved",
    val sentAt: String = ""
)

/**
 * Result from sending a message, including the guardrails verdict.
 */
data class SendMessageResult(
    val messageId: String = "",
    val requestId: String = "",
    val senderId: String = "",
    val verdict: String = "Approved",
    val reason: String? = null,
    val redactedContent: String? = null,
    val piiDetected: Boolean = false,
    val piiTypes: List<String>? = null,
    val profanityDetected: Boolean = false,
    val threatDetected: Boolean = false,
    val rateLimited: Boolean = false,
    val messagesSentInWindow: Int = 0,
    val rateLimitMax: Int = 30,
    val sentAt: String = ""
)

/**
 * A pre-defined quick response that responders can send with one tap.
 * Quick responses are known-safe and bypass the profanity filter.
 */
data class QuickResponse(
    val code: String,          // e.g., "ON_MY_WAY", "NEED_MEDICAL", "ALL_CLEAR"
    val displayText: String,   // e.g., "I'm on my way"
    val category: String       // e.g., "Movement", "Request", "Status", "Medical"
)
