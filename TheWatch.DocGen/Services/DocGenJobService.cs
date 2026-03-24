// =============================================================================
// DocGenJobService.cs — Hangfire job methods for documentation generation.
// =============================================================================
// Contains the actual work methods invoked by Hangfire:
//   - ProcessSingleFileAsync: Analyze one file, generate stubs, write back
//   - RunFullScanAsync: Analyze entire solution, generate stubs, produce report
//
// These methods are designed to be idempotent — running them multiple times
// on the same file produces the same result (stubs are only written if missing
// or if the existing doc is a regenerable stub).
//
// Hangfire Integration:
//   - Fire-and-forget: BackgroundJob.Enqueue(() => ProcessSingleFileAsync(path, ct))
//   - Recurring: RecurringJob.AddOrUpdate("full-scan", () => RunFullScanAsync(ct), "*/15 * * * *")
//   - Retry: Hangfire auto-retries failed jobs with exponential backoff
//
// Example:
//   // Enqueued by RabbitMqConsumerService on file change:
//   _jobClient.Enqueue<DocGenJobService>(j => j.ProcessSingleFileAsync(path, ct));
//
//   // Scheduled by DocGenSchedulerService as recurring job:
//   RecurringJob.AddOrUpdate<DocGenJobService>("full-scan",
//       j => j.RunFullScanAsync(CancellationToken.None), "*/15 * * * *");
//
// WAL: Job start/complete/fail events are logged with timing and member counts.
// =============================================================================

using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheWatch.DocGen.Configuration;

namespace TheWatch.DocGen.Services;

/// <summary>
/// Hangfire job service containing the documentation generation work methods.
/// Each public method is a Hangfire job entry point — must be public and serializable.
/// </summary>
public class DocGenJobService
{
    private readonly ILogger<DocGenJobService> _logger;
    private readonly DocGenOptions _options;
    private readonly RoslynDocumentationAnalyzer _analyzer;
    private readonly XmlDocWriter _writer;
    private readonly DocumentationCoverageReporter _reporter;

    public DocGenJobService(
        ILogger<DocGenJobService> logger,
        IOptions<DocGenOptions> options,
        RoslynDocumentationAnalyzer analyzer,
        XmlDocWriter writer,
        DocumentationCoverageReporter reporter)
    {
        _logger = logger;
        _options = options.Value;
        _analyzer = analyzer;
        _writer = writer;
        _reporter = reporter;
    }

    /// <summary>
    /// Processes a single file: analyze → generate stubs → write back.
    /// Called by Hangfire as a fire-and-forget job when a file change is detected.
    /// </summary>
    [Queue("docgen-default")]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [5, 30, 120])]
    [JobDisplayName("DocGen: {0}")]
    public async Task ProcessSingleFileAsync(string filePath, CancellationToken ct)
    {
        _logger.LogInformation("[WAL-DOC] Job starting: ProcessSingleFile {Path}", filePath);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("[WAL-DOC] File not found, skipping: {Path}", filePath);
            return;
        }

        try
        {
            // 1. Analyze
            var result = await _analyzer.AnalyzeFileAsync(filePath, _options, ct);

            if (result.Gaps.Count == 0)
            {
                _logger.LogDebug("[WAL-DOC] No gaps found in {Path} ({Documented}/{Total} documented)",
                    filePath, result.DocumentedMembers, result.TotalMembers);
                return;
            }

            // 2. Generate stubs and write back
            var written = await _writer.ApplyDocumentationAsync(result.Gaps, _options, ct);

            sw.Stop();
            _logger.LogInformation(
                "[WAL-DOC] Job completed: ProcessSingleFile {Path} — {Written} docs written, " +
                "{Gaps} gaps found, {Coverage:F1}% coverage, {ElapsedMs}ms",
                filePath, written, result.Gaps.Count, result.CoveragePercent, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[WAL-DOC] Job failed: ProcessSingleFile {Path}", filePath);
            throw; // Let Hangfire handle retry
        }
    }

    /// <summary>
    /// Runs a full documentation scan across the entire solution.
    /// Called by Hangfire as a recurring job (default: every 15 minutes).
    /// Also called on startup if GenerateOnStartup is true.
    /// </summary>
    [Queue("docgen-scan")]
    [AutomaticRetry(Attempts = 1)]
    [JobDisplayName("DocGen: Full Scan")]
    public async Task RunFullScanAsync(CancellationToken ct)
    {
        var solutionRoot = _options.SolutionRoot;
        _logger.LogInformation("[WAL-DOC] Job starting: FullScan {Root}", solutionRoot);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (string.IsNullOrEmpty(solutionRoot) || !Directory.Exists(solutionRoot))
        {
            _logger.LogWarning("[WAL-DOC] SolutionRoot not set or missing: {Path}", solutionRoot);
            return;
        }

        try
        {
            // 1. Analyze all files
            var results = await _analyzer.AnalyzeDirectoryAsync(solutionRoot, _options, ct);

            // 2. Aggregate stats
            var totalMembers = results.Sum(r => r.TotalMembers);
            var documented = results.Sum(r => r.DocumentedMembers);
            var totalGaps = results.Sum(r => r.Gaps.Count);
            var coveragePercent = totalMembers > 0 ? (double)documented / totalMembers * 100 : 100;

            _logger.LogInformation(
                "[WAL-DOC] Full scan analysis complete: {FileCount} files, {TotalMembers} members, " +
                "{Documented} documented, {Gaps} gaps, {Coverage:F1}% coverage",
                results.Count, totalMembers, documented, totalGaps, coveragePercent);

            // 3. Generate stubs for all gaps
            var allGaps = results.SelectMany(r => r.Gaps).ToList();
            var written = 0;
            if (allGaps.Count > 0)
            {
                written = await _writer.ApplyDocumentationAsync(allGaps, _options, ct);
            }

            // 4. Generate coverage report
            if (!string.IsNullOrEmpty(_options.OutputReportPath))
            {
                await _reporter.GenerateReportAsync(results, _options.OutputReportPath, ct);
            }

            sw.Stop();
            _logger.LogInformation(
                "[WAL-DOC] Job completed: FullScan — {Written} docs written across {FileCount} files, " +
                "{Coverage:F1}% coverage, {ElapsedMs}ms",
                written, results.Count, coveragePercent, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[WAL-DOC] Job failed: FullScan {Root}", solutionRoot);
            throw;
        }
    }

    /// <summary>
    /// Generates only the coverage report without modifying any source files.
    /// Useful for CI/CD pipelines that want to check coverage without auto-fixing.
    /// </summary>
    [Queue("docgen-default")]
    [AutomaticRetry(Attempts = 1)]
    [JobDisplayName("DocGen: Coverage Report Only")]
    public async Task GenerateCoverageReportAsync(string outputPath, CancellationToken ct)
    {
        var solutionRoot = _options.SolutionRoot;
        _logger.LogInformation("[WAL-DOC] Job starting: CoverageReport → {OutputPath}", outputPath);

        var results = await _analyzer.AnalyzeDirectoryAsync(solutionRoot, _options, ct);
        await _reporter.GenerateReportAsync(results, outputPath, ct);

        var totalMembers = results.Sum(r => r.TotalMembers);
        var documented = results.Sum(r => r.DocumentedMembers);
        var coverage = totalMembers > 0 ? (double)documented / totalMembers * 100 : 100;

        _logger.LogInformation(
            "[WAL-DOC] Job completed: CoverageReport — {Coverage:F1}% ({Documented}/{Total}), saved to {Path}",
            coverage, documented, totalMembers, outputPath);
    }
}
