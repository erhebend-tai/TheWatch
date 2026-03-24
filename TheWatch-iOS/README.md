# TheWatch iOS - Life-Safety Emergency Response App

A fully scaffolded iOS native emergency response application built with SwiftUI and modern iOS 17 frameworks.

## Architecture Overview

- **Language**: Swift 5.9+
- **UI Framework**: SwiftUI
- **Navigation**: NavigationStack with NavigationPath (centralized via AppRouter)
- **State Management**: @Observable (Observation framework) for ViewModels
- **Local Storage**: SwiftData for offline queue and persistence
- **Maps**: MapKit with SwiftUI Map
- **Authentication**: Mock MSAL/Entra ID protocol with realistic mock implementation
- **Deployment Target**: iOS 17.0+

## Project Structure

```
TheWatch-iOS/
├── README.md (this file)
├── TheWatch.xcodeproj/ (created via Xcode)
└── TheWatch/
    ├── TheWatchApp.swift (entry point)
    ├── ContentView.swift (root view with auth routing)
    ├── Navigation/
    │   └── AppRouter.swift (centralized navigation)
    ├── Models/
    │   ├── User.swift
    │   ├── EmergencyContact.swift
    │   ├── Alert.swift
    │   ├── HistoryEvent.swift
    │   ├── Responder.swift
    │   ├── CommunityAlert.swift
    │   ├── EvacuationRoute.swift
    │   └── Shelter.swift
    ├── Services/
    │   ├── Protocols/
    │   │   ├── AuthService.swift
    │   │   ├── AlertService.swift
    │   │   ├── UserService.swift
    │   │   ├── HistoryService.swift
    │   │   └── VolunteerService.swift
    │   └── Mock/
    │       ├── MockAuthService.swift
    │       ├── MockAlertService.swift
    │       ├── MockUserService.swift
    │       ├── MockHistoryService.swift
    │       └── MockVolunteerService.swift
    ├── ViewModels/
    │   ├── LoginViewModel.swift
    │   ├── SignUpViewModel.swift
    │   ├── HomeViewModel.swift
    │   ├── ProfileViewModel.swift
    │   ├── HistoryViewModel.swift
    │   └── VolunteeringViewModel.swift
    ├── Views/
    │   ├── Login/
    │   │   └── LoginView.swift
    │   ├── SignUp/
    │   │   └── SignUpView.swift
    │   ├── ForgotPassword/
    │   │   └── ForgotPasswordView.swift
    │   ├── ResetPassword/
    │   │   └── ResetPasswordView.swift
    │   ├── EULA/
    │   │   └── EULAView.swift
    │   ├── Home/
    │   │   └── HomeView.swift
    │   ├── Profile/
    │   │   └── ProfileView.swift
    │   ├── Permissions/
    │   │   └── PermissionsView.swift
    │   ├── History/
    │   │   └── HistoryView.swift
    │   ├── Volunteering/
    │   │   └── VolunteeringView.swift
    │   ├── Contacts/
    │   │   └── ContactsView.swift
    │   ├── Settings/
    │   │   └── SettingsView.swift
    │   ├── Evacuation/
    │   │   └── EvacuationView.swift
    │   └── Components/
    │       ├── SOSButton.swift
    │       ├── NavigationDrawer.swift
    │       ├── OfflineBanner.swift
    │       ├── ProximityRingOverlay.swift
    │       └── PasswordStrengthMeter.swift
    ├── Persistence/
    │   ├── SyncLogEntry.swift
    │   └── PersistenceController.swift
    └── Resources/
        ├── Assets.xcassets/
        └── Info.plist
```

## Getting Started

1. **Create Xcode Project**:
   - Open Xcode, create a new iOS App project
   - Product Name: "TheWatch"
   - Team ID: your team
   - Organization: your org
   - Deployment Target: iOS 17.0
   - Interface: SwiftUI
   - Lifecycle: SwiftUI App

2. **Copy Files**:
   - Copy all Swift files from this scaffold into your Xcode project

3. **Update Info.plist**:
   - Add required permission descriptions:
     - NSLocationWhenInUseUsageDescription
     - NSLocationAlwaysAndWhenInUseUsageDescription
     - NSFaceIDUsageDescription
     - NSContactsUsageDescription

4. **Build & Run**:
   ```bash
   xcodebuild -scheme TheWatch -configuration Debug
   ```

## Key Features

### Authentication
- Email/phone dual-format login
- Biometric Face ID support
- Mock MSAL/Entra ID integration
- Forgot password flow with OTP
- Sign-up with 3-step wizard

### Emergency Response
- **SOS Button**: 80pt red circle with 3-second countdown
- **Map View**: Real-time responder and community alerts
- **Proximity Rings**: Visual distance indicators (red/orange/yellow/gray)
- **Auto-escalation**: Configurable severity and timing

### User Management
- Complete profile with medical info
- Emergency contact management (up to 3)
- Wearable device integration
- Duress code and personal clear word

### Community Features
- Volunteering/responder enrollment
- Skill management and availability tracking
- Community alerts and community response
- Evacuation routes and shelter locations

### Offline Support
- NWPathMonitor for connectivity detection
- SwiftData persistence for offline queue
- Sync log for failed requests

### Navigation
- Centralized AppRouter with NavigationPath
- NavigationDrawer side menu
- Tab-based flow for sign-up wizard
- Deep linking support

## Color Scheme

Navy, red, and white theme:
- Primary Navy: `#001F4D`
- Alert Red: `#E63946`
- Accent Green: `#2A9D8F`
- Light Gray: `#F1F1F1`
- Dark Gray: `#333333`

## Mock Data

Pre-populated with realistic scenarios:
- User "Alex Rivera" with complete profile
- 3 emergency contacts with different relationships
- 5 history events (ranging from safety checks to emergencies)
- 3 nearby responders with various skills
- 2 community alerts
- 2 evacuation routes and 3 shelters

## Accessibility

All interactive elements include:
- `.accessibilityLabel`: Element purpose
- `.accessibilityHint`: Additional context
- Proper VoiceOver support
- Haptic feedback on critical actions

## Next Steps for Production

1. Replace mock services with real API integration
2. Implement real MSAL/Entra ID authentication
3. Add comprehensive error handling and retry logic
4. Implement proper data encryption for sensitive fields
5. Add comprehensive unit and UI tests
6. Configure AppKit signing and capabilities
7. Set up CI/CD pipeline
8. Add comprehensive analytics
9. Implement push notification handling
10. Add real-time collaboration features via WebSocket

## Note

This is a complete scaffolding project. All views are fully implemented with real UI components and mock data. No placeholder TODOs—everything is functional and production-adjacent.
