// =============================================================================
// FeaturePanel — Shows feature implementation status in a scrollable list.
// =============================================================================
// Renders each feature as a colored line:
//   ✓ Evidence Upload Controller          [Completed]  100%
//   ► EULA Management View                [InProgress]  60%
//   ✗ Data Export Screen                  [Blocked]      0%
//   ◻ Standards Inferencing               [Planned]      0%
//
// Updates via:
//   1. Full refresh from GET /api/features (periodic polling)
//   2. Individual update from SignalR "FeatureUpdated" event
//
// Example:
//   var panel = new FeaturePanel();
//   panel.SetFeatures(features);   // bulk load
//   panel.UpdateFeature(feature);  // single update from SignalR
//
// WAL: ListView.SetSource() must be called on the UI thread via Application.Invoke().
// =============================================================================

using Terminal.Gui;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Cli.Panels;

public class FeaturePanel : FrameView
{
    private readonly ListView _listView;
    private readonly Label _summaryLabel;
    private List<FeatureImplementation> _features = new();
    private List<string> _displayLines = new();
    private FeatureCategory? _categoryFilter;

    public FeaturePanel()
    {
        Title = "Features";
        BorderStyle = LineStyle.Single;

        // Summary bar at top: "47/94 done | 12 in progress | 3 blocked"
        _summaryLabel = new Label()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            Text = "Loading features..."
        };

        // Filter bar
        var filterLabel = new Label()
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = 1,
            Text = "[Tab] cycle filter | [Enter] details",
            ColorScheme = new ColorScheme
            {
                Normal = new Terminal.Gui.Attribute(Color.Gray, Color.Black)
            }
        };

        // Scrollable feature list
        _listView = new ListView()
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            AllowsMarking = false,
            CanFocus = true
        };

        _listView.KeyDown += HandleKeyPress;

        Add(_summaryLabel, filterLabel, _listView);
    }

    public void SetFeatures(List<FeatureImplementation> features)
    {
        _features = features.OrderBy(f => f.Status == FeatureStatus.Completed ? 1 : 0)
                            .ThenBy(f => f.Priority)
                            .ThenBy(f => f.Category)
                            .ToList();
        RebuildDisplay();
    }

    public void UpdateFeature(FeatureImplementation updated)
    {
        var idx = _features.FindIndex(f => f.Id == updated.Id);
        if (idx >= 0)
            _features[idx] = updated;
        else
            _features.Add(updated);
        RebuildDisplay();
    }

    private void RebuildDisplay()
    {
        var filtered = _categoryFilter.HasValue
            ? _features.Where(f => f.Category == _categoryFilter.Value).ToList()
            : _features;

        _displayLines = filtered.Select(FormatFeatureLine).ToList();
        _listView.SetSource<string>(new System.Collections.ObjectModel.ObservableCollection<string>(_displayLines));

        // Update summary
        var total = _features.Count;
        var done = _features.Count(f => f.Status == FeatureStatus.Completed);
        var inProg = _features.Count(f => f.Status == FeatureStatus.InProgress);
        var blocked = _features.Count(f => f.Status == FeatureStatus.Blocked);
        var review = _features.Count(f => f.Status == FeatureStatus.InReview);
        var testing = _features.Count(f => f.Status == FeatureStatus.Testing);

        _summaryLabel.Text = $" {done}/{total} done | {inProg} active | {review} review | {testing} test | {blocked} blocked";
        _summaryLabel.ColorScheme = new ColorScheme
        {
            Normal = new Terminal.Gui.Attribute(
                blocked > 0 ? Color.Red : (done == total ? Color.Green : Color.Cyan),
                Color.Black)
        };
    }

    private static string FormatFeatureLine(FeatureImplementation f)
    {
        var icon = f.Status switch
        {
            FeatureStatus.Completed => "✓",
            FeatureStatus.InProgress => "►",
            FeatureStatus.InReview => "⟐",
            FeatureStatus.Testing => "⚙",
            FeatureStatus.Blocked => "✗",
            FeatureStatus.Deferred => "⏸",
            _ => "◻"
        };

        var statusTag = f.Status switch
        {
            FeatureStatus.Completed => "DONE",
            FeatureStatus.InProgress => "WIP ",
            FeatureStatus.InReview => "REV ",
            FeatureStatus.Testing => "TEST",
            FeatureStatus.Blocked => "BLKD",
            FeatureStatus.Deferred => "DEFR",
            _ => "PLAN"
        };

        var pct = f.ProgressPercent.ToString().PadLeft(3);
        var name = f.Name.Length > 35 ? f.Name[..32] + "..." : f.Name.PadRight(35);
        var cat = f.Category.ToString();
        cat = cat.Length > 12 ? cat[..12] : cat.PadRight(12);

        return $" {icon} {name} [{statusTag}] {pct}% {cat}";
    }

    private void HandleKeyPress(object? sender, Key e)
    {
        if (e == Key.Tab)
        {
            // Cycle through category filters
            var categories = Enum.GetValues<FeatureCategory>();
            if (!_categoryFilter.HasValue)
                _categoryFilter = categories[0];
            else
            {
                var idx = Array.IndexOf(categories, _categoryFilter.Value);
                if (idx >= categories.Length - 1)
                    _categoryFilter = null; // back to "All"
                else
                    _categoryFilter = categories[idx + 1];
            }
            RebuildDisplay();
            e.Handled = true;
        }
        else if (e == Key.Enter)
        {
            ShowFeatureDetails();
            e.Handled = true;
        }
    }

    private void ShowFeatureDetails()
    {
        var filtered = _categoryFilter.HasValue
            ? _features.Where(f => f.Category == _categoryFilter.Value).ToList()
            : _features;

        if (_listView.SelectedItem < 0 || _listView.SelectedItem >= filtered.Count) return;

        var f = filtered[_listView.SelectedItem];
        var details = $@"
Feature: {f.Name}
ID:      {f.Id}
Status:  {f.Status} ({f.ProgressPercent}%)
Category: {f.Category}
Project:  {f.Project ?? "N/A"}
Priority: {f.Priority}
Assigned: {f.AssignedTo ?? "Unassigned"}
Files:    {string.Join(", ", f.FilePaths)}
Tags:     {string.Join(", ", f.Tags)}
Deps:     {string.Join(", ", f.Dependencies)}
Blocked:  {f.BlockedReason ?? "N/A"}
Created:  {f.CreatedAt:yyyy-MM-dd HH:mm}
Started:  {f.StartedAt?.ToString("yyyy-MM-dd HH:mm") ?? "N/A"}
Done:     {f.CompletedAt?.ToString("yyyy-MM-dd HH:mm") ?? "N/A"}
Updated:  {f.LastUpdatedAt:yyyy-MM-dd HH:mm}
Source:   {f.LastUpdateSource ?? "N/A"}
";
        MessageBox.Query("Feature Details", details, "OK");
    }
}
