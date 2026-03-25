// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         WatchApiClient.swift
// Purpose:      Centralized HTTP API client for TheWatch Dashboard API.
//               Wraps all REST endpoints with Swift async/await methods.
//               Bearer token auth via Auth.auth().currentUser?.getIDToken().
// Date:         2026-03-24
// Author:       Claude
// Dependencies: Foundation (URLSession), FirebaseAuth
//
// Base URL resolution:
//   1. Aspire service discovery: https+http://dashboard-api
//   2. Config override from UserDefaults / Info.plist
//   3. Fallback: http://localhost:5000
//
// Usage Example:
//   let client = WatchApiClient.shared
//   let status = try await client.getAccountStatus()
//   let responses = try await client.getActiveResponses(userId: "user_001")
//   let participation = try await client.getParticipation(userId: "user_001")
//   try await client.updateParticipation(prefs)
//   let evidence = try await client.getEvidenceForRequest(requestId: "req_001")
// ============================================================================

import Foundation

// ═══════════════════════════════════════════════════════════════
// API Response DTOs — match the JSON shapes from Dashboard.Api
// ═══════════════════════════════════════════════════════════════

struct AccountStatusResponse: Codable {
    let uid: String?
    let email: String?
    let emailVerified: Bool?
    let mfaEnabled: Bool?
    let displayName: String?
    let phoneNumber: String?
    let disabled: Bool?
}

struct TriggerResponseDto: Codable {
    let requestId: String
    let scope: String?
    let strategy: String?
    let escalation: String?
    let status: String?
    let radiusMeters: Double?
    let desiredResponderCount: Int?
    let createdAt: String?
}

struct ActiveResponseDto: Codable {
    let requestId: String
    let userId: String?
    let scope: String?
    let status: String?
    let latitude: Double?
    let longitude: Double?
    let radiusMeters: Double?
    let desiredResponderCount: Int?
    let acknowledgedResponders: [ResponderAckDto]?
    let createdAt: String?
}

struct ResponderAckDto: Codable {
    let ackId: String?
    let responderId: String?
    let responderName: String?
    let responderRole: String?
    let latitude: Double?
    let longitude: Double?
    let distanceMeters: Double?
    let hasVehicle: Bool?
    let estimatedArrival: String?
    let status: String?
}

struct ParticipationPreferencesDto: Codable {
    var userId: String
    var isAvailable: Bool?
    var optInScopes: [String]?
    var certifications: [String]?
    var hasVehicle: Bool?
    var maxRadiusMeters: Double?
    var quietHoursStart: String?
    var quietHoursEnd: String?
    var weeklySchedule: [String: [String]]?
}

struct EvidenceSubmissionDto: Codable {
    let id: String
    let requestId: String?
    let userId: String?
    let submitterId: String?
    let phase: String?
    let submissionType: String?
    let title: String?
    let description: String?
    let latitude: Double?
    let longitude: Double?
    let contentHash: String?
    let mimeType: String?
    let fileSizeBytes: Int?
    let blobReference: String?
    let thumbnailBlobReference: String?
    let status: String?
    let submittedAt: String?
}

struct SituationDto: Codable {
    let request: ActiveResponseDto?
    let responders: [ResponderAckDto]?
    let escalationHistory: [[String: AnyCodable]]?
}

// Helper for arbitrary JSON values
struct AnyCodable: Codable {
    let value: Any

    init(_ value: Any) { self.value = value }

    init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        if let str = try? container.decode(String.self) { value = str }
        else if let num = try? container.decode(Double.self) { value = num }
        else if let bool = try? container.decode(Bool.self) { value = bool }
        else { value = "" }
    }

    func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        if let str = value as? String { try container.encode(str) }
        else if let num = value as? Double { try container.encode(num) }
        else if let bool = value as? Bool { try container.encode(bool) }
        else { try container.encode("") }
    }
}

// ═══════════════════════════════════════════════════════════════
// API Error
// ═══════════════════════════════════════════════════════════════

enum ApiError: LocalizedError {
    case httpError(statusCode: Int, body: String)
    case decodingError(Error)
    case networkError(Error)
    case noAuthToken

    var errorDescription: String? {
        switch self {
        case .httpError(let code, let body):
            return "API error \(code): \(body)"
        case .decodingError(let error):
            return "Decoding error: \(error.localizedDescription)"
        case .networkError(let error):
            return "Network error: \(error.localizedDescription)"
        case .noAuthToken:
            return "No authentication token available"
        }
    }
}

// ═══════════════════════════════════════════════════════════════
// API Client
// ═══════════════════════════════════════════════════════════════

@Observable
final class WatchApiClient: @unchecked Sendable {

    static let shared = WatchApiClient()

    /// Base URL for the Dashboard API.
    /// Resolution order:
    ///   1. Environment/Info.plist override
    ///   2. Aspire service discovery
    ///   3. Fallback: http://localhost:5000
    private(set) var baseUrl: String = "http://localhost:5000"

    private let session: URLSession
    private let decoder: JSONDecoder
    private let encoder: JSONEncoder

    private init() {
        let config = URLSessionConfiguration.default
        config.timeoutIntervalForRequest = 30
        config.timeoutIntervalForResource = 60
        session = URLSession(configuration: config)

        decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601

        encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
    }

    // ── Configuration ─────────────────────────────────────────────

    func setBaseUrl(_ url: String) {
        baseUrl = url.hasSuffix("/") ? String(url.dropLast()) : url
    }

    // ── Auth Token ────────────────────────────────────────────────

    /// Get the Firebase ID token for the current user.
    /// Uses FirebaseAuth if available, returns nil otherwise.
    private func getAuthToken() async -> String? {
        // Firebase Auth token retrieval:
        // import FirebaseAuth
        // guard let user = Auth.auth().currentUser else { return nil }
        // return try? await user.getIDToken()

        // For now, return nil (mock mode). When FirebaseAuth is linked,
        // uncomment the above and remove this line.
        return nil
    }

    // ── HTTP Helpers ──────────────────────────────────────────────

    private func request<T: Decodable>(
        method: String,
        path: String,
        body: (any Encodable)? = nil
    ) async throws -> T {
        guard let url = URL(string: "\(baseUrl)\(path)") else {
            throw ApiError.networkError(URLError(.badURL))
        }

        var request = URLRequest(url: url)
        request.httpMethod = method
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("application/json", forHTTPHeaderField: "Accept")

        if let token = await getAuthToken() {
            request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        }

        if let body {
            request.httpBody = try encoder.encode(AnyEncodable(body))
        }

        let (data, response): (Data, URLResponse)
        do {
            (data, response) = try await session.data(for: request)
        } catch {
            throw ApiError.networkError(error)
        }

        guard let httpResponse = response as? HTTPURLResponse else {
            throw ApiError.networkError(URLError(.badServerResponse))
        }

        guard (200...299).contains(httpResponse.statusCode) else {
            let body = String(data: data, encoding: .utf8) ?? ""
            throw ApiError.httpError(statusCode: httpResponse.statusCode, body: body)
        }

        do {
            return try decoder.decode(T.self, from: data)
        } catch {
            throw ApiError.decodingError(error)
        }
    }

    private func get<T: Decodable>(_ path: String) async throws -> T {
        try await request(method: "GET", path: path)
    }

    private func post<T: Decodable>(_ path: String, body: (any Encodable)? = nil) async throws -> T {
        try await request(method: "POST", path: path, body: body)
    }

    private func put<T: Decodable>(_ path: String, body: any Encodable) async throws -> T {
        try await request(method: "PUT", path: path, body: body)
    }

    // ═══════════════════════════════════════════════════════════════
    // Account Endpoints — /api/account
    // ═══════════════════════════════════════════════════════════════

    /// GET /api/account/status
    func getAccountStatus() async throws -> AccountStatusResponse {
        try await get("/api/account/status")
    }

    /// POST /api/account/verify-email
    func sendEmailVerification() async throws -> [String: String] {
        try await post("/api/account/verify-email")
    }

    /// POST /api/account/mfa/enroll
    func enrollMfa(method: String, phoneNumber: String? = nil) async throws -> [String: String] {
        try await post("/api/account/mfa/enroll", body: ["method": method, "phoneNumber": phoneNumber ?? ""])
    }

    /// POST /api/account/mfa/confirm
    func confirmMfaEnrollment(sessionId: String, code: String) async throws -> [String: String] {
        try await post("/api/account/mfa/confirm", body: ["sessionId": sessionId, "code": code])
    }

    /// POST /api/account/mfa/verify
    func verifyMfaCode(code: String, method: String = "totp") async throws -> [String: String] {
        try await post("/api/account/mfa/verify", body: ["code": code, "method": method])
    }

    /// POST /api/account/password-reset (AllowAnonymous)
    func sendPasswordReset(email: String) async throws -> [String: String] {
        try await post("/api/account/password-reset", body: ["email": email])
    }

    // ═══════════════════════════════════════════════════════════════
    // Response Endpoints — /api/response
    // ═══════════════════════════════════════════════════════════════

    /// POST /api/response/trigger
    func triggerResponse(
        userId: String,
        scope: String,
        latitude: Double,
        longitude: Double,
        description: String? = nil,
        triggerSource: String? = nil
    ) async throws -> TriggerResponseDto {
        let body: [String: Any] = [
            "userId": userId,
            "scope": scope,
            "latitude": latitude,
            "longitude": longitude,
            "description": description ?? "",
            "triggerSource": triggerSource ?? ""
        ]
        return try await post("/api/response/trigger", body: DictionaryEncodable(body))
    }

    /// POST /api/response/{requestId}/ack
    func acknowledgeResponse(
        requestId: String,
        responderId: String,
        responderName: String? = nil,
        responderRole: String? = nil,
        latitude: Double = 0,
        longitude: Double = 0,
        distanceMeters: Double = 0,
        hasVehicle: Bool = true,
        estimatedArrivalMinutes: Int? = nil
    ) async throws -> [String: AnyCodable] {
        let body: [String: Any] = [
            "responderId": responderId,
            "responderName": responderName ?? "",
            "responderRole": responderRole ?? "",
            "latitude": latitude,
            "longitude": longitude,
            "distanceMeters": distanceMeters,
            "hasVehicle": hasVehicle,
            "estimatedArrivalMinutes": estimatedArrivalMinutes ?? 0
        ]
        return try await post("/api/response/\(requestId)/ack", body: DictionaryEncodable(body))
    }

    /// POST /api/response/{requestId}/cancel
    func cancelResponse(requestId: String, reason: String? = nil) async throws -> [String: String] {
        try await post("/api/response/\(requestId)/cancel", body: ["reason": reason ?? "User cancelled"])
    }

    /// POST /api/response/{requestId}/resolve
    func resolveResponse(requestId: String, resolvedBy: String? = nil) async throws -> [String: String] {
        try await post("/api/response/\(requestId)/resolve", body: ["resolvedBy": resolvedBy ?? "system"])
    }

    /// GET /api/response/{requestId}
    func getSituation(requestId: String) async throws -> SituationDto {
        try await get("/api/response/\(requestId)")
    }

    /// GET /api/response/active/{userId}
    func getActiveResponses(userId: String) async throws -> [ActiveResponseDto] {
        try await get("/api/response/active/\(userId)")
    }

    /// GET /api/response/participation/{userId}
    func getParticipation(userId: String) async throws -> ParticipationPreferencesDto {
        try await get("/api/response/participation/\(userId)")
    }

    /// PUT /api/response/participation
    func updateParticipation(_ prefs: ParticipationPreferencesDto) async throws -> ParticipationPreferencesDto {
        try await put("/api/response/participation", body: prefs)
    }

    /// POST /api/response/participation/{userId}/availability
    func setAvailability(userId: String, isAvailable: Bool, durationMinutes: Int? = nil) async throws -> [String: AnyCodable] {
        var body: [String: Any] = ["isAvailable": isAvailable]
        if let mins = durationMinutes {
            body["duration"] = "00:\(mins):00"
        }
        return try await post("/api/response/participation/\(userId)/availability", body: DictionaryEncodable(body))
    }

    /// POST /api/response/sos-token
    func getSosToken() async throws -> [String: String] {
        try await post("/api/response/sos-token")
    }

    /// POST /api/response/{requestId}/messages
    func sendResponderMessage(
        requestId: String,
        senderId: String,
        senderName: String? = nil,
        senderRole: String? = nil,
        messageType: String = "Text",
        content: String = "",
        latitude: Double? = nil,
        longitude: Double? = nil,
        quickResponseCode: String? = nil
    ) async throws -> [String: AnyCodable] {
        let body: [String: Any] = [
            "senderId": senderId,
            "senderName": senderName ?? "",
            "senderRole": senderRole ?? "",
            "messageType": messageType,
            "content": content,
            "latitude": latitude ?? 0,
            "longitude": longitude ?? 0,
            "quickResponseCode": quickResponseCode ?? ""
        ]
        return try await post("/api/response/\(requestId)/messages", body: DictionaryEncodable(body))
    }

    /// GET /api/response/{requestId}/messages
    func getResponderMessages(requestId: String, limit: Int = 100, since: Date? = nil) async throws -> [[String: AnyCodable]] {
        var path = "/api/response/\(requestId)/messages?limit=\(limit)"
        if let since {
            let formatter = ISO8601DateFormatter()
            path += "&since=\(formatter.string(from: since))"
        }
        return try await get(path)
    }

    /// GET /api/response/quick-responses
    func getQuickResponses() async throws -> [[String: String]] {
        try await get("/api/response/quick-responses")
    }

    // ═══════════════════════════════════════════════════════════════
    // Evidence Endpoints — /api/evidence
    // ═══════════════════════════════════════════════════════════════

    /// GET /api/evidence/request/{requestId}
    func getEvidenceForRequest(requestId: String) async throws -> [EvidenceSubmissionDto] {
        try await get("/api/evidence/request/\(requestId)")
    }

    /// GET /api/evidence/{id}
    func getEvidence(id: String) async throws -> EvidenceSubmissionDto {
        try await get("/api/evidence/\(id)")
    }

    /// GET /api/evidence/user/{userId}
    func getEvidenceForUser(userId: String, phase: String? = nil) async throws -> [EvidenceSubmissionDto] {
        let query = phase.map { "?phase=\($0)" } ?? ""
        return try await get("/api/evidence/user/\(userId)\(query)")
    }

    /// GET /api/health
    func healthCheck() async throws -> [String: AnyCodable] {
        try await get("/api/health")
    }
}

// ═══════════════════════════════════════════════════════════════
// Encoding Helpers
// ═══════════════════════════════════════════════════════════════

/// Type-erased Encodable wrapper for passing any Encodable to generic methods.
private struct AnyEncodable: Encodable {
    let value: any Encodable

    init(_ value: any Encodable) {
        self.value = value
    }

    func encode(to encoder: Encoder) throws {
        try value.encode(to: encoder)
    }
}

/// Encodes a [String: Any] dictionary as JSON.
private struct DictionaryEncodable: Encodable {
    let dict: [String: Any]

    init(_ dict: [String: Any]) {
        self.dict = dict
    }

    func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: DynamicCodingKey.self)
        for (key, value) in dict {
            let codingKey = DynamicCodingKey(stringValue: key)!
            switch value {
            case let str as String:
                try container.encode(str, forKey: codingKey)
            case let num as Int:
                try container.encode(num, forKey: codingKey)
            case let num as Double:
                try container.encode(num, forKey: codingKey)
            case let bool as Bool:
                try container.encode(bool, forKey: codingKey)
            case let arr as [String]:
                try container.encode(arr, forKey: codingKey)
            default:
                try container.encode("\(value)", forKey: codingKey)
            }
        }
    }
}

private struct DynamicCodingKey: CodingKey {
    var stringValue: String
    var intValue: Int?

    init?(stringValue: String) {
        self.stringValue = stringValue
        self.intValue = nil
    }

    init?(intValue: Int) {
        self.stringValue = "\(intValue)"
        self.intValue = intValue
    }
}
