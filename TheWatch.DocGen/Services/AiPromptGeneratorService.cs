// =============================================================================
// AiPromptGeneratorService.cs — Generates Azure AI JSONL prompt pairs from code.
// =============================================================================
// Consumes file-change events and produces structured JSONL data for AI training.
// Format: {"prompt": "...", "completion": "..."}
// =============================================================================

using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheWatch.DocGen.Configuration;

namespace TheWatch.DocGen.Services;

public class AiPromptGeneratorService
{
    private readonly ILogger<AiPromptGeneratorService> _logger;
    private readonly DocGenOptions _options;

    public AiPromptGeneratorService(ILogger<AiPromptGeneratorService> logger, IOptions<DocGenOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task GeneratePromptsAsync(string filePath, CancellationToken ct)
    {
        _logger.LogInformation("[WAL-AI] Generating AI prompts for {Path}", filePath);

        if (!File.Exists(filePath)) return;

        var sourceCode = await File.ReadAllTextAsync(filePath, ct);
        var tree = CSharpSyntaxTree.ParseText(sourceCode, cancellationToken: ct);
        var root = await tree.GetRootAsync(ct);

        var promptItems = new List<AiPromptItem>();

        // Find all classes and generate prompts
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
        foreach (var @class in classes)
        {
            promptItems.Add(new AiPromptItem(
                $"Explain the purpose of the class {@class.Identifier.Text} in the TheWatch project.",
                $"The class {@class.Identifier.Text} is defined in {Path.GetFileName(filePath)}. " +
                $"It is a {@class.Modifiers} class that handles {@class.Identifier.Text} logic within the system."
            ));

            // Find methods within the class
            var methods = @class.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var method in methods)
            {
                var methodName = method.Identifier.Text;
                var parameters = string.Join(", ", method.ParameterList.Parameters.Select(p => $"{p.Type} {p.Identifier}"));
                
                promptItems.Add(new AiPromptItem(
                    $"What does the method {methodName} do in the class {@class.Identifier.Text}?",
                    $"The method {methodName}({parameters}) is a {method.Modifiers} method in {@class.Identifier.Text}. " +
                    $"It returns {method.ReturnType} and is responsible for performing operations related to {methodName}."
                ));
            }
        }

        if (promptItems.Count > 0)
        {
            await SaveJsonLAsync(filePath, promptItems, ct);
        }
    }

    private async Task SaveJsonLAsync(string sourcePath, List<AiPromptItem> items, CancellationToken ct)
    {
        var outputDir = Path.Combine(_options.SolutionRoot, "Artifacts", "AiPrompts");
        Directory.CreateDirectory(outputDir);

        var fileName = Path.GetFileNameWithoutExtension(sourcePath) + ".jsonl";
        var outputPath = Path.Combine(outputDir, fileName);

        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
        using var writer = new StreamWriter(stream, Encoding.UTF8);

        foreach (var item in items)
        {
            var json = JsonSerializer.Serialize(item);
            await writer.WriteLineAsync(json);
        }

        _logger.LogInformation("[WAL-AI] Saved {Count} prompts to {Path}", items.Count, outputPath);
    }

    private record AiPromptItem(string prompt, string completion);
}
