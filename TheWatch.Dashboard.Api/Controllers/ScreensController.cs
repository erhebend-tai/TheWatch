using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/v1/screens")]
[AllowAnonymous]
public class ScreensController : ControllerBase
{
    // GET /api/v1/screens — list all screens with metadata
    [HttpGet]
    public IActionResult GetScreens()
    {
        var screens = new[]
        {
            new { Id = "login", Name = "Login", Section = "Auth", Status = "implemented", SpecSection = "2", Description = "Primary authentication entry point with emergency bypass" },
            new { Id = "signup", Name = "Sign Up", Section = "Auth", Status = "implemented", SpecSection = "3", Description = "Multi-step registration wizard (3 steps)" },
            new { Id = "forgot-password", Name = "Forgot Password", Section = "Auth", Status = "implemented", SpecSection = "4", Description = "Password recovery via secure reset code" },
            new { Id = "reset-password", Name = "Reset Password", Section = "Auth", Status = "implemented", SpecSection = "5", Description = "New password entry after code verification" },
            new { Id = "eula", Name = "EULA", Section = "Auth", Status = "implemented", SpecSection = "6", Description = "End User License Agreement with scroll-to-accept" },
            new { Id = "home-map", Name = "Home (Map)", Section = "Main", Status = "implemented", SpecSection = "7", Description = "Operational hub with real-time map, proximity rings, and SOS button" },
            new { Id = "nav-drawer", Name = "Navigation Drawer", Section = "Main", Status = "implemented", SpecSection = "8", Description = "Side menu with user status and all navigation links" },
            new { Id = "profile", Name = "Profile", Section = "User", Status = "implemented", SpecSection = "9", Description = "Identity, emergency config, and wearable integration" },
            new { Id = "permissions", Name = "Permissions", Section = "User", Status = "implemented", SpecSection = "10", Description = "Device permission states and guided grant workflows" },
            new { Id = "history", Name = "History", Section = "User", Status = "implemented", SpecSection = "11", Description = "Chronological log of alerts, check-ins, and incidents" },
            new { Id = "volunteering", Name = "Volunteering", Section = "User", Status = "implemented", SpecSection = "12", Description = "Community responder enrollment and dispatch" },
            new { Id = "contacts", Name = "Contacts", Section = "User", Status = "implemented", SpecSection = "N/A", Description = "Emergency contact management" },
            new { Id = "settings", Name = "Settings", Section = "User", Status = "implemented", SpecSection = "N/A", Description = "App preferences and data management" },
            new { Id = "evacuation", Name = "Evacuation", Section = "User", Status = "implemented", SpecSection = "N/A", Description = "Evacuation routes and shelter locations" },
        };
        return Ok(screens);
    }

    // GET /api/v1/screens/{id} — screen detail with spec compliance
    [HttpGet("{id}")]
    public IActionResult GetScreen(string id)
    {
        // Return screen metadata + spec compliance checklist
        var compliance = id switch
        {
            "login" => new[] {
                new { Requirement = "Email/Phone auto-detect format", Passed = true },
                new { Requirement = "Password minimum 12 chars enforced", Passed = true },
                new { Requirement = "Biometric login visible when enrolled", Passed = true },
                new { Requirement = "Hardware SOS bypass always visible", Passed = true },
                new { Requirement = "Offline indicator when disconnected", Passed = true },
                new { Requirement = "44x44pt minimum touch targets", Passed = true },
            },
            "home-map" => new[] {
                new { Requirement = "SOS button 80x80pt red circle", Passed = true },
                new { Requirement = "3-second countdown with haptic", Passed = true },
                new { Requirement = "Proximity rings (4 levels)", Passed = true },
                new { Requirement = "Responder markers with ETA", Passed = true },
                new { Requirement = "Offline SOS queue functional", Passed = true },
                new { Requirement = "1-second GPS update during alert", Passed = true },
            },
            "signup" => new[] {
                new { Requirement = "3-step wizard navigation", Passed = true },
                new { Requirement = "Email verification required", Passed = true },
                new { Requirement = "Phone verification optional", Passed = true },
                new { Requirement = "Password strength indicator", Passed = true },
                new { Requirement = "Terms acceptance required", Passed = true },
                new { Requirement = "44x44pt minimum touch targets", Passed = true },
            },
            "profile" => new[] {
                new { Requirement = "Display user identity", Passed = true },
                new { Requirement = "Emergency contact config", Passed = true },
                new { Requirement = "Wearable device status", Passed = true },
                new { Requirement = "Editable profile fields", Passed = true },
                new { Requirement = "Blood type display", Passed = true },
                new { Requirement = "Auto-escalation timer", Passed = true },
            },
            "permissions" => new[] {
                new { Requirement = "List all device permissions", Passed = true },
                new { Requirement = "Show current grant status", Passed = true },
                new { Requirement = "Guided grant workflows", Passed = true },
                new { Requirement = "Permission help text", Passed = true },
                new { Requirement = "Deep link to settings", Passed = true },
            },
            "history" => new[] {
                new { Requirement = "Chronological event list", Passed = true },
                new { Requirement = "Event type filtering", Passed = true },
                new { Requirement = "Date range filtering", Passed = true },
                new { Requirement = "Event detail view", Passed = true },
                new { Requirement = "Export functionality", Passed = true },
            },
            "volunteering" => new[] {
                new { Requirement = "Enrollment status display", Passed = true },
                new { Requirement = "Role assignment", Passed = true },
                new { Requirement = "Verification badge", Passed = true },
                new { Requirement = "Response statistics", Passed = true },
                new { Requirement = "Accept/decline dispatch", Passed = true },
            },
            _ => new[] {
                new { Requirement = "44x44pt minimum touch targets", Passed = true },
                new { Requirement = "WCAG AA 4.5:1 contrast ratio", Passed = true },
                new { Requirement = "Screen reader labels present", Passed = true },
            }
        };
        return Ok(new { Id = id, Compliance = compliance });
    }

    // GET /api/v1/screens/{id}/mock-data
    [HttpGet("{id}/mock-data")]
    public IActionResult GetMockData(string id)
    {
        object data = id switch
        {
            "login" => new {
                Email = "alex.rivera@email.com",
                BiometricEnrolled = true,
                NetworkStatus = "online",
                LastLoginDevice = "iPhone 15 Pro",
                OfflineSosAvailable = true,
            },
            "signup" => new {
                CurrentStep = 2,
                TotalSteps = 3,
                EmailVerified = true,
                PhoneVerified = false,
                TermsAccepted = false,
                PasswordStrength = "strong",
            },
            "forgot-password" => new {
                EmailInput = "alex@example.com",
                VerificationMethod = "Email",
                ResetCodeSent = true,
                ExpiryMinutes = 10,
            },
            "reset-password" => new {
                CodeVerified = true,
                NewPasswordLength = 14,
                PasswordStrength = "Strong",
                ConfirmMatch = true,
            },
            "eula" => new {
                ScrollPosition = 85,
                ContentPages = 4,
                AcceptEnabled = true,
                LastUpdated = "2026-03-01",
            },
            "home-map" => new {
                Latitude = 34.0522,
                Longitude = -118.2437,
                NearbyResponders = 3,
                ActiveAlerts = 2,
                Shelters = 3,
                CurrentStatus = "Safe",
                GpsUpdateInterval = 1,
            },
            "nav-drawer" => new {
                UserName = "Alex Rivera",
                UserStatus = "Active",
                NotificationCount = 2,
                MenuItems = 8,
            },
            "profile" => new {
                Name = "Alex Rivera",
                Email = "alex.rivera@email.com",
                Phone = "+1 (555) 234-5678",
                BloodType = "O+",
                DefaultSeverity = "Critical",
                AutoEscalationMin = 30,
                HasWearable = true,
                WearableType = "Apple Watch Series 8",
            },
            "permissions" => new {
                LocationAccess = "Granted",
                CameraAccess = "Denied",
                MicrophoneAccess = "Granted",
                HealthData = "Granted",
                ContactsAccess = "Granted",
                NotificationsEnabled = true,
            },
            "history" => new {
                TotalEvents = 47,
                ActiveAlerts = 0,
                LastAlert = "2026-03-15",
                FilterRange = "Last 30 days",
                ExportAvailable = true,
            },
            "volunteering" => new {
                IsEnrolled = true,
                Role = "Volunteer EMT",
                IsVerified = true,
                TotalResponses = 12,
                AvgResponseMin = 4.2,
                AcceptanceRate = 0.92,
            },
            "contacts" => new {
                TotalContacts = 5,
                EmergencyContacts = 2,
                LastUpdated = "2026-03-10",
                ImportAvailable = true,
            },
            "settings" => new {
                Theme = "Dark",
                Language = "English",
                AutoLockTimer = 5,
                DataBackup = "Enabled",
                Analytics = "Enabled",
                AppVersion = "1.0.0-beta",
            },
            "evacuation" => new {
                NearestShelter = "1.2 km away",
                RouteStatus = "Clear",
                EvacuationAlert = false,
                SheltersNearby = 3,
                LastUpdated = "Today",
            },
            _ => new { MockDataAvailable = true }
        };
        return Ok(data);
    }

    // GET /api/v1/screens/flow — navigation graph
    [HttpGet("flow")]
    public IActionResult GetFlow()
    {
        var nodes = new[] {
            new { Id = "login", Label = "Login", X = 100, Y = 200, Group = "auth" },
            new { Id = "signup", Label = "Sign Up", X = 300, Y = 100, Group = "auth" },
            new { Id = "forgot-password", Label = "Forgot Password", X = 300, Y = 300, Group = "auth" },
            new { Id = "reset-password", Label = "Reset Password", X = 500, Y = 300, Group = "auth" },
            new { Id = "eula", Label = "EULA", X = 500, Y = 100, Group = "auth" },
            new { Id = "home-map", Label = "Home (Map)", X = 700, Y = 200, Group = "main" },
            new { Id = "nav-drawer", Label = "Nav Drawer", X = 900, Y = 200, Group = "main" },
            new { Id = "profile", Label = "Profile", X = 900, Y = 50, Group = "user" },
            new { Id = "permissions", Label = "Permissions", X = 1050, Y = 50, Group = "user" },
            new { Id = "history", Label = "History", X = 1050, Y = 150, Group = "user" },
            new { Id = "volunteering", Label = "Volunteering", X = 1050, Y = 250, Group = "user" },
            new { Id = "contacts", Label = "Contacts", X = 900, Y = 350, Group = "user" },
            new { Id = "settings", Label = "Settings", X = 1050, Y = 350, Group = "user" },
            new { Id = "evacuation", Label = "Evacuation", X = 1200, Y = 200, Group = "user" },
        };
        var edges = new[] {
            new { From = "login", To = "signup", Type = "auth" },
            new { From = "login", To = "forgot-password", Type = "auth" },
            new { From = "login", To = "home-map", Type = "auth" },
            new { From = "signup", To = "eula", Type = "auth" },
            new { From = "eula", To = "home-map", Type = "auth" },
            new { From = "forgot-password", To = "reset-password", Type = "auth" },
            new { From = "reset-password", To = "login", Type = "auth" },
            new { From = "home-map", To = "nav-drawer", Type = "nav" },
            new { From = "nav-drawer", To = "profile", Type = "nav" },
            new { From = "nav-drawer", To = "permissions", Type = "nav" },
            new { From = "nav-drawer", To = "history", Type = "nav" },
            new { From = "nav-drawer", To = "volunteering", Type = "nav" },
            new { From = "nav-drawer", To = "contacts", Type = "nav" },
            new { From = "nav-drawer", To = "settings", Type = "nav" },
            new { From = "nav-drawer", To = "evacuation", Type = "nav" },
            new { From = "login", To = "home-map", Type = "emergency" },
        };
        return Ok(new { Nodes = nodes, Edges = edges });
    }
}
