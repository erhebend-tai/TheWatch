# TheWatch Merge Brief V2 — Full Agent Fleet + Build Server

**Date:** 2026-03-24
**Prepared by:** Cowork (Orchestrator)
**For:** Claude Code (merge executor), Barton (oversight)

---

## Fleet Status (14 Agent Workstreams)

### Cowork Agents (1-6) — ALL COMPLETE
| # | Branch | Scope | Status |
|---|--------|-------|--------|
| 1 | `feature/mobile-log-viewer-ui` | Android + iOS log viewer screens | Done |
| 2 | `feature/sos-lifecycle-correlation` | SOS lifecycle correlation tracking | Done |
| 3 | `feature/adapter-registry-mobile` | Mobile adapter registry + runtime switching | Done |
| 4 | `feature/signalr-mobile-client` | Mobile SignalR hub connection | Done |
| 5 | `feature/mobile-test-runner` | On-device test execution engine | Done |
| 6 | `feature/offline-first-sync-engine` | Generalized offline-first sync | Done |

### Claude Code Agents (7-10) — ALL COMPLETE
| # | Branch | Scope | Status |
|---|--------|-------|--------|
| 7 | `feature/alexa-skills` | Alexa Lambda skill (7 intents, SSML, i18n) | Done |
| 8 | `feature/google-home` | Google Home Actions SDK webhook | Done |
| 9 | `feature/iot-backend` | IIoTAlertPort, IIoTWebhookPort, controllers | Done |
| 10 | `feature/cli-dashboard` | Terminal.Gui TUI + ClaudeCodeBridge | Done |

### Claude Code Active Work (parallel)
| Scope | Status |
|-------|--------|
| Roslyn Analyzer service for CLI | In progress |
| Build Orchestrator service for CLI | In progress |
| Code Generator service for CLI | In progress |
| Tree-sitter multi-language parser for CLI | In progress |

### Cowork Build Server (new project)
| Scope | Status |
|-------|--------|
| TheWatch.BuildServer (LSIF/LSP/Base) | Done — ready to merge |

---

## Recommended Merge Order

### Phase 1: Foundation (zero dependencies)
1. **`feature/iot-backend`** — New ports (`IIoTAlertPort`, `IIoTWebhookPort`), controllers (`IoTAlertController`, `IoTAccountLinkController`), mock adapters. Purely additive to Aspire.
2. **`feature/signalr-mobile-client`** (Agent 4) — Android + iOS SignalR infrastructure. No conflicts with existing mobile code.
3. **`feature/mobile-log-viewer-ui`** (Agent 1) — New UI screens, reads from existing logging ports.

### Phase 2: Registry + Sync (rewires DI)
4. **`feature/adapter-registry-mobile`** (Agent 3) — **HIGH RISK**: Rewrites all 13 providers in `AppModule.kt` to route through `AdapterRegistry.getTier()`. Also modifies `TheWatchApp.swift`.
5. **`feature/offline-first-sync-engine`** (Agent 6) — Replaces `LogSyncWorker` with generalized `SyncEngine`. Touches `AppDatabase.kt` (adds `SyncTaskEntity`).

### Phase 3: Features that layer on top
6. **`feature/sos-lifecycle-correlation`** (Agent 2) — Adds `SosCorrelationManager` + `SosTimelineBuilder` provides to `AppModule.kt`. Merge AFTER Agent 3's registry rewrite.
7. **`feature/mobile-test-runner`** (Agent 5) — Depends on SignalR client (Agent 4). New test execution engine.

### Phase 4: IoT integrations
8. **`feature/alexa-skills`** (Agent 7) — External project (`TheWatch-Alexa`). No Aspire conflicts. Calls `/api/iot/*` endpoints from Agent 9.
9. **`feature/google-home`** (Agent 8) — External project (`TheWatch-GoogleHome`). Same pattern as Alexa.

### Phase 5: CLI + Build Server
10. **`feature/cli-dashboard`** (Agent 10) — `TheWatch.Cli` project. Already in the .sln. Claude Code is actively adding Roslyn/Build/CodeGen/TreeSitter services to it.
11. **TheWatch.BuildServer** (Cowork) — New project. Already added to .sln. Copy from outputs.

---

## High-Risk Files (touched by multiple agents)

### CRITICAL — `TheWatch-Android/.../di/AppModule.kt`
- **Agent 2** adds: `SosCorrelationManager`, `SosTimelineBuilder` providers
- **Agent 3** rewrites: All 13 existing providers to use `registry.getTier(slot)` pattern
- **Agent 5** may add: `TestStepExecutor`, `TestRunnerService` providers
- **Agent 6** adds: `SyncEngine`, `SyncTaskDao` providers
- **Merge strategy:** Merge Agent 3 FIRST (biggest rewrite), then cherry-pick additive providers from 2, 5, 6 using the `when (registry.getTier(slot))` pattern.

### CRITICAL — `TheWatch-Android/.../data/local/AppDatabase.kt`
- **Previous session** added: `LogEntryEntity`, bumped to version 2
- **Agent 6** adds: `SyncTaskEntity`, will need version bump to 3
- **Merge strategy:** Merge logging first, then sync engine. Bump version to 3 and add both entity classes.

### CRITICAL — `TheWatch-Android/.../TheWatchApplication.kt`
- **Previous session** added: WatchLogger init, LogSyncWorker enqueue, flush on trim
- **Agent 3** adds: AdapterRegistry initialization
- **Agent 6** may replace: LogSyncWorker with SyncEngine worker
- **Merge strategy:** Keep all three additions. SyncEngine replaces LogSyncWorker but must still handle log sync.

### HIGH — `TheWatch-iOS/TheWatchApp.swift`
- **Previous session** added: MockLoggingAdapter init, logger lifecycle
- **Agent 3** adds: AdapterRegistry.shared, SignalRAdapterSync.shared, environment injection
- **Merge strategy:** Both are additive to `init()` and `.onChange(of: scenePhase)`. Merge cleanly.

### MEDIUM — `TheWatch.sln`
- **Agent 10** added: `TheWatch.Cli` project entry
- **Cowork** added: `TheWatch.BuildServer` project entry
- **Merge strategy:** Both are additive. Just ensure no duplicate GUIDs.

### MEDIUM — `TheWatch.Dashboard.Api/Program.cs`
- **Previous session** added: `TestOrchestratorService` singleton (line 93-ish)
- **Agent 9** adds: IoT service registrations
- **Merge strategy:** Both additive to the service registration block.

---

## New AdapterSlots to Add to Registry

After merging Agent 3 (AdapterRegistry), these new slots should be added:

| Slot | Added by | Port Interface |
|------|----------|---------------|
| IoTAlert | Agent 9 | `IIoTAlertPort` |
| IoTWebhook | Agent 9 | `IIoTWebhookPort` |

The Aspire-side `AdapterRegistry.cs` needs corresponding properties:
```csharp
public string IoTAlert { get; set; } = "Mock";
public string IoTWebhook { get; set; } = "Mock";
```

---

## Build Server Integration Notes

The `TheWatch.BuildServer` project provides:
- **LSIF indexer** using Roslyn `MSBuildWorkspace` — indexes all 16+ solution projects
- **LSP server** via StreamJsonRpc — WebSocket at `ws://localhost:5002/lsp`
- **REST API** at `http://localhost:5002/api/build/*` — build queue, agent tracking, merge planning, symbol search
- **File watcher** — auto-rebuilds on .cs/.csproj changes
- **Agent branch registry** — pre-seeded with all 10 agent branches

The CLI dashboard should connect to the BuildServer's REST API for:
- `GET /api/build/status` → build health for ServicePanel
- `GET /api/build/agents` → agent tracking for AgentPanel
- `GET /api/build/index/stats` → code intelligence stats
- `POST /api/build/runs` → trigger builds from CLI

Since Claude Code is adding its own Build Orchestrator service to the CLI, coordinate so the CLI calls BuildServer's API rather than reimplementing build logic locally. The `ClaudeCodeBridge` in the CLI can invoke BuildServer endpoints.

---

## Post-Merge Validation Checklist

1. `dotnet build TheWatch.sln` — all 17 projects compile
2. `dotnet test TheWatch.sln` — shared tests pass
3. Android: `./gradlew assembleDebug` — all new entities/DAOs/adapters compile
4. iOS: `xcodebuild -scheme TheWatch-iOS` — SwiftUI views + adapters compile
5. Run BuildServer `--index-only` — verify LSIF indexes all projects and finds port-adapter links
6. Run CLI dashboard — verify FeaturePanel, AgentPanel, ServicePanel render
7. Smoke test: Alexa skill local invoke + Google Home webhook test
