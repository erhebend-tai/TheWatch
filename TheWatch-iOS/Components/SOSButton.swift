import SwiftUI

struct SOSButton: View {
    let viewModel: HomeViewModel
    @State private var isCountingDown = false
    @State private var countdown = 3
    @State private var showFeedback = false

    var body: some View {
        VStack(spacing: 16) {
            if isCountingDown {
                ZStack {
                    Circle()
                        .stroke(Color(red: 0.9, green: 0.22, blue: 0.27).opacity(0.2), lineWidth: 2)
                        .frame(width: 140, height: 140)

                    Circle()
                        .trim(from: 0, to: CGFloat(max(0, 3 - countdown)) / 3)
                        .stroke(Color(red: 0.9, green: 0.22, blue: 0.27), style: StrokeStyle(lineWidth: 2, lineCap: .round))
                        .frame(width: 140, height: 140)
                        .rotationEffect(.degrees(-90))
                        .animation(.linear, value: countdown)

                    VStack(spacing: 8) {
                        Text("\(countdown)")
                            .font(.system(size: 40, weight: .bold, design: .default))
                            .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                        Text("Release to cancel")
                            .font(.caption2)
                            .foregroundColor(.gray)
                    }
                }
                .frame(width: 140, height: 140)
                .onReceive(Timer.publish(every: 1).autoconnect()) { _ in
                    if isCountingDown {
                        countdown -= 1
                        triggerHaptic()
                        if countdown < 0 {
                            triggerSOSActivation()
                            isCountingDown = false
                        }
                    }
                }
            } else {
                Button(action: {
                    isCountingDown = true
                    countdown = 3
                    triggerHaptic()
                }) {
                    VStack(spacing: 8) {
                        ZStack {
                            Circle()
                                .fill(Color(red: 0.9, green: 0.22, blue: 0.27))
                                .frame(width: 140, height: 140)
                                .shadow(color: Color(red: 0.9, green: 0.22, blue: 0.27).opacity(0.4), radius: 8)

                            VStack(spacing: 8) {
                                Image(systemName: "exclamationmark")
                                    .font(.system(size: 40, weight: .bold))
                                    .foregroundColor(.white)
                                Text("SOS")
                                    .font(.headline)
                                    .fontWeight(.bold)
                                    .foregroundColor(.white)
                            }
                        }
                        .frame(width: 140, height: 140)

                        Text("Hold to activate")
                            .font(.caption2)
                            .foregroundColor(.gray)
                    }
                }
                .accessibilityLabel("SOS button")
                .accessibilityValue("Press and hold to activate emergency response")
            }

            if showFeedback {
                HStack(spacing: 8) {
                    Image(systemName: "checkmark.circle.fill")
                        .foregroundColor(.green)
                    Text("Emergency response activated")
                        .font(.caption)
                        .foregroundColor(.green)
                }
                .padding(8)
                .background(Color.green.opacity(0.1))
                .cornerRadius(6)
                .transition(.opacity)
            }
        }
    }

    private func triggerHaptic() {
        let impact = UIImpactFeedbackGenerator(style: .medium)
        impact.impactOccurred()
    }

    private func triggerSOSActivation() {
        showFeedback = true
        let impact = UIImpactFeedbackGenerator(style: .heavy)
        impact.impactOccurred()

        Task {
            await viewModel.triggerSOS()
        }

        DispatchQueue.main.asyncAfter(deadline: .now() + 2) {
            showFeedback = false
        }
    }
}

#Preview {
    SOSButton(viewModel: HomeViewModel(alertService: MockAlertService(), volunteerService: MockVolunteerService()))
}
