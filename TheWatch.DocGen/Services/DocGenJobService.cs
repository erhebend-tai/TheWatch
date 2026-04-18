// =============================================================================
// DocGenJobService.cs — Hangfire job methods for documentation generation.
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
    private readonly AiPromptGeneratorService _aiGenerator;

    public DocGenJobService(
        ILogger<DocGenJobService> logger,
        IOptions<DocGenOptions> options,
        RoslynDocumentationAnalyzer analyzer,
        XmlDocWriter writer,
        DocumentationCoverageReporter reporter,
        AiPromptGeneratorService aiGenerator)
    {
        _logger = logger;
        _options = options.Value;
        _analyzer = analyzer;
        _writer = writer;
        _reporter = reporter;
        _aiGenerator = aiGenerator;
    }

    /// <summary>
    /// Processes a single file: analyze → generate stubs → write back → generate AI prompts.
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
            // 1. Analyze for XML docs
            var result = await _analyzer.AnalyzeFileAsync(filePath, _options, ct);

            // 2. Generate stubs and write back if gaps exist
            if (result.Gaps.Count > 0)
            {
                await _writer.ApplyDocumentationAsync(result.Gaps, _options, ct);
            }

            // 3. Generate Azure AI JSONL Prompts
            await _aiGenerator.GeneratePromptsAsync(filePath, ct);

            sw.Stop();
            _logger.LogInformation(
                "[WAL-DOC] Job completed: ProcessSingleFile {Path} — Gaps: {Gaps}, Coverage: {Coverage:F1}%, {ElapsedMs}ms",
                filePath, result.Gaps.Count, result.CoveragePercent, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[WAL-DOC] Job failed: ProcessSingleFile {Path}", filePath);
            throw; // Let Hangfire handle retry
        }
    }

    /// <summary>
    /// Runs a full documentation scan across the entire solution.
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
            var results = await _analyzer.AnalyzeDirectoryAsync(solutionRoot, _options, ct);
            var allGaps = results.SelectMany(r => r.Gaps).ToList();
            
            if (allGaps.Count > 0)
            {
                await _writer.ApplyDocumentationAsync(allGaps, _options, ct);
            }

            // Generate prompts for all processed files
            foreach (var res in results)
            {
                await _aiGenerator.GeneratePromptsAsync(res.FilePath, ct);
            }

            if (!string.IsNullOrEmpty(_options.OutputReportPath))
            {
                await _reporter.GenerateReportAsync(results, _options.OutputReportPath, ct);
            }

            sw.Stop();
            _logger.LogInformation("[WAL-DOC] FullScan complete: {FileCount} files, {ElapsedMs}ms", results.Count, sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[WAL-DOC] Job failed: FullScan {Root}", solutionRoot);
            throw;
        }
    }
}
