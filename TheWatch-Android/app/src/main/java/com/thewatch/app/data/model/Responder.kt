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
