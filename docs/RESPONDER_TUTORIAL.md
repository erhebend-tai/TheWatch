# TheWatch Responder System — Tutorial

This tutorial walks through the complete responder lifecycle: enrollment, dispatch,
acknowledgment with directions, incident communication, and resolution.

---

## Table of Contents

1. [Enrolling as a Volunteer Responder](#1-enrolling-as-a-volunteer-responder)
2. [Setting Participation Preferences](#2-setting-participation-preferences)
3. [Receiving a Dispatch Notification](#3-receiving-a-dispatch-notification)
4. [Acknowledging & Getting Directions](#4-acknowledging--getting-directions)
5. [Communicating with Other Responders](#5-communicating-with-other-responders)
6. [Quick Responses](#6-quick-responses)
7. [Guardrails & Content Filtering](#7-guardrails--content-filtering)
8. [Vehicle & Distance Filtering](#8-vehicle--distance-filtering)
9. [Arriving On Scene](#9-arriving-on-scene)
10. [Resolution & Post-Incident](#10-resolution--post-incident)

---

## 1. Enrolling as a Volunteer Responder

### Android (Kotlin)
```kotlin
val repo: VolunteerRepository = // injected

val result = repo.enrollAsResponder(
    userId = "user-123",
    role = "VOLUNTEER",
    certifications = listOf("CPR", "FIRST_AID"),
    hasVehicle = true  // Important: affects which incidents you're dispatched to
)

result.onSuccess { responder ->
    Log.d("TheWatch", "Enrolled as ${responder.type} with id ${responder.id}")
}
```

### iOS (Swift)
```swift
let service: VolunteerServiceProtocol = MockVolunteerService()

try await service.enrollAsVolunteer(
    userId: "user-123",
    role: .volunteer,
    skills: ["CPR", "First Aid"],
    radiusMeters: 3000,
    hasVehicle: true
)
```

### REST API
```
POST /api/response/participation
Content-Type: application/json

{
    "userId": "user-123",
    "isResponderEnabled": true,
    "optedInCheckIn": true,
    "optedInNeighborhood": true,
    "optedInCommunity": true,
    "optedInEvacuation": true,
    "isCurrentlyAvailable": true,
    "certifications": ["CPR", "FIRST_AID"],
    "maxResponseRadiusMeters": 5000,
    "willingToBeFirstOnScene": true,
    "hasVehicle": true
}
```

---

## 2. Setting Participation Preferences

Responders control **what** they respond to, **when** they're available, and
**how far** they'll travel:

```
PUT /api/response/participation
{
    "userId": "user-123",
    "isResponderEnabled": true,

    // Scope opt-in: which types of incidents?
    "optedInCheckIn": true,        // Wellness checks (1km radius)
    "optedInNeighborhood": true,   // Local emergencies (3km radius)
    "optedInCommunity": false,     // Community-wide (10km) — opted out
    "optedInEvacuation": true,     // Always get evacuation alerts

    // Availability windows
    "availableFrom": "08:00",
    "availableTo": "22:00",
    "availableDays": ["Monday","Tuesday","Wednesday","Thursday","Friday"],

    // Quiet hours (no notifications)
    "quietHoursStart": "23:00",
    "quietHoursEnd": "07:00",

    // Vehicle availability — responders WITHOUT a vehicle are only
    // dispatched to incidents within walking distance (1600m / ~1 mile)
    "hasVehicle": true,

    "maxResponseRadiusMeters": 5000
}
```

### Quick availability toggle
```
POST /api/response/participation/user-123/availability
{ "isAvailable": false, "duration": "02:00:00" }
```
This sets you as unavailable for 2 hours, then auto-restores.

---

## 3. Receiving a Dispatch Notification

When someone triggers an SOS, the system:

1. Determines scope-appropriate defaults (radius, responder count, escalation)
2. Finds eligible responders using spatial indexing
3. **Filters out on-foot responders beyond walking distance** (1600m)
4. Sends push notifications (FCM on Android, APNs on iOS)

### Distance filtering rule

| Distance to incident | Has vehicle? | Dispatched? |
|---------------------|-------------|-------------|
| 800m                | No          | Yes         |
| 1500m               | No          | Yes         |
| 2000m               | No          | **No** — beyond 1600m walking limit |
| 2000m               | Yes         | Yes         |
| 5000m               | Yes         | Yes         |
| 5000m               | No          | **No**      |

This prevents asking someone on foot to walk 30+ minutes to an emergency.

---

## 4. Acknowledging & Getting Directions

When a responder taps "I'm on my way", the API records their acknowledgment
AND returns turn-by-turn navigation directions to the incident.

### REST API
```
POST /api/response/{requestId}/ack
{
    "responderId": "user-123",
    "responderName": "Marcus Chen",
    "responderRole": "EMT",
    "latitude": 30.2672,
    "longitude": -97.7431,
    "distanceMeters": 1200,
    "hasVehicle": true,
    "estimatedArrivalMinutes": 5
}
```

### Response includes directions
```json
{
    "ackId": "a1b2c3d4e5f6",
    "requestId": "f6e5d4c3b2a1",
    "responderId": "user-123",
    "status": "EnRoute",
    "estimatedArrival": "00:05:00",
    "directions": {
        "travelMode": "driving",
        "distanceMeters": 1200,
        "estimatedTravelTime": "00:04:30",
        "googleMapsUrl": "https://www.google.com/maps/dir/?api=1&origin=30.2672,-97.7431&destination=30.2750,-97.7350&travelmode=driving",
        "appleMapsUrl": "https://maps.apple.com/?saddr=30.2672,-97.7431&daddr=30.2750,-97.7350&dirflg=d",
        "wazeUrl": "https://waze.com/ul?ll=30.2750,-97.7350&navigate=yes"
    }
}
```

### Android — launch navigation
```kotlin
repo.acceptResponse(userId, alertId).onSuccess { ack ->
    // Launch Google Maps with directions to the incident
    val intent = Intent(Intent.ACTION_VIEW, Uri.parse(ack.directions.googleMapsUrl))
    intent.setPackage("com.google.android.apps.maps") // Prefer Google Maps app
    startActivity(intent)
}
```

### iOS — launch navigation
```swift
let ack = try await service.acceptResponse(userId: uid, requestId: rid)

// Launch Apple Maps with directions
if let url = URL(string: ack.directions.appleMapsUrl) {
    await UIApplication.shared.open(url)
}
```

If the responder does NOT have a vehicle, `travelMode` is `"walking"` and
the map app will show walking directions instead of driving.

---

## 5. Communicating with Other Responders

Once acknowledged, responders can message each other in an incident-scoped
channel. **All messages route through the server** for safety filtering.

### Sending a message
```
POST /api/response/{requestId}/messages
{
    "senderId": "user-123",
    "senderName": "Marcus Chen",
    "senderRole": "EMT",
    "messageType": "Text",
    "content": "I can see the building. Approaching from the north side."
}
```

### Response shows guardrails verdict
```json
{
    "messageId": "m1a2b3c4d5e6",
    "requestId": "f6e5d4c3b2a1",
    "senderId": "user-123",
    "verdict": "Approved",
    "reason": null,
    "piiDetected": false,
    "profanityDetected": false,
    "threatDetected": false,
    "rateLimited": false,
    "messagesSentInWindow": 3,
    "rateLimitMax": 30,
    "sentAt": "2026-03-24T15:30:00Z"
}
```

### Receiving messages (real-time via SignalR)
Messages are broadcast to all responders in the incident group:

```javascript
// SignalR client (web/mobile)
connection.on("ResponderMessage", (message) => {
    // message.senderId, message.senderName, message.content, message.messageType
    addMessageToChat(message);
});
```

### Fetching message history
```
GET /api/response/{requestId}/messages?limit=50&since=2026-03-24T15:00:00Z
```

### Sharing location
```json
POST /api/response/{requestId}/messages
{
    "senderId": "user-123",
    "senderName": "Marcus Chen",
    "messageType": "LocationShare",
    "content": "Current position",
    "latitude": 30.2680,
    "longitude": -97.7425
}
```

---

## 6. Quick Responses

Quick responses are pre-defined messages that responders can send with one tap.
They're known-safe and bypass the profanity filter.

### Get available quick responses
```
GET /api/response/quick-responses
```

```json
[
    { "code": "ON_MY_WAY",          "displayText": "I'm on my way",                "category": "Movement" },
    { "code": "ARRIVED",            "displayText": "I've arrived on scene",         "category": "Movement" },
    { "code": "NEED_MEDICAL",       "displayText": "Need medical assistance here",  "category": "Request" },
    { "code": "NEED_BACKUP",        "displayText": "Need additional responders",     "category": "Request" },
    { "code": "NEED_SUPPLIES",      "displayText": "Need supplies (first aid, water)", "category": "Request" },
    { "code": "ALL_CLEAR",          "displayText": "All clear — situation resolved", "category": "Status" },
    { "code": "SCENE_SECURED",      "displayText": "Scene is secured",              "category": "Status" },
    { "code": "VICTIM_CONSCIOUS",   "displayText": "Victim is conscious and alert", "category": "Medical" },
    { "code": "VICTIM_UNCONSCIOUS", "displayText": "Victim is unconscious",         "category": "Medical" },
    { "code": "CPR_IN_PROGRESS",    "displayText": "Performing CPR",                "category": "Medical" },
    { "code": "HAZARD_PRESENT",     "displayText": "Hazard present — approach with caution", "category": "Safety" },
    { "code": "STANDOWN",           "displayText": "Standing down — enough responders", "category": "Movement" },
    { "code": "DELAYED",            "displayText": "I'm delayed — ETA update coming", "category": "Movement" }
]
```

### Sending a quick response
```json
POST /api/response/{requestId}/messages
{
    "senderId": "user-123",
    "senderName": "Marcus Chen",
    "messageType": "QuickResponse",
    "content": "I'm on my way",
    "quickResponseCode": "ON_MY_WAY"
}
```

---

## 7. Guardrails & Content Filtering

Every message passes through a **5-stage server-side guardrails pipeline** before
being delivered to other responders:

### Pipeline stages

| Stage | Check | Action on match |
|-------|-------|-----------------|
| 1. Rate limiting | > 30 messages per minute | **RateLimited** — not delivered |
| 2. PII detection | SSN, phone, email, credit card patterns | **Redacted** — PII replaced with `[REDACTED]` |
| 3. Profanity filter | Blocklist + fuzzy matching | **Blocked** — not delivered |
| 4. Threat detection | Violence, harassment keywords | **Blocked** — not delivered |
| 5. Content classification | Operational relevance | Logged (future: warn on off-topic) |

### Example: PII auto-redaction
```
Input:  "Victim's SSN is 123-45-6789 and phone is 512-555-1234"
Output: "Victim's SSN is [REDACTED] and phone is [REDACTED]"
Verdict: Redacted
```

The sender sees a note that personal information was automatically redacted.
Other responders receive the redacted version.

### Example: Blocked message
```
Input:  "This damn situation is out of control"
Verdict: Blocked
Reason: "Message blocked: profanity detected. Please keep communication
         professional during emergencies."
```

The sender sees the warning. The message is NOT delivered to other responders.

### Authorization check
Only acknowledged responders (status: Acknowledged, EnRoute, or OnScene) can
send or receive messages. If someone who declined or timed out tries to send
a message, they receive:

```json
{ "verdict": "Blocked", "reason": "You are not an acknowledged responder for this incident." }
```

---

## 8. Vehicle & Distance Filtering

The system ensures on-foot responders are not unreasonably dispatched:

- **Default walking limit:** 1600 meters (~1 mile / ~20 minute walk)
- Responders who indicated `hasVehicle: false` during enrollment are only
  dispatched to incidents within this walking distance
- Responders WITH vehicles are dispatched regardless of distance (within
  the scope's radius)

### How it works in code

```csharp
// In the dispatch pipeline (IParticipationPort.FindEligibleRespondersAsync):
.Where(x => x.Prefs.HasVehicle || x.Distance <= maxWalkingDistance)
```

### Travel mode on directions

| hasVehicle | travelMode | Maps app shows |
|-----------|------------|----------------|
| true      | `driving`  | Driving directions |
| false     | `walking`  | Walking directions |

---

## 9. Arriving On Scene

### Update status via API
```
POST /api/response/{requestId}/ack
// Update an existing ack to OnScene status
```

### Via SignalR (real-time)
```javascript
// Mobile client calls hub method
await connection.invoke("ResponderOnScene", requestId, responderId);
```

### Send a quick response
```json
POST /api/response/{requestId}/messages
{
    "senderId": "user-123",
    "messageType": "QuickResponse",
    "quickResponseCode": "ARRIVED",
    "content": "I've arrived on scene"
}
```

---

## 10. Resolution & Post-Incident

### Resolve the incident
```
POST /api/response/{requestId}/resolve
{ "resolvedBy": "user-123" }
```

This:
- Marks the request as Resolved
- Cancels any pending escalation
- Broadcasts `SOSResponseResolved` to all connected clients
- Triggers post-incident survey dispatch (if configured)

### Cancel (false alarm)
```
POST /api/response/{requestId}/cancel
{ "reason": "False alarm — I'm OK" }
```

---

## Complete Flow Diagram

```
User triggers SOS (phrase/tap/button)
  │
  ├─ 1. CreateResponseAsync() — scope presets applied
  │     └─ ResponseRequest created (status: Dispatching)
  │
  ├─ 2. FindEligibleRespondersAsync()
  │     ├─ Filter by scope opt-in
  │     ├─ Filter by availability/quiet hours
  │     └─ *** Filter out on-foot responders beyond walking distance ***
  │
  ├─ 3. DispatchAsync() → push notifications to eligible responders
  │
  ├─ 4. Schedule escalation (if policy requires)
  │
  └─ 5. Broadcast "SOSResponseCreated" via SignalR

Responder receives notification
  │
  ├─ Tap "I'm on my way" → POST /{requestId}/ack
  │     ├─ Record acknowledgment
  │     ├─ *** Return navigation directions (Google Maps / Apple Maps / Waze) ***
  │     ├─ Check if enough responders → cancel escalation
  │     └─ Broadcast "ResponderAcknowledged" via SignalR
  │
  ├─ *** Send messages → POST /{requestId}/messages ***
  │     ├─ Server guardrails pipeline runs
  │     │     ├─ Rate limit check
  │     │     ├─ PII detection → auto-redact
  │     │     ├─ Profanity filter → block
  │     │     └─ Threat detection → block
  │     ├─ If approved/redacted → broadcast to response group
  │     └─ Return verdict to sender
  │
  ├─ *** Quick responses → one-tap pre-defined messages ***
  │
  ├─ Share location → LocationShare message type
  │
  ├─ Arrive on scene → ResponderOnScene hub method
  │
  └─ Situation resolved → POST /{requestId}/resolve
```

---

## API Reference Summary

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/response/trigger` | Trigger a new SOS response |
| POST | `/api/response/{id}/ack` | Acknowledge + get directions |
| POST | `/api/response/{id}/cancel` | Cancel (false alarm) |
| POST | `/api/response/{id}/resolve` | Mark as resolved |
| GET  | `/api/response/{id}` | Get situation (request + acks + escalation) |
| GET  | `/api/response/active/{userId}` | Get user's active responses |
| GET  | `/api/response/participation/{userId}` | Get participation prefs |
| PUT  | `/api/response/participation` | Update participation prefs |
| POST | `/api/response/participation/{userId}/availability` | Toggle availability |
| **POST** | **`/api/response/{id}/messages`** | **Send message (with guardrails)** |
| **GET**  | **`/api/response/{id}/messages`** | **Get message history** |
| **GET**  | **`/api/response/quick-responses`** | **Get quick response list** |

| SignalR Event | Direction | Description |
|---------------|-----------|-------------|
| `SOSResponseCreated` | Server → Client | New SOS dispatched |
| `ResponderAcknowledged` | Server → Client | Responder accepted (with directions) |
| `ResponderLocationUpdated` | Server → Client | Responder position update |
| `ResponderOnScene` | Server → Client | Responder arrived |
| `SOSResponseCancelled` | Server → Client | SOS cancelled |
| `SOSResponseResolved` | Server → Client | Situation resolved |
| **`ResponderMessage`** | **Server → Client** | **Chat message delivered** |
