import SwiftUI
import MapKit

struct ProximityRingOverlay: View {
    let activeAlert: HistoryEvent?
    let position: MapCameraPosition
    
    var body: some View {
        VStack(spacing: 12) {
            if let alert = activeAlert {
                VStack(alignment: .leading, spacing: 8) {
                    HStack {
                        VStack(alignment: .leading, spacing: 2) {
                            Text("Response Zones")
                                .font(.subheadline)
                                .fontWeight(.semibold)
                            Text(alert.eventType)
                                .font(.caption)
                                .foregroundColor(.gray)
                        }
                        
                        Spacer()
                    }
                    
                    VStack(spacing: 6) {
                        RingIndicator(
                            title: "Critical",
                            distance: "500m",
                            color: Color.red,
                            isActive: true
                        )
                        
                        RingIndicator(
                            title: "Primary",
                            distance: "1km",
                            color: Color.orange,
                            isActive: true
                        )
                        
                        RingIndicator(
                            title: "Secondary",
                            distance: "2km",
                            color: Color.yellow,
                            isActive: true
                        )
                    }
                }
                .padding(12)
                .background(Color.white)
                .cornerRadius(8)
                .shadow(radius: 4)
                .padding(12)
            }
            
            Spacer()
        }
    }
}

struct RingIndicator: View {
    let title: String
    let distance: String
    let color: Color
    let isActive: Bool
    
    var body: some View {
        HStack(spacing: 12) {
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                    .font(.caption)
                    .fontWeight(.semibold)
                Text(distance)
                    .font(.caption2)
                    .foregroundColor(.gray)
            }
            
            Spacer()
            
            Circle()
                .fill(color.opacity(0.3))
                .frame(width: 8, height: 8)
                .overlay(
                    Circle()
                        .stroke(color, lineWidth: 1)
                )
        }
        .padding(8)
        .background(color.opacity(0.05))
        .cornerRadius(6)
    }
}

#Preview {
    ProximityRingOverlay(
        activeAlert: HistoryEvent(
            id: UUID().uuidString,
            userId: "user-001",
            eventType: "SOS",
            severity: .critical,
            status: .active,
            latitude: 40.7128,
            longitude: -74.0060,
            description: "Test alert",
            respondersCount: 3,
            createdAt: Date(),
            duration: 120
        ),
        position: MapCameraPosition.automatic
    )
}
