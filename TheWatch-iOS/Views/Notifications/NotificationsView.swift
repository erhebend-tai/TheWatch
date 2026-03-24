import SwiftUI

struct NotificationsView: View {
    @State private var notifications: [Notification] = [
        Notification(
            id: UUID(),
            title: "Alert Response",
            message: "3 responders are heading to your location",
            type: .alert,
            timestamp: Date().addingTimeInterval(-300),
            isRead: false
        ),
        Notification(
            id: UUID(),
            title: "Responder Nearby",
            message: "Alex Johnson is 250m away and responding",
            type: .responder,
            timestamp: Date().addingTimeInterval(-1800),
            isRead: false
        ),
        Notification(
            id: UUID(),
            title: "Emergency Resolved",
            message: "Your SOS alert has been marked as resolved",
            type: .status,
            timestamp: Date().addingTimeInterval(-3600),
            isRead: true
        ),
        Notification(
            id: UUID(),
            title: "App Update Available",
            message: "Version 2.1.0 is ready to download",
            type: .system,
            timestamp: Date().addingTimeInterval(-86400),
            isRead: true
        )
    ]
    
    var body: some View {
        ZStack {
            Color(red: 0.97, green: 0.97, blue: 0.97)
                .ignoresSafeArea()
            
            VStack(spacing: 0) {
                // Header
                HStack {
                    Text("Notifications")
                        .font(.title2)
                        .fontWeight(.bold)
                    
                    Spacer()
                    
                    if notifications.contains(where: { !$0.isRead }) {
                        Button(action: { markAllAsRead() }) {
                            Text("Mark all read")
                                .font(.caption)
                                .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                        }
                        .accessibilityLabel("Mark all notifications as read")
                    }
                }
                .padding(16)
                .background(Color.white)
                
                Divider()
                
                if notifications.isEmpty {
                    VStack(spacing: 12) {
                        Image(systemName: "bell.slash.fill")
                            .font(.system(size: 32))
                            .foregroundColor(.gray)
                        
                        Text("No Notifications")
                            .font(.headline)
                        
                        Text("You're all caught up")
                            .font(.caption)
                            .foregroundColor(.gray)
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .background(Color(red: 0.97, green: 0.97, blue: 0.97))
                } else {
                    ScrollView {
                        VStack(spacing: 0) {
                            ForEach(notifications) { notification in
                                NotificationRow(
                                    notification: notification,
                                    onTap: { selectNotification(notification) },
                                    onDelete: { deleteNotification(notification) }
                                )
                                
                                Divider()
                                    .padding(.horizontal, 16)
                            }
                        }
                    }
                }
            }
        }
    }
    
    private func selectNotification(_ notification: Notification) {
        if let index = notifications.firstIndex(where: { $0.id == notification.id }) {
            notifications[index].isRead = true
        }
    }
    
    private func markAllAsRead() {
        for index in notifications.indices {
            notifications[index].isRead = true
        }
    }
    
    private func deleteNotification(_ notification: Notification) {
        notifications.removeAll { $0.id == notification.id }
    }
}

struct NotificationRow: View {
    let notification: Notification
    let onTap: () -> Void
    let onDelete: () -> Void
    
    var body: some View {
        HStack(spacing: 12) {
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    HStack(spacing: 8) {
                        Image(systemName: notification.type.icon)
                            .font(.caption)
                            .foregroundColor(notification.type.color)
                        
                        Text(notification.title)
                            .font(.subheadline)
                            .fontWeight(.semibold)
                            .foregroundColor(.black)
                    }
                    
                    Spacer()
                    
                    if !notification.isRead {
                        Circle()
                            .fill(Color(red: 0.9, green: 0.22, blue: 0.27))
                            .frame(width: 8, height: 8)
                    }
                }
                
                Text(notification.message)
                    .font(.caption)
                    .foregroundColor(.gray)
                    .lineLimit(2)
                
                Text(timeAgo(notification.timestamp))
                    .font(.caption2)
                    .foregroundColor(.gray)
            }
            
            Spacer()
            
            Menu {
                Button(role: .destructive, action: onDelete) {
                    Label("Delete", systemImage: "trash")
                }
            } label: {
                Image(systemName: "ellipsis")
                    .foregroundColor(.gray)
            }
            .accessibilityLabel("More options for notification")
        }
        .padding(12)
        .background(notification.isRead ? Color.white : Color(red: 0.9, green: 0.22, blue: 0.27).opacity(0.05))
        .onTapGesture {
            onTap()
        }
    }
    
    private func timeAgo(_ date: Date) -> String {
        let components = Calendar.current.dateComponents([.minute, .hour, .day], from: date, to: Date())
        
        if let day = components.day, day > 0 {
            return "\(day)d ago"
        } else if let hour = components.hour, hour > 0 {
            return "\(hour)h ago"
        } else if let minute = components.minute, minute > 0 {
            return "\(minute)m ago"
        } else {
            return "Just now"
        }
    }
}

struct Notification: Identifiable {
    enum NotificationType {
        case alert, responder, status, system
        
        var icon: String {
            switch self {
            case .alert:
                return "exclamationmark.circle.fill"
            case .responder:
                return "person.fill"
            case .status:
                return "checkmark.circle.fill"
            case .system:
                return "gear"
            }
        }
        
        var color: Color {
            switch self {
            case .alert:
                return .red
            case .responder:
                return .blue
            case .status:
                return .green
            case .system:
                return .gray
            }
        }
    }
    
    let id: UUID
    let title: String
    let message: String
    let type: NotificationType
    let timestamp: Date
    var isRead: Bool
}

#Preview {
    NotificationsView()
}
