// =============================================================================
// DocumentationCoverageReporter.cs — Generates XML coverage reports.
// =============================================================================
// Produces a structured XML report showing documentation coverage per file,
// per project, and per member kind. Useful for CI/CD gates and dashboards.
//
// Report Format:
//   <?xml version="1.0" encoding="utf-8"?>
//   <DocumentationCoverageReport>
//     <GeneratedAt>2026-03-24T10:30:00Z</GeneratedAt>
//     <SolutionRoot>C:\src\TheWatch-Aspire</SolutionRoot>
//     <Summary>
//       <TotalFiles>42</TotalFiles>
//       <TotalMembers>386</TotalMembers>
//       <DocumentedMembers>342</DocumentedMembers>
//       <UndocumentedMembers>44</UndocumentedMembers>
//       <CoveragePercent>88.6</CoveragePercent>
//     </Summary>
//     <Projects>
//       <Project Name="TheWatch.Shared">
//         <Files>
//           <File Path="Domain/Models/WorkItem.cs" Members="8" Documented="7" Gaps="1" />
//         </Files>
//       </Project>
//     </Projects>
//     <Gaps>
//       <Gap File="..." Line="42" Kind="Method" Name="DoSomething" />
//     </Gaps>
//   </DocumentationCoverageReport>
//
// Example:
//   var reporter = new DocumentationCoverageReporter(logger);
//   await reporter.GenerateReportAsync(results, "docs/coverage.xml");
//
// WAL: Report generation is logged with file count and coverage percentage.
// =============================================================================

using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace TheWatch.DocGen.Services;

/// <summary>
/// Generates XML documentation coverage reports from analysis results.
/// </summary>
public class DocumentationCoverageReporter
{
    private readonly ILogger<DocumentationCoverageReporter> _logger;

    public DocumentationCoverageReporter(ILogger<DocumentationCoverageReporter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Generates an XML coverage report and writes it to the specified path.
    /// </summary>
    public async Task GenerateReportAsync(
        List<AnalysisResult> results, string outputPath, CancellationToken ct = default)
    {
        _logger.LogInformation("[WAL-DOC] Generating coverage report: {Path}", outputPath);

        var totalMembers = results.Sum(r => r.TotalMembers);
        var documented = results.Sum(r => r.DocumentedMembers);
        var gaps = results.Sum(r => r.Gaps.Count);
        var coverage = totalMembers > 0 ? (double)documented / totalMembers * 100 : 100;

        // Group files by project (inferred from path)
        var projectGroups = results
            .Where(r => r.TotalMembers > 0)
            .GroupBy(r => InferProjectName(r.FilePath))
            .OrderBy(g => g.Key);

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement("DocumentationCoverageReport",
                new XElement("GeneratedAt", DateTime.UtcNow.ToString("O")),
                new XElement("Summary",
                    new XElement("TotalFiles", results.Count),
                    new XElement("TotalMembers", totalMembers),
                    new XElement("DocumentedMembers", documented),
                    new XElement("UndocumentedMembers", gaps),
                    new XElement("CoveragePercent", coverage.ToString("F1"))
                ),
                new XElement("Projects",
                    projectGroups.Select(pg =>
                        new XElement("Project",
                            new XAttribute("Name", pg.Key),
                            new XAttribute("Members", pg.Sum(r => r.TotalMembers)),
                            new XAttribute("Documented", pg.Sum(r => r.DocumentedMembers)),
                            new XAttribute("CoveragePercent",
                                pg.Sum(r => r.TotalMembers) > 0
                                    ? ((double)pg.Sum(r => r.DocumentedMembers) / pg.Sum(r => r.TotalMembers) * 100).ToString("F1")
                                    : "100.0"),
                            new XElement("Files",
                                pg.Select(r =>
                                    new XElement("File",
                                        new XAttribute("Path", GetRelativePath(r.FilePath)),
                                        new XAttribute("Members", r.TotalMembers),
                                        new XAttribute("Documented", r.DocumentedMembers),
                                        new XAttribute("Gaps", r.Gaps.Count),
                                        new XAttribute("CoveragePercent", r.CoveragePercent.ToString("F1"))
                                    )
                                )
                            )
                        )
                    )
                ),
                new XElement("Gaps",
                    results.SelectMany(r => r.Gaps).Select(g =>
                        new XElement("Gap",
                            new XAttribute("File", GetRelativePath(g.FilePath)),
                            new XAttribute("Line", g.LineNumber),
                            new XAttribute("Kind", g.MemberKind),
                            new XAttribute("Name", g.MemberName),
                            new XAttribute("IsStub", g.IsStub)
                        )
                    )
                ),
                // Per member-kind summary
                new XElement("ByMemberKind",
                    results.SelectMany(r => r.Gaps)
                        .GroupBy(g => g.MemberKind)
                        .OrderByDescending(g => g.Count())
                        .Select(g =>
                            new XElement("Kind",
                                new XAttribute("Name", g.Key),
                                new XAttribute("UndocumentedCount", g.Count())
                            )
                        )
                )
            )
        );

        // Ensure output directory exists
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using var stream = File.Create(outputPath);
        await doc.SaveAsync(stream, SaveOptions.OmitDuplicateNamespaces, ct);

        _logger.LogInformation(
            "[WAL-DOC] Coverage report saved: {Path} — {Coverage:F1}% ({Documented}/{Total})",
            outputPath, coverage, documented, totalMembers);
    }

    /// <summary>
    /// Infers the project name from a file path by finding the project folder name.
    /// </summary>
    private static string InferProjectName(string filePath)
    {
        var normalized = filePath.Replace('\\', '/');
        var parts = normalized.Split('/');

        // Look for a folder that starts with "TheWatch."
        for (var i = parts.Length - 2; i >= 0; i--)
        {
            if (parts[i].StartsWith("TheWatch.", StringComparison.OrdinalIgnoreCase))
                return parts[i];
        }

        return "Unknown";
    }

    /// <summary>
    /// Gets a display-friendly relative path from the file path.
    /// </summary>
    private static string GetRelativePath(string filePath)
    {
        var normalized = filePath.Replace('\\', '/');
        var idx = normalized.IndexOf("TheWatch.", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? normalized[idx..] : Path.GetFileName(filePath);
    }
}
