namespace TheWatch.Shared.Configuration;

/// <summary>
/// Configuration object read from appsettings.json "AdapterRegistry" section.
/// Controls which adapter implementations are loaded at startup.
///
/// Example appsettings.json:
/// {
///   "AdapterRegistry": {
///     "GitHub": "Mock",
///     "Azure": "Mock",
///     "AWS": "Mock",
///     "Google": "Mock",
///     "Oracle": "Disabled",
///     "Cloudflare": "Disabled",
///     "PrimaryStorage": "Mock",
///     "AuditTrail": "Mock",
///     "SpatialIndex": "Mock"
///   }
/// }
///
/// Valid values: "Mock", "Native", "Live", "Disabled"
///   Mock     = Use in-memory mock adapter (always available, no cloud needed)
///   Native   = Use platform-local hardware capabilities (SQLite, OS APIs)
///   Live     = Use real cloud adapter (requires credentials + SDK)
///   Disabled = Don't register this provider at all
/// </summary>
public class AdapterRegistry
{
    public const string SectionName = "AdapterRegistry";

    // Cloud provider adapters
    public string GitHub { get; set; } = "Mock";
    public string Azure { get; set; } = "Mock";
    public string AWS { get; set; } = "Mock";
    public string Google { get; set; } = "Mock";
    public string Oracle { get; set; } = "Disabled";
    public string Cloudflare { get; set; } = "Disabled";

    // Backward-compatible aliases (Data layer used these names)
    public string GitHubAdapter { get => GitHub; set => GitHub = value; }
    public string AzureAdapter { get => Azure; set => Azure = value; }
    public string FirestoreAdapter { get => Google; set => Google = value; }
    public string AwsAdapter { get => AWS; set => AWS = value; }

    // Data layer adapters
    public string PrimaryStorage { get; set; } = "Mock";
    public string AuditTrail { get; set; } = "Mock";
    public string SpatialIndex { get; set; } = "Mock";

    // Evidence & survey adapters
    public string BlobStorage { get; set; } = "Mock";
    public string Evidence { get; set; } = "Mock";
    public string Survey { get; set; } = "Mock";

    // Feature tracking & DevWork adapters
    public string FeatureTracking { get; set; } = "Mock";
    public string DevWork { get; set; } = "Mock";

    // AI / Embedding / Vector Search adapters
    // Each embedding provider is paired with its native vector store:
    //   AzureOpenAI → CosmosDB | Gemini → Firestore | VoyageAI → Qdrant
    public string Embedding { get; set; } = "Mock";
    public string VectorSearch { get; set; } = "Mock";

    // Build output persistence — selectable store for DevOps build data.
    // "Sqlite" (default), "SqlServer", "PostgreSql", "CosmosDB", "Firestore", "Mock"
    public string BuildOutput { get; set; } = "Sqlite";

    // IoT / Smart Home adapters — Alexa Skills, Google Home, device webhooks.
    // Both Alexa and Google Home call into the same /api/iot/* endpoints.
    public string IoTAlert { get; set; } = "Mock";
    public string IoTWebhook { get; set; } = "Mock";

    // Watch Call adapters — live video watch calls with enrollment and mock training.
    // "Mock" (in-memory), "Firestore" (production, enrollment + call state in Firestore)
    public string WatchCall { get; set; } = "Mock";

    // Scene Narration adapters — AI vision for neutral scene description during calls.
    // "Mock" (pre-scripted), "AzureOpenAI" (GPT-4o vision), "Gemini" (Gemini Pro Vision)
    public string SceneNarration { get; set; } = "Mock";

    // Swarm Agent adapters — interactive conversational agent for CLI swarm guidance.
    // "Mock" (canned responses), "AzureOpenAI" (GPT-4o chat completions)
    public string SwarmAgent { get; set; } = "Mock";

    public bool IsEnabled(string provider) =>
        provider.ToLowerInvariant() switch
        {
            "github" => GitHub != "Disabled",
            "azure" => Azure != "Disabled",
            "aws" => AWS != "Disabled",
            "google" => Google != "Disabled",
            "oracle" => Oracle != "Disabled",
            "cloudflare" => Cloudflare != "Disabled",
            "iotalert" => IoTAlert != "Disabled",
            "iotwebhook" => IoTWebhook != "Disabled",
            _ => false
        };

    public bool IsLive(string provider) =>
        provider.ToLowerInvariant() switch
        {
            "github" => GitHub == "Live",
            "azure" => Azure == "Live",
            "aws" => AWS == "Live",
            "google" => Google == "Live",
            "oracle" => Oracle == "Live",
            "cloudflare" => Cloudflare == "Live",
            "iotalert" => IoTAlert == "Live",
            "iotwebhook" => IoTWebhook == "Live",
            _ => false
        };

    public bool IsNative(string provider) =>
        provider.ToLowerInvariant() switch
        {
            "github" => GitHub == "Native",
            "azure" => Azure == "Native",
            "aws" => AWS == "Native",
            "google" => Google == "Native",
            "oracle" => Oracle == "Native",
            "cloudflare" => Cloudflare == "Native",
            "iotalert" => IoTAlert == "Native",
            "iotwebhook" => IoTWebhook == "Native",
            _ => false
        };
}
