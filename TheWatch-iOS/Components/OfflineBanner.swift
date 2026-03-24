import SwiftUI

struct OfflineBanner: View {
    let isOnline: Bool
    
    var body: some View {
        if !isOnline {
            VStack(spacing: 8) {
                HStack(spacing: 8) {
                    Image(systemName: "wifi.slash")
                        .font(.caption)
                    
                    Text("You're offline")
                        .font(.caption)
                        .fontWeight(.semibold)
                    
                    Spacer()
                    
                    Text("Changes will sync when online")
                        .font(.caption2)
                        .foregroundColor(.gray)
                }
                .padding(12)
                .background(Color(red: 1.0, green: 0.9, blue: 0.0).opacity(0.1))
                .cornerRadius(8)
            }
            .padding(12)
            .transition(.move(edge: .top).combined(with: .opacity))
        }
    }
}

#Preview {
    VStack(spacing: 16) {
        OfflineBanner(isOnline: false)
        OfflineBanner(isOnline: true)
    }
}
