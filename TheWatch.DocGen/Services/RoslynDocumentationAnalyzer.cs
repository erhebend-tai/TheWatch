// =============================================================================
// RoslynDocumentationAnalyzer.cs — Parses C# files and identifies undocumented members.
// =============================================================================
// Uses Microsoft.CodeAnalysis.CSharp to build SyntaxTrees from source files,
// then walks the tree to find public/protected/internal types and members
// that lack XML documentation comments.
//
// Analysis Output:
//   Each undocumented member is reported as a DocumentationGap record containing:
//     - FilePath, LineNumber, MemberKind (class/method/property/etc.)
//     - MemberName, FullyQualifiedName, Parameters (for methods)
//     - ReturnType (for methods/properties)
//     - ExistingDoc (null if missing, or the current doc if it's a stub)
//     - IsStub (true if existing doc contains the StubMarker)
//
// Example:
//   var analyzer = new RoslynDocumentationAnalyzer(logger);
//   var gaps = await analyzer.AnalyzeFileAsync("path/to/File.cs", options);
//   // gaps = [ { MemberName="DoSomething", MemberKind=Method, LineNumber=42, ... } ]
//
// WAL: Each analysis operation logs file path, parse time, and gap count.
// =============================================================================

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using TheWatch.DocGen.Configuration;

namespace TheWatch.DocGen.Services;

/// <summary>
/// Roslyn-based analyzer that parses C# source files and identifies members
/// missing XML documentation comments.
/// </summary>
public class RoslynDocumentationAnalyzer
{
    private readonly ILogger<RoslynDocumentationAnalyzer> _logger;

    public RoslynDocumentationAnalyzer(ILogger<RoslynDocumentationAnalyzer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyzes a single C# source file and returns all documentation gaps.
    /// </summary>
    public async Task<AnalysisResult> AnalyzeFileAsync(string filePath, DocGenOptions options, CancellationToken ct = default)
    {
        _logger.LogDebug("[WAL-DOC] AnalyzeFile starting: {FilePath}", filePath);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var sourceText = await File.ReadAllTextAsync(filePath, ct);
        var tree = CSharpSyntaxTree.ParseText(sourceText, path: filePath, cancellationToken: ct);
        var root = await tree.GetRootAsync(ct);

        var gaps = new List<DocumentationGap>();
        var documented = 0;
        var total = 0;

        // Walk all type declarations
        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (!ShouldDocument(typeDecl.Modifiers, options))
                continue;

            total++;
            var typeGap = CheckDocumentation(typeDecl, filePath, MemberKind.Type, GetTypeName(typeDecl), options);
            if (typeGap is not null)
                gaps.Add(typeGap);
            else
                documented++;

            // Check members within the type
            foreach (var member in typeDecl.Members)
            {
                var (kind, name, shouldCheck) = ClassifyMember(member, options);
                if (!shouldCheck) continue;

                total++;
                var memberGap = member switch
                {
                    MethodDeclarationSyntax method => CheckMethodDocumentation(method, filePath, options),
                    ConstructorDeclarationSyntax ctor => CheckConstructorDocumentation(ctor, filePath, options),
                    PropertyDeclarationSyntax prop => CheckDocumentation(prop, filePath, MemberKind.Property, name, options),
                    EventDeclarationSyntax evt => CheckDocumentation(evt, filePath, MemberKind.Event, name, options),
                    FieldDeclarationSyntax field => CheckFieldDocumentation(field, filePath, options),
                    IndexerDeclarationSyntax indexer => CheckDocumentation(indexer, filePath, MemberKind.Indexer, "this[]", options),
                    DelegateDeclarationSyntax del => CheckDocumentation(del, filePath, MemberKind.Delegate, del.Identifier.Text, options),
                    EnumMemberDeclarationSyntax enumMember => CheckDocumentation(enumMember, filePath, MemberKind.EnumMember, enumMember.Identifier.Text, options),
                    _ => null
                };

                if (memberGap is not null)
                    gaps.Add(memberGap);
                else
                    documented++;
            }
        }

        // Check top-level enum declarations
        foreach (var enumDecl in root.DescendantNodes().OfType<EnumDeclarationSyntax>())
        {
            if (!ShouldDocument(enumDecl.Modifiers, options))
                continue;

            total++;
            var enumGap = CheckDocumentation(enumDecl, filePath, MemberKind.Type, enumDecl.Identifier.Text, options);
            if (enumGap is not null)
                gaps.Add(enumGap);
            else
                documented++;

            foreach (var member in enumDecl.Members)
            {
                total++;
                var memberGap = CheckDocumentation(member, filePath, MemberKind.EnumMember, member.Identifier.Text, options);
                if (memberGap is not null)
                    gaps.Add(memberGap);
                else
                    documented++;
            }
        }

        // Check top-level interface methods/properties
        foreach (var ifaceDecl in root.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
        {
            if (!ShouldDocument(ifaceDecl.Modifiers, options))
                continue;

            total++;
            var ifaceGap = CheckDocumentation(ifaceDecl, filePath, MemberKind.Type, ifaceDecl.Identifier.Text, options);
            if (ifaceGap is not null)
                gaps.Add(ifaceGap);
            else
                documented++;

            foreach (var member in ifaceDecl.Members)
            {
                var (kind, name, shouldCheck) = ClassifyMember(member, options);
                if (!shouldCheck) continue;

                total++;
                var memberGap = member switch
                {
                    MethodDeclarationSyntax method => CheckMethodDocumentation(method, filePath, options),
                    PropertyDeclarationSyntax prop => CheckDocumentation(prop, filePath, MemberKind.Property, name, options),
                    EventDeclarationSyntax evt => CheckDocumentation(evt, filePath, MemberKind.Event, name, options),
                    _ => null
                };

                if (memberGap is not null)
                    gaps.Add(memberGap);
                else
                    documented++;
            }
        }

        // Check record declarations
        foreach (var recordDecl in root.DescendantNodes().OfType<RecordDeclarationSyntax>())
        {
            if (!ShouldDocument(recordDecl.Modifiers, options))
                continue;

            total++;
            var recordGap = CheckDocumentation(recordDecl, filePath, MemberKind.Type, recordDecl.Identifier.Text, options);
            if (recordGap is not null)
                gaps.Add(recordGap);
            else
                documented++;
        }

        sw.Stop();
        _logger.LogInformation(
            "[WAL-DOC] AnalyzeFile completed: {FilePath} — {GapCount} gaps, {Documented}/{Total} documented, {ElapsedMs}ms",
            filePath, gaps.Count, documented, total, sw.ElapsedMilliseconds);

        return new AnalysisResult
        {
            FilePath = filePath,
            Gaps = gaps,
            TotalMembers = total,
            DocumentedMembers = documented,
            AnalysisDurationMs = sw.ElapsedMilliseconds
        };
    }

    /// <summary>
    /// Analyzes all C# files in a directory tree.
    /// </summary>
    public async Task<List<AnalysisResult>> AnalyzeDirectoryAsync(
        string rootPath, DocGenOptions options, CancellationToken ct = default)
    {
        var files = Directory.EnumerateFiles(rootPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsExcluded(f, options))
            .ToList();

        _logger.LogInformation("[WAL-DOC] Full scan starting: {FileCount} files in {Root}", files.Count, rootPath);

        var results = new List<AnalysisResult>();
        var semaphore = new SemaphoreSlim(options.MaxConcurrency);

        var tasks = files.Select(async file =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                return await AnalyzeFileAsync(file, options, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[WAL-DOC] Failed to analyze {FilePath}", file);
                return new AnalysisResult { FilePath = file, Gaps = [], TotalMembers = 0, DocumentedMembers = 0 };
            }
            finally
            {
                semaphore.Release();
            }
        });

        results.AddRange(await Task.WhenAll(tasks));
        return results;
    }

    // ── Private Helpers ──────────────────────────────────────────────

    private bool IsExcluded(string filePath, DocGenOptions options)
    {
        var normalized = filePath.Replace('\\', '/');
        return options.ExcludedPaths.Any(ex => normalized.Contains($"/{ex}/", StringComparison.OrdinalIgnoreCase))
            || options.ExcludedFiles.Any(ex => Path.GetFileName(filePath).Equals(ex, StringComparison.OrdinalIgnoreCase));
    }

    private bool ShouldDocument(SyntaxTokenList modifiers, DocGenOptions options)
    {
        if (modifiers.Any(SyntaxKind.PublicKeyword)) return true;
        if (modifiers.Any(SyntaxKind.ProtectedKeyword)) return true;
        if (options.IncludeInternalMembers && modifiers.Any(SyntaxKind.InternalKeyword)) return true;
        if (options.IncludePrivateMembers && modifiers.Any(SyntaxKind.PrivateKeyword)) return true;

        // Interface members are implicitly public
        if (modifiers.Count == 0) return true;

        return false;
    }

    private static string GetTypeName(TypeDeclarationSyntax typeDecl)
    {
        return typeDecl switch
        {
            ClassDeclarationSyntax c => c.Identifier.Text,
            StructDeclarationSyntax s => s.Identifier.Text,
            InterfaceDeclarationSyntax i => i.Identifier.Text,
            RecordDeclarationSyntax r => r.Identifier.Text,
            _ => typeDecl.Identifier.Text
        };
    }

    private (MemberKind Kind, string Name, bool ShouldCheck) ClassifyMember(
        MemberDeclarationSyntax member, DocGenOptions options)
    {
        return member switch
        {
            MethodDeclarationSyntax m when ShouldDocument(m.Modifiers, options)
                => (MemberKind.Method, m.Identifier.Text, true),
            ConstructorDeclarationSyntax c when ShouldDocument(c.Modifiers, options)
                => (MemberKind.Constructor, c.Identifier.Text, true),
            PropertyDeclarationSyntax p when ShouldDocument(p.Modifiers, options)
                => (MemberKind.Property, p.Identifier.Text, true),
            EventDeclarationSyntax e when ShouldDocument(e.Modifiers, options)
                => (MemberKind.Event, e.Identifier.Text, true),
            FieldDeclarationSyntax f when ShouldDocument(f.Modifiers, options)
                => (MemberKind.Field, f.Declaration.Variables.First().Identifier.Text, true),
            IndexerDeclarationSyntax ix when ShouldDocument(ix.Modifiers, options)
                => (MemberKind.Indexer, "this[]", true),
            DelegateDeclarationSyntax d when ShouldDocument(d.Modifiers, options)
                => (MemberKind.Delegate, d.Identifier.Text, true),
            EnumMemberDeclarationSyntax em
                => (MemberKind.EnumMember, em.Identifier.Text, true),
            _ => (MemberKind.Unknown, string.Empty, false)
        };
    }

    private DocumentationGap? CheckDocumentation(
        SyntaxNode node, string filePath, MemberKind kind, string name, DocGenOptions options)
    {
        var trivia = node.GetLeadingTrivia();
        var xmlTrivia = trivia.FirstOrDefault(t =>
            t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
            t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

        if (xmlTrivia == default)
        {
            // No documentation at all
            return new DocumentationGap
            {
                FilePath = filePath,
                LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                MemberKind = kind,
                MemberName = name,
                ExistingDoc = null,
                IsStub = false
            };
        }

        // Has documentation — check if it's a stub
        var docText = xmlTrivia.ToFullString();
        if (docText.Contains(options.StubMarker))
        {
            return new DocumentationGap
            {
                FilePath = filePath,
                LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                MemberKind = kind,
                MemberName = name,
                ExistingDoc = docText,
                IsStub = true
            };
        }

        // Has real documentation — no gap
        return null;
    }

    private DocumentationGap? CheckMethodDocumentation(
        MethodDeclarationSyntax method, string filePath, DocGenOptions options)
    {
        var gap = CheckDocumentation(method, filePath, MemberKind.Method, method.Identifier.Text, options);
        if (gap is not null)
        {
            gap.Parameters = method.ParameterList.Parameters
                .Select(p => new ParameterInfo { Name = p.Identifier.Text, Type = p.Type?.ToString() ?? "object" })
                .ToList();
            gap.ReturnType = method.ReturnType.ToString();
            gap.IsAsync = method.Modifiers.Any(SyntaxKind.AsyncKeyword);
            gap.TypeParameters = method.TypeParameterList?.Parameters
                .Select(tp => tp.Identifier.Text).ToList() ?? [];
        }
        return gap;
    }

    private DocumentationGap? CheckConstructorDocumentation(
        ConstructorDeclarationSyntax ctor, string filePath, DocGenOptions options)
    {
        var gap = CheckDocumentation(ctor, filePath, MemberKind.Constructor, ctor.Identifier.Text, options);
        if (gap is not null)
        {
            gap.Parameters = ctor.ParameterList.Parameters
                .Select(p => new ParameterInfo { Name = p.Identifier.Text, Type = p.Type?.ToString() ?? "object" })
                .ToList();
        }
        return gap;
    }

    private DocumentationGap? CheckFieldDocumentation(
        FieldDeclarationSyntax field, string filePath, DocGenOptions options)
    {
        var name = field.Declaration.Variables.First().Identifier.Text;
        return CheckDocumentation(field, filePath, MemberKind.Field, name, options);
    }
}

// ── Analysis Models ──────────────────────────────────────────────

/// <summary>
/// Result of analyzing a single C# source file.
/// </summary>
public class AnalysisResult
{
    public string FilePath { get; set; } = string.Empty;
    public List<DocumentationGap> Gaps { get; set; } = [];
    public int TotalMembers { get; set; }
    public int DocumentedMembers { get; set; }
    public long AnalysisDurationMs { get; set; }
    public double CoveragePercent => TotalMembers > 0 ? (double)DocumentedMembers / TotalMembers * 100 : 100;
}

/// <summary>
/// Represents a single member that is missing or has stub XML documentation.
/// </summary>
public class DocumentationGap
{
    public string FilePath { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public MemberKind MemberKind { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public string? ExistingDoc { get; set; }
    public bool IsStub { get; set; }
    public List<ParameterInfo> Parameters { get; set; } = [];
    public string? ReturnType { get; set; }
    public bool IsAsync { get; set; }
    public List<string> TypeParameters { get; set; } = [];
}

/// <summary>
/// Describes a method/constructor parameter for doc generation.
/// </summary>
public class ParameterInfo
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

/// <summary>
/// The kind of member that needs documentation.
/// </summary>
public enum MemberKind
{
    Unknown,
    Type,
    Method,
    Constructor,
    Property,
    Field,
    Event,
    Indexer,
    Delegate,
    EnumMember
}
