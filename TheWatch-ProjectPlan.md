# TheWatch — Project Plan

**Life-Safety Emergency Response Platform**

Native Android (Kotlin) + Native iOS (Swift) + .NET Backend + DSL Engine

Version 1.0 — March 2026

---

## 1. Current State of the Project

### What We Have

**Design & Specification**

- UI Screen Specification (35K, 12+ screens fully specified with layouts, data bindings, accessibility, offline behavior)
- DSL Full Reference (29K, complete language spec for the deterministic rule engine)
- DSL Grammar (189K XML, full ANTLR4-compatible grammar definition)

**DSL Engine (C# / .NET 10)**

- Complete ANTLR4 grammar (`WatchDsl.g4`) with 6 bug fixes applied
- AST models: DslProgram, NodeDeclaration, RelationshipDeclaration, RuleDeclaration, TraversalDeclaration, Conditions, Actions, DslValue
- Parsing pipeline: DslParser → DslParseResult → WatchDslAstBuilder
- Runtime engine: DslRuleEngine, ConditionEvaluator, ActionExecutor
- Validation: DslRuleValidator (241 lines of rule validation logic)
- Code generation: CSharpGenerator, ICodeGenerator interface
- Integration: IDslServiceBridge, DslServiceCollectionExtensions (DI wiring)
- Configuration: DslEngineOptions, Parameters
- Scaffolding: ProjectScaffolder for new DSL projects
- Polymorphic event extensions: 6 event types (CheckIn, Evacuation, CheckOn, ActivityTimer, LiveVideo, Survey) with 13 new action types and 11 new condition types

**Database**

- SQL Server 2025 database creation script (`001_CreateDatabase.md`, 410 lines)
- WatchMetadata database with `sdk` schema for cross-platform SDK metadata
- Graph table structures for emergency routing

**Graph & Domain Data (XML)**

- `dsl_nodes.xml` (58K) — Node type definitions for the graph
- `dsl_edges.xml` (28K) — Edge/relationship definitions
- `dsl_proxrings.xml` (7K) — Proximity ring definitions (Ring 0–3)
- `dsl_standards.xml` (17K) — Federal standard references (NIST, HIPAA, etc.)
- `dsl_traversals.xml` (24K) — Graph traversal patterns

**Research & Agent Outputs**

- `FullPromptWithOpenAI.txt` (237K, 6,773 lines) — Comprehensive prompt/context document
- `agent6_raw.xml` (86K) — Agent research output
- `agent7_raw.xml` (52K) — Agent research output
- `generate_grammar_csv.py` (52K) — Grammar-to-CSV conversion tooling

**Structured Data (CSV/XLSX)**

- 6 CSV files (~1.2MB total) containing domain catalogs with columns: Category, SubCategory, ElementType, Name, DataType, Description, FederalStandard, StandardRef, ConnectsFrom, ConnectsTo, ImplementationPhase, Scenario, ProximityRing, RequiredOrOptional, KeyValuePath, EnumValues
- 1 CSV with DSL command index: HexIdx, LineNum, Topic, Subtopic, Command, DriverChain, Constructor, Properties, ReturnType, DSLClass, DSLElement
- 1 CSV with condition/result mappings: Topic, Subtopic, If_Condition, Then_Result, Example
- 1 XLSX file (60K) — structured workbook

**Mobile App Scaffolds**

- Android: 55 Kotlin files, ~6,200 lines — Jetpack Compose, Hilt DI, Room DB, Navigation Compose, Material 3
- iOS: 49 Swift files, ~7,400 lines — SwiftUI, @Observable, SwiftData, NavigationStack, MapKit

**Backend (.NET 10 / Aspire — built since initial plan)**

- .NET Aspire orchestration with service discovery and container resources
- Hexagonal architecture with 30+ port interfaces and mock + production adapters
- Blazor SSR dashboard with MudBlazor 8.6.0, Firebase Auth (Google/GitHub sign-in)
- Azure OpenAI integration (GPT-4.1, GPT-4o, GPT-4o-mini, text-embedding-3-large)
- Qdrant vector search with gRPC client
- Firestore swarm inventory with emulator support
- SignalR hub for real-time coordination (response groups, evidence notifications, watch calls, WebRTC signaling)
- Guard reporting with escalation to SOS dispatch
- CCTV integration port (RTSP/ONVIF/WebRTC/HLS/CloudAPI)
- Watch Call system with live video, enrollment, mock training, and AI scene narration
- Responder communication with server-side guardrails (PII, profanity, threat, rate-limit)
- Evidence pipeline (photo/video/audio/sitrep with processing and moderation)
- Container Apps Bicep deployment template
- Alexa Skills + Google Home Actions integrations
- CLI dashboard with Terminal.Gui TUI

**Provisioned Azure Resources**

- Azure SignalR Service (Free, East US 2)
- Azure Cache for Redis (Basic C0, East US 2)
- RabbitMQ as Container App (East US 2)
- SQL Server (Basic, Central US)
- Azure OpenAI with 4 model deployments (East US 2)
- Firebase Auth + Firestore (project: gen-lang-client-0590872284)

### What We Still Need

- Azure Container Registry (required before first deployment)
- CI/CD pipelines (GitHub Actions)
- PostgreSQL / PostGIS for production spatial queries
- Cosmos DB for DiskANN vector store
- Push notification infrastructure (APNs + FCM)
- SMS gateway integration (Twilio or equivalent)
- NG911 API integration
- Cloudflare TURN server for Watch Call WebRTC NAT traversal
- Anthropic, Gemini, VoyageAI API keys (empty in production config)
- Unit and integration test suites
- App Store / Play Store listings and provisioning profiles

---

## 2. GitHub Enterprise Setup

### Why Enterprise

TheWatch handles life-safety data, HIPAA-protected health information, location tracking, and evidence capture with chain-of-custody requirements. GitHub Enterprise provides the audit logging, SAML SSO, IP allowlisting, and compliance controls this demands.

### Setup Steps

1. **Create GitHub Enterprise Cloud account** at github.com/enterprise. Choose a slug (e.g., `thewatch-platform`).
2. **Create an organization** under the enterprise: `TheWatch` (or `thewatch-app`).
3. **Configure organization settings:**
   - Enable 2FA requirement for all members
   - Set default repository visibility to Private
   - Enable branch protection rules as org defaults
   - Configure SAML SSO if using Azure AD/Entra ID (recommended — same IdP as the app)
   - Set up IP allow lists if desired
4. **Create the monorepo:** `TheWatch/thewatch` (private)
5. **Configure repository settings:**
   - Require PR reviews (minimum 1 reviewer)
   - Require status checks to pass before merge
   - Require signed commits (GPG or SSH key signing)
   - Enable secret scanning and Dependabot alerts
   - Enable GitHub Advanced Security (code scanning with CodeQL)
6. **Set up team structure:**
   - `@TheWatch/mobile` — Android + iOS developers
   - `@TheWatch/backend` — .NET, Azure, database
   - `@TheWatch/dsl` — DSL engine and grammar
   - `@TheWatch/ops` — CI/CD, infrastructure, DevOps
7. **Create a GitHub Project board** for sprint tracking (or link to external PM tool)

---

## 3. Monorepo Structure

```
thewatch/
│
├── .github/
│   ├── workflows/
│   │   ├── android-build.yml          # Build + test Android on PR
│   │   ├── android-release.yml        # Build + deploy to Play Console
│   │   ├── ios-build.yml              # Build + test iOS on PR
│   │   ├── ios-release.yml            # Build + deploy to TestFlight
│   │   ├── backend-build.yml          # Build + test .NET backend
│   │   ├── backend-deploy.yml         # Deploy to Azure
│   │   ├── dsl-build.yml              # Build + test DSL engine
│   │   └── database-migrate.yml       # Run database migrations
│   ├── CODEOWNERS                     # Per-directory ownership
│   ├── pull_request_template.md
│   └── ISSUE_TEMPLATE/
│       ├── bug_report.md
│       ├── feature_request.md
│       └── security_vulnerability.md
│
├── android/                           # ← Move TheWatch-Android here
│   ├── app/
│   ├── build.gradle.kts
│   ├── settings.gradle.kts
│   └── gradle/
│
├── ios/                               # ← Move TheWatch-iOS here
│   ├── TheWatch.xcodeproj/
│   ├── TheWatch/
│   └── TheWatchTests/
│
├── backend/
│   ├── TheWatch.Api/                  # ASP.NET Web API
│   ├── TheWatch.Api.Tests/
│   ├── TheWatch.Services/             # Business logic layer
│   ├── TheWatch.Services.Tests/
│   ├── TheWatch.Data/                 # EF Core / data access
│   └── TheWatch.Contracts/            # Shared DTOs, API contracts
│
├── dsl/
│   ├── TheWatch.DSL/                  # ← Existing DSL engine
│   ├── TheWatch.DSL.Tests/
│   └── Grammar/
│       └── WatchDsl.g4
│
├── database/
│   ├── migrations/
│   │   └── 001_CreateDatabase.sql
│   ├── seed/                          # Seed data scripts
│   └── graph/                         # Graph table definitions
│
├── research/                          # ← Consolidated research
│   ├── domain-catalogs/               # CSV/XLSX data files
│   ├── dsl-definitions/               # XML grammar files
│   ├── agent-outputs/                 # Agent research artifacts
│   ├── prompts/                       # Prompt engineering context
│   └── README.md                      # Index of what's here and why
│
├── docs/
│   ├── architecture/
│   │   ├── system-overview.md
│   │   ├── data-flow.md
│   │   └── security-model.md
│   ├── specs/
│   │   ├── ui-screen-specification.md
│   │   └── dsl-full-reference.md
│   ├── api/
│   │   └── openapi.yaml              # OpenAPI 3.1 spec
│   ├── runbooks/
│   │   ├── incident-response.md
│   │   └── deployment.md
│   └── adr/                           # Architecture Decision Records
│       ├── 001-monorepo.md
│       ├── 002-native-over-maui.md
│       └── 003-git-flow.md
│
├── shared/
│   ├── api-contracts/                 # Language-neutral API definitions
│   │   └── openapi.yaml              # Single source of truth
│   └── mock-data/                     # Shared test fixtures
│       └── mock-responses.json
│
├── .gitignore
├── .gitattributes
├── LICENSE
└── README.md
```

### CODEOWNERS

```
# .github/CODEOWNERS
/android/         @TheWatch/mobile
/ios/             @TheWatch/mobile
/backend/         @TheWatch/backend
/dsl/             @TheWatch/dsl
/database/        @TheWatch/backend
/docs/            @TheWatch/mobile @TheWatch/backend
/.github/         @TheWatch/ops
```

---

## 4. Abstraction Layer Architecture — Everything Is Swappable

### Core Principle

**Every external dependency lives behind an interface. No layer knows or cares what implements the layer below it. The UI always renders, even if every service behind it is mocked.**

This is non-negotiable for TheWatch because:

1. **Life-safety demands** mean we cannot be locked into a vendor that might fail, deprecate, or change pricing. If Firebase goes down during a disaster, the app must still work — which means Firebase was never the real dependency; the *interface* was.
2. **Platform divergence** means Android and iOS have fundamentally different APIs for background execution, lock-screen persistence, location tracking, and sensor access. The business logic must be identical; only the platform bindings differ.
3. **Technology evolution** means the best spatial index today (H3) might not be the best in 2 years. The best audit trail today (Merkle trees, Meta's verifiable data structures) might be superseded. Our architecture must absorb these changes without rewriting screens.
4. **Multi-agent development** means any agent should be able to work on any layer without understanding the layers above or below it. Claude Code doesn't need to know if the spatial index is H3 or geohash — it codes against `ISpatialIndex`.

### The Layer Cake

```
┌─────────────────────────────────────────────────────────────────┐
│                     PRESENTATION LAYER                          │
│  Android (Jetpack Compose)  │  iOS (SwiftUI)  │  MAUI (Test)   │
│  Each platform has native UI bindings only.                     │
│  Always renders. Always works. Mock data from day zero.         │
├─────────────────────────────────────────────────────────────────┤
│                     VIEWMODEL / PRESENTER LAYER                 │
│  Platform-native ViewModels (Android ViewModel, @Observable)    │
│  Holds UI state. Calls service interfaces. Never calls          │
│  platform APIs directly — delegates to the platform layer.      │
├─────────────────────────────────────────────────────────────────┤
│                     SERVICE INTERFACE LAYER (PORTS)             │
│  IAuthService          IAlertService         ILocationService   │
│  IContactService       IHistoryService       ISpatialIndex      │
│  INotificationService  IAuditTrail           ISensorService     │
│  IStorageService       ISyncEngine           IDispatchService   │
│  IEvidenceService      IAnalyticsService     IVoiceService      │
│                                                                 │
│  These are PURE INTERFACES. No implementation details.          │
│  Defined once. Shared across platforms (conceptually).          │
├──────────────┬──────────────┬───────────────┬───────────────────┤
│  ADAPTER:    │  ADAPTER:    │  ADAPTER:     │  ADAPTER:         │
│  Mock        │  Production  │  Alternative  │  Test             │
│              │              │               │                   │
│  In-memory   │  Real APIs   │  Swap-in      │  Deterministic    │
│  Fake data   │  Real DBs    │  candidates   │  Recorded data    │
│  Always      │  Real push   │  for eval     │  For CI/CD        │
│  available   │  Real SMS    │               │                   │
└──────────────┴──────────────┴───────────────┴───────────────────┘
```

### Port Definitions (Interfaces)

Every port is an interface that defines WHAT the system does, not HOW. Below is the complete port inventory for TheWatch:

| Port (Interface) | Responsibility | Why It Must Be Swappable |
|-------------------|---------------|--------------------------|
| `IAuthService` | Login, token management, biometric, SOS bypass token | MSAL/Entra ID today, could be Auth0, Cognito, or custom tomorrow |
| `IAlertService` | Trigger, cancel, acknowledge, escalate alerts | Core pipeline; backend may change from Azure to AWS or self-hosted |
| `ILocationService` | GPS tracking, background location, geofencing | Platform-native (CLLocationManager / FusedLocationProvider); cannot be cross-platform |
| `ISpatialIndex` | Proximity queries, ring calculations, nearest-responder | **H3 recommended over geohash.** H3's hexagonal grid avoids edge distortion at poles and provides uniform area cells. Must be swappable if a better index emerges |
| `IAuditTrail` | Immutable, tamper-evident event logging | **Merkle trees recommended.** Meta's verifiable data structures (Akd/SEQ style authenticated key directories) provide cryptographic proof of append-only integrity. Must survive vendor changes |
| `IStorageService` | Local persistence (offline queue, cached data, user prefs) | **SQLite warned against for spatial/audit use cases.** Fine for simple KV and offline queue. H3-indexed spatial data may need a different local store. Realm, ObjectBox, or custom B-tree are candidates |
| `ISyncEngine` | Offline queue → server reconciliation, conflict resolution | SyncLog pattern today; could become CRDTs, operational transform, or Ditto-style mesh sync |
| `INotificationService` | Push (APNs/FCM), SMS (Twilio), Email (SendGrid) | Each channel is its own adapter. SMS provider especially volatile (Twilio pricing changes, regional regulations) |
| `ISensorService` | Wearable data: HR, SpO2, fall detection, ECG, stress | Platform-native (HealthKit / Health Connect); each sensor is a sub-adapter |
| `IEvidenceService` | Audio/photo/video capture, tamper-detection hashing | Chain-of-custody requirements mean the hashing algorithm itself must be swappable (SHA-256 today, SHA-3 or BLAKE3 tomorrow) |
| `IDispatchService` | Responder routing, skill matching, ETA calculation | DSL rule engine today; could add ML-assisted dispatch later (behind the same interface) |
| `IVoiceService` | Alexa skill interaction, voice command processing | AWS Alexa today; Google Assistant or Siri Shortcuts later |
| `IMapProvider` | Map rendering, tile sources, annotation overlays | Apple Maps (iOS) / Google Maps (Android) today; Mapbox, HERE, or OpenStreetMap tomorrow |
| `IAnalyticsService` | Telemetry, crash reporting, usage metrics | App Insights today; Datadog, Sentry, or Mixpanel tomorrow |

### Adapter Registry — Current and Candidate Implementations

| Port | Mock Adapter | Current Production | Candidate Alternatives | Warnings Received |
|------|-------------|-------------------|----------------------|-------------------|
| `ISpatialIndex` | In-memory grid | **H3** (Uber) | S2 (Google), Geohash (warned against — edge distortion, non-uniform cells) | "Cautioned against geohashing in its typical format" |
| `IAuditTrail` | In-memory list | **Merkle tree** + append-only log | Meta's authenticated data structures (AKD/SEQ), Amazon QLDB, Hyperledger Fabric | "New audit-safe database method from Meta" |
| `IStorageService` | In-memory dict | Room (Android) / SwiftData (iOS) | Realm, ObjectBox, custom B-tree, LevelDB | "Warned about SQLite" for spatial/audit scenarios |
| `INotificationService` | Console log | APNs + FCM + Twilio + SendGrid | OneSignal, Amazon SNS/Pinpoint, Vonage | Provider-specific; keep abstracted |
| `IAuthService` | Hardcoded tokens | MSAL / Entra ID (Azure AD B2C) | Auth0, AWS Cognito, Firebase Auth (warned), Supabase Auth | "Warned against Firebase and Azure" — hence the interface |
| `ILocationService` | Static coords | CLLocationManager / FusedLocation | — (must be native; no cross-platform option) | "Require native access as close as we can get" |
| `ISyncEngine` | Immediate passthrough | SyncLog + last-write-wins | CRDTs (Yjs/Automerge), Ditto mesh sync, PowerSync | "Warned about SQLite" for complex sync |
| `IMapProvider` | Static image | Apple Maps / Google Maps | Mapbox GL, HERE SDK, MapLibre (OSS) | Platform-native rendering preferred |

### How This Affects Each Project

**Android (Kotlin):**
```
com.thewatch.app/
├── domain/
│   ├── ports/          ← Pure interfaces (IAlertService, ISpatialIndex, etc.)
│   └── models/         ← Domain entities (no framework deps)
├── data/
│   ├── mock/           ← Mock adapters (always present, always work)
│   ├── production/     ← Real adapters (MSAL, H3, Merkle, Room, FCM)
│   └── di/             ← Hilt modules that wire port → adapter
├── platform/
│   ├── location/       ← FusedLocationProviderClient wrapper
│   ├── sensors/        ← Health Connect wrapper
│   ├── notifications/  ← FCM wrapper
│   └── background/     ← WorkManager + Foreground Service (lock-screen persistence)
└── ui/
    ├── screens/        ← Jetpack Compose (calls ViewModels, never adapters)
    └── viewmodels/     ← Calls ports, never adapters directly
```

**iOS (Swift):**
```
TheWatch/
├── Domain/
│   ├── Ports/          ← Pure protocols (AlertService, SpatialIndex, etc.)
│   └── Models/         ← Domain entities (no framework deps)
├── Data/
│   ├── Mock/           ← Mock adapters
│   ├── Production/     ← Real adapters (MSAL, H3, Merkle, SwiftData, APNs)
│   └── DI/             ← Service container / environment injection
├── Platform/
│   ├── Location/       ← CLLocationManager + background modes
│   ├── Sensors/        ← HealthKit wrapper
│   ├── Notifications/  ← UNUserNotificationCenter + APNs
│   └── Background/     ← BGTaskScheduler + audio session keep-alive
└── Views/
    ├── Screens/        ← SwiftUI (reads ViewModels, never adapters)
    └── ViewModels/     ← Calls protocols, never adapters directly
```

**Aspire Dashboard / MAUI Simulator:**
```
TheWatch.Dashboard.Api/
├── Services/
│   ├── Ports/          ← IGitHubService, IAzureService, IFirestoreService, IAwsService
│   ├── Mock/           ← Always-available mock adapters with realistic data
│   └── Production/     ← Real Octokit, Azure SDK, Firestore SDK, AWS SDK
└── DI/                 ← Swap mock ↔ production via configuration flag
```

### The Mock-First Rule

**Every screen, every flow, every feature must work with mock adapters before real adapters are even written.**

This means:
- A new developer (human or AI) clones the repo, builds, and runs — and sees a working app with realistic data immediately.
- No API keys, no cloud accounts, no database servers needed to see the UI.
- Mock adapters are not throwaway scaffolding — they are permanent fixtures used in development, CI, demos, and offline fallback.
- The DI container (Hilt on Android, environment injection on iOS, ASP.NET DI in Aspire) selects mock vs. production based on build configuration:

```
// Android (Hilt module)
@Module @InstallIn(SingletonComponent::class)
abstract class ServiceModule {
    // Debug/Mock builds
    @Binds abstract fun bindAlertService(impl: MockAlertService): IAlertService
}

// Production builds override via a separate module
@Module @InstallIn(SingletonComponent::class)
abstract class ProductionServiceModule {
    @Binds abstract fun bindAlertService(impl: AzureAlertService): IAlertService
}
```

```swift
// iOS (Environment injection)
extension EnvironmentValues {
    var alertService: any AlertService {
        get { self[AlertServiceKey.self] }
        set { self[AlertServiceKey.self] = newValue }
    }
}

// In TheWatchApp.swift
@main struct TheWatchApp: App {
    var body: some Scene {
        WindowGroup {
            ContentView()
                .environment(\.alertService,
                    ProcessInfo.isMock ? MockAlertService() : ProductionAlertService())
        }
    }
}
```

### Specific Technology Guidance Captured

Based on warnings and recommendations you've received:

**H3 over Geohash:**
H3 (Uber's Hexagonal Hierarchical Spatial Index) provides uniform-area hexagonal cells at 16 resolution levels. Unlike geohash (rectangular cells with edge-distortion artifacts, especially at high latitudes), H3 cells have consistent neighbor relationships and uniform areas, making proximity ring calculations predictable. The `ISpatialIndex` port abstracts this so if S2 Geometry (Google's spherical index) or a future alternative proves better for specific use cases, it can be swapped in per-adapter.

**Merkle Trees + Meta's Verifiable Data Structures:**
For the audit trail, a Merkle tree provides cryptographic proof that no record has been tampered with — each entry's hash depends on all previous entries. Meta's AKD (Authenticated Key Directory) and transparency log work extend this to provide verifiable append-only data structures with efficient proofs of inclusion/exclusion. This is critical for TheWatch's evidence chain-of-custody and NIST AU-3 compliance. The `IAuditTrail` port abstracts whether the implementation uses a custom Merkle tree, Amazon QLDB, or Meta's approach.

**SQLite Cautions:**
SQLite is fine for simple key-value storage, offline queue management, and caching. It is NOT suitable for: spatial queries at scale (use H3 + a proper spatial adapter), cryptographic audit trails (use Merkle-based adapter), or complex graph traversals (use SQL Server 2025 graph tables on the backend). The mobile apps should use SQLite/Room/SwiftData only for the offline queue and preferences, with spatial and audit functions delegated to purpose-built adapters.

**Firebase/Azure Warnings:**
Neither Firebase nor Azure is inherently bad, but vendor lock-in to either is unacceptable for a life-safety platform. Firebase Realtime Database has scalability cliffs and lacks HIPAA BAA coverage on all products. Azure has complex pricing that changes frequently. By coding against `IAuthService` (not MSAL directly), `IStorageService` (not Firestore directly), and `INotificationService` (not FCM directly), we can run on either, both, or neither.

---

## 5. Git Flow Branching Strategy — Multi-Agent Architecture

### The Problem

This project has an unusual and powerful characteristic: multiple AI agents and human developers work concurrently across the codebase. Each agent has different strengths, different tooling, and different access patterns. They must all be able to contribute without stepping on each other. The branching strategy must account for:

**Active Contributors (Agents + Humans):**

| Contributor | Type | Primary Strengths | Typical Work Area |
|------------|------|-------------------|-------------------|
| **Barton** | Human | Architecture, decisions, integration, QA | All areas |
| **Claude Code** | AI Agent (Anthropic) | Full-stack scaffolding, complex multi-file changes, research synthesis | android/, ios/, backend/, docs/ |
| **Gemini Pro** | AI Agent (Google) | Kotlin/Android deep expertise, large context analysis | android/, research/ |
| **Azure OpenAI** | AI Agent (Microsoft) | .NET/Azure backend, C# DSL engine, SQL Server | backend/, dsl/, database/ |
| **GitHub Copilot** | AI Agent (GitHub) | Inline code completion, test generation, small targeted changes | All areas (paired with human) |
| **JetBrains AI** | AI Agent (JetBrains) | IDE-integrated refactoring, code analysis, Kotlin/Swift patterns | android/, ios/ |
| **JetBrains Junie** | AI Agent (JetBrains) | Autonomous multi-file tasks, project-wide refactoring | android/, ios/, backend/ |
| **GitHub Actions** | CI/CD | Build, test, deploy, automated checks | .github/workflows/ |
| **GitHub Actions (Local - Windows)** | CI Runner | Android builds, .NET builds, SQL Server tests | android/, backend/, database/ |
| **GitHub Actions (Local - Mac)** | CI Runner | iOS builds, Xcode signing, TestFlight upload | ios/ |

### Branch Hierarchy

```
main ──────────────────────────────────────────────── production (tagged releases)
  │
  └─ develop ──────────────────────────────────────── integration branch
       │
       │  ── Human branches ──
       ├─ human/barton/feature/android-login
       ├─ human/barton/feature/research-consolidation
       ├─ human/barton/bugfix/ios-map-crash
       │
       │  ── AI Agent branches ──
       ├─ agent/claude-code/feature/android-sos-pipeline
       ├─ agent/claude-code/feature/ios-offline-sync
       ├─ agent/gemini-pro/feature/android-map-overlays
       ├─ agent/azure-openai/feature/backend-auth-api
       ├─ agent/azure-openai/feature/dsl-polymorphic-events
       ├─ agent/junie/feature/android-compose-refactor
       ├─ agent/jetbrains-ai/feature/ios-accessibility
       │
       │  ── Documentation branches ──
       ├─ docs/api-spec-v1
       ├─ docs/architecture-decision-records
       ├─ docs/research-index
       │
       │  ── Database branches ──
       ├─ database/seed-canonical-catalog
       ├─ database/migration-002-graph-edges
       │
       │  ── Research branches ──
       ├─ research/catalog-merge-dedup
       ├─ research/dsl-command-index
       │
       │  ── Release & Hotfix ──
       ├─ release/1.0.0 ───────────────────────────  release candidate
       │    └─ (bug fixes only, merges to main + develop)
       │
       └─ hotfix/critical-sos-fix ─────────────────  emergency fix
            └─ (merges directly to main + develop)
```

### Branch Naming Convention

```
<category>/<contributor>/<type>/<description>
```

| Pattern | Example | When to Use |
|---------|---------|-------------|
| `human/<name>/feature/<desc>` | `human/barton/feature/auth-flow` | Human-driven feature work |
| `human/<name>/bugfix/<desc>` | `human/barton/bugfix/sos-crash` | Human-driven bug fix |
| `agent/<agent>/feature/<desc>` | `agent/claude-code/feature/ios-login` | AI agent feature work |
| `agent/<agent>/bugfix/<desc>` | `agent/gemini-pro/bugfix/map-null` | AI agent bug fix |
| `agent/<agent>/refactor/<desc>` | `agent/junie/refactor/compose-state` | AI agent refactoring |
| `agent/<agent>/test/<desc>` | `agent/azure-openai/test/dsl-determinism` | AI agent test generation |
| `docs/<description>` | `docs/openapi-v1-draft` | Documentation-only changes |
| `database/<description>` | `database/migration-003-responders` | Database schema/seed changes |
| `research/<description>` | `research/catalog-merge` | Research consolidation |
| `release/<version>` | `release/1.0.0` | Release candidate |
| `hotfix/<description>` | `hotfix/sos-bypass-failure` | Critical production fix |

### Agent-Specific Canonical Names

To keep branch names consistent regardless of who creates them:

| Agent | Branch Prefix |
|-------|--------------|
| Claude Code (Anthropic) | `agent/claude-code/` |
| Gemini Pro (Google) | `agent/gemini-pro/` |
| Azure OpenAI (Microsoft) | `agent/azure-openai/` |
| GitHub Copilot | `agent/copilot/` |
| JetBrains AI Assistant | `agent/jetbrains-ai/` |
| JetBrains Junie | `agent/junie/` |

### Conflict Isolation Rules

The branching scheme prevents conflicts through namespace isolation, but we also enforce **area ownership per branch** to avoid two agents editing the same files:

1. **One branch = one area.** A branch should touch files in at most one top-level directory (android/, ios/, backend/, dsl/, database/, docs/, research/). Cross-cutting changes (e.g., API contract + Android + iOS) must be coordinated as linked branches or done by a human.

2. **Claim-before-work.** Before an agent starts a branch, a GitHub Issue should exist describing the work. The issue is assigned to the agent (via label: `agent:claude-code`, `agent:gemini-pro`, etc.). This prevents two agents from independently starting the same feature.

3. **Short-lived branches.** Agent branches should target merge within 1–3 days. Long-running agent branches create drift. If work takes longer, break it into smaller branches.

4. **Rebase before PR.** All branches must rebase onto current `develop` before opening a PR. This catches conflicts early rather than at merge time.

5. **CI must pass.** No merge without green status checks. This is the automated safety net regardless of who wrote the code.

### Branch Protection Rules

**`main` branch:**

- Require PR with 2 approvals (at least 1 human)
- Require ALL platform status checks to pass (Android + iOS + Backend + DSL)
- Require signed commits
- No direct pushes (even admins go through PR)
- Require linear history (squash merge)
- Auto-delete head branches after merge

**`develop` branch:**

- Require PR with 1 approval (human or agent-review bot)
- Require status checks for the affected platform(s) — path-filtered
- Allow merge commits (to preserve feature branch history)
- Require branch to be up to date before merging

**`agent/*` branches:**

- No protection rules (agents need to push freely)
- But PRs from agent branches require human approval before merging to develop
- Automated label applied: `ai-generated` for audit trail

### Merge Strategy

- Agent/Human feature → develop: **Squash merge** (clean single commit per feature)
- Release → main: **Merge commit** (preserves release branch context)
- Hotfix → main: **Squash merge**
- Main → develop (after release/hotfix): **Merge commit** (sync back)

### Tagging

Semantic versioning with platform prefixes when needed:

- `v1.0.0` — Full platform release (all components)
- `v1.0.1-android` — Android-only patch
- `v1.0.1-ios` — iOS-only patch
- `v1.0.1-backend` — Backend-only patch

### Local Self-Hosted Runners

Both your Windows and Mac machines will run GitHub Actions locally for builds that require platform-specific tooling:

| Runner | Machine | Labels | Handles |
|--------|---------|--------|---------|
| `windows-local` | Windows Pro 11 | `self-hosted`, `windows`, `android`, `dotnet` | Android debug builds, .NET builds, SQL Server integration tests |
| `mac-local` | Mac Mini | `self-hosted`, `macos`, `ios`, `xcode` | iOS builds, Xcode signing, TestFlight uploads |

Workflow files reference these via `runs-on: [self-hosted, windows]` or `runs-on: [self-hosted, macos]`. Cloud runners (`ubuntu-latest`) handle linting, lightweight checks, and platform-agnostic tests.

---

## 6. Development Environment Setup

### Machine Roles

| Machine | Primary Role | Secondary Role |
|---------|-------------|----------------|
| **Windows Pro 11** | Android development (Android Studio), Backend development (.NET / VS), Database (SSMS / SQL Server 2025) | Git operations, research consolidation, CI monitoring |
| **Mac Mini** | iOS development (Xcode), iOS builds + signing | Android development (Android Studio), backend development (Rider / VS Code) |
| **Android Phone** | Physical device testing, debug builds via ADB | Dogfooding / QA |
| **iOS Phone** | Physical device testing via TestFlight / Xcode | Dogfooding / QA |

### Windows Pro 11 Setup

1. **Git:** Install Git for Windows. Configure SSH key for GitHub Enterprise. Configure GPG key for signed commits.
2. **Android Studio:** Latest stable (Ladybug or later). Install Android SDK 35 (Android 15), SDK Build Tools, NDK. Configure an emulator (Pixel 8 API 35).
3. **JDK:** Temurin JDK 21 (required by AGP 8.x+).
4. **Visual Studio 2022:** With ASP.NET, Azure, and .NET 10 workloads.
5. **SQL Server 2025 Developer Edition:** Local instance for development. Install SSMS 20.
6. **Azure CLI:** For Azure resource management and deployment testing.
7. **Docker Desktop:** For containerized backend testing and database reset.
8. **Node.js 22 LTS:** For tooling, code generation scripts, and docx generation.

### Mac Mini Setup

1. **Xcode 16+:** With iOS 17+ SDK. Install Command Line Tools.
2. **CocoaPods / Swift Package Manager:** SPM preferred for dependencies.
3. **Apple Developer Account:** Required for provisioning profiles, signing certificates, and TestFlight.
4. **Android Studio:** Same setup as Windows for cross-platform work.
5. **Homebrew:** For tooling (`brew install git gpg swiftlint`).
6. **Git:** Configure same SSH + GPG keys as Windows.
7. **.NET SDK 10:** Install via `brew install dotnet` or official installer.

### Shared Tooling (Both Machines)

- **GitHub CLI (`gh`):** For PR creation, issue management, workflow dispatch
- **Pre-commit hooks:** Linting (ktlint for Kotlin, SwiftLint for Swift, dotnet format for C#)
- **EditorConfig:** Shared `.editorconfig` in repo root for consistent formatting
- **Commitlint:** Enforce conventional commit messages

---

## 7. CI/CD Pipeline Design

### GitHub Actions Matrix

Each platform has its own build workflow triggered by path filters, so an Android-only change doesn't trigger an iOS build.

#### Android Build (`android-build.yml`)

```
Trigger: PR to develop, paths: android/**
Runner: ubuntu-latest
Steps:
  1. Checkout
  2. Setup JDK 21
  3. Setup Gradle cache
  4. Run ktlint
  5. Build debug APK (assembleDebug)
  6. Run unit tests (testDebugUnitTest)
  7. Run instrumented tests (connectedDebugAndroidTest) on Firebase Test Lab
  8. Upload test results as artifact
  9. Upload APK as artifact
```

#### Android Release (`android-release.yml`)

```
Trigger: Push to main with tag v*-android or v* (no suffix)
Runner: ubuntu-latest
Steps:
  1–6. Same as build
  7. Sign release APK/AAB with keystore from GitHub Secrets
  8. Upload to Google Play Console (internal track) via Gradle Play Publisher
  9. Post release notes to GitHub Release
```

#### iOS Build (`ios-build.yml`)

```
Trigger: PR to develop, paths: ios/**
Runner: macos-14 (Apple Silicon)
Steps:
  1. Checkout
  2. Select Xcode version
  3. Resolve Swift packages (xcodebuild -resolvePackageDependencies)
  4. Run SwiftLint
  5. Build (xcodebuild build)
  6. Run unit tests (xcodebuild test)
  7. Run UI tests (xcodebuild test -destination 'platform=iOS Simulator')
  8. Upload test results
```

#### iOS Release (`ios-release.yml`)

```
Trigger: Push to main with tag v*-ios or v* (no suffix)
Runner: macos-14
Steps:
  1–6. Same as build
  7. Archive (xcodebuild archive)
  8. Export IPA with ExportOptions.plist
  9. Upload to TestFlight via `xcrun altool` or App Store Connect API
  10. Post release notes
```

#### Backend Build (`backend-build.yml`)

```
Trigger: PR to develop, paths: backend/** or dsl/**
Runner: ubuntu-latest
Steps:
  1. Checkout
  2. Setup .NET 10
  3. Restore packages
  4. Run dotnet format --verify-no-changes
  5. Build (dotnet build)
  6. Run unit tests (dotnet test)
  7. Run integration tests with SQL Server container (docker-compose up)
  8. Upload test results
```

### Secrets to Configure

| Secret | Purpose |
|--------|---------|
| `ANDROID_KEYSTORE_BASE64` | Release signing keystore |
| `ANDROID_KEYSTORE_PASSWORD` | Keystore password |
| `ANDROID_KEY_ALIAS` | Key alias |
| `ANDROID_KEY_PASSWORD` | Key password |
| `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON` | Play Console upload |
| `APPLE_CERTIFICATES_P12` | iOS signing certificate |
| `APPLE_CERTIFICATES_PASSWORD` | Certificate password |
| `APPLE_PROVISIONING_PROFILE` | Provisioning profile |
| `APPSTORE_CONNECT_API_KEY` | App Store Connect upload |
| `AZURE_CREDENTIALS` | Azure deployment |
| `SQL_CONNECTION_STRING` | Database connection |

---

## 8. Backend / API Design

### Architecture Overview

```
Mobile Apps (Kotlin / Swift)
        │
        ▼
   Azure API Management (rate limiting, auth validation)
        │
        ▼
   ASP.NET Web API (.NET 10)
   ├── AuthController         → MSAL / Entra ID token validation
   ├── AlertController        → Emergency trigger, SOS, phrase detection
   ├── UserController         → Profile CRUD, medical info (HIPAA)
   ├── ContactController      → Emergency contact management
   ├── HistoryController      → Event log retrieval, PDF export
   ├── VolunteerController    → Responder enrollment, dispatch
   ├── EvacuationController   → Routes, shelters, capacity
   ├── CommunityAlertController → Crowdsourced alerts
   └── NotificationController → Push, SMS, Email delivery status
        │
        ├──▶ Azure Service Bus ──▶ DSL Rule Engine (background worker)
        ├──▶ SQL Server 2025 (graph tables + relational)
        ├──▶ Azure Notification Hubs (Push → APNs + FCM)
        ├──▶ Twilio (SMS fallback)
        ├──▶ SendGrid (Email)
        └──▶ NG911 API (auto-dial integration)
```

### API Contract First

Define the API in OpenAPI 3.1 (`shared/api-contracts/openapi.yaml`) before implementation. Both mobile apps generate their API clients from this spec:

- **Android:** Use OpenAPI Generator for Kotlin + Retrofit/OkHttp
- **iOS:** Use OpenAPI Generator for Swift + URLSession/Combine
- **Backend:** Implement controllers matching the spec exactly

This guarantees Android and iOS always agree on request/response shapes.

### Authentication Flow

1. Mobile apps use MSAL libraries (MSAL Android, MSAL iOS) to authenticate against Entra ID (Azure AD B2C).
2. MSAL returns a JWT access token.
3. Every API request includes `Authorization: Bearer <token>`.
4. Backend validates the JWT signature, expiry, audience, and device attestation claim.
5. Refresh tokens stored in platform-native secure storage (Android Keystore, iOS Keychain).

### Key API Endpoints (Draft)

```
POST   /api/v1/auth/device-token       # Device-bound ephemeral token (SOS bypass)
POST   /api/v1/alerts                   # Trigger emergency alert
GET    /api/v1/alerts/{id}              # Get alert status
PUT    /api/v1/alerts/{id}/cancel       # Cancel active alert
POST   /api/v1/alerts/{id}/acknowledge  # Responder acknowledges
GET    /api/v1/users/me                 # Get current user profile
PUT    /api/v1/users/me                 # Update profile
GET    /api/v1/contacts                 # List emergency contacts
POST   /api/v1/contacts                 # Add contact
PUT    /api/v1/contacts/{id}            # Update contact
DELETE /api/v1/contacts/{id}            # Remove contact
GET    /api/v1/history                  # List events (filterable)
GET    /api/v1/history/{id}             # Event detail
GET    /api/v1/history/{id}/export      # PDF export
GET    /api/v1/responders/nearby        # Nearby responders (lat/lng/radius)
PUT    /api/v1/volunteer/enroll         # Enroll as responder
PUT    /api/v1/volunteer/availability   # Update availability
GET    /api/v1/evacuation/routes        # Active evacuation routes
GET    /api/v1/evacuation/shelters      # Active shelters with capacity
GET    /api/v1/community-alerts         # Nearby community alerts
POST   /api/v1/community-alerts        # Report community alert
POST   /api/v1/community-alerts/{id}/confirm  # Confirm alert
POST   /api/v1/community-alerts/{id}/dispute  # Dispute alert
```

---

## 9. Testing Strategy

### Test Pyramid

```
                    ┌─────────┐
                    │  E2E /  │  ← Few: critical paths only
                    │  Manual │     (SOS trigger, auth, offline)
                   ┌┴─────────┴┐
                   │ Integration│  ← Moderate: API + DB + DSL
                   │   Tests    │     real SQL Server in containers
                  ┌┴────────────┴┐
                  │  UI Tests     │  ← Moderate: Espresso (Android)
                  │               │     XCUITest (iOS)
                 ┌┴───────────────┴┐
                 │   Unit Tests     │  ← Many: ViewModels, repos,
                 │                  │     DSL engine, services
                 └──────────────────┘
```

### Life-Safety Specific Tests

These are non-negotiable and must pass on every build:

1. **SOS Trigger Reliability:** Unit test proving SOS button triggers alert pipeline within 1 second under all conditions (online, offline, degraded).
2. **Offline Queue Integrity:** Integration test proving that alerts queued in SQLite/Room/SwiftData during offline mode are delivered in order when connectivity resumes, with zero data loss.
3. **Emergency Bypass:** Test proving unauthenticated SOS flow generates a valid device-bound token and can call emergency endpoints but cannot access profile/contact/history APIs.
4. **Escalation Chain:** Test proving auto-escalation fires at the configured timer interval and contacts are notified in priority order.
5. **DSL Determinism:** Property-based tests proving the rule engine produces identical outputs for identical inputs across 10,000 randomized AlertContext scenarios. No randomness, no AI. Same input = same output, always.
6. **Duress Code:** Test proving duress code triggers silent high-priority alert with no visible on-screen indicators.

### Per-Platform Testing

**Android (Kotlin):**

- Unit tests: JUnit 5 + MockK for ViewModels, repositories, services
- UI tests: Espresso + Compose testing for screen interactions
- Firebase Test Lab: Automated testing on real device farm
- Lint: ktlint + Android Lint

**iOS (Swift):**

- Unit tests: XCTest + Swift Testing framework for ViewModels, services
- UI tests: XCUITest for screen interactions
- Simulator matrix: iPhone 15, iPhone SE (small screen), iPad
- Lint: SwiftLint

**Backend (.NET):**

- Unit tests: xUnit + NSubstitute for services, controllers
- Integration tests: TestContainers with SQL Server for data access
- DSL tests: xUnit with comprehensive rule evaluation scenarios
- Load tests: k6 or NBomber for alert pipeline throughput

---

## 10. Research Consolidation Plan

This is the work of bringing all the scattered research artifacts into a structured, indexed form that the codebase can draw from directly.

### Current Research Inventory

| Artifact | Size | Content | Target Location |
|----------|------|---------|-----------------|
| 4 domain catalog CSVs (same schema) | ~700K | Nodes, edges, properties, standards, phases, scenarios, proximity rings | `research/domain-catalogs/` → merge into single canonical catalog |
| 1 DSL command index CSV | 313K | HexIdx, command definitions, driver chains, constructors | `research/domain-catalogs/dsl-command-index.csv` |
| 1 condition/result CSV | 109K | If/Then business rules | `research/domain-catalogs/condition-rules.csv` |
| 1 XLSX workbook | 60K | Structured domain data | `research/domain-catalogs/` |
| 5 XML definition files | 135K | Nodes, edges, proximity rings, standards, traversals | `dsl/Grammar/definitions/` (these are DSL source truth) |
| DSL grammar XML | 189K | Full ANTLR grammar in XML form | `dsl/Grammar/` |
| 2 agent output XMLs | 138K | Research synthesis | `research/agent-outputs/` |
| Full prompt document | 237K | Comprehensive project context | `research/prompts/` |

### Consolidation Steps

**Phase 1: Catalog Merge and Deduplication**

The 4 CSVs with identical schemas need to be merged into a single canonical catalog, deduplicated, and validated for referential integrity (ConnectsFrom/ConnectsTo references exist as nodes).

Deliverable: `research/domain-catalogs/thewatch-canonical-catalog.csv` with a README explaining the schema.

**Phase 2: Database Seeding**

Convert the canonical catalog and XML definitions into SQL seed scripts that populate the SQL Server 2025 graph tables. This means the research data becomes the actual production data — node types, edge types, proximity ring definitions, federal standards.

Deliverable: `database/seed/` scripts that can be run after `001_CreateDatabase.sql`.

**Phase 3: Mobile Mock Data Alignment**

The mock data in both Android and iOS apps should reflect real entities from the canonical catalog, not made-up test data. Update mock repositories to serve data that matches the actual domain model.

Deliverable: Updated mock data in `android/` and `ios/` that mirrors seed data.

**Phase 4: API Contract Alignment**

The OpenAPI spec must model the same entities (nodes, edges, proximity rings, alert types, event types) as the catalog. The API becomes the bridge between the database and the mobile apps.

Deliverable: `shared/api-contracts/openapi.yaml` reflecting the canonical domain model.

**Phase 5: Research Index**

Create a `research/README.md` that indexes every artifact: what it is, where it came from, what it feeds into, and whether it's been consumed into the codebase or remains reference-only.

---

## 11. Milestone & Work Tracking System

### Why This Matters Most

With multiple AI agents, two physical machines, and a complex domain, the single greatest risk is losing track of what's done, what's in progress, and what's blocked. The tracking system is the backbone that makes parallel agent work productive rather than chaotic.

### GitHub Milestones (The Big Picture)

Each milestone maps to a major deliverable. Milestones have due dates and are visible on the GitHub repo's milestone page. Every Issue belongs to exactly one milestone.

| Milestone | Target | Exit Criteria |
|-----------|--------|---------------|
| **M0: Foundation** | Week 1 | Repo exists, branches configured, all existing code committed, both projects build on their respective machines, local runners operational |
| **M1: Research Consolidated** | Week 3 | Canonical catalog merged, seed scripts written, research indexed, domain model validated against existing data |
| **M2: CI/CD Operational** | Week 3 | All 4 build workflows green (Android, iOS, Backend, DSL), signing configured, local runners processing jobs |
| **M3: Authentication Live** | Week 5 | Entra ID configured, MSAL working on both platforms, login/signup/forgot-password functional end-to-end, SOS bypass token working |
| **M4: Emergency Pipeline** | Week 7 | SOS → API → DSL → Push notification working end-to-end, offline queue proven, escalation chain executing, life-safety tests passing |
| **M5: Full Feature Set** | Week 9 | All 12+ screens connected to real API data, map overlays live, volunteering/dispatch working, history with PDF export |
| **M6: Release Candidate** | Week 11 | Accessibility audit passed, performance targets met, security audit passed, TestFlight + Play internal track distributed |
| **M7: v1.0.0 Launch** | Week 12 | App Store + Play Store approved, production backend deployed, monitoring active |

### GitHub Issues (The Work Items)

Every piece of work — whether done by a human or an AI agent — starts as a GitHub Issue. Issues are the single source of truth for "what's left to do."

**Issue Labels:**

| Category | Labels | Purpose |
|----------|--------|---------|
| **Platform** | `platform:android`, `platform:ios`, `platform:backend`, `platform:dsl`, `platform:database`, `platform:docs`, `platform:research` | What area of the codebase |
| **Agent** | `agent:claude-code`, `agent:gemini-pro`, `agent:azure-openai`, `agent:copilot`, `agent:jetbrains-ai`, `agent:junie`, `agent:human` | Who is assigned (or best suited) |
| **Type** | `type:feature`, `type:bugfix`, `type:refactor`, `type:test`, `type:docs`, `type:research`, `type:infra` | What kind of work |
| **Priority** | `priority:critical` (life-safety), `priority:high`, `priority:medium`, `priority:low` | Urgency |
| **Status** | `status:ready` (groomed, ready to pick up), `status:blocked`, `status:in-review` | Workflow state beyond GitHub's open/closed |
| **Special** | `ai-generated` (PR was authored by an agent), `life-safety` (non-negotiable requirement), `needs-human-decision` | Flags |

**Issue Template (Feature):**

```markdown
## Description
[What needs to be built]

## Acceptance Criteria
- [ ] Criterion 1
- [ ] Criterion 2
- [ ] Tests pass

## Affected Areas
[android/ios/backend/dsl/database/docs]

## Recommended Agent
[Which AI agent is best suited, or "human"]

## Dependencies
[Issues that must be completed first: #12, #15]

## Milestone
[M0–M7]
```

### GitHub Projects Board (The Dashboard)

A GitHub Projects (v2) board gives the real-time view of what's happening across the entire project. Columns:

| Column | Meaning |
|--------|---------|
| **Backlog** | Issues created but not yet groomed or assigned |
| **Ready** | Groomed, has acceptance criteria, assigned to agent/human, dependencies met |
| **In Progress** | Active branch exists, work underway |
| **In Review** | PR opened, awaiting review/approval |
| **Done** | Merged to develop |
| **Released** | Included in a release tag on main |

**Custom Fields on the Board:**

- `Agent`: Which contributor is working on it
- `Platform`: Android / iOS / Backend / DSL / Database / Docs
- `Milestone`: M0–M7
- `Estimated Effort`: S / M / L / XL (for planning)
- `Branch`: Link to the active branch

### Documentation Tracking

Documentation is tracked as first-class work items, not afterthoughts. Each major document has its own issue and follows the same workflow.

**Documentation Inventory (to be tracked):**

| Document | Location | Status | Owner |
|----------|----------|--------|-------|
| UI Screen Specification | `docs/specs/ui-screen-specification.md` | Complete (v1.0) | Barton |
| DSL Full Reference | `docs/specs/dsl-full-reference.md` | Complete (v1.0) | Barton |
| OpenAPI Specification | `shared/api-contracts/openapi.yaml` | Not started | Azure OpenAI / Barton |
| System Architecture | `docs/architecture/system-overview.md` | Not started | Claude Code |
| Data Flow Diagram | `docs/architecture/data-flow.md` | Not started | Claude Code |
| Security Model | `docs/architecture/security-model.md` | Not started | Azure OpenAI |
| Research Index | `research/README.md` | Not started | Claude Code |
| Deployment Runbook | `docs/runbooks/deployment.md` | Not started | Barton |
| Incident Response Runbook | `docs/runbooks/incident-response.md` | Not started | Barton |
| ADR-001: Monorepo | `docs/adr/001-monorepo.md` | To write | Barton |
| ADR-002: Native over MAUI | `docs/adr/002-native-over-maui.md` | To write | Barton |
| ADR-003: Git Flow + Multi-Agent | `docs/adr/003-git-flow-multi-agent.md` | To write | Barton |
| ADR-004: Deterministic DSL (No AI at Runtime) | `docs/adr/004-deterministic-dsl.md` | To write | Barton |
| ADR-005: Ports-and-Adapters (Everything Swappable) | `docs/adr/005-ports-and-adapters.md` | To write | Claude Code |
| ADR-006: H3 over Geohash for Spatial Indexing | `docs/adr/006-h3-spatial-index.md` | To write | Claude Code |
| ADR-007: Merkle Trees for Audit Trail | `docs/adr/007-merkle-audit-trail.md` | To write | Azure OpenAI |
| ADR-008: Native over Cross-Platform for Background Execution | `docs/adr/008-native-background.md` | To write | Barton |
| Technology Risk Register | `docs/architecture/technology-risk-register.md` | To write | Claude Code |

**ADRs (Architecture Decision Records)** are especially important here because with multiple agents working on the codebase, each agent needs to understand *why* decisions were made, not just what the current code looks like. ADRs become the shared context that keeps all agents aligned.

### Agent Dispatch Model

When you're ready to assign work, the workflow looks like this:

```
1. Create GitHub Issue with acceptance criteria
2. Assign to milestone (M0–M7)
3. Label with platform + recommended agent
4. Agent creates branch: agent/<name>/feature/<issue-slug>
5. Agent does work, commits with issue reference (#42)
6. Agent opens PR → develop
7. CI runs (GitHub Actions cloud + local runners)
8. Human reviews and approves
9. Squash merge to develop
10. Issue auto-closes via "Closes #42" in PR
11. Board updates automatically
```

For parallel work, multiple agents can be dispatched simultaneously as long as they're working in different platform areas or on non-overlapping files within the same area.

### Progress Reporting

At any point, you can ask any agent: "What's our progress on M3?" and they can check the GitHub milestone to see open vs. closed issues, remaining work, and blockers. This is the "what's left to do" visibility you need.

A weekly status view can also be auto-generated from the GitHub API:

```
Milestone M2: CI/CD Operational — 65% complete (13/20 issues closed)
  Blocked: 2 issues (iOS signing cert pending, Azure subscription needed)
  In Progress: 3 issues
  Ready to pick up: 2 issues

  This week:
    ✅ agent/claude-code — Android build workflow (#14)
    ✅ agent/azure-openai — Backend build workflow (#16)
    🔄 agent/claude-code — iOS build workflow (#15) — blocked on signing cert
    🔄 human/barton — Local runner setup (#18)
```

---

## 12. Recommended Execution Order

### M0: Foundation (Week 1)

**Goal: Everything committed, building, and ready for parallel agent work.**

| # | Task | Platform | Recommended Agent | Dependencies |
|---|------|----------|-------------------|--------------|
| 1 | Set up GitHub Enterprise account and organization | infra | Barton (manual) | None |
| 2 | Create monorepo with directory structure (Section 3) | infra | Claude Code | #1 |
| 3 | Initialize Git Flow: create `main` and `develop` branches | infra | Claude Code | #2 |
| 4 | Configure branch protection rules and CODEOWNERS | infra | Barton | #3 |
| 5 | Create all GitHub labels (agent, platform, type, priority, status) | infra | Claude Code | #2 |
| 6 | Create GitHub Project board with custom fields | infra | Claude Code | #2 |
| 7 | Move Android scaffold into `android/` | android | Claude Code | #3 |
| 8 | Move iOS scaffold into `ios/` | ios | Claude Code | #3 |
| 9 | Move DSL engine source into `dsl/TheWatch.DSL/` | dsl | Claude Code | #3 |
| 10 | Move database scripts into `database/migrations/` | database | Claude Code | #3 |
| 11 | Move research artifacts into `research/` with index | research | Claude Code | #3 |
| 12 | Set up `.gitignore`, `.editorconfig`, pre-commit hooks | infra | Claude Code | #3 |
| 13 | Configure Android Studio on Windows, verify build | android | Barton | #7 |
| 14 | Configure Xcode on Mac Mini, create .xcodeproj, verify build | ios | Barton | #8 |
| 15 | Install & configure GitHub Actions self-hosted runner (Windows) | infra | Barton | #1 |
| 16 | Install & configure GitHub Actions self-hosted runner (Mac) | infra | Barton | #1 |
| 17 | Create all M0–M7 milestones and seed initial issues | infra | Claude Code | #2 |
| 18 | Write ADR-001 (Monorepo), ADR-002 (Native), ADR-003 (Multi-Agent Git Flow) | docs | Claude Code | #3 |
| 19 | Initial commit on `develop` | infra | Barton | #7–#12 |

### M1: Research Consolidated (Weeks 2–3)

**Goal: All research material organized, deduplicated, and feeding into the database and API.**

| # | Task | Platform | Recommended Agent | Dependencies |
|---|------|----------|-------------------|--------------|
| 20 | Merge and deduplicate the 4 domain catalog CSVs | research | Claude Code | M0 |
| 21 | Validate referential integrity of merged catalog | research | Claude Code | #20 |
| 22 | Index agent outputs (agent6, agent7) — extract actionable items | research | Gemini Pro | M0 |
| 23 | Parse and index the FullPromptWithOpenAI.txt (6,773 lines) | research | Gemini Pro | M0 |
| 24 | Write database seed scripts from canonical catalog | database | Azure OpenAI | #20 |
| 25 | Map canonical catalog entities to OpenAPI schema draft | docs | Claude Code | #20 |
| 26 | Align Android mock data with canonical catalog entities | android | Claude Code / Gemini Pro | #20 |
| 27 | Align iOS mock data with canonical catalog entities | ios | Claude Code | #20 |
| 28 | Create `research/README.md` — complete index of all artifacts | docs | Claude Code | #20–#23 |

### M2: CI/CD Operational (Weeks 2–3, parallel with M1)

**Goal: Every PR triggers automated builds and tests on the right platform.**

| # | Task | Platform | Recommended Agent | Dependencies |
|---|------|----------|-------------------|--------------|
| 29 | Write `android-build.yml` workflow | infra | Claude Code | M0 |
| 30 | Write `ios-build.yml` workflow | infra | Claude Code | M0 |
| 31 | Write `backend-build.yml` workflow | infra | Azure OpenAI | M0 |
| 32 | Write `dsl-build.yml` workflow | infra | Azure OpenAI | M0 |
| 33 | Configure Android signing secrets in GitHub | infra | Barton | #29 |
| 34 | Configure iOS signing certificates and provisioning profiles | infra | Barton | #30 |
| 35 | Write `android-release.yml` (Play Console deploy) | infra | Claude Code | #29, #33 |
| 36 | Write `ios-release.yml` (TestFlight deploy) | infra | Claude Code | #30, #34 |
| 37 | Set up pre-commit hooks: ktlint, SwiftLint, dotnet format | infra | Claude Code | M0 |
| 38 | Verify local runners process jobs for their platform | infra | Barton | #15, #16, #29, #30 |

### M3: Authentication Live (Weeks 4–5)

**Goal: Real auth working end-to-end on both platforms.**

| # | Task | Platform | Recommended Agent | Dependencies |
|---|------|----------|-------------------|--------------|
| 39 | Configure Entra ID (Azure AD B2C) tenant | infra | Barton / Azure OpenAI | Azure subscription |
| 40 | Build AuthController in backend | backend | Azure OpenAI | #39 |
| 41 | Build UserController in backend | backend | Azure OpenAI | #40 |
| 42 | Implement MSAL auth in Android | android | Gemini Pro | #39, #40 |
| 43 | Implement MSAL auth in iOS | ios | Claude Code | #39, #40 |
| 44 | Implement device-bound ephemeral token (SOS bypass) | backend | Azure OpenAI | #40 |
| 45 | Connect Android login/signup/forgot-password to real API | android | Gemini Pro | #40, #41, #42 |
| 46 | Connect iOS login/signup/forgot-password to real API | ios | Claude Code | #40, #41, #43 |
| 47 | Write auth integration tests | backend | Azure OpenAI | #40, #44 |
| 48 | Write OpenAPI spec for auth endpoints | docs | Claude Code | #40, #41, #44 |

### M4: Emergency Pipeline (Weeks 6–7)

**Goal: The core life-safety flow works. SOS → detection → routing → notification.**

| # | Task | Platform | Recommended Agent | Dependencies |
|---|------|----------|-------------------|--------------|
| 49 | Build AlertController in backend | backend | Azure OpenAI | M3 |
| 50 | Wire DSL rule engine into alert processing | backend/dsl | Azure OpenAI | M3 |
| 51 | Set up Azure Service Bus for event-driven routing | infra | Azure OpenAI | Azure subscription |
| 52 | Configure Azure Notification Hubs (APNs + FCM) | infra | Azure OpenAI | Azure subscription |
| 53 | Implement push notification handling (Android) | android | Gemini Pro | #52 |
| 54 | Implement push notification handling (iOS) | ios | Claude Code | #52 |
| 55 | Implement offline queue sync (SyncLog → API) on Android | android | Gemini Pro / Junie | #49 |
| 56 | Implement offline queue sync (SyncLog → API) on iOS | ios | Claude Code | #49 |
| 57 | End-to-end: SOS button → API → DSL → push notification | all | Barton (integration) | #49–#56 |
| 58 | Write life-safety test suite (Section 8) | all | Claude Code + Azure OpenAI | #49, #50 |
| 59 | SMS fallback integration (Twilio) | backend | Azure OpenAI | #49 |

### M5: Full Feature Set (Weeks 8–9)

**Goal: All 12+ screens connected to real data.**

| # | Task | Platform | Recommended Agent | Dependencies |
|---|------|----------|-------------------|--------------|
| 60 | Build remaining API controllers (History, Contacts, Volunteer, Evacuation, Community) | backend | Azure OpenAI | M4 |
| 61 | Connect History screen to API (Android + iOS) | android, ios | Gemini Pro, Claude Code | #60 |
| 62 | Connect Contacts management to API (Android + iOS) | android, ios | Gemini Pro, Claude Code | #60 |
| 63 | Connect Volunteering/dispatch to API (Android + iOS) | android, ios | Gemini Pro, Claude Code | #60 |
| 64 | Connect Evacuation + shelters to API (Android + iOS) | android, ios | Gemini Pro, Claude Code | #60 |
| 65 | Connect Community Alerts to API (Android + iOS) | android, ios | Gemini Pro, Claude Code | #60 |
| 66 | Wire map overlays to real responder/alert/shelter data | android, ios | Gemini Pro, Claude Code | #60 |
| 67 | Implement PDF export for history events | backend | Azure OpenAI | #60 |
| 68 | Complete OpenAPI spec for all endpoints | docs | Claude Code | #60 |

### M6: Release Candidate (Weeks 10–11)

**Goal: Quality gate passed. Ready for real users.**

| # | Task | Platform | Recommended Agent | Dependencies |
|---|------|----------|-------------------|--------------|
| 69 | Accessibility audit — VoiceOver (iOS) | ios | Claude Code / Barton | M5 |
| 70 | Accessibility audit — TalkBack (Android) | android | Gemini Pro / Barton | M5 |
| 71 | Performance testing (screen load <500ms, SOS <1s) | all | Claude Code | M5 |
| 72 | Security audit (cert pinning, jailbreak detect, token validation) | all | Azure OpenAI | M5 |
| 73 | Create `release/1.0.0` branch from develop | infra | Barton | #69–#72 |
| 74 | TestFlight beta distribution | ios | Barton (Mac Mini) | #73 |
| 75 | Google Play internal track distribution | android | Barton (Windows) | #73 |
| 76 | Bug fixes from beta testing | all | All agents | #74, #75 |
| 77 | Penetration test (third-party) | all | External vendor | #72 |

### M7: v1.0.0 Launch (Week 12)

| # | Task | Platform | Recommended Agent | Dependencies |
|---|------|----------|-------------------|--------------|
| 78 | Final QA on physical devices (Android phone + iOS phone) | all | Barton | M6 |
| 79 | App Store submission | ios | Barton | #78 |
| 80 | Play Store submission | android | Barton | #78 |
| 81 | Production backend deployment (Azure) | backend | Azure OpenAI / Barton | #78 |
| 82 | Monitoring + alerting setup (Azure Monitor, App Insights) | infra | Azure OpenAI | #81 |
| 83 | Merge `release/1.0.0` → main, tag `v1.0.0` | infra | Barton | #79, #80, #81 |
| 84 | Merge main → develop (sync back) | infra | Barton | #83 |
| 85 | Write deployment runbook | docs | Claude Code | #81 |
| 86 | Post-launch retrospective | docs | Barton | #83 |

---

## 13. Aspire Command Center — Expansion Plan

The Aspire dashboard is the single pane of glass for the entire project. If this app is running, you can see everything: what the mobile screens look like, what state the databases are in, which tests pass, where milestones stand, and what every agent is doing. The mock-first rule from Section 4 applies here too — every panel renders with mock data from day zero, before any real service is connected.

### Current State (What Exists Now)

7 Blazor pages: Home (overview), Milestones, WorkItems, Agents, Builds, Research, Simulation. All with basic UI and mock data. 6 API controllers, 1 SignalR hub, 5 service integrations. MAUI simulator with alert, sensor, and device pages.

### Target State — 4 Major Sections to Add

#### 1. Screen Gallery (P0 — Build First)

Interactive HTML replicas of every mobile screen, inside a phone-frame bezel, in the browser.

**Why HTML replicas instead of screenshots:** They're always available (no device needed), update the instant someone changes the spec or mock data, use the same mock data layer as the native apps, and any agent can modify a screen without needing Android Studio or Xcode. They ARE the living specification.

**New pages:** `ScreenGallery.razor` (screen list + preview), `ScreenFlow.razor` (clickable navigation diagram from Appendix A), `ScreenDetail.razor` (individual screen + props inspector)

**New components (16):**
- `PhoneFrame.razor` — CSS bezel wrapper, toggle iPhone (390x844) / Android (412x915)
- 13 mock screen components — one per screen from the UI spec: Login, SignUp (3-step wizard), ForgotPassword, ResetPassword, EULA, HomeMap (with SOS button + proximity rings), Profile, Permissions, History, Volunteering, Contacts, Settings, Evacuation
- `ScreenNavMap.razor` — SVG navigation flow diagram (Appendix A)
- `PropsInspector.razor` — side panel showing all data bindings and current values

**Each mock screen:** receives mock data via DI, renders HTML/CSS matching the native Compose/SwiftUI look, has interactive elements (buttons navigate between screens, toggles work, forms accept input), shows spec compliance checklist (e.g., "SOS button 80x80pt ✅", "Offline banner on disconnect ✅").

**New API endpoints:**
```
GET  /api/v1/screens                    # All screen definitions
GET  /api/v1/screens/{id}               # Screen detail + spec compliance
GET  /api/v1/screens/{id}/mock-data     # Mock data for this screen
GET  /api/v1/screens/flow               # Navigation graph (nodes + edges)
```

#### 2. Database Explorer (P1)

Shows every database in the system — even if mocked. See the shape of data at every layer.

**Database registry (all visible from day one):**

| Database | Type | Status at Launch | Mock Strategy |
|----------|------|-----------------|---------------|
| SQL Server 2025 (WatchMetadata) | Relational + Graph | Schema exists, seed data from CSVs | In-memory EF Core |
| Android Room (TheWatch.db) | SQLite via Room | Schema in scaffold | In-memory Room test DB |
| iOS SwiftData (TheWatch.store) | SQLite via SwiftData | Schema in scaffold | In-memory model container |
| Firestore (thewatch-dashboard) | Document store | Collection structure planned | In-memory dictionary |
| H3 Spatial Index | Hexagonal grid | Interface defined | Static hex grid for mock region |
| Merkle Audit Trail | Append-only hash chain | Interface defined | In-memory list with SHA-256 chain |

**New pages:** `DatabaseOverview.razor` (all databases at a glance), `DatabaseDetail.razor` (schema browser), `QueryConsole.razor` (read-only query interface)

**New components (8):** 6 database cards (SqlServer, Room, SwiftData, Firestore, H3Index, MerkleAudit), `SchemaViewer.razor`, `DataPipeline.razor` (CSV → seed → production flow with status per stage)

**New API endpoints:**
```
GET  /api/v1/databases                  # All databases + status
GET  /api/v1/databases/{id}/schema      # Schema for specific DB
GET  /api/v1/databases/{id}/sample      # Sample data (first 100 rows)
GET  /api/v1/databases/{id}/migrations  # Migration history
POST /api/v1/databases/{id}/query       # Read-only query execution
GET  /api/v1/data-pipeline/status       # End-to-end pipeline health
```

#### 3. Test Dashboard (P1)

**New pages:** `TestOverview.razor` (aggregate across all platforms), `TestSuiteDetail.razor` (drill into a suite)

**New components (5):** `TestPlatformCard.razor` (per-platform results), `LifeSafetyTests.razor` (the 6 non-negotiable tests — prominent, red header if any fail), `CoverageMap.razor` (per-module, red below 60%, yellow below 80%), `TestHistory.razor` (30-build trend), `FailingTests.razor` (current failures with traces)

**Life-Safety Tests (always visible, always prominent):**
1. SOS Trigger Reliability (<1 second)
2. Offline Queue Integrity (zero data loss)
3. Emergency Bypass (token scoping)
4. Escalation Chain (timer + priority order)
5. DSL Determinism (10K randomized scenarios, same input = same output)
6. Duress Code (silent alert, no visible indicators)

If ANY life-safety test is red, the entire dashboard header turns red.

**New API endpoints:**
```
GET  /api/v1/tests                      # Aggregate results
GET  /api/v1/tests/platforms/{platform} # Per-platform results
GET  /api/v1/tests/life-safety          # The 6 critical tests
GET  /api/v1/tests/coverage             # Coverage by module
GET  /api/v1/tests/history              # Historical trend
GET  /api/v1/tests/{suiteId}            # Individual suite detail
```

#### 4. Milestone Tracker Enhancements (P0)

Enhance existing `Milestones.razor` with: burndown chart per milestone, issues grouped by agent, issues grouped by platform, blocked-issue panel with reasons and dependency chains.

**New API endpoints:**
```
GET  /api/v1/milestones/{id}/burndown   # Burndown data
GET  /api/v1/milestones/{id}/by-agent   # Issues by agent
GET  /api/v1/milestones/{id}/by-platform # Issues by platform
GET  /api/v1/milestones/{id}/blocked    # Blocked issues + reasons
```

### Updated AppHost Wiring

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure
var redis = builder.AddRedis("cache");
var sqlserver = builder.AddSqlServer("sql")
    .AddDatabase("watchmetadata");

// Dashboard API
var api = builder
    .AddProject<Projects.TheWatch_Dashboard_Api>("dashboard-api")
    .WithReference(redis)
    .WithReference(sqlserver)
    .WaitFor(redis);

// Dashboard Web — the command center
builder
    .AddProject<Projects.TheWatch_Dashboard_Web>("dashboard-web")
    .WithReference(api)
    .WaitFor(api);

// MAUI, Android, iOS connect at runtime via HTTP + SignalR
// GitHub Actions report via webhooks
// AI agents report via GitHub integration
builder.Build().Run();
```

### Updated Nav Menu Structure

```
TheWatch Command Center
├── Main
│   ├── Dashboard          (Home — overview with live counters)
│   ├── Milestones         (M0–M7 with burndown)
│   └── Work Items         (Filterable by milestone/agent/platform)
├── App Preview
│   ├── Screen Gallery     (All 13 screens in phone frames)
│   └── Navigation Flow    (Clickable app flow diagram)
├── Data
│   ├── Database Explorer  (All 6 databases)
│   ├── Query Console      (Read-only queries)
│   └── Data Pipeline      (CSV → seed → production)
├── Quality
│   ├── Test Dashboard     (All platforms + life-safety)
│   └── Coverage           (Per-module)
├── Operations
│   ├── AI Agents          (Activity, branches, PRs)
│   ├── Builds             (CI/CD, local runners)
│   └── Research           (Catalog status, consolidation)
└── Tools
    ├── Simulation         (MAUI relay, scenario playback)
    └── Settings           (API keys, mock vs prod toggle)
```

### Estimated Scope

| Component | New Pages | New Endpoints | New Components | Effort |
|-----------|----------|---------------|----------------|--------|
| Screen Gallery | 3 | 4 | 16 | Large |
| Database Explorer | 3 | 6 | 8 | Medium |
| Test Dashboard | 2 | 6 | 5 | Medium |
| Milestone Enhancements | 0 | 4 | 3 | Small |
| Agent/Build Detail | 2 | 2 | 2 | Small |
| Settings | 1 | 0 | 1 | Small |
| **Total** | **11** | **22** | **35** | |

### Build Order

| Phase | What | Why |
|-------|------|-----|
| **Phase 1** | Screen Gallery (all 13 screens) + Milestone burndown | You can see the app and see the progress — the two most important things |
| **Phase 2** | Database Explorer (all 6 cards) + Test Dashboard | You can see the data and see the quality |
| **Phase 3** | Agent/Build detail views + Query Console + Pipeline viz | Power-user debugging and operational depth |
| **Phase 4** | Settings + mock/prod toggle + real service wiring | Transition from mocked to live |

---

## 14. Technology Risk Register

Every technology choice carries risk. This register tracks what we've been warned about, what we chose instead, and what happens if we need to swap.

| Technology | Risk / Warning | Current Decision | Swap-Out Plan | Effort to Swap |
|-----------|---------------|-----------------|---------------|----------------|
| **Geohash** | Edge distortion at high latitudes, non-uniform cell areas, poor for proximity rings | **Use H3 instead.** Implement behind `ISpatialIndex` | Swap adapter; no UI or service changes | Low (adapter only) |
| **SQLite** (for spatial/audit) | Not designed for spatial queries or cryptographic audit trails | **Use SQLite only for offline queue + prefs.** Spatial → H3 adapter. Audit → Merkle adapter | Already separated by port | N/A (already mitigated) |
| **Firebase** | Scalability cliffs, incomplete HIPAA BAA coverage, vendor lock-in | **Use behind interfaces only.** Firestore for Aspire dashboard sync (non-life-safety). NOT for mobile alert pipeline | Swap `IFirestoreService` adapter | Low |
| **Azure** (general) | Complex/volatile pricing, potential single-vendor dependency | **Use for backend hosting + Entra ID auth + Service Bus.** All behind `IAuthService`, `INotificationService`, etc. | Swap to AWS, GCP, or self-hosted per-adapter | Medium (per-service) |
| **MAUI** (for mobile) | Cannot provide deep background execution, lock-screen persistence, raw hardware access for life-safety tracking | **Use MAUI only for the Aspire test/simulation app.** Production mobile = native Kotlin + Swift | MAUI stays in its lane; no swap needed | N/A |
| **React Native / Flutter** | Same background execution limitations as MAUI; JS bridge overhead for real-time sensor data | **Not used.** Native only for production mobile apps | N/A | N/A |
| **Twilio** (SMS) | Pricing changes, regional availability, API deprecation risk | **Use behind `INotificationService`.** SMS is one channel adapter among many | Swap to Vonage, MessageBird, Amazon SNS | Low (adapter only) |
| **MSAL / Entra ID** | Microsoft ecosystem dependency, B2C pricing per authentication | **Use behind `IAuthService`.** MSAL libraries on both platforms | Swap to Auth0, Cognito, or custom JWT issuer | Medium (token format may differ) |
| **Apple Maps / Google Maps** | Platform-locked, different feature sets, usage-based pricing (Google) | **Use behind `IMapProvider`.** Each platform uses its native provider | Swap to Mapbox or MapLibre if needed | Medium (annotation API differs) |
| **Merkle Trees** | Custom implementation complexity, no off-the-shelf mobile library | **Implement behind `IAuditTrail`.** Start with simple hash chain, evolve to full Merkle/AKD | Swap to Amazon QLDB or Meta AKD when ready | Low (same port) |
| **H3** | Uber-maintained, could lose support; mobile libraries less mature than server-side | **Implement behind `ISpatialIndex`.** Use h3-java (Android) and h3-swift (iOS) | Swap to S2 Geometry or custom grid | Low (adapter only) |

---

## 14. Open Questions to Decide

### Infrastructure & Accounts

1. **Azure subscription:** Do you have an Azure subscription for the backend, or does that need to be provisioned? (Blocks M3, M4)
2. **Apple Developer Program:** Individual ($99/year) or Organization ($99/year but requires D-U-N-S number)? Organization is better for enterprise distribution. (Blocks M2 iOS signing)
3. **Google Play Developer Account:** Needs a one-time $25 registration fee. Personal or organization? (Blocks M2 Android release)
4. **GitHub Enterprise:** Which plan tier? Enterprise Cloud should suffice initially. (Blocks M0)

### Service Providers

5. **SMS Provider:** Twilio is the most common choice. Have you evaluated others? (Blocks M4)
6. **NG911 Integration:** This varies by jurisdiction. Which regions are you targeting for launch? (Blocks M4 partially)
7. **HIPAA BAA:** Azure and your SMS/email providers all need Business Associate Agreements for health data handling. Is legal involved? (Blocks M4 health data features)

### Quality & Security

8. **Penetration Testing:** Before launch, a third-party pen test is strongly recommended for a life-safety app. Budget for this? (Blocks M6)

### Multi-Agent Workflow

9. **Agent API Keys:** Gemini Pro, Azure OpenAI, and JetBrains AI all need API keys or subscriptions. Which are already provisioned?
10. **Agent Autonomy Level:** Should agents be able to open PRs autonomously, or should all PRs be human-initiated? (Recommendation: agents create branches and open draft PRs; human promotes to "ready for review.")
11. **Code Review Policy for AI-Generated Code:** Should all AI-generated PRs get a human review, or can certain low-risk categories (docs, test-only, formatting) be auto-merged after CI passes?

### Technology Layer Decisions

12. **H3 library selection:** h3-java (Android) and h3-swift (iOS) are the obvious choices. Have you evaluated Uber's official libraries, or are there forks/alternatives you prefer?
13. **Merkle tree implementation:** Custom implementation (full control, more work) vs. leveraging an existing library? For the server side, Amazon QLDB or a custom append-only table with hash chains in SQL Server 2025?
14. **Meta's verifiable data structures:** Which specific Meta project were you recommended? (Candidates: AKD — Authenticated Key Directory, SEQ — Transparency Logs, or their Sapling/Mononoke VCS primitives adapted for audit)
15. **Background execution strategy:** iOS is the hardest platform for persistent background execution. The current candidates are: BGTaskScheduler + silent push notifications, Audio session keep-alive (controversial, may get rejected in App Store review), Location background mode (legitimate for TheWatch, since we track location). Which combination do you want to pursue?
16. **Offline spatial strategy:** When the device is offline, H3 index queries must run locally. Should we pre-cache H3 cells for the user's region on each sync, or compute on-device from raw coordinates?

---

*This document is a living plan. It should move into `docs/` in the monorepo once the repo is created and be updated as decisions are made. The milestone and issue structure described in Sections 11–12 should be created as actual GitHub Milestones and Issues during M0.*
