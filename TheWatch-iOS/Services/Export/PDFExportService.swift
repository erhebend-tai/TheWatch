/**
 * WRITE-AHEAD LOG | File: PDFExportService.swift | Purpose: Tamper-hashed PDF incident reports
 * Created: 2026-03-24 | Author: Claude | Deps: Foundation, CryptoKit
 * Usage: let result = try await MockPDFExportService().generateIncidentReport(userId: "u1")
 * NOTE: HMAC-SHA256 tamper hash. Not PKI digital signature.
 */
import Foundation
import CryptoKit

struct PDFExportResultModel: Codable, Identifiable {
    let id: String; let filePath: String; let fileName: String; let sizeBytes: Int; let pageCount: Int; let tamperHash: String; let generatedAt: Date
    init(id: String = UUID().uuidString, filePath: String, fileName: String, sizeBytes: Int, pageCount: Int, tamperHash: String, generatedAt: Date = Date()) {
        self.id = id; self.filePath = filePath; self.fileName = fileName; self.sizeBytes = sizeBytes; self.pageCount = pageCount; self.tamperHash = tamperHash; self.generatedAt = generatedAt
    }
}

struct PDFExportConfigModel { let title: String; let includeTimeline: Bool; let pageSize: PDFPageSizeModel
    init(title: String = "TheWatch Incident Report", includeTimeline: Bool = true, pageSize: PDFPageSizeModel = .a4) { self.title = title; self.includeTimeline = includeTimeline; self.pageSize = pageSize }
}
enum PDFPageSizeModel { case a4, letter, legal; var size: CGSize { switch self { case .a4: return CGSize(width: 595, height: 842); case .letter: return CGSize(width: 612, height: 792); case .legal: return CGSize(width: 612, height: 1008) } } }

protocol PDFExportServiceProtocol: AnyObject, Sendable {
    func generateIncidentReport(userId: String, config: PDFExportConfigModel) async throws -> PDFExportResultModel
    func generateSingleIncidentReport(incidentId: String, config: PDFExportConfigModel) async throws -> PDFExportResultModel
    func verifyTamperHash(filePath: String) async -> Bool
    func listGeneratedReports() async -> [PDFExportResultModel]
    func deleteReport(filePath: String) async -> Bool
}

@Observable final class MockPDFExportService: PDFExportServiceProtocol, @unchecked Sendable {
    private var reports: [PDFExportResultModel] = []

    func generateIncidentReport(userId: String, config: PDFExportConfigModel = PDFExportConfigModel()) async throws -> PDFExportResultModel {
        try await Task.sleep(nanoseconds: 2_000_000_000)
        let docs = NSSearchPathForDirectoriesInDomains(.documentDirectory, .userDomainMask, true).first ?? "/tmp"
        let r = PDFExportResultModel(filePath: "\(docs)/TheWatch_Report_\(UUID().uuidString.prefix(8)).pdf", fileName: "TheWatch_Report_\(userId).pdf", sizeBytes: 245_760, pageCount: 4, tamperHash: hmac("\(userId)-\(Date().timeIntervalSince1970)"))
        reports.append(r); return r
    }

    func generateSingleIncidentReport(incidentId: String, config: PDFExportConfigModel = PDFExportConfigModel()) async throws -> PDFExportResultModel {
        try await Task.sleep(nanoseconds: 1_500_000_000)
        let docs = NSSearchPathForDirectoriesInDomains(.documentDirectory, .userDomainMask, true).first ?? "/tmp"
        let r = PDFExportResultModel(filePath: "\(docs)/TheWatch_Incident_\(incidentId).pdf", fileName: "TheWatch_Incident_\(incidentId).pdf", sizeBytes: 61_440, pageCount: 1, tamperHash: hmac("\(incidentId)-\(Date().timeIntervalSince1970)"))
        reports.append(r); return r
    }

    func verifyTamperHash(filePath: String) async -> Bool { true }
    func listGeneratedReports() async -> [PDFExportResultModel] { reports }
    func deleteReport(filePath: String) async -> Bool { reports.removeAll { $0.filePath == filePath }; return true }

    private func hmac(_ input: String) -> String {
        let key = SymmetricKey(data: Data("TheWatch-Device-Key".utf8))
        return HMAC<SHA256>.authenticationCode(for: Data(input.utf8), using: key).map { String(format: "%02x", $0) }.joined()
    }
}
