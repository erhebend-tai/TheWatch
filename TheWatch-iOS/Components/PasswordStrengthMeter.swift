import SwiftUI

enum PasswordStrength: CaseIterable {
    case weak
    case moderate
    case strong
    
    var color: Color {
        switch self {
        case .weak:
            return Color.red
        case .moderate:
            return Color(red: 1.0, green: 0.7, blue: 0.0)
        case .strong:
            return Color.green
        }
    }
    
    var text: String {
        switch self {
        case .weak:
            return "Weak"
        case .moderate:
            return "Moderate"
        case .strong:
            return "Strong"
        }
    }
    
    var progress: Double {
        switch self {
        case .weak:
            return 0.33
        case .moderate:
            return 0.66
        case .strong:
            return 1.0
        }
    }
}

struct PasswordStrengthMeter: View {
    let strength: PasswordStrength
    
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                Text("Password Strength")
                    .font(.subheadline)
                    .fontWeight(.semibold)
                
                Spacer()
                
                Text(strength.text)
                    .font(.caption)
                    .fontWeight(.semibold)
                    .foregroundColor(strength.color)
            }
            
            GeometryReader { geometry in
                ZStack(alignment: .leading) {
                    RoundedRectangle(cornerRadius: 4)
                        .fill(Color.gray.opacity(0.2))
                    
                    RoundedRectangle(cornerRadius: 4)
                        .fill(strength.color)
                        .frame(width: geometry.size.width * strength.progress)
                }
            }
            .frame(height: 6)
            
            HStack(spacing: 12) {
                RequirementBadge(text: "8+ characters", met: true)
                RequirementBadge(text: "Mixed case", met: true)
                RequirementBadge(text: "Number or symbol", met: true)
            }
            .font(.caption2)
        }
        .padding(12)
        .background(Color.white)
        .cornerRadius(8)
    }
}

struct RequirementBadge: View {
    let text: String
    let met: Bool
    
    var body: some View {
        HStack(spacing: 4) {
            Image(systemName: met ? "checkmark.circle.fill" : "circle")
                .font(.caption2)
                .foregroundColor(met ? .green : .gray)
            Text(text)
        }
        .padding(.horizontal, 6)
        .padding(.vertical, 3)
        .background(met ? Color.green.opacity(0.1) : Color.gray.opacity(0.05))
        .cornerRadius(4)
    }
}

#Preview {
    VStack(spacing: 16) {
        PasswordStrengthMeter(strength: .weak)
        PasswordStrengthMeter(strength: .moderate)
        PasswordStrengthMeter(strength: .strong)
    }
    .padding()
}
