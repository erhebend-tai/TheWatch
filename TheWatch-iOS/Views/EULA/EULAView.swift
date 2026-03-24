import SwiftUI

struct EULAView: View {
    @State private var hasScrolledToBottom = false
    @State private var userAccepted = false
    @Environment(\.dismiss) var dismiss

    var body: some View {
        ZStack {
            Color(red: 0.97, green: 0.97, blue: 0.97)
                .ignoresSafeArea()

            VStack(spacing: 0) {
                // Header
                HStack {
                    Text("End User License Agreement")
                        .font(.headline)
                        .fontWeight(.bold)
                    Spacer()
                    Button(action: { dismiss() }) {
                        Image(systemName: "xmark.circle.fill")
                            .foregroundColor(.gray)
                    }
                    .accessibilityLabel("Close EULA")
                }
                .padding(16)
                .background(Color.white)

                Divider()

                // Content with scroll tracking
                ScrollViewReader { reader in
                    ScrollView {
                        VStack(alignment: .leading, spacing: 16) {
                            Text(eulaContent)
                                .font(.body)
                                .lineSpacing(4)

                            // Hidden marker for bottom detection
                            Color.clear
                                .frame(height: 1)
                                .id("bottom")
                                .onAppear {
                                    // Content is visible when marker appears
                                }
                        }
                        .padding(16)
                        .onScrollGeometryChange(
                            for: CGFloat.self,
                            of: { geo in
                                geo.contentOffset.y + geo.contentSize.height - geo.frame.height
                            },
                            action: { _, newValue in
                                if newValue <= 10 {
                                    hasScrolledToBottom = true
                                }
                            }
                        )
                    }
                }

                Divider()

                // Acceptance controls (sticky bottom)
                VStack(spacing: 12) {
                    Toggle("I have read and accept the EULA", isOn: $userAccepted)
                        .padding(12)
                        .accessibilityLabel("Accept EULA")
                        .disabled(!hasScrolledToBottom)

                    HStack(spacing: 12) {
                        Button(action: { dismiss() }) {
                            Text("Decline")
                                .frame(maxWidth: .infinity)
                                .padding(12)
                                .background(Color.gray.opacity(0.1))
                                .foregroundColor(.black)
                                .cornerRadius(8)
                        }
                        .accessibilityLabel("Decline EULA")

                        Button(action: { dismiss() }) {
                            Text("Accept")
                                .frame(maxWidth: .infinity)
                                .padding(12)
                                .background(Color(red: 0.9, green: 0.22, blue: 0.27))
                                .foregroundColor(.white)
                                .cornerRadius(8)
                        }
                        .disabled(!userAccepted || !hasScrolledToBottom)
                        .opacity(userAccepted && hasScrolledToBottom ? 1.0 : 0.6)
                        .accessibilityLabel("Accept and continue")
                    }
                }
                .padding(16)
                .background(Color.white)

                // Hint
                if !hasScrolledToBottom {
                    Text("Please scroll to the bottom to accept")
                        .font(.caption)
                        .foregroundColor(.gray)
                        .padding(8)
                }
            }
        }
    }

    private let eulaContent = """
    THEWATCH END USER LICENSE AGREEMENT

    Last Updated: March 24, 2026

    IMPORTANT: READ THIS AGREEMENT CAREFULLY BEFORE USING THE APPLICATION.

    1. GRANT OF LICENSE
    TheWatch Inc. grants you a non-exclusive, non-transferable license to use this application solely for personal, non-commercial purposes, subject to the terms and conditions of this agreement.

    2. RESTRICTIONS
    You may not:
    - Copy, modify, or create derivative works
    - Reverse engineer, decompile, or disassemble
    - Rent, lease, or lend the application
    - Use the application for any unlawful purpose
    - Interfere with or disrupt the service

    3. USER RESPONSIBILITIES
    You agree to:
    - Provide accurate personal information
    - Maintain the confidentiality of your account credentials
    - Notify us immediately of unauthorized access
    - Use this service responsibly and legally
    - Comply with all applicable laws and regulations

    4. EMERGENCY SERVICES DISCLOSURE
    TheWatch is a supplementary emergency response service. It is not a substitute for traditional emergency services. Always call 911 for life-threatening emergencies.

    5. DISCLAIMER OF WARRANTIES
    THE APPLICATION IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, AND NONINFRINGEMENT.

    6. LIMITATION OF LIABILITY
    IN NO EVENT SHALL THEWATCH INC. BE LIABLE FOR:
    - Indirect, incidental, special, consequential, or punitive damages
    - Loss of data, revenue, or business opportunity
    - Delay or failure in emergency response
    - Any damages arising from your use or inability to use the application

    7. PRIVACY
    Your use of TheWatch is subject to our Privacy Policy. We collect and process personal data as described in that policy.

    8. INTELLECTUAL PROPERTY
    All content, features, and functionality of TheWatch are owned by TheWatch Inc., protected by copyright, trademark, and other intellectual property laws.

    9. TERMINATION
    We may terminate or suspend your account immediately for violation of this agreement or any law.

    10. CHANGES TO THIS AGREEMENT
    We reserve the right to modify this agreement at any time. Continued use constitutes acceptance of changes.

    11. ENTIRE AGREEMENT
    This agreement constitutes the entire agreement between you and TheWatch Inc. regarding the application.

    12. CONTACT
    For questions about this agreement, contact: legal@thewatch.app

    By accepting this agreement, you acknowledge that you have read, understood, and agree to be bound by all terms and conditions.
    """
}

#Preview {
    EULAView()
}
