// =============================================================================
// MultiLanguageParser — Regex-based multi-language symbol extraction
// =============================================================================
// Extracts classes, interfaces, functions, structs, enums, and other symbols
// from source files in Kotlin, Swift, Python, TypeScript, JavaScript, and C
// using regex-based parsing. No native dependencies required.
//
// Accuracy target: ~80% — good enough for indexing, not a compiler.
//
// Supported languages and extracted constructs:
//   Kotlin  — class, data class, object, interface, enum class, sealed class/interface,
//             abstract class, top-level fun, import, constructor DI deps
//   Swift   — class, struct, protocol, enum, actor, func, import, conformances
//   Python  — class (with bases), def (top-level), import/from-import, decorators
//   TypeScript/JavaScript — class, interface, type, enum, export function/const, import
//   C/H     — struct, enum, typedef, function declarations, #include
//
// Each parser returns List<ParsedSymbol> per file. The indexer calls
// TryParse(filePath, content) which dispatches by extension.
//
// Example:
//   var symbols = MultiLanguageParser.TryParse("Models/User.kt", fileContent);
//   // => [ ParsedSymbol("User", "data class", null, "data class User(...)", "Parcelable", 5, 22) ]
//
//   var symbols = MultiLanguageParser.TryParse("views/HomeView.swift", fileContent);
//   // => [ ParsedSymbol("HomeView", "struct", null, "struct HomeView : View", "View", 3, 45) ]
//
// WAL: Regex-only — no tree-sitter, no native bindings, no platform-specific deps.
//      Multi-line signatures handled by joining continuation lines before matching.
//      Nested types may be detected but nesting depth is not tracked.
// =============================================================================

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TheWatch.Cli.Services;

/// <summary>
/// A symbol extracted from a source file by regex-based parsing.
/// </summary>
/// <param name="Name">Symbol name (class name, function name, etc.)</param>
/// <param name="Kind">Symbol kind: class, interface, function, struct, enum, data class, etc.</param>
/// <param name="Namespace">Namespace/package if detected, null otherwise.</param>
/// <param name="Signature">Human-readable signature string.</param>
/// <param name="DependsOn">Semicolon-separated dependency list (base types, imports, includes).</param>
/// <param name="StartLine">1-based start line in the source file.</param>
/// <param name="EndLine">1-based end line in the source file.</param>
public record ParsedSymbol(
    string Name,
    string Kind,
    string? Namespace,
    string Signature,
    string DependsOn,
    int StartLine,
    int EndLine
);

/// <summary>
/// Unified regex-based parser for Kotlin, Swift, Python, TypeScript, JavaScript, and C.
/// Call <see cref="TryParse"/> with a file path and its content to get symbols.
/// </summary>
public static class MultiLanguageParser
{
    // ── Extension → language mapping ────────────────────────────────────
    private static readonly Dictionary<string, string> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".kt"]   = "kotlin",
        [".kts"]  = "kotlin",
        [".swift"] = "swift",
        [".py"]   = "python",
        [".pyi"]  = "python",
        [".ts"]   = "typescript",
        [".tsx"]  = "typescript",
        [".js"]   = "javascript",
        [".jsx"]  = "javascript",
        [".mjs"]  = "javascript",
        [".cjs"]  = "javascript",
        [".c"]    = "c",
        [".h"]    = "c",
        [".cpp"]  = "cpp",
        [".cc"]   = "cpp",
        [".cxx"]  = "cpp",
        [".hpp"]  = "cpp",
        [".java"] = "java",
        [".rs"]   = "rust",
        [".go"]   = "go",
        [".rb"]   = "ruby",
        [".dart"] = "dart",
        [".sql"]  = "sql",
        [".proto"] = "protobuf",
        [".tf"]   = "terraform",
        [".bicep"] = "bicep",
        [".ps1"]  = "powershell",
        [".psm1"] = "powershell",
        [".sh"]   = "shell",
        [".bash"] = "shell",
        [".razor"] = "razor",
        [".xaml"] = "xaml",
        [".css"]  = "css",
        [".scss"] = "scss",
        [".html"] = "html",
        [".openapi.json"] = "openapi",
        // Note: regular .json and .yaml files should NOT be auto-parsed — only OpenAPI-specific ones.
        // Use IsOpenApiFile() to detect OpenAPI files by filename or content sniffing.
    };

    /// <summary>
    /// Returns the language string for a file extension, or null if unsupported.
    /// </summary>
    public static string? GetLanguage(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return ExtensionMap.GetValueOrDefault(ext);
    }

    /// <summary>
    /// Returns true if the file extension is supported by this parser.
    /// </summary>
    public static bool IsSupported(string filePath)
        => GetLanguage(filePath) != null;

    /// <summary>
    /// Parse a source file and return all extracted symbols.
    /// Returns an empty list if the language is unsupported or parsing fails.
    /// </summary>
    public static List<ParsedSymbol> TryParse(string filePath, string content)
    {
        try
        {
            var lang = GetLanguage(filePath);
            return lang switch
            {
                "kotlin"     => ParseKotlin(content),
                "swift"      => ParseSwift(content),
                "python"     => ParsePython(content),
                "typescript" => ParseTypeScript(content),
                "javascript" => ParseTypeScript(content), // JS and TS share the parser
                "c"          => ParseC(content),
                "cpp"        => ParseC(content),          // C++ reuses C parser (structs, functions, enums)
                "java"       => ParseJava(content),
                "rust"       => ParseRust(content),
                "go"         => ParseGo(content),
                "ruby"       => ParseRuby(content),
                "dart"       => ParseDart(content),
                "sql"        => ParseSql(content),
                "protobuf"   => ParseProtobuf(content),
                "terraform"  => ParseTerraform(content),
                "bicep"      => ParseBicep(content),
                "powershell" => ParsePowerShell(content),
                "shell"      => ParseShell(content),
                "openapi"    => ParseOpenApi(content),
                "razor" or "xaml" or "css" or "scss" or "html" => [], // Markup: file-level only
                _            => []
            };
        }
        catch
        {
            // Graceful fallback — return empty on any parse error
            return [];
        }
    }

    // ====================================================================
    // Kotlin Parser
    // ====================================================================
    // Extracts: class, data class, object, interface, enum class,
    //           sealed class/interface, abstract class, open class,
    //           top-level fun, import statements, constructor DI deps
    //
    // Example matches:
    //   data class User(val name: String, val age: Int) : Parcelable
    //   sealed interface Result<out T>
    //   object AppModule
    //   fun calculateDistance(lat: Double, lon: Double): Double
    //   abstract class BaseRepository<T>(private val dao: Dao<T>)
    // ====================================================================

    private static readonly Regex KotlinPackageRx = new(
        @"^\s*package\s+([\w.]+)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex KotlinImportRx = new(
        @"^\s*import\s+([\w.*]+)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // Matches: (modifier)? (class|interface|object|enum class) Name (: BaseTypes)?
    // Modifiers: data, sealed, abstract, open, inner, private, internal, protected, public, actual, expect
    private static readonly Regex KotlinTypeRx = new(
        @"^\s*(?:(?:private|internal|protected|public|actual|expect|annotation)\s+)*" +
        @"(?<mod>(?:data|sealed|abstract|open|inner)\s+)?" +
        @"(?<kind>enum\s+class|class|interface|object)\s+" +
        @"(?<name>\w+)" +
        @"(?:<[^>]+>)?" +                         // optional generic params
        @"(?:\s*\([^)]*\))?" +                     // optional primary constructor
        @"(?:\s*:\s*(?<bases>[^{]+?))??" +          // optional base types
        @"\s*(?:\{|$)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // Matches top-level fun declarations (not indented or at class-body level)
    private static readonly Regex KotlinFunRx = new(
        @"^(?:(?:private|internal|protected|public|actual|expect|inline|suspend|tailrec|operator|infix|external|override)\s+)*" +
        @"fun\s+(?:<[^>]+>\s+)?" +                // optional generic params
        @"(?:(?<receiver>\w+)\.)?" +               // optional receiver type
        @"(?<name>\w+)\s*\(" +
        @"(?<params>[^)]*)\)" +
        @"(?:\s*:\s*(?<ret>[^\s{=]+))?",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // Extract constructor parameter types for DI tracking
    private static readonly Regex KotlinCtorParamRx = new(
        @"(?:val|var)\s+\w+\s*:\s*(?<type>[\w.]+)",
        RegexOptions.Compiled);

    private static List<ParsedSymbol> ParseKotlin(string content)
    {
        var symbols = new List<ParsedSymbol>();
        var lines = content.Split('\n');

        // Package
        string? package = null;
        var pkgMatch = KotlinPackageRx.Match(content);
        if (pkgMatch.Success)
            package = pkgMatch.Groups[1].Value;

        // Imports (for dependency tracking on types)
        var imports = new List<string>();
        foreach (Match m in KotlinImportRx.Matches(content))
            imports.Add(m.Groups[1].Value);

        // Type declarations
        foreach (Match m in KotlinTypeRx.Matches(content))
        {
            var mod = m.Groups["mod"].Value.Trim();
            var rawKind = m.Groups["kind"].Value.Trim();
            var name = m.Groups["name"].Value;
            var basesRaw = m.Groups["bases"].Success ? m.Groups["bases"].Value.Trim().TrimEnd('{').Trim() : "";

            // Compose kind string: "data class", "sealed interface", "abstract class", etc.
            var kind = string.IsNullOrEmpty(mod) ? rawKind : $"{mod} {rawKind}";
            // Normalize "enum class" spacing
            kind = Regex.Replace(kind, @"\s+", " ");

            var startLine = GetLineNumber(content, m.Index);
            var endLine = FindBlockEnd(lines, startLine - 1); // 0-based for lines array

            // Extract base types / interfaces
            var deps = new List<string>();
            if (!string.IsNullOrWhiteSpace(basesRaw))
            {
                foreach (var b in SplitBaseTypes(basesRaw))
                {
                    var cleanBase = b.Split('<')[0].Split('(')[0].Trim();
                    if (!string.IsNullOrWhiteSpace(cleanBase))
                        deps.Add(cleanBase);
                }
            }

            // Extract constructor parameter types for DI
            // Look for primary constructor in the match region
            var matchText = m.Value;
            foreach (Match cp in KotlinCtorParamRx.Matches(matchText))
            {
                var ptype = cp.Groups["type"].Value.Split('.').Last();
                if (!IsPrimitiveType(ptype))
                    deps.Add(ptype);
            }

            var signature = BuildSignature(kind, name, basesRaw);

            symbols.Add(new ParsedSymbol(
                name, kind, package, signature,
                string.Join("; ", deps.Distinct()),
                startLine, endLine
            ));
        }

        // Top-level functions (lines that start at column 0 or with only modifiers before 'fun')
        foreach (Match m in KotlinFunRx.Matches(content))
        {
            // Check indentation: only top-level functions (no leading whitespace beyond modifiers)
            var lineStart = content.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
            var prefix = content[lineStart..m.Index];
            // If there's significant indentation (inside a class body), skip
            if (prefix.Length > 0 && prefix.All(c => c == ' ' || c == '\t') && prefix.Length >= 4)
                continue;

            var funcName = m.Groups["name"].Value;
            var paramsStr = m.Groups["params"].Value.Trim();
            var retType = m.Groups["ret"].Success ? m.Groups["ret"].Value.Trim() : "";
            var receiver = m.Groups["receiver"].Success ? m.Groups["receiver"].Value : "";

            var startLine = GetLineNumber(content, m.Index);
            var endLine = FindBlockEnd(lines, startLine - 1);

            var sig = receiver.Length > 0
                ? $"fun {receiver}.{funcName}({Abbreviate(paramsStr)}){(retType.Length > 0 ? $": {retType}" : "")}"
                : $"fun {funcName}({Abbreviate(paramsStr)}){(retType.Length > 0 ? $": {retType}" : "")}";

            // Extract parameter types as deps
            var deps = ExtractParamTypes(paramsStr);

            symbols.Add(new ParsedSymbol(
                funcName, "function", package, sig,
                string.Join("; ", deps),
                startLine, endLine
            ));
        }

        return symbols;
    }

    // ====================================================================
    // Swift Parser
    // ====================================================================
    // Extracts: class, struct, protocol, enum, actor, func, import,
    //           protocol conformances (: SomeProtocol)
    //
    // Example matches:
    //   class HomeViewModel: ObservableObject
    //   struct ContentView: View
    //   protocol VolunteerService: AnyObject
    //   enum AlertLevel: String, CaseIterable
    //   actor DataStore
    //   func fetchNearbyResponders(location: CLLocation) async throws -> [Responder]
    //   @MainActor class AppCoordinator
    // ====================================================================

    private static readonly Regex SwiftImportRx = new(
        @"^\s*import\s+(\w+)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex SwiftTypeRx = new(
        @"^\s*(?:(?:@\w+(?:\([^)]*\))?\s+)*)" +       // optional attributes (@MainActor, @objc, etc.)
        @"(?:(?:public|private|internal|fileprivate|open|final)\s+)*" +  // access modifiers
        @"(?<kind>class|struct|protocol|enum|actor)\s+" +
        @"(?<name>\w+)" +
        @"(?:<[^>]+>)?" +                              // optional generics
        @"(?:\s*:\s*(?<bases>[^{]+?))??" +
        @"\s*(?:\{|$)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex SwiftFuncRx = new(
        @"^\s*(?:(?:@\w+(?:\([^)]*\))?\s+)*)" +
        @"(?:(?:public|private|internal|fileprivate|open|override|static|class|mutating|nonmutating)\s+)*" +
        @"func\s+(?<name>\w+)" +
        @"\s*(?:<[^>]+>)?" +
        @"\s*\((?<params>[^)]*)\)" +
        @"(?:\s*(?:async\s*)?(?:throws\s*)?(?:rethrows\s*)?)?" +
        @"(?:\s*->\s*(?<ret>[^\s{]+))?",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // Swift extension: extension TypeName: Protocol
    private static readonly Regex SwiftExtensionRx = new(
        @"^\s*(?:(?:public|private|internal|fileprivate)\s+)?" +
        @"extension\s+(?<name>\w+)" +
        @"(?:\s*:\s*(?<bases>[^{]+?))??" +
        @"\s*(?:where\s+[^{]+)?" +
        @"\s*\{",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static List<ParsedSymbol> ParseSwift(string content)
    {
        var symbols = new List<ParsedSymbol>();
        var lines = content.Split('\n');

        // Imports
        var imports = new List<string>();
        foreach (Match m in SwiftImportRx.Matches(content))
            imports.Add(m.Groups[1].Value);

        // Type declarations
        foreach (Match m in SwiftTypeRx.Matches(content))
        {
            var kind = m.Groups["kind"].Value;
            var name = m.Groups["name"].Value;
            var basesRaw = m.Groups["bases"].Success ? m.Groups["bases"].Value.Trim().TrimEnd('{').Trim() : "";

            var startLine = GetLineNumber(content, m.Index);
            var endLine = FindBlockEnd(lines, startLine - 1);

            var deps = new List<string>();
            if (!string.IsNullOrWhiteSpace(basesRaw))
            {
                foreach (var b in SplitBaseTypes(basesRaw))
                {
                    var cleanBase = b.Split('<')[0].Trim();
                    if (!string.IsNullOrWhiteSpace(cleanBase) && !IsSwiftPrimitive(cleanBase))
                        deps.Add(cleanBase);
                }
            }

            var signature = BuildSignature(kind, name, basesRaw);

            symbols.Add(new ParsedSymbol(
                name, kind, null, signature,
                string.Join("; ", deps.Distinct()),
                startLine, endLine
            ));
        }

        // Extensions
        foreach (Match m in SwiftExtensionRx.Matches(content))
        {
            var name = m.Groups["name"].Value;
            var basesRaw = m.Groups["bases"].Success ? m.Groups["bases"].Value.Trim().TrimEnd('{').Trim() : "";

            var startLine = GetLineNumber(content, m.Index);
            var endLine = FindBlockEnd(lines, startLine - 1);

            var deps = new List<string>();
            deps.Add(name); // Extension depends on the type it extends
            if (!string.IsNullOrWhiteSpace(basesRaw))
            {
                foreach (var b in SplitBaseTypes(basesRaw))
                {
                    var cleanBase = b.Split('<')[0].Trim();
                    if (!string.IsNullOrWhiteSpace(cleanBase))
                        deps.Add(cleanBase);
                }
            }

            var sig = string.IsNullOrWhiteSpace(basesRaw)
                ? $"extension {name}"
                : $"extension {name}: {basesRaw}";

            symbols.Add(new ParsedSymbol(
                $"{name}+ext", "extension", null, sig,
                string.Join("; ", deps.Distinct()),
                startLine, endLine
            ));
        }

        // Top-level functions (not deeply indented)
        foreach (Match m in SwiftFuncRx.Matches(content))
        {
            var lineStart = content.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
            var prefix = content[lineStart..m.Index];
            if (prefix.Length > 0 && prefix.All(c => c == ' ' || c == '\t') && prefix.Length >= 8)
                continue; // Skip deeply-nested methods (inside class/struct bodies)

            var funcName = m.Groups["name"].Value;
            var paramsStr = m.Groups["params"].Value.Trim();
            var retType = m.Groups["ret"].Success ? m.Groups["ret"].Value.Trim() : "";

            var startLine = GetLineNumber(content, m.Index);
            var endLine = FindBlockEnd(lines, startLine - 1);

            var sig = $"func {funcName}({Abbreviate(paramsStr)})" +
                      (retType.Length > 0 ? $" -> {retType}" : "");

            var deps = ExtractSwiftParamTypes(paramsStr);
            if (retType.Length > 0)
            {
                var cleanRet = retType.TrimStart('[').TrimEnd(']').TrimEnd('?').Split('<')[0];
                if (!IsSwiftPrimitive(cleanRet))
                    deps.Add(cleanRet);
            }

            symbols.Add(new ParsedSymbol(
                funcName, "function", null, sig,
                string.Join("; ", deps.Distinct()),
                startLine, endLine
            ));
        }

        return symbols;
    }

    // ====================================================================
    // Python Parser
    // ====================================================================
    // Extracts: class (with base classes), def (top-level), import/from-import,
    //           decorators for tagging (@app.route, @pytest.fixture, etc.)
    //
    // Example matches:
    //   class UserService(BaseService, AuthMixin):
    //   def calculate_risk_score(location: tuple, history: list) -> float:
    //   from flask import Flask, jsonify
    //   import numpy as np
    //   @app.route("/api/alerts")
    //   @pytest.fixture
    // ====================================================================

    private static readonly Regex PythonImportRx = new(
        @"^\s*import\s+([\w.]+)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex PythonFromImportRx = new(
        @"^\s*from\s+([\w.]+)\s+import\s+(.+)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex PythonClassRx = new(
        @"^(?<decs>(?:\s*@[\w.]+(?:\([^)]*\))?\s*\n)*)" +
        @"^(?<indent>\s*)class\s+(?<name>\w+)" +
        @"(?:\s*\((?<bases>[^)]*)\))?" +
        @"\s*:",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex PythonDefRx = new(
        @"^(?<decs>(?:\s*@[\w.]+(?:\([^)]*\))?\s*\n)*)" +
        @"^(?<indent>\s*)def\s+(?<name>\w+)\s*\(" +
        @"(?<params>[^)]*)\)" +
        @"(?:\s*->\s*(?<ret>[^\s:]+))?\s*:",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex PythonDecoratorRx = new(
        @"@(?<dec>[\w.]+)(?:\([^)]*\))?",
        RegexOptions.Compiled);

    private static List<ParsedSymbol> ParsePython(string content)
    {
        var symbols = new List<ParsedSymbol>();
        var lines = content.Split('\n');

        // Collect imports for dependency tracking
        var importDeps = new List<string>();
        foreach (Match m in PythonImportRx.Matches(content))
            importDeps.Add(m.Groups[1].Value);
        foreach (Match m in PythonFromImportRx.Matches(content))
            importDeps.Add(m.Groups[1].Value);

        // Classes
        foreach (Match m in PythonClassRx.Matches(content))
        {
            var name = m.Groups["name"].Value;
            var basesRaw = m.Groups["bases"].Success ? m.Groups["bases"].Value.Trim() : "";
            var decsRaw = m.Groups["decs"].Value;

            var startLine = GetLineNumber(content, m.Index);
            var endLine = FindPythonBlockEnd(lines, startLine - 1);

            var deps = new List<string>();
            if (!string.IsNullOrWhiteSpace(basesRaw))
            {
                foreach (var b in basesRaw.Split(','))
                {
                    var cleanBase = b.Trim().Split('=')[0].Trim().Split('[')[0].Trim(); // handle metaclass=..., Generic[T]
                    if (!string.IsNullOrWhiteSpace(cleanBase) && !IsPythonBuiltin(cleanBase))
                        deps.Add(cleanBase);
                }
            }

            // Extract decorators for tag context
            var decorators = new List<string>();
            foreach (Match dm in PythonDecoratorRx.Matches(decsRaw))
                decorators.Add(dm.Groups["dec"].Value);

            var sig = string.IsNullOrWhiteSpace(basesRaw)
                ? $"class {name}"
                : $"class {name}({basesRaw})";
            if (decorators.Count > 0)
                sig = $"@{string.Join(" @", decorators)} {sig}";

            symbols.Add(new ParsedSymbol(
                name, "class", null, sig,
                string.Join("; ", deps.Distinct()),
                startLine, endLine
            ));
        }

        // Top-level functions (indent == 0 or very small)
        foreach (Match m in PythonDefRx.Matches(content))
        {
            var indent = m.Groups["indent"].Value;
            // Only top-level (no indent) or module-level functions
            if (indent.Length > 0) continue;

            var funcName = m.Groups["name"].Value;
            var paramsStr = m.Groups["params"].Value.Trim();
            var retType = m.Groups["ret"].Success ? m.Groups["ret"].Value.Trim() : "";
            var decsRaw = m.Groups["decs"].Value;

            var startLine = GetLineNumber(content, m.Index);
            var endLine = FindPythonBlockEnd(lines, startLine - 1);

            // Extract decorators
            var decorators = new List<string>();
            foreach (Match dm in PythonDecoratorRx.Matches(decsRaw))
                decorators.Add(dm.Groups["dec"].Value);

            var sig = $"def {funcName}({Abbreviate(paramsStr)})" +
                      (retType.Length > 0 ? $" -> {retType}" : "");
            if (decorators.Count > 0)
                sig = $"@{string.Join(" @", decorators)} {sig}";

            // Extract parameter type annotations as deps
            var deps = ExtractPythonParamTypes(paramsStr);
            if (retType.Length > 0 && !IsPythonBuiltin(retType))
                deps.Add(retType.TrimStart('[').TrimEnd(']'));

            symbols.Add(new ParsedSymbol(
                funcName, "function", null, sig,
                string.Join("; ", deps.Distinct()),
                startLine, endLine
            ));
        }

        return symbols;
    }

    // ====================================================================
    // TypeScript / JavaScript Parser
    // ====================================================================
    // Extracts: class, interface, type, enum declarations,
    //           export function/const, import statements
    //
    // Example matches:
    //   export class AlertService extends BaseService implements IAlertService
    //   interface ResponderPayload { ... }
    //   type AlertLevel = 'low' | 'medium' | 'high'
    //   export enum SensorType { ... }
    //   export function calculateRiskScore(data: SensorData): number
    //   export const processAlert = async (alert: Alert): Promise<void> => { ... }
    //   import { Injectable } from '@angular/core'
    //   import type { User } from './models'
    // ====================================================================

    private static readonly Regex TsImportRx = new(
        @"^\s*import\s+(?:type\s+)?(?:\{[^}]+\}|[\w*]+(?:\s+as\s+\w+)?)\s+from\s+['""]([^'""]+)['""]",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex TsTypeRx = new(
        @"^\s*(?:export\s+)?(?:default\s+)?(?:declare\s+)?(?:abstract\s+)?" +
        @"(?<kind>class|interface|type|enum)\s+" +
        @"(?<name>\w+)" +
        @"(?:<[^>]+>)?" +
        @"(?:\s+extends\s+(?<extends>[\w.,\s<>]+?))?" +
        @"(?:\s+implements\s+(?<implements>[\w.,\s<>]+?))?" +
        @"(?:\s*=\s*|(?:\s*\{)|\s*$)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex TsFunctionRx = new(
        @"^\s*(?:export\s+)?(?:default\s+)?(?:declare\s+)?(?:async\s+)?" +
        @"function\s+(?:\*\s*)?" +      // generator support
        @"(?<name>\w+)" +
        @"(?:<[^>]+>)?" +
        @"\s*\((?<params>[^)]*)\)" +
        @"(?:\s*:\s*(?<ret>[^\s{]+))?",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // export const name = (params) => ...  or  export const name: Type = ...
    private static readonly Regex TsConstFuncRx = new(
        @"^\s*export\s+const\s+(?<name>\w+)" +
        @"(?:\s*:\s*(?<type>[^\s=]+))?" +
        @"\s*=\s*(?:async\s+)?" +
        @"(?:\([^)]*\)\s*(?::\s*[^\s=]+\s*)?=>|function)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static List<ParsedSymbol> ParseTypeScript(string content)
    {
        var symbols = new List<ParsedSymbol>();
        var lines = content.Split('\n');

        // Imports for dependency tracking
        var imports = new List<string>();
        foreach (Match m in TsImportRx.Matches(content))
            imports.Add(m.Groups[1].Value);

        // Type declarations (class, interface, type, enum)
        foreach (Match m in TsTypeRx.Matches(content))
        {
            var kind = m.Groups["kind"].Value;
            var name = m.Groups["name"].Value;
            var extendsRaw = m.Groups["extends"].Success ? m.Groups["extends"].Value.Trim() : "";
            var implementsRaw = m.Groups["implements"].Success ? m.Groups["implements"].Value.Trim() : "";

            var startLine = GetLineNumber(content, m.Index);
            var endLine = kind == "type"
                ? startLine // type aliases are typically single-line or short
                : FindBlockEnd(lines, startLine - 1);

            var deps = new List<string>();
            if (!string.IsNullOrWhiteSpace(extendsRaw))
            {
                foreach (var b in SplitBaseTypes(extendsRaw))
                {
                    var clean = b.Split('<')[0].Trim();
                    if (!string.IsNullOrWhiteSpace(clean))
                        deps.Add(clean);
                }
            }
            if (!string.IsNullOrWhiteSpace(implementsRaw))
            {
                foreach (var b in SplitBaseTypes(implementsRaw))
                {
                    var clean = b.Split('<')[0].Trim();
                    if (!string.IsNullOrWhiteSpace(clean))
                        deps.Add(clean);
                }
            }

            var baseParts = new List<string>();
            if (extendsRaw.Length > 0) baseParts.Add($"extends {extendsRaw}");
            if (implementsRaw.Length > 0) baseParts.Add($"implements {implementsRaw}");
            var basesStr = string.Join(" ", baseParts);

            var signature = basesStr.Length > 0
                ? $"{kind} {name} {basesStr}"
                : $"{kind} {name}";

            symbols.Add(new ParsedSymbol(
                name, kind, null, signature,
                string.Join("; ", deps.Distinct()),
                startLine, endLine
            ));
        }

        // Exported functions
        foreach (Match m in TsFunctionRx.Matches(content))
        {
            var funcName = m.Groups["name"].Value;
            var paramsStr = m.Groups["params"].Value.Trim();
            var retType = m.Groups["ret"].Success ? m.Groups["ret"].Value.Trim() : "";

            var startLine = GetLineNumber(content, m.Index);
            var endLine = FindBlockEnd(lines, startLine - 1);

            var sig = $"function {funcName}({Abbreviate(paramsStr)})" +
                      (retType.Length > 0 ? $": {retType}" : "");

            var deps = ExtractTsParamTypes(paramsStr);

            symbols.Add(new ParsedSymbol(
                funcName, "function", null, sig,
                string.Join("; ", deps.Distinct()),
                startLine, endLine
            ));
        }

        // Exported const arrow functions
        foreach (Match m in TsConstFuncRx.Matches(content))
        {
            var constName = m.Groups["name"].Value;
            var typeAnnotation = m.Groups["type"].Success ? m.Groups["type"].Value.Trim() : "";

            var startLine = GetLineNumber(content, m.Index);
            var endLine = FindBlockEnd(lines, startLine - 1);

            var sig = typeAnnotation.Length > 0
                ? $"const {constName}: {typeAnnotation}"
                : $"const {constName} = (...)";

            symbols.Add(new ParsedSymbol(
                constName, "function", null, sig,
                "",
                startLine, endLine
            ));
        }

        return symbols;
    }

    // ====================================================================
    // C Parser
    // ====================================================================
    // Extracts: struct, enum, typedef, function declarations, #include
    //
    // Example matches:
    //   struct sensor_data { float lat; float lon; int signal; };
    //   enum alert_level { ALERT_LOW, ALERT_MEDIUM, ALERT_HIGH };
    //   typedef struct { int x; int y; } Point;
    //   typedef enum { RED, GREEN, BLUE } Color;
    //   int calculate_distance(double lat1, double lon1, double lat2, double lon2);
    //   void* thread_worker(void* arg) { ... }
    //   static inline int max(int a, int b) { ... }
    //   #include <stdio.h>
    //   #include "sensor_hal.h"
    // ====================================================================

    private static readonly Regex CIncludeRx = new(
        @"^\s*#\s*include\s+[<""]([^>""]+)[>""]",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // struct Name { or struct Name;
    private static readonly Regex CStructRx = new(
        @"^\s*(?:typedef\s+)?struct\s+(?<name>\w+)" +
        @"\s*(?:\{|;)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // enum Name { or typedef enum { ... } Name;
    private static readonly Regex CEnumRx = new(
        @"^\s*(?:typedef\s+)?enum\s+(?<name>\w+)?\s*\{",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // typedef <something> Name;
    private static readonly Regex CTypedefRx = new(
        @"^\s*typedef\s+(?<original>.+?)\s+(?<name>\w+)\s*;",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // Function declarations: return_type name(params) { or ;
    // Handles: static, inline, extern, const, unsigned, signed, etc.
    private static readonly Regex CFunctionRx = new(
        @"^(?<qualifiers>(?:(?:static|inline|extern|__attribute__\([^)]*\))\s+)*)" +
        @"(?<ret>(?:(?:const|unsigned|signed|long|short|volatile|struct|enum)\s+)*\w[\w*\s]*?)\s+" +
        @"(?<ptr>\**)(?<name>\w+)\s*\(" +
        @"(?<params>[^)]*)\)" +
        @"\s*(?:\{|;)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static List<ParsedSymbol> ParseC(string content)
    {
        var symbols = new List<ParsedSymbol>();
        var lines = content.Split('\n');

        // Includes
        var includes = new List<string>();
        foreach (Match m in CIncludeRx.Matches(content))
            includes.Add(m.Groups[1].Value);
        var includeDeps = string.Join("; ", includes);

        // Structs
        foreach (Match m in CStructRx.Matches(content))
        {
            var name = m.Groups["name"].Value;
            var startLine = GetLineNumber(content, m.Index);
            var endLine = m.Value.Contains('{') ? FindBlockEnd(lines, startLine - 1) : startLine;

            symbols.Add(new ParsedSymbol(
                name, "struct", null, $"struct {name}",
                includeDeps,
                startLine, endLine
            ));
        }

        // Enums
        foreach (Match m in CEnumRx.Matches(content))
        {
            var name = m.Groups["name"].Success ? m.Groups["name"].Value : "";
            var startLine = GetLineNumber(content, m.Index);
            var endLine = FindBlockEnd(lines, startLine - 1);

            // For anonymous enums, try to find typedef name after closing brace
            if (string.IsNullOrEmpty(name))
            {
                if (endLine <= lines.Length)
                {
                    var closingLine = lines[endLine - 1];
                    var typedefMatch = Regex.Match(closingLine, @"\}\s*(\w+)\s*;");
                    if (typedefMatch.Success)
                        name = typedefMatch.Groups[1].Value;
                    else
                        name = "<anonymous_enum>";
                }
            }

            symbols.Add(new ParsedSymbol(
                name, "enum", null, $"enum {name}",
                includeDeps,
                startLine, endLine
            ));
        }

        // Typedefs (that are not struct/enum typedefs already captured)
        foreach (Match m in CTypedefRx.Matches(content))
        {
            var name = m.Groups["name"].Value;
            var original = m.Groups["original"].Value.Trim();
            var startLine = GetLineNumber(content, m.Index);

            // Skip if this is a struct or enum typedef (already handled)
            if (original.StartsWith("struct") || original.StartsWith("enum"))
                continue;

            symbols.Add(new ParsedSymbol(
                name, "typedef", null, $"typedef {Abbreviate(original)} {name}",
                includeDeps,
                startLine, startLine
            ));
        }

        // Functions
        foreach (Match m in CFunctionRx.Matches(content))
        {
            var name = m.Groups["name"].Value;
            var retType = (m.Groups["ret"].Value.Trim() + m.Groups["ptr"].Value).Trim();
            var paramsStr = m.Groups["params"].Value.Trim();

            // Skip common false positives: macros, if/for/while/switch/return
            if (IsCKeyword(name)) continue;

            var startLine = GetLineNumber(content, m.Index);
            var endLine = m.Value.TrimEnd().EndsWith("{")
                ? FindBlockEnd(lines, startLine - 1)
                : startLine;

            var sig = $"{retType} {name}({Abbreviate(paramsStr)})";

            symbols.Add(new ParsedSymbol(
                name, "function", null, sig,
                includeDeps,
                startLine, endLine
            ));
        }

        return symbols;
    }

    // ====================================================================
    // Shared Helpers
    // ====================================================================

    /// <summary>
    /// Gets the 1-based line number for a character offset in the content string.
    /// </summary>
    private static int GetLineNumber(string content, int charIndex)
    {
        var line = 1;
        for (var i = 0; i < charIndex && i < content.Length; i++)
        {
            if (content[i] == '\n') line++;
        }
        return line;
    }

    /// <summary>
    /// Finds the end of a brace-delimited block starting near the given 0-based line index.
    /// Uses brace counting ({/}) to handle nesting.
    /// </summary>
    private static int FindBlockEnd(string[] lines, int startLineIndex)
    {
        var braceDepth = 0;
        var foundOpen = false;

        for (var i = startLineIndex; i < lines.Length; i++)
        {
            var line = lines[i];
            // Skip strings/comments crudely
            foreach (var ch in line)
            {
                if (ch == '{') { braceDepth++; foundOpen = true; }
                else if (ch == '}') braceDepth--;
            }

            if (foundOpen && braceDepth <= 0)
                return i + 1; // 1-based
        }

        // If no matching close brace found, estimate ~20 lines or end of file
        return Math.Min(startLineIndex + 20, lines.Length);
    }

    /// <summary>
    /// Finds the end of a Python indentation-based block.
    /// The block ends when a non-empty line has equal or less indentation than the def/class line.
    /// </summary>
    private static int FindPythonBlockEnd(string[] lines, int startLineIndex)
    {
        if (startLineIndex >= lines.Length) return startLineIndex + 1;

        var startIndent = GetIndentLevel(lines[startLineIndex]);

        for (var i = startLineIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            // Skip blank lines and comment-only lines
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.TrimStart().StartsWith('#')) continue;

            var indent = GetIndentLevel(line);
            if (indent <= startIndent)
                return i; // 1-based: the line before is the last line of the block
        }

        return lines.Length; // Block extends to end of file
    }

    private static int GetIndentLevel(string line)
    {
        var count = 0;
        foreach (var ch in line)
        {
            if (ch == ' ') count++;
            else if (ch == '\t') count += 4;
            else break;
        }
        return count;
    }

    /// <summary>
    /// Splits base types separated by commas, respecting angle brackets for generics.
    /// e.g., "BaseClass, IFoo&lt;Bar, Baz&gt;, IBaz" => ["BaseClass", "IFoo<Bar, Baz>", "IBaz"]
    /// </summary>
    private static List<string> SplitBaseTypes(string bases)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var depth = 0;

        foreach (var ch in bases)
        {
            if (ch == '<') depth++;
            else if (ch == '>') depth--;
            else if (ch == ',' && depth == 0)
            {
                var val = current.ToString().Trim();
                if (val.Length > 0) result.Add(val);
                current.Clear();
                continue;
            }
            current.Append(ch);
        }

        var last = current.ToString().Trim();
        if (last.Length > 0) result.Add(last);

        return result;
    }

    /// <summary>
    /// Builds a signature string like "class Foo : Bar, IBaz"
    /// </summary>
    private static string BuildSignature(string kind, string name, string bases)
    {
        return string.IsNullOrWhiteSpace(bases)
            ? $"{kind} {name}"
            : $"{kind} {name} : {bases}";
    }

    /// <summary>
    /// Abbreviates a parameter list to max 80 chars, appending "..." if truncated.
    /// </summary>
    private static string Abbreviate(string text, int maxLen = 80)
    {
        if (text.Length <= maxLen) return text;
        return text[..(maxLen - 3)] + "...";
    }

    /// <summary>
    /// Extracts type names from Kotlin-style parameter list: "name: Type, name2: Type2"
    /// </summary>
    private static List<string> ExtractParamTypes(string paramsStr)
    {
        var types = new List<string>();
        var rx = new Regex(@"\w+\s*:\s*(?<type>[\w.]+)");
        foreach (Match m in rx.Matches(paramsStr))
        {
            var t = m.Groups["type"].Value.Split('.').Last();
            if (!IsPrimitiveType(t))
                types.Add(t);
        }
        return types;
    }

    /// <summary>
    /// Extracts type names from Swift-style parameter list: "label name: Type"
    /// </summary>
    private static List<string> ExtractSwiftParamTypes(string paramsStr)
    {
        var types = new List<string>();
        var rx = new Regex(@":\s*(?<type>[\w.]+[\w.?]*)");
        foreach (Match m in rx.Matches(paramsStr))
        {
            var t = m.Groups["type"].Value.TrimEnd('?');
            if (!IsSwiftPrimitive(t))
                types.Add(t);
        }
        return types;
    }

    /// <summary>
    /// Extracts type names from Python parameter annotations: "name: Type"
    /// </summary>
    private static List<string> ExtractPythonParamTypes(string paramsStr)
    {
        var types = new List<string>();
        var rx = new Regex(@"\w+\s*:\s*(?<type>[\w.]+)");
        foreach (Match m in rx.Matches(paramsStr))
        {
            var t = m.Groups["type"].Value;
            if (!IsPythonBuiltin(t))
                types.Add(t);
        }
        return types;
    }

    /// <summary>
    /// Extracts type names from TypeScript parameter list: "name: Type"
    /// </summary>
    private static List<string> ExtractTsParamTypes(string paramsStr)
    {
        var types = new List<string>();
        var rx = new Regex(@"\w+\s*:\s*(?<type>[\w.]+)");
        foreach (Match m in rx.Matches(paramsStr))
        {
            var t = m.Groups["type"].Value;
            if (!IsTsPrimitive(t))
                types.Add(t);
        }
        return types;
    }

    private static bool IsPrimitiveType(string t) =>
        t is "String" or "Int" or "Long" or "Boolean" or "Double" or "Float"
            or "Byte" or "Short" or "Char" or "Unit" or "Any" or "Nothing"
            or "Void" or "List" or "Map" or "Set" or "Array";

    private static bool IsSwiftPrimitive(string t) =>
        t is "String" or "Int" or "Double" or "Float" or "Bool" or "Void"
            or "Any" or "AnyObject" or "Never" or "Optional" or "Array"
            or "Dictionary" or "Set" or "Data" or "Date" or "URL"
            or "Int8" or "Int16" or "Int32" or "Int64"
            or "UInt" or "UInt8" or "UInt16" or "UInt32" or "UInt64"
            or "Character" or "CaseIterable" or "Codable" or "Hashable"
            or "Equatable" or "Identifiable" or "Sendable" or "Error";

    private static bool IsPythonBuiltin(string t) =>
        t is "str" or "int" or "float" or "bool" or "None" or "bytes"
            or "list" or "dict" or "set" or "tuple" or "type" or "object"
            or "Any" or "Optional" or "Union" or "List" or "Dict" or "Set"
            or "Tuple" or "Type" or "Callable" or "Awaitable" or "Coroutine"
            or "Iterator" or "Generator" or "Sequence" or "Mapping"
            or "self" or "cls" or "metaclass";

    private static bool IsTsPrimitive(string t) =>
        t is "string" or "number" or "boolean" or "void" or "null" or "undefined"
            or "any" or "never" or "unknown" or "object" or "symbol" or "bigint"
            or "Array" or "Promise" or "Map" or "Set" or "Record" or "Partial"
            or "Required" or "Readonly" or "Pick" or "Omit";

    private static bool IsCKeyword(string name) =>
        name is "if" or "else" or "for" or "while" or "do" or "switch" or "case"
            or "return" or "sizeof" or "typeof" or "goto" or "break" or "continue"
            or "default" or "main" or "defined";

    // ====================================================================
    // Java Parser
    // ====================================================================
    private static List<ParsedSymbol> ParseJava(string content)
    {
        var symbols = new List<ParsedSymbol>();
        var lines = content.Split('\n');
        string? pkg = null;
        var imports = new List<string>();

        var typeRx = new Regex(@"^\s*(public\s+|private\s+|protected\s+)?(static\s+)?(abstract\s+|final\s+)?(class|interface|enum|record|@interface)\s+(\w+)(\s*<[^>]+>)?(\s+extends\s+(\w+))?(\s+implements\s+([^{]+))?", RegexOptions.Compiled);
        var methodRx = new Regex(@"^\s*(public|private|protected)\s+(static\s+)?[\w<>\[\],\s]+\s+(\w+)\s*\(", RegexOptions.Compiled);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            if (line.StartsWith("package ")) { pkg = line.Replace("package ", "").TrimEnd(';').Trim(); continue; }
            if (line.StartsWith("import ")) { var imp = line.Replace("import ", "").TrimEnd(';').Trim().Split('.').Last(); imports.Add(imp); continue; }

            var tm = typeRx.Match(line);
            if (tm.Success)
            {
                var kind = tm.Groups[4].Value;
                var name = tm.Groups[5].Value;
                var extends_ = tm.Groups[8].Value;
                var implements_ = tm.Groups[10].Value;
                var deps = new List<string>();
                if (!string.IsNullOrEmpty(extends_)) deps.Add(extends_);
                if (!string.IsNullOrEmpty(implements_)) deps.AddRange(implements_.Split(',').Select(s => s.Trim().Split('<')[0]).Where(s => s.Length > 0));
                var endLine = FindBlockEnd(lines, i);
                symbols.Add(new ParsedSymbol(name, kind, pkg, $"{kind} {name}", string.Join("; ", deps), i + 1, endLine + 1));
                continue;
            }
        }
        return symbols;
    }

    // ====================================================================
    // Rust Parser
    // ====================================================================
    private static List<ParsedSymbol> ParseRust(string content)
    {
        var symbols = new List<ParsedSymbol>();
        var lines = content.Split('\n');

        var structRx = new Regex(@"^\s*(pub\s+)?(struct|enum|trait|type|union)\s+(\w+)", RegexOptions.Compiled);
        var fnRx = new Regex(@"^\s*(pub\s+)?(async\s+)?fn\s+(\w+)", RegexOptions.Compiled);
        var implRx = new Regex(@"^\s*impl\s+(?:<[^>]+>\s+)?(\w+)(?:\s+for\s+(\w+))?", RegexOptions.Compiled);
        var useRx = new Regex(@"^\s*use\s+.*::(\w+)", RegexOptions.Compiled);
        var modRx = new Regex(@"^\s*mod\s+(\w+)", RegexOptions.Compiled);
        string? currentMod = null;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var mm = modRx.Match(line);
            if (mm.Success) { currentMod = mm.Groups[1].Value; continue; }

            var sm = structRx.Match(line);
            if (sm.Success)
            {
                var kind = sm.Groups[2].Value;
                var name = sm.Groups[3].Value;
                var endLine = FindBlockEnd(lines, i);
                symbols.Add(new ParsedSymbol(name, kind, currentMod, $"{kind} {name}", "", i + 1, endLine + 1));
                continue;
            }

            var im = implRx.Match(line);
            if (im.Success)
            {
                var typeName = im.Groups[1].Value;
                var forType = im.Groups[2].Value;
                var sig = string.IsNullOrEmpty(forType) ? $"impl {typeName}" : $"impl {typeName} for {forType}";
                var endLine = FindBlockEnd(lines, i);
                symbols.Add(new ParsedSymbol(typeName, "impl", currentMod, sig, forType, i + 1, endLine + 1));
                continue;
            }

            var fm = fnRx.Match(line);
            if (fm.Success && !line.TrimStart().StartsWith("//"))
            {
                var name = fm.Groups[3].Value;
                if (name is "main" or "test" or "new") continue;
                var endLine = FindBlockEnd(lines, i);
                symbols.Add(new ParsedSymbol(name, "fn", currentMod, $"fn {name}()", "", i + 1, endLine + 1));
            }
        }
        return symbols;
    }

    // ====================================================================
    // Go Parser
    // ====================================================================
    private static List<ParsedSymbol> ParseGo(string content)
    {
        var symbols = new List<ParsedSymbol>();
        var lines = content.Split('\n');
        string? pkg = null;

        var structRx = new Regex(@"^\s*type\s+(\w+)\s+(struct|interface)", RegexOptions.Compiled);
        var funcRx = new Regex(@"^\s*func\s+(?:\([^)]+\)\s+)?(\w+)\s*\(", RegexOptions.Compiled);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.StartsWith("package ")) { pkg = line.Replace("package ", "").Trim(); continue; }

            var sm = structRx.Match(line);
            if (sm.Success)
            {
                var name = sm.Groups[1].Value;
                var kind = sm.Groups[2].Value;
                var endLine = FindBlockEnd(lines, i);
                symbols.Add(new ParsedSymbol(name, kind, pkg, $"type {name} {kind}", "", i + 1, endLine + 1));
                continue;
            }

            var fm = funcRx.Match(line);
            if (fm.Success)
            {
                var name = fm.Groups[1].Value;
                if (char.IsUpper(name[0])) // Only exported functions
                {
                    var endLine = FindBlockEnd(lines, i);
                    symbols.Add(new ParsedSymbol(name, "func", pkg, $"func {name}()", "", i + 1, endLine + 1));
                }
            }
        }
        return symbols;
    }

    // ====================================================================
    // Ruby Parser
    // ====================================================================
    private static List<ParsedSymbol> ParseRuby(string content)
    {
        var symbols = new List<ParsedSymbol>();
        var lines = content.Split('\n');
        string? currentModule = null;

        var classRx = new Regex(@"^\s*(class|module)\s+(\w+)(?:\s*<\s*(\w+))?", RegexOptions.Compiled);
        var defRx = new Regex(@"^\s*def\s+(self\.)?(\w+[?!]?)", RegexOptions.Compiled);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            var cm = classRx.Match(line);
            if (cm.Success)
            {
                var kind = cm.Groups[1].Value;
                var name = cm.Groups[2].Value;
                var parent = cm.Groups[3].Value;
                if (kind == "module") currentModule = name;
                var endLine = FindRubyEnd(lines, i);
                symbols.Add(new ParsedSymbol(name, kind, currentModule, $"{kind} {name}", parent, i + 1, endLine + 1));
                continue;
            }

            var dm = defRx.Match(line);
            if (dm.Success)
            {
                var name = dm.Groups[2].Value;
                var isStatic = dm.Groups[1].Success;
                var endLine = FindRubyEnd(lines, i);
                symbols.Add(new ParsedSymbol(name, isStatic ? "class_method" : "method", currentModule, $"def {name}", "", i + 1, endLine + 1));
            }
        }
        return symbols;
    }

    private static int FindRubyEnd(string[] lines, int startLine)
    {
        int depth = 0;
        for (int i = startLine; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("#")) continue;
            if (Regex.IsMatch(trimmed, @"^(class|module|def|do|if|unless|while|until|for|case|begin)\b") && !trimmed.Contains(" end"))
                depth++;
            if (trimmed == "end" || trimmed.StartsWith("end ") || trimmed.StartsWith("end#"))
            {
                depth--;
                if (depth <= 0) return i;
            }
        }
        return Math.Min(startLine + 50, lines.Length - 1);
    }

    // ====================================================================
    // Dart Parser
    // ====================================================================
    private static List<ParsedSymbol> ParseDart(string content)
    {
        var symbols = new List<ParsedSymbol>();
        var lines = content.Split('\n');

        var classRx = new Regex(@"^\s*(abstract\s+)?(class|mixin|enum|extension)\s+(\w+)(?:\s+(?:extends|with|on)\s+(\w+))?(?:\s+implements\s+([^{]+))?", RegexOptions.Compiled);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var cm = classRx.Match(line);
            if (cm.Success)
            {
                var kind = cm.Groups[2].Value;
                var name = cm.Groups[3].Value;
                var extends_ = cm.Groups[4].Value;
                var implements_ = cm.Groups[5].Value;
                var deps = new List<string>();
                if (!string.IsNullOrEmpty(extends_)) deps.Add(extends_);
                if (!string.IsNullOrEmpty(implements_)) deps.AddRange(implements_.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));
                var endLine = FindBlockEnd(lines, i);
                symbols.Add(new ParsedSymbol(name, kind, null, $"{kind} {name}", string.Join("; ", deps), i + 1, endLine + 1));
            }
        }
        return symbols;
    }

    // ====================================================================
    // SQL Parser (tables, views, stored procedures, functions)
    // ====================================================================
    private static List<ParsedSymbol> ParseSql(string content)
    {
        var symbols = new List<ParsedSymbol>();
        var tableRx = new Regex(@"CREATE\s+TABLE\s+(?:\[?dbo\]?\.)?\[?(\w+)\]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var viewRx = new Regex(@"CREATE\s+(?:OR\s+ALTER\s+)?VIEW\s+(?:\[?dbo\]?\.)?\[?(\w+)\]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var procRx = new Regex(@"CREATE\s+(?:OR\s+ALTER\s+)?PROC(?:EDURE)?\s+(?:\[?dbo\]?\.)?\[?(\w+)\]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var funcRx = new Regex(@"CREATE\s+(?:OR\s+ALTER\s+)?FUNCTION\s+(?:\[?dbo\]?\.)?\[?(\w+)\]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        foreach (Match m in tableRx.Matches(content)) symbols.Add(new ParsedSymbol(m.Groups[1].Value, "table", null, $"CREATE TABLE {m.Groups[1].Value}", "", 0, 0));
        foreach (Match m in viewRx.Matches(content)) symbols.Add(new ParsedSymbol(m.Groups[1].Value, "view", null, $"CREATE VIEW {m.Groups[1].Value}", "", 0, 0));
        foreach (Match m in procRx.Matches(content)) symbols.Add(new ParsedSymbol(m.Groups[1].Value, "procedure", null, $"CREATE PROCEDURE {m.Groups[1].Value}", "", 0, 0));
        foreach (Match m in funcRx.Matches(content)) symbols.Add(new ParsedSymbol(m.Groups[1].Value, "function", null, $"CREATE FUNCTION {m.Groups[1].Value}", "", 0, 0));
        return symbols;
    }

    // ====================================================================
    // Protobuf Parser (messages, services, enums)
    // ====================================================================
    private static List<ParsedSymbol> ParseProtobuf(string content)
    {
        var symbols = new List<ParsedSymbol>();
        var lines = content.Split('\n');
        string? pkg = null;

        var msgRx = new Regex(@"^\s*message\s+(\w+)", RegexOptions.Compiled);
        var svcRx = new Regex(@"^\s*service\s+(\w+)", RegexOptions.Compiled);
        var enumRx = new Regex(@"^\s*enum\s+(\w+)", RegexOptions.Compiled);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.StartsWith("package ")) { pkg = line.Replace("package ", "").TrimEnd(';').Trim(); continue; }

            var mm = msgRx.Match(line); if (mm.Success) { symbols.Add(new ParsedSymbol(mm.Groups[1].Value, "message", pkg, $"message {mm.Groups[1].Value}", "", i + 1, FindBlockEnd(lines, i) + 1)); continue; }
            var sm = svcRx.Match(line); if (sm.Success) { symbols.Add(new ParsedSymbol(sm.Groups[1].Value, "service", pkg, $"service {sm.Groups[1].Value}", "", i + 1, FindBlockEnd(lines, i) + 1)); continue; }
            var em = enumRx.Match(line); if (em.Success) { symbols.Add(new ParsedSymbol(em.Groups[1].Value, "enum", pkg, $"enum {em.Groups[1].Value}", "", i + 1, FindBlockEnd(lines, i) + 1)); }
        }
        return symbols;
    }

    // ====================================================================
    // Terraform Parser (resources, variables, outputs, data sources)
    // ====================================================================
    private static List<ParsedSymbol> ParseTerraform(string content)
    {
        var symbols = new List<ParsedSymbol>();
        var resourceRx = new Regex(@"^(resource|data)\s+""(\w+)""\s+""(\w+)""", RegexOptions.Multiline | RegexOptions.Compiled);
        var varRx = new Regex(@"^variable\s+""(\w+)""", RegexOptions.Multiline | RegexOptions.Compiled);
        var outputRx = new Regex(@"^output\s+""(\w+)""", RegexOptions.Multiline | RegexOptions.Compiled);
        var moduleRx = new Regex(@"^module\s+""(\w+)""", RegexOptions.Multiline | RegexOptions.Compiled);

        foreach (Match m in resourceRx.Matches(content)) symbols.Add(new ParsedSymbol($"{m.Groups[2].Value}.{m.Groups[3].Value}", m.Groups[1].Value, null, $"{m.Groups[1].Value} \"{m.Groups[2].Value}\" \"{m.Groups[3].Value}\"", m.Groups[2].Value, 0, 0));
        foreach (Match m in varRx.Matches(content)) symbols.Add(new ParsedSymbol(m.Groups[1].Value, "variable", null, $"variable \"{m.Groups[1].Value}\"", "", 0, 0));
        foreach (Match m in outputRx.Matches(content)) symbols.Add(new ParsedSymbol(m.Groups[1].Value, "output", null, $"output \"{m.Groups[1].Value}\"", "", 0, 0));
        foreach (Match m in moduleRx.Matches(content)) symbols.Add(new ParsedSymbol(m.Groups[1].Value, "module", null, $"module \"{m.Groups[1].Value}\"", "", 0, 0));
        return symbols;
    }

    // ====================================================================
    // Bicep Parser (resources, params, variables, outputs, modules)
    // ====================================================================
    private static List<ParsedSymbol> ParseBicep(string content)
    {
        var symbols = new List<ParsedSymbol>();
        var resourceRx = new Regex(@"^resource\s+(\w+)\s+'([^']+)'", RegexOptions.Multiline | RegexOptions.Compiled);
        var paramRx = new Regex(@"^param\s+(\w+)\s+(\w+)", RegexOptions.Multiline | RegexOptions.Compiled);
        var varRx = new Regex(@"^var\s+(\w+)\s*=", RegexOptions.Multiline | RegexOptions.Compiled);
        var outputRx = new Regex(@"^output\s+(\w+)\s+(\w+)", RegexOptions.Multiline | RegexOptions.Compiled);
        var moduleRx = new Regex(@"^module\s+(\w+)\s+'([^']+)'", RegexOptions.Multiline | RegexOptions.Compiled);

        foreach (Match m in resourceRx.Matches(content)) symbols.Add(new ParsedSymbol(m.Groups[1].Value, "resource", null, $"resource {m.Groups[1].Value} '{m.Groups[2].Value}'", m.Groups[2].Value, 0, 0));
        foreach (Match m in paramRx.Matches(content)) symbols.Add(new ParsedSymbol(m.Groups[1].Value, "param", null, $"param {m.Groups[1].Value} {m.Groups[2].Value}", "", 0, 0));
        foreach (Match m in varRx.Matches(content)) symbols.Add(new ParsedSymbol(m.Groups[1].Value, "var", null, $"var {m.Groups[1].Value}", "", 0, 0));
        foreach (Match m in outputRx.Matches(content)) symbols.Add(new ParsedSymbol(m.Groups[1].Value, "output", null, $"output {m.Groups[1].Value} {m.Groups[2].Value}", "", 0, 0));
        foreach (Match m in moduleRx.Matches(content)) symbols.Add(new ParsedSymbol(m.Groups[1].Value, "module", null, $"module {m.Groups[1].Value}", m.Groups[2].Value, 0, 0));
        return symbols;
    }

    // ====================================================================
    // PowerShell Parser (functions, classes, enums)
    // ====================================================================
    private static List<ParsedSymbol> ParsePowerShell(string content)
    {
        var symbols = new List<ParsedSymbol>();
        var lines = content.Split('\n');

        var funcRx = new Regex(@"^\s*function\s+([\w-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var classRx = new Regex(@"^\s*class\s+(\w+)(?:\s*:\s*(\w+))?", RegexOptions.Compiled);
        var enumRx = new Regex(@"^\s*enum\s+(\w+)", RegexOptions.Compiled);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var fm = funcRx.Match(line); if (fm.Success) { symbols.Add(new ParsedSymbol(fm.Groups[1].Value, "function", null, $"function {fm.Groups[1].Value}", "", i + 1, FindBlockEnd(lines, i) + 1)); continue; }
            var cm = classRx.Match(line); if (cm.Success) { symbols.Add(new ParsedSymbol(cm.Groups[1].Value, "class", null, $"class {cm.Groups[1].Value}", cm.Groups[2].Value, i + 1, FindBlockEnd(lines, i) + 1)); continue; }
            var em = enumRx.Match(line); if (em.Success) { symbols.Add(new ParsedSymbol(em.Groups[1].Value, "enum", null, $"enum {em.Groups[1].Value}", "", i + 1, FindBlockEnd(lines, i) + 1)); }
        }
        return symbols;
    }

    // ====================================================================
    // Shell Script Parser (functions only)
    // ====================================================================
    private static List<ParsedSymbol> ParseShell(string content)
    {
        var symbols = new List<ParsedSymbol>();
        var lines = content.Split('\n');

        // function name() { ... } or name() { ... }
        var funcRx = new Regex(@"^\s*(?:function\s+)?([\w-]+)\s*\(\s*\)", RegexOptions.Compiled);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.TrimStart().StartsWith("#")) continue;
            var fm = funcRx.Match(line);
            if (fm.Success)
            {
                var name = fm.Groups[1].Value;
                symbols.Add(new ParsedSymbol(name, "function", null, $"function {name}()", "", i + 1, FindBlockEnd(lines, i) + 1));
            }
        }
        return symbols;
    }

    // ====================================================================
    // OpenAPI / Swagger Parser
    // ====================================================================
    // Extracts: API title+version, paths/{endpoint}.{method} → endpoints,
    //           components/schemas (OpenAPI 3.x) or definitions (Swagger 2)
    //
    // Handles both OpenAPI 3.x and Swagger 2.0 JSON formats.
    // YAML support requires YamlDotNet — currently JSON only; YAML files
    // are skipped with a comment symbol indicating the limitation.
    //
    // Example matches:
    //   {"openapi":"3.0.3","info":{"title":"TheWatch API","version":"1.0"}, ...}
    //   {"swagger":"2.0","info":{"title":"Emergency API"}, ...}
    //
    // Example usage:
    //   var symbols = MultiLanguageParser.ParseOpenApi(jsonContent);
    //   // => [ ParsedSymbol("TheWatch API v1.0", "api", ...),
    //   //      ParsedSymbol("GET /api/users", "endpoint", ...),
    //   //      ParsedSymbol("User", "schema", ...) ]
    //
    // WAL: JSON-only parsing via System.Text.Json. YAML OpenAPI files need
    //      YamlDotNet NuGet to deserialize; those are currently skipped.
    //      Missing fields are handled gracefully — no exceptions on incomplete specs.
    // ====================================================================

    /// <summary>
    /// Returns true if the file is likely an OpenAPI/Swagger specification.
    /// Checks: filename contains "openapi" or "swagger", OR content starts with
    /// known OpenAPI/Swagger JSON or YAML markers.
    /// </summary>
    public static bool IsOpenApiFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath).ToLowerInvariant();

        // Filename-based detection
        if (fileName.Contains("openapi") || fileName.Contains("swagger"))
            return true;

        return false;
    }

    /// <summary>
    /// Content-based OpenAPI detection — reads the first portion of file content
    /// to determine if it's an OpenAPI/Swagger spec.
    /// </summary>
    public static bool IsOpenApiContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        var trimmed = content.TrimStart();

        // JSON OpenAPI 3.x: {"openapi":"3...
        if (trimmed.StartsWith("{\"openapi\":", StringComparison.Ordinal) ||
            trimmed.StartsWith("{ \"openapi\":", StringComparison.Ordinal))
            return true;

        // JSON Swagger 2.0: {"swagger":"2...
        if (trimmed.StartsWith("{\"swagger\":", StringComparison.Ordinal) ||
            trimmed.StartsWith("{ \"swagger\":", StringComparison.Ordinal))
            return true;

        // YAML OpenAPI 3.x: openapi: "3... or openapi: 3...
        if (trimmed.StartsWith("openapi:", StringComparison.OrdinalIgnoreCase))
            return true;

        // YAML Swagger 2.0: swagger: "2... or swagger: 2...
        if (trimmed.StartsWith("swagger:", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Parse an OpenAPI/Swagger JSON spec and return symbols for:
    ///   - The API itself (title + version)
    ///   - Each endpoint (path + method → operationId, tags, parameters, request/response types)
    ///   - Each schema definition (components/schemas for 3.x, definitions for 2.0)
    /// Dependencies: endpoints depend on their referenced schema names.
    /// </summary>
    public static List<ParsedSymbol> ParseOpenApi(string content)
    {
        var symbols = new List<ParsedSymbol>();

        if (string.IsNullOrWhiteSpace(content))
            return symbols;

        var trimmed = content.TrimStart();

        // YAML detection — currently unsupported without YamlDotNet
        if (!trimmed.StartsWith("{"))
        {
            // YAML OpenAPI file detected but cannot parse without YamlDotNet
            // TODO: Add YamlDotNet NuGet package to support YAML OpenAPI specs
            if (trimmed.StartsWith("openapi:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("swagger:", StringComparison.OrdinalIgnoreCase))
            {
                symbols.Add(new ParsedSymbol(
                    "OpenAPI (YAML — parse skipped)",
                    "api",
                    null,
                    "YAML OpenAPI spec — needs YamlDotNet for parsing",
                    "",
                    1, 1
                ));
            }
            return symbols;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(content, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        }
        catch
        {
            return symbols; // Not valid JSON
        }

        var root = doc.RootElement;

        // ── Detect version: OpenAPI 3.x vs Swagger 2.0 ─────────────────
        var isSwagger2 = root.TryGetProperty("swagger", out var swaggerProp);
        var isOpenApi3 = root.TryGetProperty("openapi", out var openapiProp);
        var specVersion = isSwagger2
            ? swaggerProp.GetString() ?? "2.0"
            : isOpenApi3
                ? openapiProp.GetString() ?? "3.0"
                : "unknown";

        // ── API title + version as top-level symbol ─────────────────────
        var apiTitle = "Untitled API";
        var apiVersion = specVersion;
        if (root.TryGetProperty("info", out var infoProp))
        {
            if (infoProp.TryGetProperty("title", out var titleProp))
                apiTitle = titleProp.GetString() ?? apiTitle;
            if (infoProp.TryGetProperty("version", out var versionProp))
                apiVersion = versionProp.GetString() ?? apiVersion;
        }

        symbols.Add(new ParsedSymbol(
            $"{apiTitle} v{apiVersion}",
            "api",
            null,
            isSwagger2 ? $"Swagger {specVersion}" : $"OpenAPI {specVersion}",
            "",
            1, 1
        ));

        // ── Parse paths → endpoints ─────────────────────────────────────
        if (root.TryGetProperty("paths", out var pathsProp))
        {
            var endpointLine = 2; // approximate
            foreach (var pathEntry in pathsProp.EnumerateObject())
            {
                var pathStr = pathEntry.Name; // e.g., "/api/users"

                foreach (var methodEntry in pathEntry.Value.EnumerateObject())
                {
                    var method = methodEntry.Name.ToUpperInvariant();

                    // Skip non-HTTP-method keys like "parameters", "summary", "$ref"
                    if (method is not ("GET" or "POST" or "PUT" or "DELETE" or "PATCH" or "OPTIONS" or "HEAD" or "TRACE"))
                        continue;

                    var endpointName = $"{method} {pathStr}";
                    var operationId = "";
                    var tagsStr = "";
                    var paramSummary = "";
                    var requestType = "";
                    var responseTypes = new List<string>();
                    var schemaDeps = new List<string>();

                    var op = methodEntry.Value;

                    // operationId
                    if (op.TryGetProperty("operationId", out var opIdProp))
                        operationId = opIdProp.GetString() ?? "";

                    // tags
                    if (op.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Array)
                        tagsStr = string.Join(", ", tagsProp.EnumerateArray().Select(t => t.GetString() ?? ""));

                    // parameters summary
                    if (op.TryGetProperty("parameters", out var paramsProp) && paramsProp.ValueKind == JsonValueKind.Array)
                    {
                        var paramNames = new List<string>();
                        foreach (var p in paramsProp.EnumerateArray())
                        {
                            var pName = "";
                            var pIn = "";
                            if (p.TryGetProperty("name", out var pNameProp)) pName = pNameProp.GetString() ?? "";
                            if (p.TryGetProperty("in", out var pInProp)) pIn = pInProp.GetString() ?? "";
                            paramNames.Add($"{pName}({pIn})");

                            // Extract schema refs from parameters
                            var schemaRef = ExtractSchemaRef(p);
                            if (schemaRef != null) schemaDeps.Add(schemaRef);
                        }
                        paramSummary = string.Join(", ", paramNames);
                    }

                    // requestBody (OpenAPI 3.x)
                    if (op.TryGetProperty("requestBody", out var reqBodyProp))
                    {
                        var reqRef = ExtractDeepSchemaRef(reqBodyProp);
                        if (reqRef != null)
                        {
                            requestType = reqRef;
                            schemaDeps.Add(reqRef);
                        }
                    }

                    // body parameter (Swagger 2.0)
                    if (isSwagger2 && op.TryGetProperty("parameters", out var swParams) && swParams.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var p in swParams.EnumerateArray())
                        {
                            if (p.TryGetProperty("in", out var inProp) && inProp.GetString() == "body")
                            {
                                var bodyRef = ExtractSchemaRef(p);
                                if (bodyRef != null)
                                {
                                    requestType = bodyRef;
                                    schemaDeps.Add(bodyRef);
                                }
                            }
                        }
                    }

                    // responses
                    if (op.TryGetProperty("responses", out var responsesProp))
                    {
                        foreach (var resp in responsesProp.EnumerateObject())
                        {
                            var respRef = isSwagger2
                                ? ExtractSchemaRef(resp.Value)
                                : ExtractDeepSchemaRef(resp.Value);
                            if (respRef != null)
                            {
                                responseTypes.Add(respRef);
                                schemaDeps.Add(respRef);
                            }
                        }
                    }

                    // Build signature
                    var sigParts = new List<string>();
                    if (!string.IsNullOrEmpty(operationId)) sigParts.Add($"operationId={operationId}");
                    if (!string.IsNullOrEmpty(tagsStr)) sigParts.Add($"tags=[{tagsStr}]");
                    if (!string.IsNullOrEmpty(paramSummary)) sigParts.Add($"params=({paramSummary})");
                    if (!string.IsNullOrEmpty(requestType)) sigParts.Add($"request={requestType}");
                    if (responseTypes.Count > 0) sigParts.Add($"response={string.Join("|", responseTypes.Distinct())}");
                    var signature = $"{endpointName} {{{string.Join("; ", sigParts)}}}";

                    // Deduplicate schema dependencies
                    var dependsOn = string.Join(";", schemaDeps.Distinct());

                    symbols.Add(new ParsedSymbol(
                        endpointName,
                        "endpoint",
                        null,
                        signature,
                        dependsOn,
                        endpointLine, endpointLine
                    ));

                    endpointLine++;
                }
            }
        }

        // ── Parse schemas ───────────────────────────────────────────────
        // OpenAPI 3.x: components.schemas
        // Swagger 2.0: definitions
        JsonElement? schemaContainer = null;
        if (root.TryGetProperty("components", out var componentsProp) &&
            componentsProp.TryGetProperty("schemas", out var schemasProp))
        {
            schemaContainer = schemasProp;
        }
        else if (root.TryGetProperty("definitions", out var defsProp))
        {
            schemaContainer = defsProp;
        }

        if (schemaContainer.HasValue)
        {
            var schemaLine = symbols.Count + 2; // approximate line numbers
            foreach (var schema in schemaContainer.Value.EnumerateObject())
            {
                var schemaName = schema.Name;
                var schemaType = "object";
                var properties = new List<string>();
                var schemaDeps = new List<string>();

                if (schema.Value.TryGetProperty("type", out var typeProp))
                    schemaType = typeProp.GetString() ?? "object";

                // Extract properties
                if (schema.Value.TryGetProperty("properties", out var propsProp))
                {
                    foreach (var prop in propsProp.EnumerateObject())
                    {
                        var propType = "any";
                        if (prop.Value.TryGetProperty("type", out var ptProp))
                            propType = ptProp.GetString() ?? "any";

                        // Check for $ref in the property
                        var propRef = ExtractSchemaRef(prop.Value);
                        if (propRef != null)
                        {
                            propType = propRef;
                            schemaDeps.Add(propRef);
                        }

                        // Check for array items ref
                        if (prop.Value.TryGetProperty("items", out var itemsProp))
                        {
                            var itemRef = ExtractSchemaRef(itemsProp);
                            if (itemRef != null)
                            {
                                propType = $"{itemRef}[]";
                                schemaDeps.Add(itemRef);
                            }
                        }

                        properties.Add($"{prop.Name}: {propType}");
                    }
                }

                // Check allOf/oneOf/anyOf for composition deps
                foreach (var compositionKey in new[] { "allOf", "oneOf", "anyOf" })
                {
                    if (schema.Value.TryGetProperty(compositionKey, out var compProp) &&
                        compProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in compProp.EnumerateArray())
                        {
                            var compRef = ExtractSchemaRef(item);
                            if (compRef != null)
                                schemaDeps.Add(compRef);
                        }
                    }
                }

                // Build signature
                var propSummary = properties.Count > 0
                    ? string.Join(", ", properties.Take(10))
                    : "no properties";
                if (properties.Count > 10) propSummary += $", ... +{properties.Count - 10} more";

                var schemaSig = $"{schemaType} {schemaName} {{ {propSummary} }}";
                var dependsOn = string.Join(";", schemaDeps.Distinct());

                symbols.Add(new ParsedSymbol(
                    schemaName,
                    "schema",
                    null,
                    schemaSig,
                    dependsOn,
                    schemaLine, schemaLine
                ));

                schemaLine++;
            }
        }

        doc.Dispose();
        return symbols;
    }

    /// <summary>
    /// Extract a schema name from a $ref string like "#/components/schemas/User" or "#/definitions/User".
    /// Returns just the schema name (last segment), or null if no $ref found.
    /// </summary>
    private static string? ExtractSchemaRef(JsonElement element)
    {
        if (element.TryGetProperty("$ref", out var refProp))
        {
            var refStr = refProp.GetString();
            if (refStr != null)
            {
                var lastSlash = refStr.LastIndexOf('/');
                return lastSlash >= 0 ? refStr[(lastSlash + 1)..] : refStr;
            }
        }

        // Check for schema property with $ref inside
        if (element.TryGetProperty("schema", out var schemaProp))
        {
            return ExtractSchemaRef(schemaProp);
        }

        return null;
    }

    /// <summary>
    /// Extract schema ref from nested content structures (OpenAPI 3.x requestBody/response).
    /// Navigates: content → application/json → schema → $ref
    /// Also handles direct $ref and schema.$ref.
    /// </summary>
    private static string? ExtractDeepSchemaRef(JsonElement element)
    {
        // Direct $ref
        var direct = ExtractSchemaRef(element);
        if (direct != null) return direct;

        // content → {mediaType} → schema → $ref
        if (element.TryGetProperty("content", out var contentProp))
        {
            foreach (var mediaType in contentProp.EnumerateObject())
            {
                if (mediaType.Value.TryGetProperty("schema", out var schemaProp))
                {
                    var schemaRef = ExtractSchemaRef(schemaProp);
                    if (schemaRef != null) return schemaRef;

                    // Array of items
                    if (schemaProp.TryGetProperty("items", out var itemsProp))
                    {
                        var itemRef = ExtractSchemaRef(itemsProp);
                        if (itemRef != null) return itemRef;
                    }
                }
            }
        }

        return null;
    }
}
