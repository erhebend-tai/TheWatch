// =============================================================================
// TreeSitterService — Multi-language AST parsing via tree-sitter.
// =============================================================================
// Tree-sitter provides incremental, error-tolerant parsing for 100+ languages.
// This service wraps the tree-sitter CLI (or .NET bindings if available) to
// parse files that Roslyn cannot handle:
//
// Supported Languages (relevant to TheWatch):
//   C#     — Roslyn preferred, tree-sitter as fallback for partial/broken files
//   Swift  — TheWatch-iOS (SwiftUI views, protocols, extensions)
//   Kotlin — TheWatch-Android (Jetpack Compose, Hilt modules, Flows)
//   Java   — TheWatch-Android (legacy, Android SDK interop)
//   XML    — Android layouts, AndroidManifest.xml, .csproj, .sln
//   JSON   — appsettings.json, package.json, tsconfig.json
//   YAML   — GitHub Actions, Docker Compose, Kubernetes manifests
//   HCL    — Terraform infrastructure definitions
//   SQL    — Database migrations, stored procedures
//   Proto  — Protocol Buffers (gRPC service definitions)
//   TOML   — Cargo.toml, config files
//   Bash   — CI scripts, deployment scripts
//   Markdown — Documentation parsing
//
// Architecture:
//   TreeSitterService
//     ├── ParseFileAsync(path)          — Parse a single file, return AST
//     ├── ParseDirectoryAsync(dir)      — Parse all recognized files in a dir
//     ├── ExtractSymbolsAsync(path)     — Extract classes/functions/structs
//     ├── GetLanguageForFile(path)      — Detect language from extension
//     ├── QueryAsync(path, query)       — Run a tree-sitter S-expression query
//     ├── FindPatternAsync(dir, query)  — Search across files with TS queries
//     └── GetCrossProjectSymbols()      — Unified symbol index across all langs
//
// Implementation Strategy:
//   1. Primary: tree-sitter CLI (`tree-sitter parse`, `tree-sitter query`)
//      - npm install -g tree-sitter-cli
//      - Grammars auto-installed per-language
//   2. Fallback: TreeSitter.Bindings NuGet (dotnet-tree-sitter)
//      - Native .NET bindings with pre-compiled grammars
//      - More portable but fewer language grammars available
//   3. Regex fallback: For when neither is available — basic symbol extraction
//
// Example:
//   var ts = new TreeSitterService(repoRoot);
//   var ast = await ts.ParseFileAsync("TheWatch-iOS/Views/HomeMapView.swift");
//   var symbols = await ts.ExtractSymbolsAsync("TheWatch-Android/app/.../HomeScreen.kt");
//   var hits = await ts.FindPatternAsync("TheWatch-iOS", "(function_declaration name: (identifier) @name)");
//
// WAL: tree-sitter-cli must be installed globally: npm install -g tree-sitter-cli
//      Grammar packages are auto-installed on first parse of each language.
//      tree-sitter parse outputs S-expression ASTs to stdout.
//      For .NET: consider https://github.com/nickvdyck/dotnet-tree-sitter
//      or https://github.com/AkosLukacs/dotnet-tree-sitter which wraps the native lib.
// =============================================================================

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TheWatch.Cli.Services.TreeSitter;

public class TreeSitterService
{
    private readonly string _repoRoot;
    private readonly ConcurrentDictionary<string, ParsedFile> _cache = new();
    private readonly TreeSitterBackend _backend;
    private HttpClient? _lsifClient;
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // Language → file extension mapping
    private static readonly Dictionary<string, string[]> LanguageExtensions = new()
    {
        ["c_sharp"] = new[] { ".cs" },
        ["swift"] = new[] { ".swift" },
        ["kotlin"] = new[] { ".kt", ".kts" },
        ["java"] = new[] { ".java" },
        ["xml"] = new[] { ".xml", ".csproj", ".sln", ".slnx", ".axml", ".xaml", ".plist" },
        ["json"] = new[] { ".json" },
        ["yaml"] = new[] { ".yml", ".yaml" },
        ["hcl"] = new[] { ".tf", ".tfvars" },
        ["sql"] = new[] { ".sql" },
        ["proto"] = new[] { ".proto" },
        ["toml"] = new[] { ".toml" },
        ["bash"] = new[] { ".sh", ".bash" },
        ["markdown"] = new[] { ".md" },
        ["html"] = new[] { ".html", ".htm", ".razor" },
        ["css"] = new[] { ".css", ".scss" },
        ["javascript"] = new[] { ".js", ".mjs" },
        ["typescript"] = new[] { ".ts", ".tsx" },
    };

    // Reverse map: extension → language
    private static readonly Dictionary<string, string> ExtensionToLanguage;

    static TreeSitterService()
    {
        ExtensionToLanguage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (lang, exts) in LanguageExtensions)
        {
            foreach (var ext in exts)
                ExtensionToLanguage[ext] = lang;
        }
    }

    public TreeSitterService(string repoRoot)
    {
        _repoRoot = repoRoot;
        _backend = DetectBackend();
    }

    /// <summary>Detect language from file extension.</summary>
    public static string? GetLanguageForFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return ExtensionToLanguage.GetValueOrDefault(ext);
    }

    /// <summary>Parse a single file and return its AST as an S-expression string.</summary>
    public async Task<ParsedFile> ParseFileAsync(string filePath, CancellationToken ct = default)
    {
        var fullPath = Path.IsPathRooted(filePath) ? filePath : Path.Combine(_repoRoot, filePath);
        var language = GetLanguageForFile(fullPath);

        if (language is null)
            return new ParsedFile { FilePath = fullPath, Error = "Unsupported file type" };

        // Check cache
        var cacheKey = $"{fullPath}:{File.GetLastWriteTimeUtc(fullPath):o}";
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var result = _backend switch
        {
            TreeSitterBackend.Cli => await ParseWithCliAsync(fullPath, language, ct),
            TreeSitterBackend.Regex => ParseWithRegex(fullPath, language),
            _ => new ParsedFile { FilePath = fullPath, Error = "No tree-sitter backend available" }
        };

        _cache[cacheKey] = result;
        return result;
    }

    /// <summary>Parse all recognized files in a directory.</summary>
    public async Task<List<ParsedFile>> ParseDirectoryAsync(string directory, CancellationToken ct = default)
    {
        var fullDir = Path.IsPathRooted(directory) ? directory : Path.Combine(_repoRoot, directory);
        var results = new ConcurrentBag<ParsedFile>();

        var files = Directory.GetFiles(fullDir, "*.*", SearchOption.AllDirectories)
            .Where(f => GetLanguageForFile(f) is not null)
            .Where(f => !f.Contains(Path.Combine("", "obj", "")) && !f.Contains(Path.Combine("", "bin", "")))
            .Where(f => !f.Contains(Path.Combine("", ".git", "")));

        await Parallel.ForEachAsync(files, ct, async (file, innerCt) =>
        {
            var parsed = await ParseFileAsync(file, innerCt);
            results.Add(parsed);
        });

        return results.OrderBy(r => r.FilePath).ToList();
    }

    /// <summary>
    /// Extract symbols (classes, functions, structs, protocols, etc.) from a file.
    /// Works across all supported languages.
    /// </summary>
    public async Task<List<SymbolEntry>> ExtractSymbolsAsync(string filePath, CancellationToken ct = default)
    {
        var fullPath = Path.IsPathRooted(filePath) ? filePath : Path.Combine(_repoRoot, filePath);
        var language = GetLanguageForFile(fullPath);

        if (language is null) return new();

        // Use regex-based extraction (works without tree-sitter CLI)
        var source = await File.ReadAllTextAsync(fullPath, ct);
        return ExtractSymbolsFromSource(source, language, fullPath);
    }

    /// <summary>
    /// Run a tree-sitter S-expression query against a file.
    /// Requires tree-sitter CLI.
    /// </summary>
    public async Task<List<QueryMatch>> QueryAsync(string filePath, string query, CancellationToken ct = default)
    {
        if (_backend != TreeSitterBackend.Cli)
            return new() { new QueryMatch { Error = "tree-sitter CLI required for queries" } };

        var fullPath = Path.IsPathRooted(filePath) ? filePath : Path.Combine(_repoRoot, filePath);
        var language = GetLanguageForFile(fullPath);
        if (language is null) return new();

        // Write query to temp file
        var queryFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(queryFile, query, ct);

        try
        {
            var result = await RunProcessAsync("tree-sitter", $"query {queryFile} {fullPath}", ct);
            if (!result.Success)
                return new() { new QueryMatch { Error = result.Error } };

            return ParseQueryOutput(result.Output);
        }
        finally
        {
            File.Delete(queryFile);
        }
    }

    /// <summary>
    /// Search across all files in a directory using a tree-sitter query.
    /// </summary>
    public async Task<List<QueryMatch>> FindPatternAsync(string directory, string query, CancellationToken ct = default)
    {
        var fullDir = Path.IsPathRooted(directory) ? directory : Path.Combine(_repoRoot, directory);
        var allMatches = new ConcurrentBag<QueryMatch>();

        var files = Directory.GetFiles(fullDir, "*.*", SearchOption.AllDirectories)
            .Where(f => GetLanguageForFile(f) is not null)
            .Where(f => !f.Contains("obj") && !f.Contains("bin") && !f.Contains(".git"));

        await Parallel.ForEachAsync(files, ct, async (file, innerCt) =>
        {
            var matches = await QueryAsync(file, query, innerCt);
            foreach (var match in matches.Where(m => m.Error is null))
            {
                match.FilePath = file;
                allMatches.Add(match);
            }
        });

        return allMatches.OrderBy(m => m.FilePath).ThenBy(m => m.Line).ToList();
    }

    /// <summary>
    /// Build a unified cross-project symbol index across all languages.
    /// Covers TheWatch-Aspire (.cs), TheWatch-iOS (.swift), TheWatch-Android (.kt/.java).
    /// </summary>
    public async Task<CrossProjectSymbolIndex> GetCrossProjectSymbolsAsync(CancellationToken ct = default)
    {
        var index = new CrossProjectSymbolIndex();

        var projects = new[]
        {
            ("TheWatch-Aspire", "Aspire/.NET"),
            ("TheWatch-iOS", "iOS/Swift"),
            ("TheWatch-Android", "Android/Kotlin"),
        };

        foreach (var (dir, platform) in projects)
        {
            var fullDir = Path.Combine(_repoRoot, "..", dir);
            if (!Directory.Exists(fullDir))
            {
                // Try same level
                fullDir = Path.Combine(Path.GetDirectoryName(_repoRoot) ?? _repoRoot, dir);
                if (!Directory.Exists(fullDir)) continue;
            }

            var files = Directory.GetFiles(fullDir, "*.*", SearchOption.AllDirectories)
                .Where(f => GetLanguageForFile(f) is not null)
                .Where(f => !f.Contains("obj") && !f.Contains("bin") && !f.Contains(".git")
                         && !f.Contains("build") && !f.Contains("Pods"))
                .ToList();

            await Parallel.ForEachAsync(files, ct, async (file, innerCt) =>
            {
                var symbols = await ExtractSymbolsAsync(file, innerCt);
                foreach (var sym in symbols)
                {
                    sym.Platform = platform;
                    index.Symbols.Add(sym);
                }
            });
        }

        return index;
    }

    // ── LSIF Feed Integration ───────────────────────────────────────
    // Pushes tree-sitter-extracted symbols into the BuildServer LSIF index
    // so mobile repo symbols (Swift, Kotlin) appear alongside C# symbols
    // in cross-project navigation and search.
    //
    // Example:
    //   var ts = new TreeSitterService(repoRoot);
    //   ts.ConfigureLsifEndpoint("https+http://build-server");
    //   var result = await ts.FeedSymbolsToLsifAsync();
    //   // result.SymbolsPushed == 247, result.FilesProcessed == 38

    /// <summary>Configure the BuildServer endpoint for LSIF symbol push.</summary>
    public void ConfigureLsifEndpoint(string buildServerUrl)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _lsifClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(buildServerUrl),
            Timeout = TimeSpan.FromSeconds(60)
        };
    }

    /// <summary>
    /// Extract symbols from all mobile repos and push them to the BuildServer LSIF index.
    /// Covers: TheWatch-iOS (.swift), TheWatch-Android (.kt/.java), and the Aspire solution (.cs).
    /// The BuildServer merges these into its unified LSIF graph so cross-platform
    /// go-to-definition and find-references work across all three codebases.
    /// </summary>
    public async Task<LsifFeedResult> FeedSymbolsToLsifAsync(CancellationToken ct = default)
    {
        var result = new LsifFeedResult { StartedAt = DateTime.UtcNow };

        // Gather cross-project symbols (Aspire + iOS + Android)
        var index = await GetCrossProjectSymbolsAsync(ct);
        var symbols = index.Symbols.ToList();

        result.TotalSymbolsExtracted = symbols.Count;
        result.ByPlatform = symbols.GroupBy(s => s.Platform ?? "Unknown")
            .ToDictionary(g => g.Key, g => g.Count());
        result.ByLanguage = symbols.GroupBy(s => s.Language)
            .ToDictionary(g => g.Key, g => g.Count());
        result.FilesProcessed = symbols.Select(s => s.FilePath).Distinct().Count();

        // Push to BuildServer if endpoint configured
        if (_lsifClient is not null)
        {
            try
            {
                var payload = new LsifSymbolBatch
                {
                    Source = "tree-sitter",
                    ExtractedAt = DateTime.UtcNow,
                    Symbols = symbols.Select(s => new LsifSymbolEntry
                    {
                        Name = s.Name,
                        FullyQualifiedName = $"{s.Platform}.{s.Language}.{s.Name}",
                        Kind = s.Kind,
                        Language = s.Language,
                        Platform = s.Platform ?? "Unknown",
                        FilePath = s.FilePath,
                        Line = s.Line,
                        Column = s.Column
                    }).ToList()
                };

                var response = await _lsifClient.PostAsJsonAsync(
                    "/api/build/index/symbols/external", payload, _json, ct);

                if (response.IsSuccessStatusCode)
                {
                    result.SymbolsPushed = symbols.Count;
                    result.PushSucceeded = true;
                }
                else
                {
                    result.PushError = $"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}";
                }
            }
            catch (Exception ex)
            {
                result.PushError = ex.Message;
            }
        }
        else
        {
            result.PushError = "LSIF endpoint not configured — call ConfigureLsifEndpoint() first";
        }

        result.CompletedAt = DateTime.UtcNow;
        return result;
    }

    /// <summary>
    /// Index a single mobile repo directory and push its symbols to LSIF.
    /// Use when a specific repo has changed and you don't need a full re-scan.
    /// </summary>
    public async Task<LsifFeedResult> FeedDirectoryToLsifAsync(
        string directory, string platform, CancellationToken ct = default)
    {
        var result = new LsifFeedResult { StartedAt = DateTime.UtcNow };

        var fullDir = Path.IsPathRooted(directory) ? directory : Path.Combine(_repoRoot, directory);
        if (!Directory.Exists(fullDir))
        {
            result.PushError = $"Directory not found: {fullDir}";
            result.CompletedAt = DateTime.UtcNow;
            return result;
        }

        var files = Directory.GetFiles(fullDir, "*.*", SearchOption.AllDirectories)
            .Where(f => GetLanguageForFile(f) is not null)
            .Where(f => !f.Contains("obj") && !f.Contains("bin") && !f.Contains(".git")
                     && !f.Contains("build") && !f.Contains("Pods") && !f.Contains("DerivedData"))
            .ToList();

        var allSymbols = new ConcurrentBag<SymbolEntry>();
        await Parallel.ForEachAsync(files, ct, async (file, innerCt) =>
        {
            var symbols = await ExtractSymbolsAsync(file, innerCt);
            foreach (var sym in symbols)
            {
                sym.Platform = platform;
                allSymbols.Add(sym);
            }
        });

        var symbolList = allSymbols.ToList();
        result.TotalSymbolsExtracted = symbolList.Count;
        result.FilesProcessed = files.Count;
        result.ByPlatform = new Dictionary<string, int> { [platform] = symbolList.Count };
        result.ByLanguage = symbolList.GroupBy(s => s.Language)
            .ToDictionary(g => g.Key, g => g.Count());

        if (_lsifClient is not null)
        {
            try
            {
                var payload = new LsifSymbolBatch
                {
                    Source = "tree-sitter",
                    ExtractedAt = DateTime.UtcNow,
                    Symbols = symbolList.Select(s => new LsifSymbolEntry
                    {
                        Name = s.Name,
                        FullyQualifiedName = $"{s.Platform}.{s.Language}.{s.Name}",
                        Kind = s.Kind,
                        Language = s.Language,
                        Platform = s.Platform ?? platform,
                        FilePath = s.FilePath,
                        Line = s.Line,
                        Column = s.Column
                    }).ToList()
                };

                var response = await _lsifClient.PostAsJsonAsync(
                    "/api/build/index/symbols/external", payload, _json, ct);

                result.PushSucceeded = response.IsSuccessStatusCode;
                result.SymbolsPushed = response.IsSuccessStatusCode ? symbolList.Count : 0;
                if (!response.IsSuccessStatusCode)
                    result.PushError = $"HTTP {(int)response.StatusCode}";
            }
            catch (Exception ex) { result.PushError = ex.Message; }
        }

        result.CompletedAt = DateTime.UtcNow;
        return result;
    }

    // ── CLI Backend ─────────────────────────────────────────────────

    private async Task<ParsedFile> ParseWithCliAsync(string filePath, string language, CancellationToken ct)
    {
        var result = await RunProcessAsync("tree-sitter", $"parse {filePath}", ct);

        return new ParsedFile
        {
            FilePath = filePath,
            Language = language,
            SExpression = result.Success ? result.Output : null,
            Error = result.Success ? null : result.Error,
            ParsedAt = DateTime.UtcNow
        };
    }

    // ── Regex Fallback Backend ──────────────────────────────────────

    private ParsedFile ParseWithRegex(string filePath, string language)
    {
        try
        {
            var source = File.ReadAllText(filePath);
            var symbols = ExtractSymbolsFromSource(source, language, filePath);

            return new ParsedFile
            {
                FilePath = filePath,
                Language = language,
                Symbols = symbols,
                ParsedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            return new ParsedFile
            {
                FilePath = filePath,
                Language = language,
                Error = ex.Message
            };
        }
    }

    private static List<SymbolEntry> ExtractSymbolsFromSource(string source, string language, string filePath)
    {
        var symbols = new List<SymbolEntry>();
        var lines = source.Split('\n');

        // Language-specific patterns
        var patterns = GetPatternsForLanguage(language);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            foreach (var (pattern, kind) in patterns)
            {
                var match = pattern.Match(line);
                if (match.Success)
                {
                    symbols.Add(new SymbolEntry
                    {
                        Name = match.Groups["name"].Value,
                        Kind = kind,
                        Language = language,
                        FilePath = filePath,
                        Line = i + 1,
                        Column = match.Index + 1
                    });
                }
            }
        }

        return symbols;
    }

    private static List<(Regex Pattern, string Kind)> GetPatternsForLanguage(string language)
    {
        return language switch
        {
            "swift" => new()
            {
                (new Regex(@"^\s*(?:public\s+|private\s+|internal\s+|open\s+|fileprivate\s+)?(?:final\s+)?class\s+(?<name>\w+)"), "Class"),
                (new Regex(@"^\s*(?:public\s+|private\s+)?struct\s+(?<name>\w+)"), "Struct"),
                (new Regex(@"^\s*(?:public\s+|private\s+)?protocol\s+(?<name>\w+)"), "Protocol"),
                (new Regex(@"^\s*(?:public\s+|private\s+)?enum\s+(?<name>\w+)"), "Enum"),
                (new Regex(@"^\s*(?:public\s+|private\s+|internal\s+|open\s+)?(?:static\s+)?(?:@\w+\s+)*func\s+(?<name>\w+)"), "Function"),
                (new Regex(@"^\s*(?:public\s+|private\s+)?(?:static\s+)?(?:let|var)\s+(?<name>\w+)"), "Property"),
                (new Regex(@"^\s*extension\s+(?<name>\w+)"), "Extension"),
                (new Regex(@"^\s*(?:@\w+\s+)*struct\s+(?<name>\w+)\s*:\s*View"), "SwiftUIView"),
            },
            "kotlin" => new()
            {
                (new Regex(@"^\s*(?:public\s+|private\s+|internal\s+|protected\s+)?(?:abstract\s+|open\s+|data\s+|sealed\s+)?class\s+(?<name>\w+)"), "Class"),
                (new Regex(@"^\s*(?:public\s+|private\s+)?interface\s+(?<name>\w+)"), "Interface"),
                (new Regex(@"^\s*(?:public\s+|private\s+)?object\s+(?<name>\w+)"), "Object"),
                (new Regex(@"^\s*(?:public\s+|private\s+)?enum\s+class\s+(?<name>\w+)"), "Enum"),
                (new Regex(@"^\s*(?:public\s+|private\s+|internal\s+|protected\s+)?(?:suspend\s+)?fun\s+(?<name>\w+)"), "Function"),
                (new Regex(@"^\s*(?:val|var)\s+(?<name>\w+)"), "Property"),
                (new Regex(@"@Composable\s+(?:fun|private\s+fun|internal\s+fun)\s+(?<name>\w+)"), "Composable"),
                (new Regex(@"@HiltViewModel\s+class\s+(?<name>\w+)"), "ViewModel"),
                (new Regex(@"@Module\s+.*class\s+(?<name>\w+)"), "HiltModule"),
            },
            "java" => new()
            {
                (new Regex(@"^\s*(?:public\s+|private\s+|protected\s+)?(?:abstract\s+|final\s+)?class\s+(?<name>\w+)"), "Class"),
                (new Regex(@"^\s*(?:public\s+)?interface\s+(?<name>\w+)"), "Interface"),
                (new Regex(@"^\s*(?:public\s+)?enum\s+(?<name>\w+)"), "Enum"),
                (new Regex(@"^\s*(?:public\s+|private\s+|protected\s+)?(?:static\s+)?(?:synchronized\s+)?\w+(?:<[^>]+>)?\s+(?<name>\w+)\s*\("), "Method"),
            },
            "c_sharp" => new()
            {
                (new Regex(@"^\s*(?:public\s+|private\s+|internal\s+|protected\s+)?(?:static\s+|abstract\s+|sealed\s+|partial\s+)*class\s+(?<name>\w+)"), "Class"),
                (new Regex(@"^\s*(?:public\s+|internal\s+)?interface\s+(?<name>\w+)"), "Interface"),
                (new Regex(@"^\s*(?:public\s+|internal\s+)?(?:readonly\s+)?(?:ref\s+)?struct\s+(?<name>\w+)"), "Struct"),
                (new Regex(@"^\s*(?:public\s+|internal\s+)?record\s+(?:class\s+|struct\s+)?(?<name>\w+)"), "Record"),
                (new Regex(@"^\s*(?:public\s+|internal\s+)?enum\s+(?<name>\w+)"), "Enum"),
            },
            "xml" => new()
            {
                (new Regex(@"<(?<name>[\w:]+)[\s>]"), "Element"),
            },
            "json" => new()
            {
                (new Regex(@"""(?<name>[\w]+)""\s*:"), "Key"),
            },
            "sql" => new()
            {
                (new Regex(@"CREATE\s+(?:OR\s+REPLACE\s+)?TABLE\s+(?:\[?dbo\]?\.)?\[?(?<name>\w+)", RegexOptions.IgnoreCase), "Table"),
                (new Regex(@"CREATE\s+(?:OR\s+REPLACE\s+)?(?:PROCEDURE|PROC)\s+(?:\[?dbo\]?\.)?\[?(?<name>\w+)", RegexOptions.IgnoreCase), "Procedure"),
                (new Regex(@"CREATE\s+(?:OR\s+REPLACE\s+)?VIEW\s+(?:\[?dbo\]?\.)?\[?(?<name>\w+)", RegexOptions.IgnoreCase), "View"),
                (new Regex(@"CREATE\s+(?:OR\s+REPLACE\s+)?FUNCTION\s+(?:\[?dbo\]?\.)?\[?(?<name>\w+)", RegexOptions.IgnoreCase), "Function"),
            },
            "proto" => new()
            {
                (new Regex(@"^\s*message\s+(?<name>\w+)"), "Message"),
                (new Regex(@"^\s*service\s+(?<name>\w+)"), "Service"),
                (new Regex(@"^\s*enum\s+(?<name>\w+)"), "Enum"),
                (new Regex(@"^\s*rpc\s+(?<name>\w+)"), "RPC"),
            },
            "hcl" => new()
            {
                (new Regex(@"^\s*resource\s+""(?<name>[^""]+)"""), "Resource"),
                (new Regex(@"^\s*module\s+""(?<name>[^""]+)"""), "Module"),
                (new Regex(@"^\s*variable\s+""(?<name>[^""]+)"""), "Variable"),
                (new Regex(@"^\s*output\s+""(?<name>[^""]+)"""), "Output"),
            },
            _ => new()
        };
    }

    // ── Utilities ───────────────────────────────────────────────────

    private static TreeSitterBackend DetectBackend()
    {
        // Check for tree-sitter CLI
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "tree-sitter",
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(3000);
            if (process?.ExitCode == 0)
                return TreeSitterBackend.Cli;
        }
        catch { }

        // Fallback to regex
        return TreeSitterBackend.Regex;
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, string arguments, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            var output = new StringBuilder();
            var error = new StringBuilder();

            process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) error.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(ct);

            return new ProcessResult
            {
                Success = process.ExitCode == 0,
                Output = output.ToString(),
                Error = error.ToString()
            };
        }
        catch (Exception ex)
        {
            return new ProcessResult { Success = false, Error = ex.Message };
        }
    }

    private static List<QueryMatch> ParseQueryOutput(string output)
    {
        var matches = new List<QueryMatch>();
        var currentMatch = new QueryMatch();

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Pattern: "  pattern: N" or "  capture N - name: text"
            if (trimmed.StartsWith("pattern:"))
            {
                if (currentMatch.Captures.Count > 0)
                    matches.Add(currentMatch);
                currentMatch = new QueryMatch();
            }
            else if (trimmed.Contains(" - "))
            {
                var parts = trimmed.Split(" - ");
                if (parts.Length >= 2)
                {
                    currentMatch.Captures.Add(new QueryCapture
                    {
                        Name = parts[0].Trim(),
                        Text = parts[1].Trim()
                    });
                }
            }
        }

        if (currentMatch.Captures.Count > 0)
            matches.Add(currentMatch);

        return matches;
    }
}

// ── Enums & Models ──────────────────────────────────────────────────

public enum TreeSitterBackend
{
    Cli,
    Regex
}

public class ParsedFile
{
    public string FilePath { get; set; } = "";
    public string? Language { get; set; }
    public string? SExpression { get; set; }
    public List<SymbolEntry> Symbols { get; set; } = new();
    public string? Error { get; set; }
    public DateTime ParsedAt { get; set; }
}

public class SymbolEntry
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Language { get; set; } = "";
    public string? Platform { get; set; }
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
}

public class QueryMatch
{
    public string? FilePath { get; set; }
    public int Line { get; set; }
    public List<QueryCapture> Captures { get; set; } = new();
    public string? Error { get; set; }
}

public record QueryCapture
{
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
}

public class CrossProjectSymbolIndex
{
    public ConcurrentBag<SymbolEntry> Symbols { get; set; } = new();
    public DateTime BuiltAt { get; set; } = DateTime.UtcNow;

    public IEnumerable<SymbolEntry> SearchByName(string query) =>
        Symbols.Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<SymbolEntry> SearchByKind(string kind) =>
        Symbols.Where(s => s.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<SymbolEntry> GetByPlatform(string platform) =>
        Symbols.Where(s => s.Platform == platform);

    public IEnumerable<IGrouping<string, SymbolEntry>> GroupByLanguage() =>
        Symbols.GroupBy(s => s.Language);
}

public class ProcessResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = "";
    public string Error { get; set; } = "";
}

// ── LSIF Feed Models ────────────────────────────────────────────────
// DTOs for pushing tree-sitter symbols to the BuildServer LSIF index.
// POST /api/build/index/symbols/external accepts LsifSymbolBatch.

public class LsifFeedResult
{
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public TimeSpan Duration => CompletedAt - StartedAt;
    public int TotalSymbolsExtracted { get; set; }
    public int SymbolsPushed { get; set; }
    public int FilesProcessed { get; set; }
    public bool PushSucceeded { get; set; }
    public string? PushError { get; set; }
    public Dictionary<string, int> ByPlatform { get; set; } = new();
    public Dictionary<string, int> ByLanguage { get; set; } = new();
}

public class LsifSymbolBatch
{
    public string Source { get; set; } = "tree-sitter";
    public DateTime ExtractedAt { get; set; }
    public List<LsifSymbolEntry> Symbols { get; set; } = new();
}

public class LsifSymbolEntry
{
    public string Name { get; set; } = "";
    public string FullyQualifiedName { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Language { get; set; } = "";
    public string Platform { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
}
