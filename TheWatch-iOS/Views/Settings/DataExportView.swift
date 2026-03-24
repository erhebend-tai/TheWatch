/**
 * WRITE-AHEAD LOG | File: DataExportView.swift | Purpose: GDPR Art.20 data export UI
 * Created: 2026-03-24 | Author: Claude | Deps: SwiftUI, DataExportService
 * Usage: NavigationLink("Export My Data") { DataExportView() }
 */
import SwiftUI
import Combine

struct DataExportView: View {
    @Environment(\.dismiss) var dismiss
    @State private var selectedCategories = Set(GDPRDataCategory.allCases)
    @State private var isExporting = false
    @State private var progressPercent = 0
    @State private var exportedJSON: String? = nil
    @State private var exportService = MockDataExportService()
    @State private var cancellables = Set<AnyCancellable>()

    var body: some View {
        ZStack {
            Color(red: 0.97, green: 0.97, blue: 0.97).ignoresSafeArea()
            VStack(spacing: 0) {
                HStack { Button(action: { dismiss() }) { HStack(spacing: 4) { Image(systemName: "chevron.left"); Text("Back") }.foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27)) }; Spacer(); Text("Export My Data").font(.headline).fontWeight(.bold); Spacer() }.padding(16).background(Color.white)
                Divider()
                ScrollView {
                    VStack(spacing: 16) {
                        VStack(alignment: .leading, spacing: 8) {
                            Text("Your Data, Your Right").font(.headline).fontWeight(.bold)
                            Text("Under GDPR Article 20, you can receive your data as JSON.").font(.caption).foregroundColor(.secondary)
                        }.padding(16).frame(maxWidth: .infinity, alignment: .leading).background(Color(red: 0.94, green: 0.97, blue: 1.0)).cornerRadius(12).padding(.horizontal, 16)

                        VStack(alignment: .leading, spacing: 12) {
                            Text("Select Categories").font(.subheadline).fontWeight(.bold).padding(.horizontal, 16)
                            ForEach(GDPRDataCategory.allCases, id: \.self) { cat in
                                HStack { Image(systemName: selectedCategories.contains(cat) ? "checkmark.square.fill" : "square").foregroundColor(selectedCategories.contains(cat) ? Color(red: 0.9, green: 0.22, blue: 0.27) : .gray).frame(width: 44, height: 44); Text(cat.rawValue.capitalized).font(.subheadline); Spacer() }
                                .contentShape(Rectangle()).onTapGesture { if selectedCategories.contains(cat) { selectedCategories.remove(cat) } else { selectedCategories.insert(cat) } }.padding(.horizontal, 16)
                            }
                        }

                        if isExporting { VStack(spacing: 8) { Text("Preparing... \(progressPercent)%").font(.caption).foregroundColor(.secondary); ProgressView(value: Double(progressPercent), total: 100).tint(Color(red: 0.9, green: 0.22, blue: 0.27)) }.padding(.horizontal, 16) }

                        if exportedJSON != nil {
                            HStack(spacing: 12) { Image(systemName: "checkmark.circle.fill").foregroundColor(.green).font(.title2); VStack(alignment: .leading) { Text("Export Complete").font(.subheadline).fontWeight(.bold).foregroundColor(.green); Text("\((exportedJSON?.utf8.count ?? 0)/1024)KB JSON").font(.caption).foregroundColor(.secondary) }; Spacer() }.padding(16).background(Color(red: 0.91, green: 0.96, blue: 0.91)).cornerRadius(12).padding(.horizontal, 16)
                        }

                        Button(action: startExport) { Text(isExporting ? "Exporting..." : "Export Selected Data").frame(maxWidth: .infinity).padding(12).background((!isExporting && !selectedCategories.isEmpty) ? Color(red: 0.9, green: 0.22, blue: 0.27) : Color.gray).foregroundColor(.white).cornerRadius(8) }.disabled(isExporting || selectedCategories.isEmpty).padding(.horizontal, 16)
                        Spacer().frame(height: 20)
                    }.padding(.vertical, 16)
                }
            }
        }
    }

    private func startExport() {
        isExporting = true; exportedJSON = nil; progressPercent = 0
        exportService.exportWithProgress(userId: "user-001", categories: selectedCategories).receive(on: DispatchQueue.main).sink { status in
            switch status { case .preparing(let p): progressPercent = p; case .complete(let json, _): exportedJSON = json; isExporting = false; case .failed(_, _): isExporting = false }
        }.store(in: &cancellables)
    }
}
