/**
 * WRITE-AHEAD LOG | File: DataExportService.swift | Purpose: GDPR Art.20 data portability protocol + mock
 * Created: 2026-03-24 | Author: Claude | Deps: Foundation, Combine
 * Usage: let json = try await MockDataExportService().exportAllUserData(userId: "user-001")
 * Regulatory: GDPR Art.20, CCPA 1798.100, LGPD Art.18(V)
 */
import Foundation
import Combine

enum GDPRDataCategory: String, CaseIterable, Codable {
    case profile, emergencyContacts, incidentHistory, locationLogs, consentRecords
    case eulaAcceptanceHistory, volunteerData, deviceRegistrations, notificationPreferences
    case biometricEnrollmentMetadata, phraseDetectionConfig, sosConfiguration, responderInteractions
}

enum GDPRDataExportStatus {
    case preparing(progressPercent: Int)
    case complete(jsonPayload: String, sizeBytes: Int)
    case failed(reason: String, retryable: Bool)
}

struct GDPRExportAuditRecord: Codable, Identifiable {
    let id: String; let userId: String; let requestedAt: Date; let completedAt: Date?
    let categories: [GDPRDataCategory]; let format: String; let sizeBytes: Int
    init(id: String = UUID().uuidString, userId: String, requestedAt: Date = Date(), completedAt: Date? = nil, categories: [GDPRDataCategory] = GDPRDataCategory.allCases, format: String = "application/json", sizeBytes: Int = 0) {
        self.id = id; self.userId = userId; self.requestedAt = requestedAt; self.completedAt = completedAt; self.categories = categories; self.format = format; self.sizeBytes = sizeBytes
    }
}

protocol DataExportServiceProtocol: AnyObject, Sendable {
    func exportAllUserData(userId: String, categories: Set<GDPRDataCategory>) async throws -> String
    func exportWithProgress(userId: String, categories: Set<GDPRDataCategory>) -> AnyPublisher<GDPRDataExportStatus, Never>
    func getStoredCategories(userId: String) async -> Set<GDPRDataCategory>
    func recordExportAudit(_ record: GDPRExportAuditRecord) async
    func getExportHistory(userId: String) async -> [GDPRExportAuditRecord]
}

@Observable
final class MockDataExportService: DataExportServiceProtocol, @unchecked Sendable {
    private var auditRecords: [GDPRExportAuditRecord] = []

    func exportAllUserData(userId: String, categories: Set<GDPRDataCategory> = Set(GDPRDataCategory.allCases)) async throws -> String {
        try await Task.sleep(nanoseconds: 1_500_000_000); return buildJSON(userId: userId, categories: categories)
    }

    func exportWithProgress(userId: String, categories: Set<GDPRDataCategory> = Set(GDPRDataCategory.allCases)) -> AnyPublisher<GDPRDataExportStatus, Never> {
        let subject = PassthroughSubject<GDPRDataExportStatus, Never>()
        Task {
            subject.send(.preparing(progressPercent: 0)); try? await Task.sleep(nanoseconds: 500_000_000)
            subject.send(.preparing(progressPercent: 30)); try? await Task.sleep(nanoseconds: 500_000_000)
            subject.send(.preparing(progressPercent: 65)); try? await Task.sleep(nanoseconds: 500_000_000)
            subject.send(.preparing(progressPercent: 90)); try? await Task.sleep(nanoseconds: 300_000_000)
            let json = buildJSON(userId: userId, categories: categories)
            subject.send(.complete(jsonPayload: json, sizeBytes: json.utf8.count))
            subject.send(completion: .finished)
        }
        return subject.eraseToAnyPublisher()
    }

    func getStoredCategories(userId: String) async -> Set<GDPRDataCategory> { Set(GDPRDataCategory.allCases) }
    func recordExportAudit(_ record: GDPRExportAuditRecord) async { auditRecords.append(record) }
    func getExportHistory(userId: String) async -> [GDPRExportAuditRecord] { auditRecords.filter { $0.userId == userId } }

    private func buildJSON(userId: String, categories: Set<GDPRDataCategory>) -> String {
        var s: [String] = []
        if categories.contains(.profile) { s.append(#""profile":{"userId":"\#(userId)","email":"alex@example.com","firstName":"Alex","lastName":"Rivera"}"#) }
        if categories.contains(.emergencyContacts) { s.append(#""emergencyContacts":[{"name":"Jordan Rivera","phone":"+1-555-0456"}]"#) }
        if categories.contains(.incidentHistory) { s.append(#""incidentHistory":[{"id":"inc-001","type":"SOS","severity":"high","status":"resolved"}]"#) }
        if categories.contains(.volunteerData) { s.append(#""volunteerData":{"isVolunteer":true,"hasCar":true,"isOver18":true}"#) }
        return #"{"exportMetadata":{"exportId":"\#(UUID().uuidString)","userId":"\#(userId)","format":"TheWatch GDPR Export v1.0"},\#(s.joined(separator: ","))}"#
    }
}
