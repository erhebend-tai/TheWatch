// =============================================================================
// TheWatch.DocGen — Worker Service Entry Point
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

var docGenSection = builder.Configuration.GetSection(DocGenOptions.SectionName);
if (string.IsNullOrEmpty(docGenSection["SolutionRoot"]))
{
    var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    docGenSection["SolutionRoot"] = solutionRoot;
}

// ── RabbitMQ ─────────────────────────────────────────────────────
builder.AddRabbitMQClient("thewatch-rabbitmq");

// ── Hangfire ─────────────────────────────────────────────────────
builder.Services.AddHangfire(config =>
{
    config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseInMemoryStorage(new InMemoryStorageOptions { MaxExpirationTime = TimeSpan.FromHours(6) });
});

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = 2;
    options.Queues = ["docgen-critical", "docgen-default", "docgen-scan"];
    options.ServerName = "TheWatch.DocGen";
});

// ── Services ─────────────────────────────────────────────────────
builder.Services.AddSingleton<RoslynDocumentationAnalyzer>();
builder.Services.AddSingleton<XmlDocWriter>();
builder.Services.AddSingleton<DocumentationCoverageReporter>();
builder.Services.AddSingleton<AiPromptGeneratorService>(); // Registered new service
builder.Services.AddSingleton<DocGenJobService>();

// ── Hosted Services ──────────────────────────────────────────────
builder.Services.AddHostedService<FileWatcherService>();
builder.Services.AddHostedService<RabbitMqConsumerService>();
builder.Services.AddHostedService<DocGenSchedulerService>();

var host = builder.Build();
host.Run();
