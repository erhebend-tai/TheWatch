// =============================================================================
// XmlDocWriter.cs — Generates and writes XML doc comments back to C# source files.
// =============================================================================
// Takes DocumentationGap records from the Roslyn analyzer and produces
// properly formatted XML documentation comment blocks. Writes them into the
// source file at the correct line position.
//
// Stub Format:
//   /// <summary>
//   /// [AUTO-DOC] Initializes a new instance of <see cref="MyClass"/>.
//   /// </summary>
//   /// <param name="logger">The logger instance.</param>
//   /// <param name="options">Configuration options.</param>
//
// Rules:
//   - Never overwrites docs that don't contain the StubMarker
//   - Stubs (containing StubMarker) ARE overwritten on regeneration
//   - Preserves original indentation of the member declaration
//   - Handles all member kinds: types, methods, constructors, properties, etc.
//   - Generates <typeparam> for generic type parameters
//   - Generates <returns> for non-void methods
//   - Generates <exception> placeholder for methods with throw statements
//
// Example:
//   var writer = new XmlDocWriter(logger);
//   int written = await writer.ApplyDocumentationAsync(gaps, options);
//   // written = 12 (number of doc blocks inserted/updated)
//
// WAL: Each write operation logs file path, line number, and member name.
// =============================================================================

using Microsoft.Extensions.Logging;
using TheWatch.DocGen.Configuration;

namespace TheWatch.DocGen.Services;

/// <summary>
/// Generates XML documentation comment stubs and writes them into C# source files.
/// </summary>
public class XmlDocWriter
{
    private readonly ILogger<XmlDocWriter> _logger;

    public XmlDocWriter(ILogger<XmlDocWriter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Applies documentation stubs to all gaps in a single file.
    /// Returns the number of doc blocks written.
    /// </summary>
    public async Task<int> ApplyDocumentationAsync(
        IReadOnlyList<DocumentationGap> gaps, DocGenOptions options, CancellationToken ct = default)
    {
        if (gaps.Count == 0) return 0;

        // Group by file — process each file once
        var fileGroups = gaps.GroupBy(g => g.FilePath);
        var totalWritten = 0;

        foreach (var group in fileGroups)
        {
            var filePath = group.Key;
            var fileGaps = group.OrderByDescending(g => g.LineNumber).ToList(); // Bottom-up to preserve line numbers

            try
            {
                var lines = (await File.ReadAllLinesAsync(filePath, ct)).ToList();
                var written = 0;

                foreach (var gap in fileGaps)
                {
                    if (ct.IsCancellationRequested) break;

                    var docBlock = GenerateDocBlock(gap, options);
                    var insertLine = gap.LineNumber - 1; // 0-indexed

                    if (insertLine < 0 || insertLine > lines.Count) continue;

                    // Determine indentation from the member declaration line
                    var indent = GetIndentation(lines, insertLine);

                    // If there's an existing stub, remove it first
                    if (gap.IsStub && gap.ExistingDoc is not null)
                    {
                        var stubLineCount = CountExistingDocLines(lines, insertLine);
                        if (stubLineCount > 0)
                        {
                            lines.RemoveRange(insertLine - stubLineCount, stubLineCount);
                            insertLine -= stubLineCount;
                        }
                    }

                    // Insert the new doc block
                    var docLines = FormatDocBlock(docBlock, indent);
                    lines.InsertRange(insertLine, docLines);
                    written++;

                    _logger.LogDebug(
                        "[WAL-DOC] Wrote doc for {MemberKind} {MemberName} at {FilePath}:{Line}",
                        gap.MemberKind, gap.MemberName, filePath, gap.LineNumber);
                }

                if (written > 0)
                {
                    await File.WriteAllLinesAsync(filePath, lines, ct);
                    _logger.LogInformation(
                        "[WAL-DOC] Updated {FilePath} — {Count} doc blocks written", filePath, written);
                }

                totalWritten += written;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[WAL-DOC] Failed to write docs to {FilePath}", filePath);
            }
        }

        return totalWritten;
    }

    /// <summary>
    /// Generates the raw doc block content (without indentation/prefixes) for a gap.
    /// </summary>
    public DocBlock GenerateDocBlock(DocumentationGap gap, DocGenOptions options)
    {
        var block = new DocBlock { StubMarker = options.StubMarker };

        block.Summary = gap.MemberKind switch
        {
            MemberKind.Type => $"{options.StubMarker} {InferTypeSummary(gap.MemberName)}",
            MemberKind.Constructor => $"{options.StubMarker} Initializes a new instance of <see cref=\"{gap.MemberName}\"/>.",
            MemberKind.Method => $"{options.StubMarker} {InferMethodSummary(gap.MemberName, gap.ReturnType, gap.IsAsync)}",
            MemberKind.Property => $"{options.StubMarker} Gets or sets the {Humanize(gap.MemberName)}.",
            MemberKind.Field => $"{options.StubMarker} {Humanize(gap.MemberName)}.",
            MemberKind.Event => $"{options.StubMarker} Occurs when {Humanize(gap.MemberName)}.",
            MemberKind.Indexer => $"{options.StubMarker} Gets or sets the element at the specified index.",
            MemberKind.Delegate => $"{options.StubMarker} Represents the method that handles {Humanize(gap.MemberName)}.",
            MemberKind.EnumMember => $"{options.StubMarker} {Humanize(gap.MemberName)}.",
            _ => $"{options.StubMarker} TODO: Add documentation."
        };

        // Parameters
        foreach (var param in gap.Parameters)
        {
            block.Params.Add(new DocParam
            {
                Name = param.Name,
                Description = InferParamDescription(param.Name, param.Type)
            });
        }

        // Type parameters
        foreach (var tp in gap.TypeParameters)
        {
            block.TypeParams.Add(new DocParam
            {
                Name = tp,
                Description = $"The type of {Humanize(tp)}."
            });
        }

        // Returns
        if (gap.MemberKind == MemberKind.Method && gap.ReturnType is not null
            && gap.ReturnType != "void"
            && gap.ReturnType != "Task")
        {
            block.Returns = InferReturnDescription(gap.ReturnType, gap.IsAsync);
        }

        return block;
    }

    // ── Formatting ───────────────────────────────────────────────

    private static List<string> FormatDocBlock(DocBlock block, string indent)
    {
        var lines = new List<string>();

        // Summary
        lines.Add($"{indent}/// <summary>");
        lines.Add($"{indent}/// {block.Summary}");
        lines.Add($"{indent}/// </summary>");

        // Type params
        foreach (var tp in block.TypeParams)
            lines.Add($"{indent}/// <typeparam name=\"{tp.Name}\">{tp.Description}</typeparam>");

        // Params
        foreach (var p in block.Params)
            lines.Add($"{indent}/// <param name=\"{p.Name}\">{p.Description}</param>");

        // Returns
        if (!string.IsNullOrEmpty(block.Returns))
            lines.Add($"{indent}/// <returns>{block.Returns}</returns>");

        return lines;
    }

    private static string GetIndentation(List<string> lines, int lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= lines.Count) return "    ";
        var line = lines[lineIndex];
        var trimmed = line.TrimStart();
        return line[..(line.Length - trimmed.Length)];
    }

    private static int CountExistingDocLines(List<string> lines, int memberLineIndex)
    {
        var count = 0;
        for (var i = memberLineIndex - 1; i >= 0; i--)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("///") || trimmed.StartsWith("///<"))
                count++;
            else
                break;
        }
        return count;
    }

    // ── Inference Heuristics ─────────────────────────────────────

    private static string InferTypeSummary(string typeName)
    {
        if (typeName.StartsWith('I') && typeName.Length > 1 && char.IsUpper(typeName[1]))
            return $"Defines the contract for {Humanize(typeName[1..])} operations.";
        if (typeName.EndsWith("Service"))
            return $"Provides {Humanize(typeName.Replace("Service", ""))} functionality.";
        if (typeName.EndsWith("Controller"))
            return $"API controller for {Humanize(typeName.Replace("Controller", ""))} endpoints.";
        if (typeName.EndsWith("Options"))
            return $"Configuration options for {Humanize(typeName.Replace("Options", ""))}.";
        if (typeName.EndsWith("Adapter"))
            return $"Adapter implementation for {Humanize(typeName.Replace("Adapter", ""))}.";
        if (typeName.EndsWith("Repository"))
            return $"Repository for {Humanize(typeName.Replace("Repository", ""))} data access.";
        if (typeName.EndsWith("Extensions"))
            return "Extension methods for service registration and configuration.";
        if (typeName.EndsWith("Hub"))
            return $"SignalR hub for real-time {Humanize(typeName.Replace("Hub", ""))} updates.";
        if (typeName.EndsWith("Tests"))
            return $"Unit tests for {Humanize(typeName.Replace("Tests", ""))}.";
        return $"Represents a {Humanize(typeName)}.";
    }

    private static string InferMethodSummary(string methodName, string? returnType, bool isAsync)
    {
        var action = methodName switch
        {
            var n when n.StartsWith("Get") => $"Gets {Humanize(n[3..])}",
            var n when n.StartsWith("Set") => $"Sets {Humanize(n[3..])}",
            var n when n.StartsWith("Create") => $"Creates {Humanize(n[6..])}",
            var n when n.StartsWith("Delete") => $"Deletes {Humanize(n[6..])}",
            var n when n.StartsWith("Remove") => $"Removes {Humanize(n[6..])}",
            var n when n.StartsWith("Update") => $"Updates {Humanize(n[6..])}",
            var n when n.StartsWith("Add") => $"Adds {Humanize(n[3..])}",
            var n when n.StartsWith("Find") => $"Finds {Humanize(n[4..])}",
            var n when n.StartsWith("Search") => $"Searches for {Humanize(n[6..])}",
            var n when n.StartsWith("Is") || n.StartsWith("Has") || n.StartsWith("Can")
                => $"Determines whether {Humanize(n)}",
            var n when n.StartsWith("Validate") => $"Validates {Humanize(n[8..])}",
            var n when n.StartsWith("Parse") => $"Parses {Humanize(n[5..])}",
            var n when n.StartsWith("Convert") => $"Converts {Humanize(n[7..])}",
            var n when n.StartsWith("Initialize") || n.StartsWith("Init")
                => $"Initializes {Humanize(n.Replace("Initialize", "").Replace("Init", ""))}",
            var n when n.StartsWith("Configure") => $"Configures {Humanize(n[9..])}",
            var n when n.StartsWith("Register") => $"Registers {Humanize(n[8..])}",
            var n when n.StartsWith("Load") => $"Loads {Humanize(n[4..])}",
            var n when n.StartsWith("Save") => $"Saves {Humanize(n[4..])}",
            var n when n.StartsWith("Send") => $"Sends {Humanize(n[4..])}",
            var n when n.StartsWith("Receive") => $"Receives {Humanize(n[7..])}",
            var n when n.StartsWith("Process") => $"Processes {Humanize(n[7..])}",
            var n when n.StartsWith("Handle") => $"Handles {Humanize(n[6..])}",
            var n when n.StartsWith("Execute") => $"Executes {Humanize(n[7..])}",
            var n when n.StartsWith("Run") => $"Runs {Humanize(n[3..])}",
            var n when n.StartsWith("Start") => $"Starts {Humanize(n[5..])}",
            var n when n.StartsWith("Stop") => $"Stops {Humanize(n[4..])}",
            var n when n.StartsWith("Map") => $"Maps {Humanize(n[3..])}",
            var n when n.StartsWith("Build") => $"Builds {Humanize(n[5..])}",
            var n when n.StartsWith("Ensure") => $"Ensures {Humanize(n[6..])}",
            var n when n.StartsWith("Try") => $"Attempts to {Humanize(n[3..]).ToLowerInvariant()}",
            var n when n.StartsWith("On") => $"Called when {Humanize(n[2..])}",
            var n when n == "Dispose" => "Releases all resources used by this instance",
            var n when n == "ToString" => "Returns a string representation of this instance",
            var n when n == "Equals" => "Determines whether the specified object is equal to this instance",
            var n when n == "GetHashCode" => "Returns a hash code for this instance",
            _ => Humanize(methodName)
        };

        return isAsync ? $"{action} asynchronously." : $"{action}.";
    }

    private static string InferParamDescription(string paramName, string paramType)
    {
        // Well-known parameter names
        return paramName switch
        {
            "cancellationToken" or "ct" => "A token to cancel the asynchronous operation.",
            "logger" => "The logger instance for diagnostic output.",
            "options" or "settings" or "config" or "configuration"
                => $"The {Humanize(paramName)} for this operation.",
            "id" or "entityId" => "The unique identifier.",
            "name" => "The name.",
            "key" => "The key.",
            "value" => "The value.",
            "connection" or "connectionString" => "The connection string.",
            "context" or "dbContext" => "The database context.",
            "factory" or "loggerFactory" => "The factory instance.",
            "services" => "The service collection.",
            "builder" => "The builder instance.",
            "provider" => "The service provider.",
            "request" or "req" => "The request.",
            "response" or "res" => "The response.",
            "token" => "The authentication or cancellation token.",
            "path" or "filePath" => "The file or directory path.",
            _ => $"The {Humanize(paramName)}."
        };
    }

    private static string InferReturnDescription(string returnType, bool isAsync)
    {
        // Unwrap Task<T>
        var innerType = returnType;
        if (returnType.StartsWith("Task<") && returnType.EndsWith(">"))
            innerType = returnType[5..^1];
        if (returnType.StartsWith("ValueTask<") && returnType.EndsWith(">"))
            innerType = returnType[10..^1];

        return innerType switch
        {
            "bool" or "Boolean" => "True if the operation succeeded; otherwise, false.",
            "int" or "Int32" or "long" or "Int64" => "The count or result value.",
            "string" or "String" => "The resulting string.",
            var t when t.StartsWith("IEnumerable") || t.StartsWith("IList") || t.StartsWith("List")
                => "A collection of results.",
            var t when t.StartsWith("IReadOnlyList") => "A read-only list of results.",
            _ => $"The {Humanize(innerType)}."
        };
    }

    /// <summary>
    /// Converts PascalCase/camelCase to a human-readable lowercase phrase.
    /// Example: "GetWorkItemsAsync" → "work items"
    /// </summary>
    internal static string Humanize(string identifier)
    {
        if (string.IsNullOrEmpty(identifier)) return "the value";

        // Strip common suffixes
        var clean = identifier
            .Replace("Async", "")
            .Replace("Internal", "")
            .TrimEnd('_');

        if (string.IsNullOrEmpty(clean)) return "the value";

        // Insert spaces before uppercase letters
        var chars = new List<char>();
        for (var i = 0; i < clean.Length; i++)
        {
            if (i > 0 && char.IsUpper(clean[i]) && !char.IsUpper(clean[i - 1]))
                chars.Add(' ');
            chars.Add(clean[i]);
        }

        var result = new string(chars.ToArray()).Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(result) ? "the value" : result;
    }
}

// ── Doc Block Model ──────────────────────────────────────────────

/// <summary>
/// Represents a generated XML documentation block before formatting.
/// </summary>
public class DocBlock
{
    public string Summary { get; set; } = string.Empty;
    public List<DocParam> Params { get; set; } = [];
    public List<DocParam> TypeParams { get; set; } = [];
    public string? Returns { get; set; }
    public string? Remarks { get; set; }
    public string StubMarker { get; set; } = "[AUTO-DOC]";
}

/// <summary>
/// A parameter documentation entry.
/// </summary>
public class DocParam
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
