/**
 * WRITE-AHEAD LOG | File: AccountDeletionView.swift | Purpose: GDPR Art.17 account deletion UI
 * Created: 2026-03-24 | Author: Claude | Deps: SwiftUI, AccountDeletionService
 * Usage: NavigationLink("Delete Account") { AccountDeletionView() }
 */
import SwiftUI

struct AccountDeletionView: View {
    @Environment(\.dismiss) var dismiss
    @State private var service = MockAccountDeletionService()
    @State private var step = 0
    @State private var confirmText = ""
    @State private var reason = ""
    @State private var isProcessing = false
    @State private var request: GDPRDeletionRequest? = nil
    @State private var ackDataLoss = false
    @State private var errorMsg: String? = nil

    var body: some View {
        ZStack {
            Color(red: 0.97, green: 0.97, blue: 0.97).ignoresSafeArea()
            VStack(spacing: 0) {
                HStack { Button(action: { dismiss() }) { HStack(spacing: 4) { Image(systemName: "chevron.left"); Text("Back") }.foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27)) }; Spacer(); Text("Delete Account").font(.headline).fontWeight(.bold); Spacer() }.padding(16).background(Color.white)
                Divider()
                ScrollView {
                    VStack(spacing: 16) {
                        if let req = request { existingRequestView(req) }
                        else {
                            // Warning
                            VStack(alignment: .leading, spacing: 8) { HStack { Image(systemName: "exclamationmark.triangle.fill").foregroundColor(.red); Text("This is irreversible").font(.headline).foregroundColor(.red) }; Text("After 30-day grace period, ALL data permanently deleted.").font(.caption).foregroundColor(.secondary) }.padding(16).background(Color(red: 1, green: 0.93, blue: 0.93)).cornerRadius(12).padding(.horizontal, 16)

                            if step >= 0 { VStack(alignment: .leading, spacing: 8) { Text("Step 1: Export Data First").font(.subheadline).fontWeight(.bold); Text("We recommend exporting your data first.").font(.caption).foregroundColor(.secondary) }.padding(16).background(Color.white).cornerRadius(8).padding(.horizontal, 16) }
                            if step >= 1 { VStack(alignment: .leading, spacing: 12) { Text("Step 2: Acknowledge Data Loss").font(.subheadline).fontWeight(.bold); Toggle("I understand this data will be permanently lost", isOn: $ackDataLoss).font(.caption).fontWeight(.semibold) }.padding(16).background(Color.white).cornerRadius(8).padding(.horizontal, 16) }
                            if step >= 2 { VStack(alignment: .leading, spacing: 8) { Text("Step 3: Type DELETE").font(.subheadline).fontWeight(.bold); TextField("Type DELETE", text: $confirmText).textFieldStyle(.roundedBorder) }.padding(16).background(Color.white).cornerRadius(8).padding(.horizontal, 16) }
                            if step >= 3 { VStack(alignment: .leading, spacing: 8) { Text("Step 4: Reason (Optional)").font(.subheadline).fontWeight(.bold); TextField("Why are you leaving?", text: $reason, axis: .vertical).textFieldStyle(.roundedBorder).lineLimit(3...5) }.padding(16).background(Color.white).cornerRadius(8).padding(.horizontal, 16) }

                            HStack(spacing: 8) { ForEach(0..<4, id: \.self) { i in Circle().fill(i <= step ? Color(red: 0.9, green: 0.22, blue: 0.27) : Color.gray.opacity(0.3)).frame(width: 10, height: 10) } }.padding(.top, 8)

                            if step < 3 { Button(action: { withAnimation { step += 1 } }) { Text("Continue").frame(maxWidth: .infinity).padding(12).background(canProceed ? Color(red: 0.9, green: 0.22, blue: 0.27) : Color.gray).foregroundColor(.white).cornerRadius(8) }.disabled(!canProceed).padding(.horizontal, 16) }
                            else { Button(action: submitDeletion) { Text(isProcessing ? "Processing..." : "Permanently Delete My Account").frame(maxWidth: .infinity).padding(12).background(confirmText == "DELETE" ? Color.red : Color.gray).foregroundColor(.white).cornerRadius(8) }.disabled(confirmText != "DELETE" || isProcessing).padding(.horizontal, 16) }
                        }
                        if let e = errorMsg { Text(e).font(.caption).foregroundColor(.red).padding(.horizontal, 16) }
                        Spacer().frame(height: 20)
                    }.padding(.vertical, 16)
                }
            }
        }.task { request = await service.getDeletionStatus(userId: "user-001") }
    }

    private var canProceed: Bool { switch step { case 1: return ackDataLoss; case 2: return confirmText == "DELETE"; default: return true } }

    @ViewBuilder private func existingRequestView(_ req: GDPRDeletionRequest) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack { Image(systemName: "clock.fill").foregroundColor(.orange); Text("Deletion Scheduled").font(.headline).foregroundColor(.orange) }
            Text("Scheduled in \(req.daysRemaining) days.").font(.subheadline)
            Button(action: cancelDeletion) { Text(isProcessing ? "Cancelling..." : "Cancel Deletion").frame(maxWidth: .infinity).padding(12).background(Color.green).foregroundColor(.white).cornerRadius(8) }.disabled(isProcessing)
        }.padding(16).background(Color.white).cornerRadius(12).padding(.horizontal, 16)
    }

    private func submitDeletion() { isProcessing = true; Task { do { request = try await service.requestDeletion(userId: "user-001", reason: reason.isEmpty ? nil : reason) } catch { errorMsg = error.localizedDescription }; isProcessing = false } }
    private func cancelDeletion() { isProcessing = true; Task { do { try await service.cancelDeletion(userId: "user-001"); request = nil } catch { errorMsg = error.localizedDescription }; isProcessing = false } }
}
