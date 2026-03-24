const fs = require("fs");
const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell,
  Header, Footer, AlignmentType, LevelFormat,
  HeadingLevel, BorderStyle, WidthType, ShadingType,
  PageNumber, PageBreak
} = require("docx");

// ── Color Palette ──────────────────────────────────────────────
const NAVY = "1B2A4A";
const RED = "E6384B";
const GREEN = "2E8B57";
const AMBER = "D4890E";
const GRAY = "6B7280";
const LIGHT_GRAY = "F3F4F6";
const WHITE = "FFFFFF";
const BLUE = "2563EB";

// ── Status Colors ──────────────────────────────────────────────
const STATUS_DONE = "D1FAE5";      // light green
const STATUS_PARTIAL = "FEF3C7";   // light amber
const STATUS_PLANNED = "DBEAFE";   // light blue
const STATUS_NOT = "F3F4F6";       // light gray

// ── Reusable Styles ────────────────────────────────────────────
const thinBorder = { style: BorderStyle.SINGLE, size: 1, color: "D1D5DB" };
const borders = { top: thinBorder, bottom: thinBorder, left: thinBorder, right: thinBorder };
const noBorder = { style: BorderStyle.NONE, size: 0 };
const noBorders = { top: noBorder, bottom: noBorder, left: noBorder, right: noBorder };

const PAGE_W = 12240;
const PAGE_H = 15840;
const MARGIN = 1080; // 0.75 inch
const CONTENT_W = PAGE_W - MARGIN * 2; // 10080

// Column widths for feature table: Feature | Android | iOS | MAUI/Dash | Spec | Notes
const COL_FEATURE = 2700;
const COL_ANDROID = 1200;
const COL_IOS = 1200;
const COL_MAUI = 1200;
const COL_SPEC = 1200;
const COL_NOTES = 2580;

function statusCell(status, text) {
  let fill = STATUS_NOT;
  let color = GRAY;
  if (status === "done") { fill = STATUS_DONE; color = "065F46"; }
  else if (status === "partial") { fill = STATUS_PARTIAL; color = "92400E"; }
  else if (status === "planned") { fill = STATUS_PLANNED; color = "1E40AF"; }
  return new TableCell({
    borders,
    width: { size: COL_ANDROID, type: WidthType.DXA },
    shading: { fill, type: ShadingType.CLEAR },
    margins: { top: 60, bottom: 60, left: 80, right: 80 },
    verticalAlign: "center",
    children: [new Paragraph({
      alignment: AlignmentType.CENTER,
      spacing: { before: 0, after: 0, line: 240 },
      children: [new TextRun({ text, font: "Arial", size: 18, bold: true, color })]
    })]
  });
}

function featureCell(text, bold = false, indent = 0) {
  return new TableCell({
    borders,
    width: { size: COL_FEATURE, type: WidthType.DXA },
    margins: { top: 60, bottom: 60, left: 80 + indent, right: 80 },
    children: [new Paragraph({
      spacing: { before: 0, after: 0, line: 240 },
      children: [new TextRun({ text, font: "Arial", size: 18, bold, color: bold ? NAVY : "374151" })]
    })]
  });
}

function notesCell(text) {
  return new TableCell({
    borders,
    width: { size: COL_NOTES, type: WidthType.DXA },
    margins: { top: 60, bottom: 60, left: 80, right: 80 },
    children: [new Paragraph({
      spacing: { before: 0, after: 0, line: 240 },
      children: [new TextRun({ text, font: "Arial", size: 16, color: GRAY, italics: true })]
    })]
  });
}

function specCell(status, text) {
  let fill = STATUS_NOT;
  let color = GRAY;
  if (status === "done") { fill = STATUS_DONE; color = "065F46"; }
  else if (status === "partial") { fill = STATUS_PARTIAL; color = "92400E"; }
  else if (status === "planned") { fill = STATUS_PLANNED; color = "1E40AF"; }
  return new TableCell({
    borders,
    width: { size: COL_SPEC, type: WidthType.DXA },
    shading: { fill, type: ShadingType.CLEAR },
    margins: { top: 60, bottom: 60, left: 80, right: 80 },
    verticalAlign: "center",
    children: [new Paragraph({
      alignment: AlignmentType.CENTER,
      spacing: { before: 0, after: 0, line: 240 },
      children: [new TextRun({ text, font: "Arial", size: 18, bold: true, color })]
    })]
  });
}

function headerRow() {
  const hdrCell = (text, width) => new TableCell({
    borders,
    width: { size: width, type: WidthType.DXA },
    shading: { fill: NAVY, type: ShadingType.CLEAR },
    margins: { top: 80, bottom: 80, left: 60, right: 60 },
    children: [new Paragraph({
      alignment: AlignmentType.CENTER,
      spacing: { before: 0, after: 0 },
      children: [new TextRun({ text, font: "Arial", size: 16, bold: true, color: WHITE })]
    })]
  });
  return new TableRow({
    tableHeader: true,
    children: [
      hdrCell("Feature", COL_FEATURE),
      hdrCell("Android", COL_ANDROID),
      hdrCell("iOS", COL_IOS),
      hdrCell("MAUI/Dash", COL_MAUI),
      hdrCell("V1.0 Spec", COL_SPEC),
      hdrCell("Notes", COL_NOTES),
    ]
  });
}

function mauiCell(status, text) {
  let fill = STATUS_NOT;
  let color = GRAY;
  if (status === "done") { fill = STATUS_DONE; color = "065F46"; }
  else if (status === "partial") { fill = STATUS_PARTIAL; color = "92400E"; }
  else if (status === "planned") { fill = STATUS_PLANNED; color = "1E40AF"; }
  return new TableCell({
    borders,
    width: { size: COL_MAUI, type: WidthType.DXA },
    shading: { fill, type: ShadingType.CLEAR },
    margins: { top: 60, bottom: 60, left: 60, right: 60 },
    verticalAlign: "center",
    children: [new Paragraph({
      alignment: AlignmentType.CENTER,
      spacing: { before: 0, after: 0, line: 240 },
      children: [new TextRun({ text, font: "Arial", size: 16, bold: true, color })]
    })]
  });
}

function sectionRow(title) {
  return new TableRow({
    children: [
      new TableCell({
        borders,
        width: { size: CONTENT_W, type: WidthType.DXA },
        columnSpan: 6,
        shading: { fill: "EEF2FF", type: ShadingType.CLEAR },
        margins: { top: 80, bottom: 80, left: 120, right: 80 },
        children: [new Paragraph({
          spacing: { before: 0, after: 0 },
          children: [new TextRun({ text: title, font: "Arial", size: 20, bold: true, color: NAVY })]
        })]
      }),
    ]
  });
}

function featureRow(name, android, ios, maui, spec, notes, indent = false) {
  return new TableRow({
    children: [
      featureCell(name, false, indent ? 200 : 0),
      statusCell(android.s, android.t),
      statusCell(ios.s, ios.t),
      mauiCell(maui.s, maui.t),
      specCell(spec.s, spec.t),
      notesCell(notes),
    ]
  });
}

const D = (t = "Done") => ({ s: "done", t });
const P = (t = "Partial") => ({ s: "partial", t });
const PL = (t = "Planned") => ({ s: "planned", t });
const N = (t = "---") => ({ s: "none", t });
const NA = () => ({ s: "none", t: "N/A" }); // Not applicable to this platform

// ── Legend ──────────────────────────────────────────────────────
function legendTable() {
  const legendCell = (fill, text, width) => new TableCell({
    borders: noBorders,
    width: { size: width, type: WidthType.DXA },
    shading: { fill, type: ShadingType.CLEAR },
    margins: { top: 40, bottom: 40, left: 80, right: 80 },
    children: [new Paragraph({
      alignment: AlignmentType.CENTER,
      spacing: { before: 0, after: 0 },
      children: [new TextRun({ text, font: "Arial", size: 16, bold: true, color: "374151" })]
    })]
  });
  return new Table({
    width: { size: 6000, type: WidthType.DXA },
    columnWidths: [1500, 1500, 1500, 1500],
    rows: [new TableRow({
      children: [
        legendCell(STATUS_DONE, "Done", 1500),
        legendCell(STATUS_PARTIAL, "Partial", 1500),
        legendCell(STATUS_PLANNED, "Planned", 1500),
        legendCell(STATUS_NOT, "Not Started", 1500),
      ]
    })]
  });
}

// ── Summary Stats ──────────────────────────────────────────────
function summaryTable() {
  const CW = 2520; // column width for 4 columns
  const cell = (text, bold = false, fill = WHITE) => new TableCell({
    borders,
    width: { size: CW, type: WidthType.DXA },
    shading: { fill, type: ShadingType.CLEAR },
    margins: { top: 60, bottom: 60, left: 100, right: 100 },
    children: [new Paragraph({
      spacing: { before: 0, after: 0 },
      children: [new TextRun({ text, font: "Arial", size: 20, bold, color: NAVY })]
    })]
  });
  const hdrCell = (text) => new TableCell({
    borders, width: { size: CW, type: WidthType.DXA },
    shading: { fill: NAVY, type: ShadingType.CLEAR },
    margins: { top: 60, bottom: 60, left: 100, right: 100 },
    children: [new Paragraph({ alignment: AlignmentType.CENTER, spacing: { before: 0, after: 0 },
      children: [new TextRun({ text, font: "Arial", size: 20, bold: true, color: WHITE })] })]
  });
  const statCell = (text, fill, color) => new TableCell({
    borders, width: { size: CW, type: WidthType.DXA },
    shading: { fill, type: ShadingType.CLEAR },
    margins: { top: 60, bottom: 60, left: 100, right: 100 },
    children: [new Paragraph({ alignment: AlignmentType.CENTER, spacing: { before: 0, after: 0 },
      children: [new TextRun({ text, font: "Arial", size: 20, bold: true, color })] })]
  });
  return new Table({
    width: { size: CONTENT_W, type: WidthType.DXA },
    columnWidths: [CW, CW, CW, CW],
    rows: [
      new TableRow({ children: [
        cell("", true, NAVY), hdrCell("Android"), hdrCell("iOS"), hdrCell("MAUI/Dash"),
      ]}),
      new TableRow({ children: [
        cell("Features Done", true),
        statCell("53 / 94", STATUS_DONE, "065F46"),
        statCell("60 / 99", STATUS_DONE, "065F46"),
        statCell("23 / 55", STATUS_DONE, "065F46"),
      ]}),
      new TableRow({ children: [
        cell("Partial / Building", true),
        statCell("3", STATUS_PARTIAL, "92400E"),
        statCell("1", STATUS_PARTIAL, "92400E"),
        statCell("6", STATUS_PARTIAL, "92400E"),
      ]}),
      new TableRow({ children: [
        cell("Planned / Not Started", true),
        statCell("38", STATUS_PLANNED, "1E40AF"),
        statCell("38", STATUS_PLANNED, "1E40AF"),
        statCell("26", STATUS_PLANNED, "1E40AF"),
      ]}),
    ]
  });
}

// ── Build Feature Rows ─────────────────────────────────────────
const rows = [
  headerRow(),

  // ── AUTHENTICATION ───────────────────────────────────────────
  sectionRow("1. AUTHENTICATION"),
  featureRow("Login (email/phone + password)", D(), D(), NA(), D(), "Mock auth repos on both platforms"),
  featureRow("Sign Up wizard (3-step)", D(), D(), NA(), D(), "Identity, contacts, EULA"),
  featureRow("Forgot Password / Reset", D(), D(), NA(), D(), "3-step code verification flow"),
  featureRow("Biometric login (Face ID / fingerprint)", N("---"), D(), NA(), D(), "iOS has LocalAuthentication; Android planned"),
  featureRow("MSAL / Entra ID (Azure AD B2C)", PL(), PL(), PL(), D(), "Spec requires; currently using mock auth"),
  featureRow("Hardware SOS bypass (no auth)", PL(), PL(), NA(), D(), "Unauthenticated emergency access"),
  featureRow("Guardian consent (under 18)", PL(), PL(), NA(), D(), "Spec requires age verification"),

  // ── SOS TRIGGERS ─────────────────────────────────────────────
  sectionRow("2. SOS TRIGGERS"),
  featureRow("Manual SOS button (3s countdown)", D(), D(), NA(), D(), "Red button, haptic, countdown ring"),
  featureRow("Phrase detection (on-device STT)", D(), D(), NA(), D(), "SpeechRecognizer / SFSpeechRecognizer"),
  featureRow("Deterministic phrase matching", D(), D(), NA(), D(), "4 strategies: exact, fuzzy, substring, phonetic"),
  featureRow("Duress code (silent SOS)", D(), D(), NA(), D(), "No visible UI change on trigger"),
  featureRow("Personal clear word (cancel SOS)", D(), D(), NA(), D(), "Fuzzy match at 0.80 threshold"),
  featureRow("Quick-tap detection (4x in 5s)", D(), D(), NA(), D(), "Volume, power, screen tap, shake"),
  featureRow("SOS countdown cancel", D(), D(), NA(), D(), "User can abort during 3s window"),

  // ── LOCATION ─────────────────────────────────────────────────
  sectionRow("3. LOCATION TRACKING"),
  featureRow("Normal mode (30s / 100m)", D(), D(), NA(), D(), "Battery-balanced tracking"),
  featureRow("Emergency mode (1s / best accuracy)", D(), D(), NA(), D(), "Activated on any SOS trigger"),
  featureRow("Passive mode (60s / low power)", D(), D("---"), NA(), D(), "Android only; iOS uses significant change"),
  featureRow("Location coordinator (app-scoped)", D(), D(), NA(), D(), "Escalate/deescalate API"),
  featureRow("Background location (always-on)", D(), D(), NA(), D(), "Foreground service / background task"),
  featureRow("WorkManager periodic sync", D(), N("---"), NA(), D(), "Android LocationTrackingWorker; iOS uses BGTask"),
  featureRow("Offline location queuing", P("TODO"), D(), NA(), D(), "Android has TODO for Room storage; iOS has SyncLog"),

  // ── NOTIFICATIONS ────────────────────────────────────────────
  sectionRow("4. NOTIFICATIONS & SMS"),
  featureRow("Push notification channels", D(), D(), NA(), D(), "Critical (DND bypass), High, Normal"),
  featureRow("FCM / APNs integration", D(), D(), NA(), D(), "NotificationService on both platforms"),
  featureRow("SOS dispatch notification", D(), D(), D(), D(), "Accept / Decline action buttons"),
  featureRow("Escalation alert notification", D(), D(), D(), D(), "Accept / Decline / Call 911"),
  featureRow("Check-in request notification", D(), D(), D(), D(), "I'm OK / Need Help"),
  featureRow("Evacuation notice notification", D(), D(), D(), D(), "Acknowledged / Need Assistance"),
  featureRow("Notification response handler", D(), D(), D(), D(), "Routes responses back to API"),
  featureRow("SMS dispatch + inbound replies", N("---"), N("---"), D("Mock"), D(), "Backend mock built; needs Twilio adapter"),
  featureRow("SMS webhook receiver (Functions)", D("Backend"), D("Backend"), D(), D(), "Azure Functions SmsInbound endpoint"),
  featureRow("Device token registration", D(), D(), D(), D(), "Auto-registers on new token"),
  featureRow("Deep links (thewatch://response/)", D(), PL(), NA(), D(), "Android done; iOS needs URL scheme"),

  // ── MAPS & PROXIMITY ─────────────────────────────────────────
  sectionRow("5. MAPS & PROXIMITY"),
  featureRow("Map view (Google / Apple Maps)", D(), D(), NA(), D(), "Full-screen with overlays"),
  featureRow("Proximity ring overlays", N("---"), D(), NA(), D(), "iOS has 3-ring system; Android needs impl"),
  featureRow("Responder map markers", PL(), PL(), PL(), D(), "Spec: green markers with ETA"),
  featureRow("Community alert markers", PL(), PL(), PL(), D(), "Spec: amber pins with confidence"),
  featureRow("Evacuation route polylines", PL(), PL(), NA(), D(), "Spec: green polylines + red hazard zones"),
  featureRow("Shelter markers (capacity)", PL(), PL(), NA(), D(), "Spec: blue house with capacity fill"),

  // ── USER PROFILE ─────────────────────────────────────────────
  sectionRow("6. USER PROFILE & SETTINGS"),
  featureRow("Profile editing", D(), D(), NA(), D(), "Name, photo, medical info"),
  featureRow("Blood type / medical conditions", N("---"), D(), NA(), D(), "iOS has HIPAA-protected fields"),
  featureRow("Duress code configuration", D(), D(), NA(), D(), "Synced to phrase detection"),
  featureRow("Clear word configuration", D(), D(), NA(), D(), "Synced to phrase detection"),
  featureRow("Auto-escalation timer", PL(), PL(), PL(), D(), "Spec: 5-120min configurable"),
  featureRow("911 auto-escalation toggle", PL(), PL(), PL(), D(), "Spec: NG911 API integration"),
  featureRow("Check-in schedule", PL(), PL(), PL(), D(), "Spec: daily/12h/6h/custom"),
  featureRow("Settings screen (toggles)", D(), D(), NA(), D(), "Phrase, location, notification toggles"),
  featureRow("Dark mode", PL(), PL(), PL(), D(), "Android light-only; iOS light-only"),

  // ── CONTACTS ─────────────────────────────────────────────────
  sectionRow("7. EMERGENCY CONTACTS"),
  featureRow("Contact list (CRUD)", D(), D(), NA(), D(), "Add, edit, delete, reorder"),
  featureRow("Priority ordering", N("---"), D(), NA(), D(), "iOS has priority-sorted fetch"),
  featureRow("Relationship types + trust levels", PL(), PL(), NA(), D(), "Spec: Family=5, Medical=4, Friend=3"),
  featureRow("Device contacts import", D(), D(), NA(), D(), "READ_CONTACTS / CNContactStore"),

  // ── VOLUNTEERING ─────────────────────────────────────────────
  sectionRow("8. VOLUNTEERING & RESPONDERS"),
  featureRow("Volunteer enrollment toggle", D(), D(), NA(), D(), "Opt in/out as responder"),
  featureRow("Skills/certifications", D(), D(), NA(), D(), "10 skills, radius config"),
  featureRow("Availability schedule", PL(), PL(), NA(), D(), "Spec: weekly calendar + instant toggle"),
  featureRow("Response radius slider", D(), D(), NA(), D(), "1-50km configurable"),
  featureRow("Response history + stats", PL(), D(), NA(), D(), "iOS has stats; Android needs impl"),
  featureRow("Certification uploads", PL(), PL(), NA(), D(), "Spec: PDF/image with tamper hashing"),
  featureRow("Background check consent", PL(), PL(), NA(), D(), "Spec: required for vulnerable populations"),

  // ── EVIDENCE ─────────────────────────────────────────────────
  sectionRow("9. EVIDENCE & MEDIA CAPTURE"),
  featureRow("Photo capture during incident", PL(), PL(), NA(), D(), "Claude Code building submission system"),
  featureRow("Video capture during incident", PL(), PL(), NA(), D(), "Needs camera service implementation"),
  featureRow("Audio recording during incident", PL(), PL(), NA(), D(), "Microphone permission in place"),
  featureRow("Text / sitrep submission", PL(), PL(), PL(), D(), "Part of evidence submission pipeline"),
  featureRow("Survey dispatch + response", PL(), PL(), D("Hub"), D(), "SignalR hub methods built in backend"),
  featureRow("Tamper-detection hashing", PL(), PL(), PL(), D(), "Spec: chain-of-custody compliance"),
  featureRow("Evidence thumbnail generation", PL(), PL(), PL(), D(), "Worker Service processing planned"),

  // ── OFFLINE ──────────────────────────────────────────────────
  sectionRow("10. OFFLINE SUPPORT"),
  featureRow("Network status monitoring", D(), D(), NA(), D(), "ConnectivityManager / NWPathMonitor"),
  featureRow("Offline banner UI", N("---"), D(), NA(), D(), "iOS has yellow banner"),
  featureRow("SyncLog queuing", P("TODO"), D(), NA(), D(), "Android TODO; iOS fully implemented"),
  featureRow("Background offline flush", N("---"), D(), NA(), D(), "iOS BGProcessingTask with retry"),
  featureRow("SMS fallback (GSM)", PL(), PL(), NA(), D(), "Spec: auto-activate when offline"),
  featureRow("BLE mesh communication", PL(), PL(), NA(), D(), "Spec: device-to-device offline"),

  // ── PERMISSIONS ──────────────────────────────────────────────
  sectionRow("11. PERMISSIONS"),
  featureRow("Permission manager", D(), D(), NA(), D(), "Version-aware, all categories"),
  featureRow("Permissions screen UI", D(), D(), NA(), D(), "Grant/deny with explanations"),
  featureRow("Progressive permission request", D(), D(), NA(), D(), "Ordered sequence on first launch"),
  featureRow("Background services", D(), D(), NA(), D(), "3 foreground services / BGTasks"),

  // ── HEALTH & WEARABLES ───────────────────────────────────────
  sectionRow("12. HEALTH & WEARABLES"),
  featureRow("HealthKit / Health Connect", PL(), D("Framework"), NA(), D(), "iOS has permission; needs sensor reads"),
  featureRow("Wearable device management", PL(), D(), NA(), D(), "iOS has add/remove/toggle"),
  featureRow("Implicit emergency detection", PL(), PL(), NA(), D(), "Spec: fall + HR + no movement"),
  featureRow("Bluetooth integration", PL(), PL(), NA(), D(), "Permissions declared; no impl"),

  // ── LOGGING & DIAGNOSTICS ───────────────────────────────────
  sectionRow("13. LOGGING & DIAGNOSTICS"),
  featureRow("Structured logging port (LoggingPort)", D(), D(), D("Serilog"), D(), "Port interface + LogEntry model on all 3 platforms"),
  featureRow("WatchLogger facade", D(), D(), NA(), D(), "Serilog-style API: logger.information(source, template, props)"),
  featureRow("Mock logging adapter", D(), D(), NA(), D(), "In-memory + Logcat/os_log. First-class, permanent"),
  featureRow("Native logging adapter (Room/SwiftData)", D(), D(), NA(), D(), "On-device persistence, works offline"),
  featureRow("Firestore log sync adapter", D(), D(), NA(), D(), "Batched push to thewatch-logs/{deviceId}/entries"),
  featureRow("Log sync worker (WorkManager/BGTask)", D(), PL(), NA(), D(), "Android WorkManager 15min; iOS BGProcessingTask TODO"),
  featureRow("Correlation ID tracking", D(), D(), D(), D(), "Links all logs for a single SOS lifecycle"),
  featureRow("Platform enrichment (OS, version, device)", D(), D(), D(), D(), "Auto-added to every log entry"),
  featureRow("Local log retention + pruning", D(), D(), NA(), D(), "7-day local, 90-day Firestore"),
  featureRow("Real-time log observation (Flow/Combine)", D(), D(), PL(), D(), "For on-device log viewer UI"),
  featureRow("On-device log viewer UI", PL(), PL(), P("Building"), D(), "Claude Code building in MAUI; mobile planned"),
  featureRow("Cross-device log pull", D(), D(), PL(), D(), "Pull from Firestore collectionGroup query"),

  // ── ADVANCED (MOBILE) ────────────────────────────────────────
  sectionRow("14. ADVANCED / FUTURE (MOBILE)"),
  featureRow("Chat / messaging", PL(), PL(), PL(), PL(), "Not in V1.0 spec; future feature"),
  featureRow("History screen (event timeline)", D(), PL(), PL(), D(), "Android done; iOS needs detail view"),
  featureRow("History PDF export", PL(), PL(), PL(), D(), "Spec: tamper-hashed PDF report"),
  featureRow("EULA management + re-accept", PL(), PL(), NA(), D(), "Spec: diff summary on update"),
  featureRow("Data export (GDPR Art. 20)", PL(), PL(), PL(), D(), "Spec: full data export"),
  featureRow("Account deletion (GDPR Art. 17)", PL(), PL(), PL(), D(), "Spec: with 30-day grace"),
  featureRow("Certificate pinning (SPKI)", PL(), PL(), NA(), D(), "Spec: TLS chain validation"),
  featureRow("Device integrity (root/jailbreak)", PL(), PL(), NA(), D(), "Spec: attestation claim"),
  featureRow("WCAG 2.1 AA compliance", P("Partial"), P("Partial"), PL(), D(), "Touch targets done; screen reader partial"),
  featureRow("DSL rule engine integration", PL(), PL(), PL(), D(), "TheWatch.DSL deterministic routing"),

  // ── MAUI DASHBOARD & TEST ORCHESTRATOR ───────────────────────
  sectionRow("15. MAUI DASHBOARD & ASPIRE (Web)"),
  featureRow("SignalR real-time hub", NA(), NA(), D(), D(), "Milestones, builds, SOS, evidence, surveys"),
  featureRow("Infrastructure health monitoring", NA(), NA(), D(), D(), "Mock adapters: Azure, AWS, GCP, GitHub"),
  featureRow("Simulation service", NA(), NA(), D(), D(), "SimulationController + SimulationService"),
  featureRow("Build status tracking", NA(), NA(), D(), D(), "BuildsController"),
  featureRow("Agent activity tracking", NA(), NA(), D(), D(), "AgentsController"),
  featureRow("Milestone / work item mgmt", NA(), NA(), D(), D(), "MilestonesController, WorkItemsController"),
  featureRow("Response coordination API", NA(), NA(), D(), D(), "ResponseController (full SOS pipeline)"),
  featureRow("Hangfire job dashboard", NA(), NA(), D(), D(), "/hangfire (dev only)"),
  featureRow("OpenAPI / Swagger", NA(), NA(), D(), D(), "API documentation endpoint"),
  featureRow("CORS + SignalR backplane", NA(), NA(), D(), D(), "Redis-backed SignalR"),
  featureRow("MudBlazor DataGrid", NA(), NA(), P("Building"), D(), "Claude Code building now"),
  featureRow("Serilog structured logging", NA(), NA(), P("Building"), D(), "Claude Code integrating now"),
  featureRow("Feature tracker (Firestore)", NA(), NA(), P("Building"), D(), "IFeatureTrackingPort + mock adapter"),
  featureRow("DevWork log viewer", NA(), NA(), P("Building"), D(), "Claude Code interaction audit trail"),
  featureRow("DevWork webhooks (GitHub)", NA(), NA(), P("Building"), D(), "RabbitMQ-triggered Claude Code"),
  featureRow("Adapter tier switcher", NA(), NA(), PL(), PL(), "Toggle mock/native/live per service"),
  featureRow("Database Explorer", NA(), NA(), PL(), D(), "Aspire Phase 2"),
  featureRow("Test Dashboard / orchestrator", NA(), NA(), PL(), D(), "Screen-by-screen mobile test runner"),
  featureRow("Deployment pipeline viz", NA(), NA(), PL(), PL(), "CI/CD status visualization"),
  featureRow("Claude Code response viewer", NA(), NA(), PL(), PL(), "Diff display for AI-generated code"),
  featureRow("Firestore live sync", NA(), NA(), PL(), PL(), "Cross-agent feature status sync"),
  featureRow("Google Cloud Functions logs", NA(), NA(), PL(), PL(), "Firestore data populating DataGrid"),
  featureRow("Local Azure Functions", NA(), NA(), D(), D(), "Response dispatch, escalation, webhooks"),
];

// ── Document ───────────────────────────────────────────────────
const doc = new Document({
  styles: {
    default: { document: { run: { font: "Arial", size: 22 } } },
    paragraphStyles: [
      { id: "Heading1", name: "Heading 1", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 36, bold: true, font: "Arial", color: NAVY },
        paragraph: { spacing: { before: 360, after: 200 }, outlineLevel: 0 } },
      { id: "Heading2", name: "Heading 2", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 28, bold: true, font: "Arial", color: NAVY },
        paragraph: { spacing: { before: 240, after: 120 }, outlineLevel: 1 } },
    ]
  },
  sections: [{
    properties: {
      page: {
        size: { width: PAGE_W, height: PAGE_H },
        margin: { top: MARGIN, right: MARGIN, bottom: MARGIN, left: MARGIN }
      }
    },
    headers: {
      default: new Header({
        children: [new Paragraph({
          alignment: AlignmentType.RIGHT,
          border: { bottom: { style: BorderStyle.SINGLE, size: 4, color: NAVY, space: 4 } },
          spacing: { after: 0 },
          children: [
            new TextRun({ text: "TheWatch ", font: "Arial", size: 18, bold: true, color: NAVY }),
            new TextRun({ text: "Mobile Features Tracker", font: "Arial", size: 18, color: GRAY }),
            new TextRun({ text: "  |  March 2026", font: "Arial", size: 16, color: GRAY }),
          ]
        })]
      })
    },
    footers: {
      default: new Footer({
        children: [new Paragraph({
          alignment: AlignmentType.CENTER,
          border: { top: { style: BorderStyle.SINGLE, size: 2, color: "D1D5DB", space: 4 } },
          children: [
            new TextRun({ text: "Page ", font: "Arial", size: 16, color: GRAY }),
            new TextRun({ children: [PageNumber.CURRENT], font: "Arial", size: 16, color: GRAY }),
            new TextRun({ text: "  |  CONFIDENTIAL  |  TheWatch Life-Safety Platform", font: "Arial", size: 16, color: GRAY }),
          ]
        })]
      })
    },
    children: [
      // Title
      new Paragraph({
        alignment: AlignmentType.CENTER,
        spacing: { before: 400, after: 100 },
        children: [new TextRun({ text: "THE WATCH", font: "Arial", size: 52, bold: true, color: NAVY })]
      }),
      new Paragraph({
        alignment: AlignmentType.CENTER,
        spacing: { before: 0, after: 80 },
        children: [new TextRun({ text: "Mobile Application Features Tracker", font: "Arial", size: 28, color: GRAY })]
      }),
      new Paragraph({
        alignment: AlignmentType.CENTER,
        spacing: { before: 0, after: 60 },
        children: [new TextRun({ text: "Android (Kotlin/Compose)  |  iOS (SwiftUI)  |  MAUI Dashboard  |  V1.0 Spec", font: "Arial", size: 20, color: GRAY })]
      }),
      new Paragraph({
        alignment: AlignmentType.CENTER,
        spacing: { before: 0, after: 300 },
        border: { bottom: { style: BorderStyle.SINGLE, size: 6, color: RED, space: 8 } },
        children: [new TextRun({ text: "Last Updated: March 24, 2026", font: "Arial", size: 18, italics: true, color: GRAY })]
      }),

      // Legend
      new Paragraph({
        heading: HeadingLevel.HEADING_2,
        children: [new TextRun("Status Legend")]
      }),
      legendTable(),
      new Paragraph({ spacing: { before: 200, after: 100 }, children: [] }),

      // Summary
      new Paragraph({
        heading: HeadingLevel.HEADING_2,
        children: [new TextRun("Summary")]
      }),
      summaryTable(),
      new Paragraph({ spacing: { before: 100, after: 100 }, children: [
        new TextRun({ text: "Counts include sub-features within each section. ", font: "Arial", size: 18, color: GRAY, italics: true }),
        new TextRun({ text: "Planned features are defined in the V1.0 UI Screen Specification.", font: "Arial", size: 18, color: GRAY, italics: true }),
      ]}),

      new Paragraph({ children: [new PageBreak()] }),

      // Feature Matrix
      new Paragraph({
        heading: HeadingLevel.HEADING_1,
        children: [new TextRun("Feature Matrix")]
      }),
      new Paragraph({
        spacing: { before: 0, after: 200 },
        children: [new TextRun({
          text: "Each row tracks implementation status across both native platforms and compliance with the V1.0 UI Screen Specification.",
          font: "Arial", size: 18, color: GRAY
        })]
      }),

      new Table({
        width: { size: CONTENT_W, type: WidthType.DXA },
        columnWidths: [COL_FEATURE, COL_ANDROID, COL_IOS, COL_SPEC, COL_NOTES],
        rows,
      }),

      new Paragraph({ children: [new PageBreak()] }),

      // Architecture Notes
      new Paragraph({
        heading: HeadingLevel.HEADING_1,
        children: [new TextRun("Architecture Notes")]
      }),
      new Paragraph({
        spacing: { before: 100, after: 100 },
        children: [
          new TextRun({ text: "Three-Tier Adapter Pattern: ", font: "Arial", size: 20, bold: true, color: NAVY }),
          new TextRun({ text: "All services use ports-and-adapters (hexagonal architecture). Each port interface in TheWatch.Shared supports three adapter tiers:", font: "Arial", size: 20, color: "374151" }),
        ]
      }),
      new Paragraph({
        spacing: { before: 80, after: 40 },
        indent: { left: 360 },
        children: [
          new TextRun({ text: "Mock ", font: "Arial", size: 20, bold: true, color: "065F46" }),
          new TextRun({ text: "- In-memory, log-everything implementations for development and Aspire dashboard. Permanent first-class code.", font: "Arial", size: 20, color: "374151" }),
        ]
      }),
      new Paragraph({
        spacing: { before: 40, after: 40 },
        indent: { left: 360 },
        children: [
          new TextRun({ text: "Native ", font: "Arial", size: 20, bold: true, color: "1E40AF" }),
          new TextRun({ text: "- On-device/on-premise implementations (SQLite, local filesystem, platform APIs). Works without internet.", font: "Arial", size: 20, color: "374151" }),
        ]
      }),
      new Paragraph({
        spacing: { before: 40, after: 200 },
        indent: { left: 360 },
        children: [
          new TextRun({ text: "Live ", font: "Arial", size: 20, bold: true, color: "92400E" }),
          new TextRun({ text: "- Cloud service integrations (Twilio, FCM/APNs, Azure Blob, Cosmos DB). Production external-service tier.", font: "Arial", size: 20, color: "374151" }),
        ]
      }),

      new Paragraph({
        spacing: { before: 100, after: 100 },
        children: [
          new TextRun({ text: "Backend Infrastructure (Aspire): ", font: "Arial", size: 20, bold: true, color: NAVY }),
          new TextRun({ text: "SQL Server, PostgreSQL/PostGIS, Redis, Cosmos DB, RabbitMQ, Hangfire, Azure Functions, SignalR hub. All orchestrated via .NET Aspire AppHost with service discovery.", font: "Arial", size: 20, color: "374151" }),
        ]
      }),

      new Paragraph({
        spacing: { before: 100, after: 100 },
        children: [
          new TextRun({ text: "Active AI Agents: ", font: "Arial", size: 20, bold: true, color: NAVY }),
          new TextRun({ text: "Claude Code (Aspire backend, evidence submission system), Cowork (notification/SMS system, feature tracking), Gemini Antigravity (native adapter scaffolding, three-tier architecture).", font: "Arial", size: 20, color: "374151" }),
        ]
      }),

      new Paragraph({
        spacing: { before: 200, after: 0 },
        border: { top: { style: BorderStyle.SINGLE, size: 2, color: "D1D5DB", space: 8 } },
        alignment: AlignmentType.CENTER,
        children: [new TextRun({ text: "End of Features Tracker", font: "Arial", size: 18, italics: true, color: GRAY })]
      }),
    ]
  }]
});

Packer.toBuffer(doc).then(buffer => {
  fs.writeFileSync("/sessions/happy-funny-clarke/mnt/outputs/TheWatch-Mobile-Features-Tracker.docx", buffer);
  console.log("Document created successfully.");
});
