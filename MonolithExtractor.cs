using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheWatch.Extractor
{
    public record GenerativeItem(
        string Id,
        string SourceRepo,
        string SourceProject,
        string SourceFile,
        string Kind,
        string Name,
        string Signature,
        string[] Tags,
        string TargetProject,
        string TargetPath,
        string GenerationTemplate,
        string[] Dependencies
    );

    public class MonolithExtractor
    {
        private readonly string _csvPath;
        private readonly string _outputPath;

        private static readonly Dictionary<string, string> ProjectMappings = new()
        {
            { "APIS", "Watch-Web" },
            { "TheWatch", "Watch-Web" },
            { "TheWatch-Android", "Watch-Android" },
            { "TheWatch-iOS", "Watch-iOS" },
            { "TheWatch.Data", "Watch-Data" },
            { "TheWatch.Dashboard.Api", "Watch-Dashboard-Api" },
            { "TheWatch.Shared", "Watch-Shared" }
        };

        private static readonly Dictionary<string, string> TemplateMap = new()
        {
            { ".razor", "BlazorPageTemplate" },
            { ".razor.cs", "BlazorCodeBehindTemplate" },
            { ".cs", "CSharpClassTemplate" },
            { ".kt", "KotlinClassTemplate" },
            { ".swift", "SwiftClassTemplate" },
            { ".sql", "SqlScriptTemplate" },
            { ".md", "MarkdownDocTemplate" }
        };

        public MonolithExtractor(string csvPath, string outputPath)
        {
            _csvPath = csvPath;
            _outputPath = outputPath;
        }

        public void Execute()
        {
            Console.WriteLine($"🚀 Starting Extraction from {_csvPath}...");
            
            var items = new List<GenerativeItem>();
            if (!File.Exists(_csvPath)) {
                Console.WriteLine($"❌ File not found: {_csvPath}");
                return;
            }

            var lines = File.ReadAllLines(_csvPath);
            var startIndex = lines.Length > 0 && !lines[0].Contains(".cs") && !lines[0].Contains(".razor") ? 1 : 0;

            foreach (var line in lines.Skip(startIndex))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var item = ParseLine(line);
                    if (item != null) items.Add(item);
                }
                catch (Exception ex) { Console.WriteLine($"⚠️ Error: {ex.Message}"); }
            }

            WriteJsonL(items);
            Console.WriteLine($"✅ Generated {items.Count} items to {_outputPath}");
        }

        private GenerativeItem? ParseLine(string line)
        {
            var parts = line.Split(',');
            if (parts.Length < 6) return null;

            var repo = parts[0].Trim();
            var project = parts[1].Trim();
            var filePath = parts[2].Trim();
            var kind = parts[3].Trim();
            var name = parts[4].Trim();
            var signature = parts[5].Trim();
            var tagsRaw = parts.Length > 6 ? parts[6].Trim() : "";
            var dependsOnRaw = parts.Length > 7 ? parts[7].Trim() : "";

            var targetProject = ProjectMappings.GetValueOrDefault(project, project);
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var template = TemplateMap.GetValueOrDefault(ext, "GenericTemplate");
            
            var refinedKind = kind;
            if (kind == "class" && filePath.Contains("Controller")) refinedKind = "Controller";
            if (kind == "class" && filePath.Contains("Service")) refinedKind = "Service";
            if (kind == "class" && filePath.Contains("Model")) refinedKind = "Model";
            if (kind == "source" && filePath.EndsWith(".razor")) refinedKind = "RazorComponent";

            var id = $"gen_{repo}_{project}_{Path.GetFileNameWithoutExtension(filePath)}".Replace("-", "_").Replace(".", "_").ToLowerInvariant();

            return new GenerativeItem(
                Id: id,
                SourceRepo: repo,
                SourceProject: project,
                SourceFile: filePath,
                Kind: refinedKind,
                Name: name,
                Signature: signature,
                Tags: ParseTags(tagsRaw),
                TargetProject: targetProject,
                TargetPath: Path.Combine(targetProject, GetRelativePath(filePath)),
                GenerationTemplate: template,
                Dependencies: ParseDependencies(dependsOnRaw)
            );
        }

        private string[] ParseTags(string raw) => raw.Split(new[] { '#', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.TrimStart('#')).Distinct().ToArray();
        private string[] ParseDependencies(string raw) => raw.Split(';').Select(d => d.Trim()).Where(d => !string.IsNullOrEmpty(d)).ToArray();
        private string GetRelativePath(string fullPath) {
            var clean = fullPath.Replace("\\", "/");
            if (clean.Contains(":")) clean = clean.Substring(clean.IndexOf(':') + 1);
            return clean.TrimStart('/', '\\');
        }

        private void WriteJsonL(List<GenerativeItem> items)
        {
            var options = new JsonSerializerOptions { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            using var writer = new StreamWriter(_outputPath);
            foreach (var item in items) writer.WriteLine(JsonSerializer.Serialize(item, options));
        }
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            var input = args.Length > 0 ? args[0] : "code-index-all-repos.csv";
            var output = args.Length > 1 ? args[1] : "generative-items.jsonl";
            new MonolithExtractor(input, output).Execute();
        }
    }
}
