/**
 * WRITE-AHEAD LOG | File: EULAManagementView.swift | Purpose: EULA management with diff, re-accept, version tracking
 * Created: 2026-03-24 | Author: Claude | Deps: SwiftUI
 * Usage: NavigationLink("EULA & Terms") { EULAManagementView() }
 * NOTE: Blocking flow if user declines - app restricted.
 */
import SwiftUI

struct EULAVersionModel: Identifiable { let id = UUID(); let version: String; let publishedAt: String; let content: String; let changes: [String] }
struct EULAAcceptanceModel: Identifiable { let id = UUID(); let version: String; let acceptedAt: String; let userId: String }

@Observable final class EULAManagementViewModel {
    var current: EULAVersionModel? = nil; var acceptedVersion: String? = nil; var needsReAccept = false; var diffs: [String] = []; var history: [EULAAcceptanceModel] = []; var isLoading = true; var isAccepting = false; var error: String? = nil

    init() { Task { @MainActor in
        try? await Task.sleep(nanoseconds: 500_000_000)
        current = EULAVersionModel(version: "2.1.0", publishedAt: "2026-03-20", content: Self.eulaText, changes: ["Data portability (Art.20)", "Retention 180->90 days", "Volunteer liability", "Deletion grace period", "H3 geohash"])
        acceptedVersion = "2.0.0"; needsReAccept = true
        diffs = ["Sec 4.2: Portability expanded", "Sec 7.1: Retention reduced", "Sec 9.3: Volunteer liability", "Sec 11: Deletion grace", "Sec 12.5: H3 geohash"]
        history = [EULAAcceptanceModel(version: "2.0.0", acceptedAt: "2026-02-15", userId: "user-001"), EULAAcceptanceModel(version: "1.0.0", acceptedAt: "2026-01-15", userId: "user-001")]
        isLoading = false
    } }

    func accept() { Task { @MainActor in isAccepting = true; try? await Task.sleep(nanoseconds: 800_000_000); guard let v = current?.version else { return }; acceptedVersion = v; needsReAccept = false; isAccepting = false; history.insert(EULAAcceptanceModel(version: v, acceptedAt: ISO8601DateFormatter().string(from: Date()), userId: "user-001"), at: 0) } }
    func decline() { error = "You must accept the updated EULA to continue using TheWatch." }

    static let eulaText = """
    EULA - TheWatch Safety Platform v2.1.0 | March 20, 2026
    1. ACCEPTANCE - By using TheWatch, you agree to this EULA.
    2. SERVICE - Community safety: phrase detection, location, volunteer coordination.
    3. DATA - Location, voice, motion, profile, contacts, history. Per GDPR Art.6(1)(a).
    4. PORTABILITY - Art.15 access, Art.20 JSON export, Art.17 erasure with 30-day grace.
    5. VOLUNTEERS - 18+. Not 911 replacement. Good Samaritan protections.
    6. RETENTION - Local: 7d. Cloud: 90d. Incidents: per law.
    7. LOCATION - Always permission. H3 indexed.
    8. DELETION - 30-day grace. Irreversible after.
    9. LAW - GDPR, CCPA, LGPD, PIPA, PIPEDA.
    """
}

struct EULAManagementView: View {
    @Environment(\.dismiss) var dismiss
    @State private var vm = EULAManagementViewModel()

    var body: some View {
        ZStack {
            Color(red: 0.97, green: 0.97, blue: 0.97).ignoresSafeArea()
            VStack(spacing: 0) {
                HStack { Button(action: { dismiss() }) { HStack(spacing: 4) { Image(systemName: "chevron.left"); Text("Back") }.foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27)) }; Spacer(); Text("EULA & Terms").font(.headline).fontWeight(.bold); Spacer() }.padding(16).background(Color.white)
                Divider()
                ScrollView {
                    VStack(spacing: 16) {
                        if vm.needsReAccept {
                            VStack(alignment: .leading, spacing: 8) { Text("EULA Update Required").font(.headline).foregroundColor(Color(red: 0.9, green: 0.4, blue: 0)); Text("v\(vm.acceptedVersion ?? "?") -> v\(vm.current?.version ?? "?")").font(.caption).foregroundColor(.secondary) }.padding(16).frame(maxWidth: .infinity, alignment: .leading).background(Color(red: 1, green: 0.95, blue: 0.88)).cornerRadius(12).padding(.horizontal, 16)

                            VStack(alignment: .leading, spacing: 8) {
                                Text("What Changed").font(.subheadline).fontWeight(.bold)
                                ForEach(vm.diffs, id: \.self) { d in HStack(alignment: .top, spacing: 8) { Text("+").foregroundColor(.green).fontWeight(.bold); Text(d).font(.caption).foregroundColor(.secondary) } }
                            }.padding(16).background(Color.white).cornerRadius(8).padding(.horizontal, 16)
                        }

                        VStack(alignment: .leading, spacing: 8) { Text("Current EULA (v\(vm.current?.version ?? "..."))").font(.subheadline).fontWeight(.bold); ScrollView { Text(vm.current?.content ?? "Loading...").font(.caption2).foregroundColor(.secondary).lineSpacing(4) }.frame(height: 200) }.padding(16).background(Color.white).cornerRadius(8).padding(.horizontal, 16)

                        if let e = vm.error { Text(e).font(.caption).foregroundColor(.red).padding(12).background(Color(red: 1, green: 0.93, blue: 0.93)).cornerRadius(8).padding(.horizontal, 16) }

                        if vm.needsReAccept {
                            Button(action: { vm.accept() }) { Text(vm.isAccepting ? "Accepting..." : "I Accept the Updated EULA").frame(maxWidth: .infinity).padding(12).background(Color.green).foregroundColor(.white).cornerRadius(8) }.disabled(vm.isAccepting).padding(.horizontal, 16)
                            Button(action: { vm.decline() }) { Text("Decline").frame(maxWidth: .infinity).padding(12).overlay(RoundedRectangle(cornerRadius: 8).stroke(Color.red, lineWidth: 1)).foregroundColor(.red) }.padding(.horizontal, 16)
                        }

                        VStack(alignment: .leading, spacing: 8) {
                            Text("Acceptance History").font(.subheadline).fontWeight(.bold)
                            ForEach(vm.history) { a in HStack { VStack(alignment: .leading) { Text("Version \(a.version)").font(.subheadline).fontWeight(.semibold); Text(a.acceptedAt).font(.caption).foregroundColor(.gray) }; Spacer(); Image(systemName: "checkmark.circle.fill").foregroundColor(.green) }.padding(12).background(Color(white: 0.96)).cornerRadius(8) }
                        }.padding(16).background(Color.white).cornerRadius(8).padding(.horizontal, 16)
                        Spacer().frame(height: 20)
                    }.padding(.vertical, 16)
                }
            }
        }
    }
}
