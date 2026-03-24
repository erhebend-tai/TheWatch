// =============================================================================
// TheWatch.DocGen — Worker Service Entry Point
// =============================================================================
// Hosts the XML documentation generator with Hangfire job scheduling and
// RabbitMQ file-change event consumption.
//
// Startup sequence:
//   1. Bind DocGenOptions from configuration
//   2. Connect to RabbitMQ (Aspire-injected "thewatch-rabbitmq")
//   3. Configure Hangfire with in-memory storage
//   4. Register analysis services (Roslyn analyzer, XmlDocWriter, reporter)
//   5. Start FileWatcherService → publishes to RabbitMQ on .cs file changes
//   6. Start RabbitMqConsumer → dequeues file changes → enqueues Hangfire jobs
//   7. Start Hangfire recurring job: full-scan every 15 minutes
//
// Example — running via Aspire AppHost:
//   The AppHost injects DocGen:SolutionRoot and RabbitMQ connection automatically.
//
// Example — running standalone:
//   dotnet run --project TheWatch.DocGen -- \
//     --DocGen:SolutionRoot=C:\src\TheWatch-Aspire \
//     --ConnectionStrings:thewatch-rabbitmq=amqp://guest:guest@localhost:5672
//
// WAL: Worker lifecycle events logged via OpenTelemetry.
// =============================================================================

using Hangfire;
using Hangfire.InMemory;
using Microsoft.Extensions.Hosting;
using TheWatch.DocGen.Configuration;
using TheWatch.DocGen.Services;

var builder = Host.CreateApplicationBuilder(args);

// ── Aspire Service Defaults ──────────────────────────────────────
builder.AddServiceDefaults();

// ── Configuration ────────────────────────────────────────────────
builder.Services.Configure<DocGenOptions>(
    builder.Configuration.GetSection(DocGenOptions.SectionName));

// Auto-resolve SolutionRoot if not explicitly set
var docGenSection = builder.Configuration.GetSection(DocGenOptions.SectionName);
if (string.IsNullOrEmpty(docGenSection["SolutionRoot"]))
{
    // Default: two directories up from the DocGen project = solution root
    var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    docGenSection["SolutionRoot"] = solutionRoot;
}

// ── RabbitMQ (Aspire client integration) ─────────────────────────
// Connection string injected by Aspire as "thewatch-rabbitmq".
// The Aspire.RabbitMQ.Client package provides IConnection via DI.
builder.AddRabbitMQClient("thewatch-rabbitmq");

// ── Hangfire (in-memory storage for dev; swap to SQL/Redis in prod) ──
builder.Services.AddHangfire(config =>
{
    config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseInMemoryStorage(new InMemoryStorageOptions
        {
            MaxExpirationTime = TimeSpan.FromHours(6)
        });
});

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = 2; // Two concurrent doc-gen workers
    options.Queues = ["docgen-critical", "docgen-default", "docgen-scan"];
    options.ServerName = "TheWatch.DocGen";
});

// ── Services ─────────────────────────────────────────────────────
builder.Services.AddSingleton<RoslynDocumentationAnalyzer>();
builder.Services.AddSingleton<XmlDocWriter>();
builder.Services.AddSingleton<DocumentationCoverageReporter>();
builder.Services.AddSingleton<DocGenJobService>();

// ── Hosted Services (background workers) ─────────────────────────
builder.Services.AddHostedService<FileWatcherService>();      // Watches .cs files → RabbitMQ
builder.Services.AddHostedService<RabbitMqConsumerService>();  // RabbitMQ → Hangfire jobs
builder.Services.AddHostedService<DocGenSchedulerService>();   // Registers recurring Hangfire jobs

var host = builder.Build();
host.Run();
