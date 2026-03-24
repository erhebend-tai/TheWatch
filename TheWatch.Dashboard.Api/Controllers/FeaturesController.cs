// FeaturesController — REST endpoints for the MudBlazor Feature Tracker DataGrid.
// Data originates from Firestore (via Google Cloud Functions) and Aspire logs.
// The MudBlazor DataGrid on the web frontend queries these endpoints.
//
// Endpoints:
//   GET    /api/features              — All features (DataGrid data source)
//   GET    /api/features/{id}         — Single feature
//   GET    /api/features/stats        — Category + status counts for dashboard cards
//   POST   /api/features              — Create/upsert a feature
//   PUT    /api/features/{id}/status  — Update status + progress
//   DELETE /api/features/{id}         — Delete a feature

using Microsoft.AspNetCore.Mvc;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FeaturesController : ControllerBase
{
    private readonly IFeatureTrackingPort _featurePort;
    private readonly ILogger<FeaturesController> _logger;

    public FeaturesController(IFeatureTrackingPort featurePort, ILogger<FeaturesController> logger)
    {
        _featurePort = featurePort;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] FeatureCategory? category, [FromQuery] FeatureStatus? status, CancellationToken ct)
    {
        if (category is not null)
            return Ok((await _featurePort.GetByCategoryAsync(category.Value, ct)).Data ?? new());
        if (status is not null)
            return Ok((await _featurePort.GetByStatusAsync(status.Value, ct)).Data ?? new());
        return Ok((await _featurePort.GetAllAsync(ct)).Data ?? new());
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var result = await _featurePort.GetByIdAsync(id, ct);
        return result.Success ? Ok(result.Data) : NotFound(new { error = result.ErrorMessage });
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var categories = await _featurePort.GetCategoryCountsAsync(ct);
        var statuses = await _featurePort.GetStatusCountsAsync(ct);
        var all = await _featurePort.GetAllAsync(ct);
        var features = all.Data ?? new();

        return Ok(new
        {
            Total = features.Count,
            Completed = features.Count(f => f.Status == FeatureStatus.Completed),
            InProgress = features.Count(f => f.Status == FeatureStatus.InProgress),
            Planned = features.Count(f => f.Status == FeatureStatus.Planned),
            Blocked = features.Count(f => f.Status == FeatureStatus.Blocked),
            OverallProgress = features.Count > 0 ? (int)features.Average(f => f.ProgressPercent) : 0,
            CategoryCounts = categories.Data,
            StatusCounts = statuses.Data
        });
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] FeatureImplementation feature, CancellationToken ct)
    {
        var result = await _featurePort.UpsertAsync(feature, ct);
        _logger.LogInformation("Feature upserted: {FeatureId} = {Name}", feature.Id, feature.Name);
        return result.Success ? Ok(result.Data) : StatusCode(500, new { error = result.ErrorMessage });
    }

    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(string id, [FromBody] FeatureStatusUpdate update, CancellationToken ct)
    {
        var result = await _featurePort.UpdateStatusAsync(id, update.Status, update.ProgressPercent, ct);
        return result.Success ? Ok(new { id, Status = update.Status.ToString(), update.ProgressPercent }) : NotFound(new { error = result.ErrorMessage });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var result = await _featurePort.DeleteAsync(id, ct);
        return result.Success ? Ok(new { id, deleted = true }) : NotFound(new { error = result.ErrorMessage });
    }
}

public record FeatureStatusUpdate(FeatureStatus Status, int ProgressPercent);
