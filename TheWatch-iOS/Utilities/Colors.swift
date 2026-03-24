import SwiftUI

extension Color {
    static let primaryRed = Color(red: 0.9, green: 0.22, blue: 0.27)
    static let primaryNavy = Color(red: 0.0, green: 0.15, blue: 0.35)
    static let lightGray = Color(red: 0.97, green: 0.97, blue: 0.97)
    static let darkGray = Color(red: 0.5, green: 0.5, blue: 0.5)
    
    // Semantic colors for status
    static let statusSafe = Color.green
    static let statusCaution = Color.orange
    static let statusDanger = Color.red
    static let statusWarning = Color(red: 1.0, green: 0.7, blue: 0.0)
    
    // Alert type colors
    static let alertMedical = Color.red
    static let alertSecurity = Color.orange
    static let alertWildfire = Color(red: 1.0, green: 0.5, blue: 0.0)
    static let alertFlood = Color.blue
}
