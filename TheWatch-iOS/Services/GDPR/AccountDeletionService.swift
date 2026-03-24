/**
 * WRITE-AHEAD LOG | File: AccountDeletionService.swift | Purpose: GDPR Art.17 right to erasure protocol + mock
 * Created: 2026-03-24 | Author: Claude | Deps: Foundation
 * Usage: let req = try await MockAccountDeletionService().requestDeletion(userId: "u1")
 * Regulatory: GDPR Art.17, Apple 5.1.1(v), Google Play
 */
import Foundation

enum GDPRDeletionState: String, Codable { case none, pendingGracePeriod, executing, completed, cancelled, failed }
enum GDPRDeletionConfirmationStep: CaseIterable { case reAuthenticate, typeConfirmation, acknowledgeDataLoss, offerExport }

struct GDPRDeletionRequest: Codable, Identifiable {
    let id: String; let userId: String; let requestedAt: Date; let scheduledDeletionAt: Date; let reason: String?; var state: GDPRDeletionState; let cancellationDeadline: Date; var daysRemaining: Int
    init(id: String = UUID().uuidString, userId: String, requestedAt: Date = Date(), scheduledDeletionAt: Date, reason: String? = nil, state: GDPRDeletionState = .pendingGracePeriod, cancellationDeadline: Date, daysRemaining: Int = 30) {
        self.id = id; self.userId = userId; self.requestedAt = requestedAt; self.scheduledDeletionAt = scheduledDeletionAt; self.reason = reason; self.state = state; self.cancellationDeadline = cancellationDeadline; self.daysRemaining = daysRemaining
    }
}

struct GDPRDeletionAuditEntry: Codable, Identifiable {
    let id: String; let timestamp: Date; let action: String; let userId: String; let performedBy: String; let details: String
    init(id: String = UUID().uuidString, timestamp: Date = Date(), action: String, userId: String, performedBy: String, details: String) {
        self.id = id; self.timestamp = timestamp; self.action = action; self.userId = userId; self.performedBy = performedBy; self.details = details
    }
}

protocol AccountDeletionServiceProtocol: AnyObject, Sendable {
    func getConfirmationSteps() -> [GDPRDeletionConfirmationStep]
    func requestDeletion(userId: String, reason: String?) async throws -> GDPRDeletionRequest
    func getDeletionStatus(userId: String) async -> GDPRDeletionRequest?
    func cancelDeletion(userId: String) async throws
    func executeDeletion(userId: String) async throws -> [GDPRDataCategory]
    func getGracePeriodDaysRemaining(userId: String) async -> Int
    func getDeletionAuditTrail(userId: String) async -> [GDPRDeletionAuditEntry]
}

@Observable
final class MockAccountDeletionService: AccountDeletionServiceProtocol, @unchecked Sendable {
    private var requests: [String: GDPRDeletionRequest] = [:]
    private var audit: [GDPRDeletionAuditEntry] = []

    func getConfirmationSteps() -> [GDPRDeletionConfirmationStep] { [.offerExport, .acknowledgeDataLoss, .typeConfirmation, .reAuthenticate] }

    func requestDeletion(userId: String, reason: String? = nil) async throws -> GDPRDeletionRequest {
        try await Task.sleep(nanoseconds: 800_000_000)
        let scheduled = Calendar.current.date(byAdding: .day, value: 30, to: Date())!
        let req = GDPRDeletionRequest(userId: userId, scheduledDeletionAt: scheduled, reason: reason, cancellationDeadline: scheduled)
        requests[userId] = req
        audit.append(GDPRDeletionAuditEntry(action: "DELETION_REQUESTED", userId: userId, performedBy: userId, details: "Reason: \(reason ?? "N/A")"))
        return req
    }

    func getDeletionStatus(userId: String) async -> GDPRDeletionRequest? {
        guard var req = requests[userId] else { return nil }
        req.daysRemaining = max(0, Calendar.current.dateComponents([.day], from: Date(), to: req.scheduledDeletionAt).day ?? 0)
        return req
    }

    func cancelDeletion(userId: String) async throws {
        try await Task.sleep(nanoseconds: 500_000_000)
        guard var req = requests[userId], req.state == .pendingGracePeriod else { throw NSError(domain: "Deletion", code: 1, userInfo: [NSLocalizedDescriptionKey: "No pending deletion"]) }
        req.state = .cancelled; requests[userId] = req
        audit.append(GDPRDeletionAuditEntry(action: "DELETION_CANCELLED", userId: userId, performedBy: userId, details: "Cancelled during grace period"))
    }

    func executeDeletion(userId: String) async throws -> [GDPRDataCategory] {
        try await Task.sleep(nanoseconds: 2_000_000_000)
        requests[userId]?.state = .completed
        audit.append(GDPRDeletionAuditEntry(action: "DELETION_EXECUTED", userId: userId, performedBy: "system", details: "All data erased"))
        return GDPRDataCategory.allCases
    }

    func getGracePeriodDaysRemaining(userId: String) async -> Int {
        guard let req = requests[userId], req.state == .pendingGracePeriod else { return 0 }
        return max(0, Calendar.current.dateComponents([.day], from: Date(), to: req.scheduledDeletionAt).day ?? 0)
    }

    func getDeletionAuditTrail(userId: String) async -> [GDPRDeletionAuditEntry] { audit.filter { $0.userId == userId } }
}
