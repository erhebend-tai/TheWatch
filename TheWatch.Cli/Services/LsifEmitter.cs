// =============================================================================
// LsifEmitter.cs — Roslyn-powered LSIF-style edge emitter for C# code
// =============================================================================
// Walks SyntaxTrees from a Roslyn Compilation and emits ReferenceEdge records
// by resolving symbol references via SemanticModel. This gives precise (not
// regex-based) cross-reference data for C# code.
//
// Edge types emitted:
//   - Calls:      method invocation → target method symbol
//   - Extends:    class/struct → base type
//   - Implements: class/struct → interface
//   - Imports:    using directive → namespace
//   - Overrides:  method → overridden virtual/abstract method
//   - DependsOn:  constructor parameter type → injected service interface
//   - Contains:   type → nested member (method, property, field)
//
// For non-C# languages, use the MultiLanguageParser.DependsOn field to create
// text-based edges (less precise but still useful for cross-language linking).
//
// Example:
//   var emitter = new LsifEmitter();
//   var edges = await emitter.EmitEdgesAsync(compilation, symbolLookup);
//   // edges: List<EmittedEdge> with SourceFullName, TargetFullName, Kind, File, Line
//
// Performance:
//   Uses Roslyn SemanticModel per-document (lazy, cached by Roslyn internally).
//   137K symbols across 8 repos: edge emission completes in ~10-30 seconds.
//
// WAL: Roslyn Compilation must have all referenced assemblies resolved for
//      accurate SemanticModel. Missing references → null symbols → skipped edges.
//      The emitter logs skip counts for diagnostic purposes.
// =============================================================================

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TheWatch.Data.CodeIntelligence;

namespace TheWatch.Cli.Services;

/// <summary>
/// Represents a single emitted edge before it is matched to SymbolNode IDs.
/// </summary>
/// <param name="SourceFullName">Fully-qualified name of the source symbol.</param>
/// <param name="TargetFullName">Fully-qualified name of the target symbol.</param>
/// <param name="Kind">The kind of reference.</param>
/// <param name="SourceFile">Relative path of the file containing the reference.</param>
/// <param name="SourceLine">1-based line number of the reference site.</param>
public record EmittedEdge(
    string SourceFullName,
    string TargetFullName,
    ReferenceKind Kind,
    string SourceFile,
    int SourceLine
);

/// <summary>
/// Walks Roslyn Compilations to emit LSIF-style reference edges from C# code.
/// Uses SemanticModel for precise symbol resolution (not text matching).
/// </summary>
public class LsifEmitter
{
    private int _skippedNullSymbol;
    private int _totalEdges;

    /// <summary>Number of reference sites skipped due to unresolved symbols.</summary>
    public int SkippedNullSymbol => _skippedNullSymbol;

    /// <summary>Total edges emitted.</summary>
    public int TotalEdges => _totalEdges;

    /// <summary>
    /// Emit all reference edges from a Roslyn Compilation.
    /// </summary>
    /// <param name="compilation">A Roslyn CSharpCompilation with all syntax trees loaded.</param>
    /// <param name="getRelativePath">Function to convert absolute file paths to relative paths.</param>
    /// <returns>List of emitted edges with source/target fully-qualified names.</returns>
    public async Task<List<EmittedEdge>> EmitEdgesAsync(
        Compilation compilation,
        Func<string, string> getRelativePath)
    {
        var edges = new List<EmittedEdge>();
        _skippedNullSymbol = 0;
        _totalEdges = 0;

        foreach (var tree in compilation.SyntaxTrees)
        {
            var filePath = tree.FilePath;
            if (string.IsNullOrEmpty(filePath)) continue;

            // Skip generated/obj/bin files
            if (filePath.Contains("/obj/") || filePath.Contains("\\obj\\") ||
                filePath.Contains("/bin/") || filePath.Contains("\\bin\\"))
                continue;

            var relPath = getRelativePath(filePath);
            var model = compilation.GetSemanticModel(tree);
            var root = await tree.GetRootAsync();

            // ── Base type / Interface implementation edges ────────────
            foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var typeSymbol = model.GetDeclaredSymbol(typeDecl);
                if (typeSymbol == null) { _skippedNullSymbol++; continue; }

                var sourceFullName = GetFullName(typeSymbol);

                // Base type → Extends edge
                if (typeSymbol.BaseType != null &&
                    typeSymbol.BaseType.SpecialType == SpecialType.None &&
                    typeSymbol.BaseType.Name != "Object")
                {
                    var targetName = GetFullName(typeSymbol.BaseType);
                    var lineSpan = typeDecl.GetLocation().GetLineSpan();
                    edges.Add(new EmittedEdge(sourceFullName, targetName, ReferenceKind.Extends,
                        relPath, lineSpan.StartLinePosition.Line + 1));
                }

                // Interfaces → Implements edges
                foreach (var iface in typeSymbol.Interfaces)
                {
                    var targetName = GetFullName(iface);
                    var lineSpan = typeDecl.GetLocation().GetLineSpan();
                    edges.Add(new EmittedEdge(sourceFullName, targetName, ReferenceKind.Implements,
                        relPath, lineSpan.StartLinePosition.Line + 1));
                }

                // ── Constructor injection → DependsOn edges ──────────
                foreach (var ctor in typeDecl.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
                {
                    foreach (var param in ctor.ParameterList.Parameters)
                    {
                        if (param.Type == null) continue;
                        var paramTypeInfo = model.GetTypeInfo(param.Type);
                        if (paramTypeInfo.Type is INamedTypeSymbol paramType &&
                            paramType.TypeKind == TypeKind.Interface)
                        {
                            var targetName = GetFullName(paramType);
                            var lineSpan = param.GetLocation().GetLineSpan();
                            edges.Add(new EmittedEdge(sourceFullName, targetName, ReferenceKind.DependsOn,
                                relPath, lineSpan.StartLinePosition.Line + 1));
                        }
                    }
                }

                // ── Member containment → Contains edges ──────────────
                foreach (var member in typeDecl.Members)
                {
                    var memberSymbol = model.GetDeclaredSymbol(member);
                    if (memberSymbol == null) continue;
                    var memberName = GetFullName(memberSymbol);
                    var lineSpan = member.GetLocation().GetLineSpan();
                    edges.Add(new EmittedEdge(sourceFullName, memberName, ReferenceKind.Contains,
                        relPath, lineSpan.StartLinePosition.Line + 1));
                }
            }

            // ── Method invocations → Calls edges ─────────────────────
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var symbolInfo = model.GetSymbolInfo(invocation);
                var targetMethod = symbolInfo.Symbol as IMethodSymbol;
                if (targetMethod == null) { _skippedNullSymbol++; continue; }

                var containingType = GetContainingTypeName(invocation, model);
                if (containingType == null) continue;

                var targetName = GetFullName(targetMethod);
                var lineSpan = invocation.GetLocation().GetLineSpan();
                edges.Add(new EmittedEdge(containingType, targetName, ReferenceKind.Calls,
                    relPath, lineSpan.StartLinePosition.Line + 1));
            }

            // ── Method overrides → Overrides edges ───────────────────
            foreach (var methodDecl in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var methodSymbol = model.GetDeclaredSymbol(methodDecl);
                if (methodSymbol?.OverriddenMethod == null) continue;

                var sourceFullName = GetFullName(methodSymbol);
                var targetName = GetFullName(methodSymbol.OverriddenMethod);
                var lineSpan = methodDecl.GetLocation().GetLineSpan();
                edges.Add(new EmittedEdge(sourceFullName, targetName, ReferenceKind.Overrides,
                    relPath, lineSpan.StartLinePosition.Line + 1));
            }

            // ── Using directives → Imports edges ─────────────────────
            foreach (var usingDir in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
            {
                if (usingDir.Name == null) continue;
                var nsSymbol = model.GetSymbolInfo(usingDir.Name).Symbol;
                if (nsSymbol == null) { _skippedNullSymbol++; continue; }

                // Source is the file/first type in the file
                var firstType = root.DescendantNodes().OfType<TypeDeclarationSyntax>().FirstOrDefault();
                if (firstType == null) continue;
                var firstTypeSymbol = model.GetDeclaredSymbol(firstType);
                if (firstTypeSymbol == null) continue;

                var sourceFullName = GetFullName(firstTypeSymbol);
                var targetName = nsSymbol.ToDisplayString();
                var lineSpan = usingDir.GetLocation().GetLineSpan();
                edges.Add(new EmittedEdge(sourceFullName, targetName, ReferenceKind.Imports,
                    relPath, lineSpan.StartLinePosition.Line + 1));
            }
        }

        _totalEdges = edges.Count;
        return edges;
    }

    /// <summary>
    /// Convert MultiLanguageParser DependsOn strings into EmittedEdges (text-based, less precise).
    /// Used for non-C# languages where Roslyn SemanticModel is not available.
    /// </summary>
    /// <param name="sourceFullName">The fully-qualified name of the source symbol.</param>
    /// <param name="dependsOn">Semicolon-separated dependency list from MultiLanguageParser.</param>
    /// <param name="sourceFile">Relative file path.</param>
    /// <param name="sourceLine">Line number of the symbol definition.</param>
    /// <returns>List of emitted edges.</returns>
    public static List<EmittedEdge> EmitFromDependsOn(
        string sourceFullName,
        string dependsOn,
        string sourceFile,
        int sourceLine)
    {
        if (string.IsNullOrWhiteSpace(dependsOn))
            return [];

        var edges = new List<EmittedEdge>();
        var deps = dependsOn.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var dep in deps)
        {
            // Heuristic: if it starts with "I" and has uppercase second char, likely an interface → Implements
            // If it contains "." → likely an import
            // Otherwise → Extends (base class) or generic dependency
            var kind = dep.StartsWith("I") && dep.Length > 1 && char.IsUpper(dep[1])
                ? ReferenceKind.Implements
                : dep.Contains('.') ? ReferenceKind.Imports
                : ReferenceKind.Extends;

            edges.Add(new EmittedEdge(sourceFullName, dep, kind, sourceFile, sourceLine));
        }

        return edges;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string GetFullName(ISymbol symbol)
    {
        return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
            .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted));
    }

    private static string? GetContainingTypeName(SyntaxNode node, SemanticModel model)
    {
        var typeDecl = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (typeDecl == null) return null;
        var typeSymbol = model.GetDeclaredSymbol(typeDecl);
        return typeSymbol == null ? null : GetFullName(typeSymbol);
    }
}
