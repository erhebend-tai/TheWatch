# Changelog

All notable changes to TheWatch will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-03-24

### Summary

First production release of TheWatch emergency response platform. Spans eight
development milestones (M0-M7), delivering a complete SOS-to-resolution pipeline
with real-time coordination, multi-platform mobile clients, AI-assisted operations,
and production-grade infrastructure.

### Milestone 0 — Project Foundation

- Established hexagonal (ports-and-adapters) architecture with TheWatch.Shared domain core
- Created Arcade-style versioning system (eng/Versions.props) with CI/release suffix computation
- Set up solution structure: Dashboard.Api, Dashboard.Web, WorkerServices, Functions, Shared, Data
- Configured Aspire AppHost for local orchestration of SQL Server, Redis, RabbitMQ, and Cosmos DB
- Added .editorconfig, Directory.Build.props, and global.json for consistent build configuration

### Milestone 1 — Domain Model and Port Interfaces

- Defined core domain models: Incident, Responder, SOS trigger, Escalation, AuditEntry
- Created port interfaces: IAuditTrail, IResponseCoordinationPort, IAuthPort, IGuardReportPort
- Implemented AdapterRegistry pattern for runtime selection of Mock vs Live adapters
- Added EgressModels, ThreatModels, SensorModels, StructureModels, PersonCapabilityModels
- Defined enums: AuditAction, EgressType, ThreatType, SensorType, StructureType, PersonCapabilityType

### Milestone 2 — Data Layer and Adapters

- Implemented AuditTrail adapters: CosmosDb, SqlServer, PostgreSql, Mock
- Created AuditTrailAdapterBase with shared validation and timestamp logic
- Built Firebase Auth adapter for ID token validation
- Added Qdrant vector search adapter for RAG-based context retrieval
- Configured ServiceCollectionExtensions for data layer DI wiring from appsettings

### Milestone 3 — API and Real-Time Pipeline

- Built ResponseController with full SOS trigger, dispatch, track, and resolve endpoints
- Implemented ResponseCoordinationService with 4-stage escalation chain (dispatch, widen, emergency contacts, first responders)
- Created DashboardHub (SignalR) for real-time SOS alerts, sitreps, and responder tracking
- Added GuardReportController for responder-submitted check-in reports
- Implemented rate limiting, security headers middleware, and SOS bypass token service
- Configured Hangfire for escalation timer jobs with priority queues

### Milestone 4 — Mobile Clients (Android and iOS)

- Built Android app models: Responder, GuardReport, EgressModels, SensorModels, ThreatModels, StructureModels, PersonCapabilityModels (Kotlin/Jetpack)
- Built iOS app models: Responder, GuardReport, EgressModels, SensorModels, ThreatModels, StructureModels, PersonCapabilityModels (Swift/SwiftUI)
- Defined VolunteerService protocol (iOS) for responder registration and availability management
- Created store listing metadata for Google Play Store and Apple App Store

### Milestone 5 — AI Swarm and Agents

- Integrated Azure OpenAI with GPT-4.1 (supervisor), GPT-4o (general), GPT-4o-mini (specialist), and text-embedding-3-large (RAG)
- Created AzureOpenAISwarmAdapter for multi-agent task orchestration
- Built SwarmCommand CLI for interactive swarm operations
- Implemented MockSwarmPort, MockSwarmAgentAdapter, MockSwarmInventoryPort for development
- Added SwarmPresets for pre-configured agent topologies
- Built scene narration adapter for AI-generated incident summaries

### Milestone 6 — Infrastructure and CI/CD

- Created infra/main.bicep deploying: SignalR, Redis, SQL Server, Container Apps, RabbitMQ, Azure OpenAI, Log Analytics
- Built GitHub Actions CI workflow with build, test, format check, and artifact publishing
- Built GitHub Actions Deploy workflow with staging auto-deploy and production approval gates
- Created container image build pipeline for Dashboard.Api, Functions, and WorkerServices
- Added build.cmd and build.sh cross-platform build scripts
- Configured TeamCity build definitions (.teamcity/)

### Milestone 7 — Production Readiness

- Created infra/monitoring.bicep with 7 alert rules: 5xx rate, SOS latency, queue depth, SignalR failures, container restarts, CPU, memory
- Built Azure Portal operations dashboard (infra/dashboards.json) with 5 panels
- Created release workflow (.github/workflows/release.yml) with manual trigger, NuGet packaging, and GitHub Release creation
- Added CHANGELOG.md following Keep a Changelog format
- Created RELEASE_CHECKLIST.md with step-by-step production launch procedure
- Configured Firebase hosting (firebase.json, firestore.rules, firestore.indexes.json)
- Added Kubernetes deployment manifests (deploy/)

[1.0.0]: https://github.com/erheb/TheWatch/releases/tag/v1.0.0
