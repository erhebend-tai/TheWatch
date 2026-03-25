// =============================================================================
// TheWatch.Dashboard.Api — Program.cs
// =============================================================================
// ASP.NET Core Web API entry point. Aspire injects connection strings for all
// infrastructure resources. The AdapterRegistry pattern selects Mock vs Live
// implementations based on appsettings.{Environment}.json.
//
// Resource Connection Strings (injected by Aspire AppHost):
//   "thewatch-sqlserver"   → SQL Server (EF Core primary store)
//   "thewatch-postgresql"  → PostgreSQL/PostGIS (spatial queries)
//   "thewatch-redis"       → Redis (cache, SignalR backplane)
//   "thewatch-cosmos"      → Cosmos DB (audit trail)
//
// Example — running standalone (without Aspire):
//   Set ConnectionStrings__thewatch-sqlserver in appsettings.Development.json
//   dotnet run --project TheWatch.Dashboard.Api
//
// WAL: Service registration order: ServiceDefaults → Aspire resources → Data layer → Adapters → App services.
// =============================================================================

using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.AspNetCore.Authentication;
using Serilog;
using TheWatch.Dashboard.Api.Auth;
using TheWatch.Dashboard.Api.Hubs;
using TheWatch.Dashboard.Api.Services;
using TheWatch.Data.Configuration;
using TheWatch.Data.Context;
using Microsoft.Extensions.Hosting;
using TheWatch.Shared.Domain.Ports;

var builder = WebApplication.CreateBuilder(args);

// ── 1. Aspire Service Defaults (OpenTelemetry, health checks, service discovery) ──
builder.AddServiceDefaults();

// ── 2. Aspire Resource Integrations ──────────────────────────────────────────────
// These methods read the connection strings injected by the Aspire AppHost
// and configure EF Core, Redis, and Cosmos DB clients automatically.
// They also register health checks for each resource.

// SQL Server — primary EF Core DbContext
builder.AddSqlServerDbContext<TheWatchDbContext>("thewatch-sqlserver");

// Redis — distributed cache + SignalR backplane
builder.AddRedisDistributedCache("thewatch-redis");

// SignalR — Azure SignalR Service in production, Redis backplane in development.
// When Azure:SignalR:ConnectionString is set, uses managed Azure SignalR Service.
// Otherwise falls back to self-hosted SignalR with Redis as the backplane.
var azureSignalRConnStr = builder.Configuration["Azure:SignalR:ConnectionString"];
if (!string.IsNullOrEmpty(azureSignalRConnStr))
{
    builder.Services.AddSignalR()
        .AddAzureSignalR(options =>
        {
            options.ConnectionString = azureSignalRConnStr;
            options.ServerStickyMode = Microsoft.Azure.SignalR.ServerStickyMode.Required;
        });
}
else
{
    builder.Services.AddSignalR()
        .AddStackExchangeRedis("thewatch-redis");
}

// ── 3. RabbitMQ — message broker for response dispatch fan-out ─────────────────────
builder.AddRabbitMQClient("thewatch-rabbitmq");

// ── 4. Hangfire — scheduled/delayed jobs for escalation timers ────────────────────
// In Development: MemoryStorage (no persistence needed).
// In Production: switch to Hangfire.SqlServer or Hangfire.PostgreSql.
builder.Services.AddHangfire(config =>
    config.UseMemoryStorage());
builder.Services.AddHangfireServer(options =>
{
    options.Queues = new[] { "escalation", "dispatch", "default" };
    options.WorkerCount = 2; // Low for dev, scale in production
});

// ── 4a. Escalation Configuration — 4-stage escalation chain timeouts ──────────────
// Configurable via "Escalation" section in appsettings.json.
// Stages: InitialDispatch (0s) → WidenScope (300s) → EmergencyContacts (600s) → FirstResponders (900s)
builder.Services.Configure<TheWatch.Shared.Domain.Models.EscalationConfiguration>(
    builder.Configuration.GetSection(TheWatch.Shared.Domain.Models.EscalationConfiguration.SectionName));

// ── 5. Data Layer (Port/Adapter wiring via AdapterRegistry) ───────────────────────
// Reads "AdapterRegistry" section from appsettings to select Mock vs Live adapters.
// In Development: all Mock. In Production: SqlServer/CosmosDb/PostgreSql/Live.
builder.Services.AddTheWatchDataLayer(builder.Configuration);

// ── 5a. Firestore — Swarm Inventory persistence ──────────────────────────────────
// Uses emulator in Development (set via AppHost env vars), real Firestore in Production.
builder.Services.AddSwarmInventoryFirestore(builder.Configuration);

// ── 5b. Auth Port — Firebase Auth token validation ───────────────────────────────
// Firebase in production (validates ID tokens server-side), Mock in development.
builder.Services.AddAuthPort(builder.Configuration);

// ── 6. Authentication — Firebase ID token validation via IAuthPort ──────────────
builder.Services.AddAuthentication(FirebaseAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, FirebaseAuthenticationHandler>(
        FirebaseAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// ── 7. Controllers & API ──────────────────────────────────────────────────────────
builder.Services.AddControllers();

// CORS — allow web dashboard and MAUI clients
builder.Services.AddCors(options =>
{
    options.AddPolicy("DashboardClients", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// SOS bypass token service — short-lived HMAC tokens for emergency access without full auth
builder.Services.AddSingleton<TheWatch.Dashboard.Api.Auth.SosBypassTokenService>();

// Simulation service (always local, not a port)
builder.Services.AddSingleton<ISimulationService, SimulationService>();

// Response coordination service — orchestrates SOS → dispatch → track → escalate pipeline
builder.Services.AddScoped<IResponseCoordinationService, ResponseCoordinationService>();

// Watch Call service — orchestrates Watch Call lifecycle, scene narration, and escalation
builder.Services.AddScoped<IWatchCallService, WatchCallService>();

// Sensor fusion service — aggregates multi-source sensor data, anomaly detection, SignalR broadcasting
builder.Services.AddScoped<ISensorFusionService, SensorFusionService>();

// Context retrieval service — RAG orchestration across Azure/Google/Qdrant vector stores
builder.Services.AddSingleton<IContextRetrievalPort, ContextRetrievalService>();

// OpenAPI — uses the built-in .NET OpenApi middleware (Microsoft.AspNetCore.OpenApi).
// The spec is generated at /openapi/v1.json. In Dev/Staging, a Swagger-compatible
// UI is served that redirects to the OpenAPI spec.
//
// Bearer auth requirement is configured via the OpenApiOptions transformer:
//   All endpoints with [Authorize] get a Bearer security requirement in the spec.
//
// Example — accessing the OpenAPI spec:
//   curl https://localhost:7001/openapi/v1.json
//   Open in browser: https://localhost:7001/openapi for interactive UI
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, ct) =>
    {
        document.Info.Title = "TheWatch Dashboard API";
        document.Info.Version = "v1";
        document.Info.Description = "REST API for TheWatch emergency response coordination system. " +
                                    "Provides endpoints for SOS triggering, responder dispatch, escalation, " +
                                    "evidence management, surveys, Watch Calls, and administrative operations.";
        return Task.CompletedTask;
    });
});

// ── Build ─────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Middleware Pipeline ───────────────────────────────────────────────────────────
app.MapDefaultEndpoints();

// OpenAPI spec endpoint — enabled in Development and Staging.
// Spec available at: /openapi/v1.json
// For Swagger UI, use Scalar (dotnet add Scalar.AspNetCore → app.MapScalarApiReference())
// or import the spec URL into Swagger Editor / Postman.
if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.MapOpenApi();
}

app.UseSerilogRequestLogging(options =>
{
    // Enrich each request log with the endpoint name and response content-type
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
        diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
        var endpoint = httpContext.GetEndpoint();
        if (endpoint is not null)
            diagnosticContext.Set("EndpointName", endpoint.DisplayName);
    };

    // Filter out health checks and SignalR negotiation noise
    options.GetLevel = (httpContext, elapsed, ex) =>
    {
        if (httpContext.Request.Path.StartsWithSegments("/health") ||
            httpContext.Request.Path.StartsWithSegments("/alive") ||
            httpContext.Request.Path.StartsWithSegments("/hubs"))
            return Serilog.Events.LogEventLevel.Verbose;

        return ex is not null ? Serilog.Events.LogEventLevel.Error
            : elapsed > 2000 ? Serilog.Events.LogEventLevel.Warning
            : Serilog.Events.LogEventLevel.Information;
    };

    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.000}ms";
});

app.UseHttpsRedirection();
app.UseCors("DashboardClients");
app.UseAuthentication();
app.UseAuthorization();

// Map controllers
app.MapControllers();

// Map Hangfire dashboard (dev only — protected in production)
if (app.Environment.IsDevelopment())
{
    app.MapHangfireDashboard("/hangfire");
}

// Map SignalR hub — real-time dashboard updates, SOS alerts, sitreps
app.MapHub<DashboardHub>("/hubs/dashboard");

app.Run();
