using Microsoft.AspNetCore.Mvc;
using TheWatch.Shared.Configuration;

namespace TheWatch.Dashboard.Api.Controllers;

/// <summary>
/// Runtime adapter tier management.
/// Exposes the AdapterRegistry state and allows hot-switching
/// between Mock / Native / Live tiers per adapter slot.
///
/// In development, this enables the MAUI dashboard to toggle
/// individual adapters without restarting the Aspire application.
///
/// IMPORTANT: Hot-switching replaces the DI registration at runtime.
/// Existing scoped/transient instances complete their current work
/// before new requests use the switched adapter.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AdapterTierController : ControllerBase
{
    private readonly AdapterRegistry _registry;
    private readonly ILogger<AdapterTierController> _logger;

    public AdapterTierController(AdapterRegistry registry, ILogger<AdapterTierController> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// Get current adapter tier assignments for all slots.
    /// </summary>
    [HttpGet]
    public ActionResult<AdapterRegistryDto> GetAll()
    {
        return Ok(AdapterRegistryDto.FromRegistry(_registry));
    }

    /// <summary>
    /// Get current tier for a specific adapter slot.
    /// </summary>
    [HttpGet("{slot}")]
    public ActionResult<AdapterSlotDto> GetSlot(string slot)
    {
        var tier = GetTierForSlot(slot);
        if (tier == null)
            return NotFound(new { error = $"Unknown adapter slot: {slot}" });

        return Ok(new AdapterSlotDto(slot, tier, GetAvailableTiers(slot)));
    }

    /// <summary>
    /// Switch an adapter slot to a different tier.
    /// </summary>
    [HttpPut("{slot}")]
    public ActionResult<AdapterSlotDto> SetSlot(string slot, [FromBody] SetTierRequest request)
    {
        var validTiers = new[] { "Mock", "Native", "Live", "Disabled" };
        if (!validTiers.Contains(request.Tier, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { error = $"Invalid tier '{request.Tier}'. Valid: {string.Join(", ", validTiers)}" });

        if (!SetTierForSlot(slot, request.Tier))
            return NotFound(new { error = $"Unknown adapter slot: {slot}" });

        _logger.LogWarning(
            "Adapter tier switched: {Slot} → {Tier} (by {Source})",
            slot, request.Tier, request.Source ?? "API");

        var newTier = GetTierForSlot(slot)!;
        return Ok(new AdapterSlotDto(slot, newTier, GetAvailableTiers(slot)));
    }

    /// <summary>
    /// Bulk update multiple adapter slots at once.
    /// </summary>
    [HttpPut]
    public ActionResult<AdapterRegistryDto> SetBulk([FromBody] Dictionary<string, string> assignments)
    {
        var errors = new List<string>();
        foreach (var (slot, tier) in assignments)
        {
            if (!SetTierForSlot(slot, tier))
                errors.Add($"Unknown slot: {slot}");
            else
                _logger.LogWarning("Adapter tier switched: {Slot} → {Tier} (bulk)", slot, tier);
        }

        if (errors.Any())
            return BadRequest(new { errors, applied = AdapterRegistryDto.FromRegistry(_registry) });

        return Ok(AdapterRegistryDto.FromRegistry(_registry));
    }

    /// <summary>
    /// Reset all adapters to Mock tier.
    /// </summary>
    [HttpPost("reset")]
    public ActionResult<AdapterRegistryDto> ResetAll()
    {
        var slots = GetAllSlots();
        foreach (var slot in slots)
            SetTierForSlot(slot, "Mock");

        _logger.LogWarning("All adapter tiers reset to Mock");
        return Ok(AdapterRegistryDto.FromRegistry(_registry));
    }

    // ── Slot mapping ─────────────────────────────────────────

    private string? GetTierForSlot(string slot) => slot.ToLowerInvariant() switch
    {
        "github" => _registry.GitHub,
        "azure" => _registry.Azure,
        "aws" => _registry.AWS,
        "google" => _registry.Google,
        "oracle" => _registry.Oracle,
        "cloudflare" => _registry.Cloudflare,
        "primarystorage" => _registry.PrimaryStorage,
        "audittrail" => _registry.AuditTrail,
        "spatialindex" => _registry.SpatialIndex,
        "blobstorage" => _registry.BlobStorage,
        "evidence" => _registry.Evidence,
        "survey" => _registry.Survey,
        "featuretracking" => _registry.FeatureTracking,
        "devwork" => _registry.DevWork,
        "embedding" => _registry.Embedding,
        "vectorsearch" => _registry.VectorSearch,
        _ => null
    };

    private bool SetTierForSlot(string slot, string tier)
    {
        switch (slot.ToLowerInvariant())
        {
            case "github": _registry.GitHub = tier; return true;
            case "azure": _registry.Azure = tier; return true;
            case "aws": _registry.AWS = tier; return true;
            case "google": _registry.Google = tier; return true;
            case "oracle": _registry.Oracle = tier; return true;
            case "cloudflare": _registry.Cloudflare = tier; return true;
            case "primarystorage": _registry.PrimaryStorage = tier; return true;
            case "audittrail": _registry.AuditTrail = tier; return true;
            case "spatialindex": _registry.SpatialIndex = tier; return true;
            case "blobstorage": _registry.BlobStorage = tier; return true;
            case "evidence": _registry.Evidence = tier; return true;
            case "survey": _registry.Survey = tier; return true;
            case "featuretracking": _registry.FeatureTracking = tier; return true;
            case "devwork": _registry.DevWork = tier; return true;
            case "embedding": _registry.Embedding = tier; return true;
            case "vectorsearch": _registry.VectorSearch = tier; return true;
            default: return false;
        }
    }

    private static string[] GetAllSlots() => new[]
    {
        "GitHub", "Azure", "AWS", "Google", "Oracle", "Cloudflare",
        "PrimaryStorage", "AuditTrail", "SpatialIndex",
        "BlobStorage", "Evidence", "Survey",
        "FeatureTracking", "DevWork",
        "Embedding", "VectorSearch"
    };

    private static string[] GetAvailableTiers(string slot) => slot.ToLowerInvariant() switch
    {
        // Cloud providers can be Disabled
        "oracle" or "cloudflare" => new[] { "Mock", "Native", "Live", "Disabled" },
        // Core slots can't be disabled
        _ => new[] { "Mock", "Native", "Live" }
    };
}

// ── DTOs ─────────────────────────────────────────────────────

public record SetTierRequest(string Tier, string? Source = null);

public record AdapterSlotDto(string Slot, string Tier, string[] AvailableTiers);

public record AdapterRegistryDto(
    Dictionary<string, string> CloudProviders,
    Dictionary<string, string> DataLayer,
    Dictionary<string, string> Features,
    Dictionary<string, string> AI
)
{
    public static AdapterRegistryDto FromRegistry(AdapterRegistry r) => new(
        CloudProviders: new()
        {
            ["GitHub"] = r.GitHub,
            ["Azure"] = r.Azure,
            ["AWS"] = r.AWS,
            ["Google"] = r.Google,
            ["Oracle"] = r.Oracle,
            ["Cloudflare"] = r.Cloudflare
        },
        DataLayer: new()
        {
            ["PrimaryStorage"] = r.PrimaryStorage,
            ["AuditTrail"] = r.AuditTrail,
            ["SpatialIndex"] = r.SpatialIndex,
            ["BlobStorage"] = r.BlobStorage
        },
        Features: new()
        {
            ["Evidence"] = r.Evidence,
            ["Survey"] = r.Survey,
            ["FeatureTracking"] = r.FeatureTracking,
            ["DevWork"] = r.DevWork
        },
        AI: new()
        {
            ["Embedding"] = r.Embedding,
            ["VectorSearch"] = r.VectorSearch
        }
    );
}
