import SwiftUI

struct HealthView: View {
    @State private var heartRate: Int = 72
    @State private var bloodPressure = "120/80"
    @State private var oxygenLevel: Int = 98
    @State private var lastUpdated = Date()
    @State private var showHealthDetails = false
    
    var body: some View {
        ZStack {
            Color(red: 0.97, green: 0.97, blue: 0.97)
                .ignoresSafeArea()
            
            VStack(spacing: 0) {
                // Header
                HStack {
                    Text("Health Metrics")
                        .font(.title2)
                        .fontWeight(.bold)
                    
                    Spacer()
                    
                    Menu {
                        Button(action: { showHealthDetails.toggle() }) {
                            Label("View Details", systemImage: "list.bullet")
                        }
                    } label: {
                        Image(systemName: "ellipsis")
                            .foregroundColor(.black)
                    }
                    .accessibilityLabel("Health menu")
                }
                .padding(16)
                .background(Color.white)
                
                Divider()
                
                ScrollView {
                    VStack(spacing: 16) {
                        // Last updated
                        HStack {
                            Text("Last updated: \(lastUpdated.formatted(date: .abbreviated, time: .shortened))")
                                .font(.caption)
                                .foregroundColor(.gray)
                            
                            Spacer()
                            
                            Button(action: { updateMetrics() }) {
                                Text("Refresh")
                                    .font(.caption)
                                    .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                            }
                            .accessibilityLabel("Refresh health metrics")
                        }
                        .padding(.horizontal, 16)
                        
                        // Vital Signs Grid
                        VStack(spacing: 12) {
                            HStack(spacing: 12) {
                                HealthMetricCard(
                                    title: "Heart Rate",
                                    value: "\(heartRate)",
                                    unit: "bpm",
                                    icon: "heart.fill",
                                    color: Color.red,
                                    status: heartRate > 100 ? "elevated" : "normal"
                                )
                                
                                HealthMetricCard(
                                    title: "Blood Pressure",
                                    value: bloodPressure,
                                    unit: "mmHg",
                                    icon: "bolt.fill",
                                    color: Color.orange,
                                    status: "normal"
                                )
                            }
                            
                            HStack(spacing: 12) {
                                HealthMetricCard(
                                    title: "Oxygen Level",
                                    value: "\(oxygenLevel)",
                                    unit: "%",
                                    icon: "lungs.fill",
                                    color: Color.blue,
                                    status: oxygenLevel >= 95 ? "good" : "low"
                                )
                                
                                HealthMetricCard(
                                    title: "Temperature",
                                    value: "98.6",
                                    unit: "°F",
                                    icon: "thermometer",
                                    color: Color.green,
                                    status: "normal"
                                )
                            }
                        }
                        .padding(.horizontal, 16)
                        
                        // Health Alerts
                        VStack(spacing: 12) {
                            Text("Health Alerts")
                                .font(.subheadline)
                                .fontWeight(.semibold)
                                .frame(maxWidth: .infinity, alignment: .leading)
                            
                            VStack(spacing: 8) {
                                HStack(spacing: 8) {
                                    Image(systemName: "checkmark.circle.fill")
                                        .foregroundColor(.green)
                                    Text("All vital signs within normal range")
                                        .font(.caption)
                                    Spacer()
                                }
                                .padding(12)
                                .background(Color.green.opacity(0.1))
                                .cornerRadius(8)
                            }
                        }
                        .padding(.horizontal, 16)
                        
                        // Connected Devices
                        VStack(spacing: 12) {
                            Text("Connected Wearables")
                                .font(.subheadline)
                                .fontWeight(.semibold)
                                .frame(maxWidth: .infinity, alignment: .leading)
                            
                            VStack(spacing: 8) {
                                DeviceRow(
                                    name: "Apple Watch Series 8",
                                    status: "Connected",
                                    lastSync: "2 minutes ago",
                                    isConnected: true
                                )
                                
                                DeviceRow(
                                    name: "Fitbit Charge 6",
                                    status: "Disconnected",
                                    lastSync: "1 hour ago",
                                    isConnected: false
                                )
                            }
                        }
                        .padding(.horizontal, 16)
                        
                        Spacer()
                    }
                    .padding(.vertical, 16)
                }
            }
        }
        .sheet(isPresented: $showHealthDetails) {
            HealthDetailsView()
        }
    }
    
    private func updateMetrics() {
        heartRate = Int.random(in: 60...100)
        oxygenLevel = Int.random(in: 95...100)
        lastUpdated = Date()
    }
}

struct HealthMetricCard: View {
    let title: String
    let value: String
    let unit: String
    let icon: String
    let color: Color
    let status: String
    
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 8) {
                Image(systemName: icon)
                    .font(.title3)
                    .foregroundColor(color)
                
                Text(title)
                    .font(.caption)
                    .foregroundColor(.gray)
                
                Spacer()
                
                Text(status)
                    .font(.caption2)
                    .fontWeight(.semibold)
                    .foregroundColor(status == "normal" || status == "good" ? .green : .orange)
            }
            
            HStack(alignment: .baseline, spacing: 4) {
                Text(value)
                    .font(.title2)
                    .fontWeight(.bold)
                
                Text(unit)
                    .font(.caption)
                    .foregroundColor(.gray)
            }
        }
        .padding(12)
        .background(Color.white)
        .cornerRadius(8)
    }
}

struct DeviceRow: View {
    let name: String
    let status: String
    let lastSync: String
    let isConnected: Bool
    
    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: "applewatch")
                .font(.title3)
                .foregroundColor(.blue)
            
            VStack(alignment: .leading, spacing: 2) {
                Text(name)
                    .font(.caption)
                    .fontWeight(.semibold)
                
                HStack(spacing: 8) {
                    Circle()
                        .fill(isConnected ? Color.green : Color.gray)
                        .frame(width: 6, height: 6)
                    
                    Text(status)
                        .font(.caption2)
                        .foregroundColor(.gray)
                    
                    Text("• \(lastSync)")
                        .font(.caption2)
                        .foregroundColor(.gray)
                }
            }
            
            Spacer()
        }
        .padding(12)
        .background(Color.white)
        .cornerRadius(8)
    }
}

struct HealthDetailsView: View {
    @Environment(\.dismiss) var dismiss
    
    var body: some View {
        NavigationStack {
            ZStack {
                Color(red: 0.97, green: 0.97, blue: 0.97)
                    .ignoresSafeArea()
                
                VStack {
                    HStack {
                        Text("Health Details")
                            .font(.headline)
                        
                        Spacer()
                        
                        Button(action: { dismiss() }) {
                            Image(systemName: "xmark.circle.fill")
                                .foregroundColor(.gray)
                        }
                        .accessibilityLabel("Close health details")
                    }
                    .padding(16)
                    .background(Color.white)
                    
                    ScrollView {
                        VStack(spacing: 16) {
                            Text("Historical data and trends will appear here")
                                .font(.caption)
                                .foregroundColor(.gray)
                                .padding(16)
                        }
                    }
                }
            }
        }
    }
}

#Preview {
    HealthView()
}
