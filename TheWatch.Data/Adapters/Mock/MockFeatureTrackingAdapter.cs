// MockFeatureTrackingAdapter — ConcurrentDictionary-backed IFeatureTrackingPort.
// Seeds realistic feature data on construction for the MudBlazor DataGrid demo.
//
// Example:
//   services.AddSingleton<IFeatureTrackingPort, MockFeatureTrackingAdapter>();

using System.Collections.Concurrent;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Data.Adapters.Mock;

public class MockFeatureTrackingAdapter : IFeatureTrackingPort
{
    private readonly ConcurrentDictionary<string, FeatureImplementation> _features = new();

    public MockFeatureTrackingAdapter() => SeedFeatures();

    public Task<StorageResult<List<FeatureImplementation>>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult(StorageResult<List<FeatureImplementation>>.Ok(
            _features.Values.OrderBy(f => f.Priority).ThenBy(f => f.Category).ToList()));

    public Task<StorageResult<FeatureImplementation>> GetByIdAsync(string featureId, CancellationToken ct = default)
    {
        if (_features.TryGetValue(featureId, out var f))
            return Task.FromResult(StorageResult<FeatureImplementation>.Ok(f));
        return Task.FromResult(StorageResult<FeatureImplementation>.Fail($"Feature '{featureId}' not found"));
    }

    public Task<StorageResult<List<FeatureImplementation>>> GetByCategoryAsync(FeatureCategory category, CancellationToken ct = default) =>
        Task.FromResult(StorageResult<List<FeatureImplementation>>.Ok(
            _features.Values.Where(f => f.Category == category).OrderBy(f => f.Priority).ToList()));

    public Task<StorageResult<List<FeatureImplementation>>> GetByStatusAsync(FeatureStatus status, CancellationToken ct = default) =>
        Task.FromResult(StorageResult<List<FeatureImplementation>>.Ok(
            _features.Values.Where(f => f.Status == status).OrderBy(f => f.Priority).ToList()));

    public Task<StorageResult<FeatureImplementation>> UpsertAsync(FeatureImplementation feature, CancellationToken ct = default)
    {
        feature.LastUpdatedAt = DateTime.UtcNow;
        _features[feature.Id] = feature;
        return Task.FromResult(StorageResult<FeatureImplementation>.Ok(feature));
    }

    public Task<StorageResult<bool>> UpdateStatusAsync(string featureId, FeatureStatus status, int progressPercent, CancellationToken ct = default)
    {
        if (_features.TryGetValue(featureId, out var f))
        {
            f.Status = status;
            f.ProgressPercent = progressPercent;
            f.LastUpdatedAt = DateTime.UtcNow;
            if (status == FeatureStatus.InProgress && f.StartedAt is null) f.StartedAt = DateTime.UtcNow;
            if (status == FeatureStatus.Completed) { f.CompletedAt = DateTime.UtcNow; f.ProgressPercent = 100; }
            return Task.FromResult(StorageResult<bool>.Ok(true));
        }
        return Task.FromResult(StorageResult<bool>.Fail($"Feature '{featureId}' not found"));
    }

    public Task<StorageResult<bool>> DeleteAsync(string featureId, CancellationToken ct = default) =>
        Task.FromResult(StorageResult<bool>.Ok(_features.TryRemove(featureId, out _)));

    public Task<StorageResult<Dictionary<FeatureCategory, int>>> GetCategoryCountsAsync(CancellationToken ct = default) =>
        Task.FromResult(StorageResult<Dictionary<FeatureCategory, int>>.Ok(
            _features.Values.GroupBy(f => f.Category).ToDictionary(g => g.Key, g => g.Count())));

    public Task<StorageResult<Dictionary<FeatureStatus, int>>> GetStatusCountsAsync(CancellationToken ct = default) =>
        Task.FromResult(StorageResult<Dictionary<FeatureStatus, int>>.Ok(
            _features.Values.GroupBy(f => f.Status).ToDictionary(g => g.Key, g => g.Count())));

    private void SeedFeatures()
    {
        var features = new List<FeatureImplementation>
        {
            new() { Id = "feat-aspire-host", Name = "Aspire AppHost Orchestration", Category = FeatureCategory.CoreInfrastructure, Status = FeatureStatus.Completed, Project = "TheWatch.AppHost", ProgressPercent = 100, Priority = 1, AssignedTo = "Claude Code", CompletedAt = DateTime.UtcNow.AddDays(-5) },
            new() { Id = "feat-sqlserver-adapter", Name = "SQL Server Storage Adapter", Category = FeatureCategory.CoreInfrastructure, Status = FeatureStatus.Completed, Project = "TheWatch.Data", ProgressPercent = 100, Priority = 1, AssignedTo = "Claude Code", CompletedAt = DateTime.UtcNow.AddDays(-4) },
            new() { Id = "feat-response-coordination", Name = "SOS Response Coordination Pipeline", Category = FeatureCategory.ResponseCoordination, Status = FeatureStatus.Completed, Project = "TheWatch.Dashboard.Api", ProgressPercent = 100, Priority = 1, AssignedTo = "Claude Code", CompletedAt = DateTime.UtcNow.AddDays(-2) },
            new() { Id = "feat-evidence-upload", Name = "Evidence Upload Controller", Category = FeatureCategory.EvidenceSystem, Status = FeatureStatus.Completed, Project = "TheWatch.Dashboard.Api", ProgressPercent = 100, Priority = 2, AssignedTo = "Claude Code", CompletedAt = DateTime.UtcNow.AddDays(-1) },
            new() { Id = "feat-survey-system", Name = "Survey Templates & Responses", Category = FeatureCategory.SurveySystem, Status = FeatureStatus.Completed, Project = "TheWatch.Shared", ProgressPercent = 100, Priority = 2, AssignedTo = "Claude Code", CompletedAt = DateTime.UtcNow.AddDays(-1) },
            new() { Id = "feat-signalr-hub", Name = "SignalR Real-Time Hub", Category = FeatureCategory.CoreInfrastructure, Status = FeatureStatus.Completed, Project = "TheWatch.Dashboard.Api", ProgressPercent = 100, Priority = 1, AssignedTo = "Claude Code", CompletedAt = DateTime.UtcNow.AddDays(-3) },
            new() { Id = "feat-mudblazor-datagrid", Name = "MudBlazor Feature Tracker DataGrid", Category = FeatureCategory.DashboardWeb, Status = FeatureStatus.InProgress, Project = "TheWatch.Dashboard.Web", ProgressPercent = 75, Priority = 1, AssignedTo = "Claude Code", StartedAt = DateTime.UtcNow },
            new() { Id = "feat-serilog", Name = "Serilog Structured Logging", Category = FeatureCategory.DevOps, Status = FeatureStatus.InProgress, Project = "TheWatch.ServiceDefaults", ProgressPercent = 60, Priority = 1, AssignedTo = "Claude Code", StartedAt = DateTime.UtcNow },
            new() { Id = "feat-claude-code-integration", Name = "Claude Code DevWork Integration", Category = FeatureCategory.ClaudeCodeIntegration, Status = FeatureStatus.InProgress, Project = "TheWatch.Dashboard.Api", ProgressPercent = 50, Priority = 1, AssignedTo = "Claude Code", StartedAt = DateTime.UtcNow },
            new() { Id = "feat-firestore-sync", Name = "Firestore Bi-Directional Sync", Category = FeatureCategory.FirestoreSync, Status = FeatureStatus.Planned, Project = "TheWatch.Data", ProgressPercent = 0, Priority = 2, Tags = new() { "google-cloud", "firestore" } },
            new() { Id = "feat-android-native", Name = "Android Native (Jetpack Compose)", Category = FeatureCategory.MobileApp, Status = FeatureStatus.Planned, Project = "TheWatch.Android", ProgressPercent = 0, Priority = 1, Tags = new() { "android", "jetpack" } },
            new() { Id = "feat-ios-native", Name = "iOS Native (SwiftUI)", Category = FeatureCategory.MobileApp, Status = FeatureStatus.Planned, Project = "TheWatch.iOS", ProgressPercent = 0, Priority = 1, Tags = new() { "ios", "swiftui" } },
            new() { Id = "feat-offline-evidence", Name = "Offline Evidence Sync", Category = FeatureCategory.OfflineSupport, Status = FeatureStatus.Completed, Project = "TheWatch.Dashboard.Api", ProgressPercent = 100, Priority = 2, AssignedTo = "Claude Code", CompletedAt = DateTime.UtcNow.AddDays(-1) },
            new() { Id = "feat-spatial-h3", Name = "H3 Geospatial Indexing", Category = FeatureCategory.SpatialIndexing, Status = FeatureStatus.InProgress, Project = "TheWatch.Data", ProgressPercent = 40, Priority = 3, Tags = new() { "h3", "geospatial" } },
            new() { Id = "feat-push-notifications", Name = "FCM/APNs Push Notifications", Category = FeatureCategory.Notifications, Status = FeatureStatus.Planned, Project = "TheWatch.Shared", ProgressPercent = 0, Priority = 2, Tags = new() { "fcm", "apns" } },
            new() { Id = "feat-standards-inferencing", Name = "Standards Inferencing System", Category = FeatureCategory.Standards, Status = FeatureStatus.InProgress, Project = "TheWatch.Shared", ProgressPercent = 30, Priority = 2, Tags = new() { "ISO-27001", "compliance" } },
            new() { Id = "feat-webhook-receiver", Name = "DevWork Webhook Receiver", Category = FeatureCategory.ClaudeCodeIntegration, Status = FeatureStatus.InProgress, Project = "TheWatch.Functions", ProgressPercent = 50, Priority = 1, AssignedTo = "Claude Code", StartedAt = DateTime.UtcNow },
            new() { Id = "feat-value-calculator", Name = "Asset Value Calculator", Category = FeatureCategory.Analytics, Status = FeatureStatus.Planned, Project = "TheWatch.Shared", ProgressPercent = 0, Priority = 3, Description = "Calculate worth of valuables for insurance/security purposes" },
        };

        foreach (var f in features)
            _features[f.Id] = f;
    }
}
