// =============================================================================
// DocGenSchedulerService.cs — Registers Hangfire recurring jobs on startup.
// =============================================================================
// Configures the Hangfire recurring job schedule:
//   - "docgen-full-scan": Full solution scan every 15 minutes
//   - "docgen-startup-scan": One-time scan at startup (if GenerateOnStartup=true)
//
// Example:
//   // Registered as IHostedService — runs once on startup to configure Hangfire
//
// WAL: Job registration is logged at startup.
// =============================================================================

using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheWatch.DocGen.Configuration;

namespace TheWatch.DocGen.Services;

/// <summary>
/// Registers Hangfire recurring jobs for documentation generation on startup.
/// </summary>
public class DocGenSchedulerService : BackgroundService
{
    private readonly ILogger<DocGenSchedulerService> _logger;
    private readonly DocGenOptions _options;
    private readonly IRecurringJobManager _recurringJobs;
    private readonly IBackgroundJobClient _jobClient;

    public DocGenSchedulerService(
        ILogger<DocGenSchedulerService> logger,
        IOptions<DocGenOptions> options,
        IRecurringJobManager recurringJobs,
        IBackgroundJobClient jobClient)
    {
        _logger = logger;
        _options = options.Value;
        _recurringJobs = recurringJobs;
        _jobClient = jobClient;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[WAL-DOC] Configuring Hangfire recurring jobs");

        // Recurring: full solution scan every 15 minutes
        _recurringJobs.AddOrUpdate<DocGenJobService>(
            "docgen-full-scan",
            job => job.RunFullScanAsync(CancellationToken.None),
            "*/15 * * * *", // Every 15 minutes
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc,
                MisfireHandling = MisfireHandlingMode.Ignorable
            });

        _logger.LogInformation("[WAL-DOC] Registered recurring job: docgen-full-scan (every 15 min)");

        // One-time startup scan
        if (_options.GenerateOnStartup)
        {
            var jobId = _jobClient.Enqueue<DocGenJobService>(
                job => job.RunFullScanAsync(CancellationToken.None));
            _logger.LogInformation("[WAL-DOC] Enqueued startup full-scan job: {JobId}", jobId);
        }

        // Recurring: coverage report every hour (if output path configured)
        if (!string.IsNullOrEmpty(_options.OutputReportPath))
        {
            _recurringJobs.AddOrUpdate<DocGenJobService>(
                "docgen-coverage-report",
                job => job.GenerateCoverageReportAsync(_options.OutputReportPath, CancellationToken.None),
                "0 * * * *", // Every hour on the hour
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Utc,
                    MisfireHandling = MisfireHandlingMode.Ignorable
                });

            _logger.LogInformation("[WAL-DOC] Registered recurring job: docgen-coverage-report (hourly)");
        }

        return Task.CompletedTask;
    }
}
