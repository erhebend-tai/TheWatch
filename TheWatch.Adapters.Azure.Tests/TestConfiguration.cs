// TestConfiguration — loads Azure OpenAI credentials from environment or .env file.
//
// Resolution order:
//   1. Environment variables (CI/CD, dotnet user-secrets)
//   2. .env file in solution root
//
// Example:
//   var config = TestConfiguration.Load();
//   var client = new AzureOpenAIClient(new Uri(config.Endpoint), new AzureKeyCredential(config.ApiKey));

using System.Reflection;

namespace TheWatch.Adapters.Azure.Tests;

public class TestConfiguration
{
    public string Endpoint { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public string DeploymentGpt41 { get; init; } = "gpt-4.1";
    public string DeploymentGpt4o { get; init; } = "gpt-4o";
    public string DeploymentGpt4oMini { get; init; } = "gpt-4o-mini";
    public string DeploymentEmbedding { get; init; } = "text-embedding-3-large";

    public static TestConfiguration Load()
    {
        // Try .env file first (solution root)
        var solutionRoot = FindSolutionRoot();
        if (solutionRoot is not null)
        {
            var envPath = Path.Combine(solutionRoot, ".env");
            if (File.Exists(envPath))
                LoadEnvFile(envPath);
        }

        var endpoint = Env("AZURE_OPENAI_ENDPOINT");
        var apiKey = Env("AZURE_OPENAI_API_KEY");

        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException(
                "AZURE_OPENAI_ENDPOINT and AZURE_OPENAI_API_KEY must be set. " +
                "Run 'infra\\deploy.cmd' or set environment variables.");

        return new TestConfiguration
        {
            Endpoint = endpoint,
            ApiKey = apiKey,
            DeploymentGpt41 = Env("AZURE_OPENAI_DEPLOYMENT_GPT41") ?? "gpt-4.1",
            DeploymentGpt4o = Env("AZURE_OPENAI_DEPLOYMENT_GPT4O") ?? "gpt-4o",
            DeploymentGpt4oMini = Env("AZURE_OPENAI_DEPLOYMENT_GPT4O_MINI") ?? "gpt-4o-mini",
            DeploymentEmbedding = Env("AZURE_OPENAI_DEPLOYMENT_EMBEDDING") ?? "text-embedding-3-large",
        };
    }

    private static string? Env(string name) =>
        Environment.GetEnvironmentVariable(name);

    private static string? FindSolutionRoot()
    {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        while (dir is not null)
        {
            if (Directory.GetFiles(dir, "*.slnx").Length > 0 ||
                Directory.GetFiles(dir, "*.sln").Length > 0)
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static void LoadEnvFile(string path)
    {
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            var eq = trimmed.IndexOf('=');
            if (eq <= 0) continue;

            var key = trimmed[..eq].Trim();
            var value = trimmed[(eq + 1)..].Trim();

            // Only set if not already in environment (env vars take precedence)
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                Environment.SetEnvironmentVariable(key, value);
        }
    }
}
