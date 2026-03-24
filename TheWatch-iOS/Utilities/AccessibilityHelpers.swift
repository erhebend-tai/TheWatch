/**
 * WRITE-AHEAD LOG | File: AccessibilityHelpers.swift | Purpose: WCAG 2.1 AA compliance helpers
 * Created: 2026-03-24 | Author: Claude | Deps: SwiftUI, UIKit
 * Usage: Button("SOS") { }.minimumTouchTarget().accessibilityActionLabel("Trigger SOS")
 * WCAG: 1.4.3 Contrast, 1.4.4 Dynamic Type, 1.4.11 Non-text, 2.4.6 Headings, 2.4.7 Focus, 2.5.5 Target (44pt), 4.1.2 Name/Role
 */
import SwiftUI

// ── Touch Target (WCAG 2.5.5: 44pt minimum) ──
let kMinTouchTarget: CGFloat = 44.0

extension View {
    func minimumTouchTarget() -> some View { self.frame(minWidth: kMinTouchTarget, minHeight: kMinTouchTarget) }
    func touchTargetSpacing() -> some View { self.padding(4) }
}

// ── VoiceOver Semantics (WCAG 4.1.2, 2.4.6) ──
extension View {
    func accessibilityHeading() -> some View { self.accessibilityAddTraits(.isHeader) }
    func accessibilityActionLabel(_ label: String) -> some View { self.accessibilityLabel(label).accessibilityAddTraits(.isButton) }
    func accessibilityToggleState(_ isOn: Bool, label: String) -> some View { self.accessibilityLabel(label).accessibilityValue(isOn ? "Enabled" : "Disabled").accessibilityAddTraits(.isButton) }
    func accessibilityImageLabel(_ alt: String) -> some View { self.accessibilityLabel(alt).accessibilityAddTraits(.isImage) }
    func accessibilityDecorative() -> some View { self.accessibilityHidden(true) }
}

// ── Contrast (WCAG 1.4.3, 1.4.11) ──
func wcagRelativeLuminance(r: Double, g: Double, b: Double) -> Double {
    func lin(_ c: Double) -> Double { c <= 0.03928 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4) }
    return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b)
}
func wcagContrastRatio(fgR: Double, fgG: Double, fgB: Double, bgR: Double, bgG: Double, bgB: Double) -> Double {
    let l1 = wcagRelativeLuminance(r: fgR, g: fgG, b: fgB); let l2 = wcagRelativeLuminance(r: bgR, g: bgG, b: bgB)
    return (max(l1, l2) + 0.05) / (min(l1, l2) + 0.05)
}
func wcagMeetsAANormalText(fgR: Double, fgG: Double, fgB: Double, bgR: Double, bgG: Double, bgB: Double) -> Bool { wcagContrastRatio(fgR: fgR, fgG: fgG, fgB: fgB, bgR: bgR, bgG: bgG, bgB: bgB) >= 4.5 }
func wcagMeetsAALargeText(fgR: Double, fgG: Double, fgB: Double, bgR: Double, bgG: Double, bgB: Double) -> Bool { wcagContrastRatio(fgR: fgR, fgG: fgG, fgB: fgB, bgR: bgR, bgG: bgG, bgB: bgB) >= 3.0 }

// ── High Contrast Colors ──
enum WCAGHighContrastColors {
    static let background = Color.black; static let surface = Color(red: 0.1, green: 0.1, blue: 0.1); static let onBackground = Color.white; static let onSurface = Color.white
    static let primary = Color(red: 1, green: 0.42, blue: 0.42); static let onPrimary = Color.black; static let error = Color(red: 1, green: 0.27, blue: 0.27)
    static let warning = Color.yellow; static let success = Color.green; static let border = Color.white
}

// ── Dynamic Type (WCAG 1.4.4) ──
var wcagIsAccessibilityTextSize: Bool { UIApplication.shared.preferredContentSizeCategory.isAccessibilityCategory }

// ── Focus Indicator (WCAG 2.4.7) ──
enum WCAGFocusIndicator { static let strokeWidth: CGFloat = 3; static let cornerRadius: CGFloat = 8; static let color = Color.blue; static let highContrastColor = Color.yellow; static let offset: CGFloat = 2 }

extension View {
    @ViewBuilder func wcagEnhancedFocus(isFocused: Bool, highContrast: Bool = false) -> some View {
        if isFocused { self.overlay(RoundedRectangle(cornerRadius: WCAGFocusIndicator.cornerRadius).stroke(highContrast ? WCAGFocusIndicator.highContrastColor : WCAGFocusIndicator.color, lineWidth: WCAGFocusIndicator.strokeWidth).padding(-WCAGFocusIndicator.offset)) } else { self }
    }
    func wcagSafeAnimation<V: Equatable>(_ animation: Animation?, value: V) -> some View {
        self.animation(UIAccessibility.isReduceMotionEnabled ? nil : animation, value: value)
    }
}
