# TheWatch

Life-safety emergency response platform with community-powered dispatch, AI-assisted de-escalation, and multi-provider resilience.

## Architecture

.NET Aspire orchestrated backend (net10.0) with hexagonal architecture. Every external dependency lives behind a port interface — adapters are swapped via `AdapterRegistry` configuration at startup.

### Backend (.NET 10 / Aspire)

| Project | Purpose |
|---------|---------|
| `TheWatch.AppHost` | Aspire orchestrator — wires service discovery, container resources |
| `TheWatch.Dashboard.Api` | REST API + SignalR hub for real-time coordination |
| `TheWatch.Dashboard.Web` | Blazor SSR + InteractiveServer with MudBlazor 8.6.0 |
| `TheWatch.Shared` | Domain ports (interfaces), models, enums, configuration |
| `TheWatch.Data` | Adapter implementations + DI registration |
| `TheWatch.Adapters.Mock` | Full-featured in-memory mock adapters (no cloud needed) |
| `TheWatch.DSL` | Deterministic rule engine (ANTLR4 grammar, no AI at runtime) |
| `TheWatch.Functions` | Azure Functions for background processing |
| `TheWatch.ServiceDefaults` | Aspire service defaults (OpenTelemetry, health checks) |

### Mobile

| Project | Stack |
|---------|-------|
| `TheWatch-Android` | Kotlin, Jetpack Compose, Hilt, Room, Material 3 |
| `TheWatch-iOS` | Swift 5.9, SwiftUI, SwiftData, MapKit, iOS 17+ |

### Port Interfaces (Hexagonal Architecture)

Core domain ports in `TheWatch.Shared/Domain/Ports/`:

| Port | Purpose | Production Adapter |
|------|---------|-------------------|
| `IStorageService` | Primary data persistence | SqlServer, PostgreSql, CosmosDb, Firestore |
| `IAuditTrail` | Immutable audit log | SqlServer, PostgreSql, CosmosDb |
| `ISpatialIndex` | Geospatial queries | PostgreSql (PostGIS) |
| `IEmbeddingPort` | Text-to-vector embedding | Azure OpenAI (text-embedding-3-large, 3072 dims) |
| `IVectorSearchPort` | Vector similarity search | Qdrant (HNSW) |
| `IAuthPort` | Authentication + token validation | Firebase Auth |
| `ISwarmInventoryPort` | Agent swarm state | Firestore |
| `IResponseRequestPort` | SOS lifecycle management | Mock (production TBD) |
| `IResponseDispatchPort` | Fan-out dispatch via RabbitMQ | Mock (production TBD) |
| `ICCTVPort` | Personal CCTV camera integration | Mock (RTSP/ONVIF/WebRTC planned) |
| `IGuardReportPort` | Security guard reporting + escalation | Mock |
| `IWatchCallPort` | Live video watch calls + enrollment | Mock (Firestore planned) |
| `ISceneNarrationPort` | AI vision scene narrator (de-escalation) | Mock (Azure OpenAI GPT-4o vision planned) |
| `INotificationSendPort` | Push notifications | Mock (FCM/APNs planned) |
| `IIoTAlertPort` | Smart home device alerts | Mock (Alexa/Google Home) |

### Multi-Provider RAG Strategy

| Embedding Provider | Vector Store | Index Type |
|-------------------|-------------|------------|
| Azure OpenAI (text-embedding-3-large) | Cosmos DB | DiskANN |
| Google Gemini (text-embedding-004) | Firestore | KNN |
| VoyageAI (voyage-3) | Qdrant | HNSW |

### Infrastructure

| Service | Dev | Production |
|---------|-----|------------|
| SignalR | Redis backplane | Azure SignalR Service |
| Message Queue | — | RabbitMQ (Container App) |
| SQL Database | LocalDB / Emulator | Azure SQL (Central US) |
| Document DB | Firestore Emulator | Google Cloud Firestore |
| Vector DB | Qdrant (Docker) | Qdrant (Container App) |
| Auth | MockAuthAdapter | Firebase Auth (Google/GitHub) |
| Video Signaling | Google STUN | Google STUN + Cloudflare TURN |
| AI Vision | Mock narrations | Azure OpenAI GPT-4o |

## Key Features

### Emergency Response Coordination
SOS triggers (phrase detection, quick-tap, manual button, wearable, fall detection) create a `ResponseRequest` with scope-based dispatch (CheckIn, Neighborhood, Community, Evacuation, SilentDuress). Auto-escalation policies range from manual to full cascade with 911 integration.

### Watch Calls (Live Video De-escalation)
Users enroll in their neighborhood and complete mandatory mock call training before participating in live calls. During a call, an AI scene narrator describes what it sees in neutral, factual language — no race, no assumed intent, no subjective assessments. Users who complete mock calls experience being the "subject" first, building empathy and reducing bias.

### Guard Reporting
Security guards (professional, neighborhood watch, campus security) file structured observation reports that can be escalated to full Watch Calls or SOS dispatch with one action.

### CCTV Integration
Personal security cameras (RTSP, ONVIF, HLS, WebRTC, cloud APIs) feed into a detection pipeline. Alerts can auto-escalate to SOS based on user-configured thresholds.

### Evidence Chain
Photos, video, audio, and sitreps are captured during active incidents, processed (thumbnails, transcription, moderation), and broadcast to responders via SignalR.

### Responder Communication
Incident-scoped chat with server-side guardrails (PII redaction, profanity filter, threat detection, rate limiting). Messages that fail guardrails are blocked before delivery.

## Development

### Prerequisites

- .NET 10 SDK (10.0.201+)
- Node.js 20+ (for Firebase CLI)
- Docker (for Aspire container resources)

### Quick Start

```bash
# Restore and build
dotnet build

# Run with Aspire (starts all services + emulators)
dotnet run --project TheWatch.AppHost
```

### Configuration

Adapter selection is driven by `appsettings.json`:

```json
{
  "AdapterRegistry": {
    "PrimaryStorage": "Mock",
    "Embedding": "Mock",
    "VectorSearch": "Mock",
    "WatchCall": "Mock",
    "SceneNarration": "Mock"
  }
}
```

Set any adapter to its production value (e.g., `"AzureOpenAI"`, `"Qdrant"`, `"Firestore"`) and provide the corresponding connection strings / API keys.

## Deployment

See `deploy/README.md` for Azure Container Apps deployment, provisioned resources, and Bicep templates.

## Pre-existing Build Errors

Two files have known errors from prior development that are delegated to a separate workstream:

- `ScreenFlow.razor` — quote-nesting errors (CS1026, CS7036)
- `ResponseCoordinationService.cs` — constructor parameter mismatches (CS7036, CS1739)

These do not affect the rest of the solution.
