// =============================================================================
// AgentPanel — Displays active AI agents/subagents and their current tasks.
// =============================================================================
// Renders agent activity as a live feed:
//   ● ClaudeCode    feat-evidence-upload       2m ago
//   ● GeminiPro     scan-documentation         5m ago
//   ○ JetBrainsAI   idle                       —
//   ● Human         reviewing PR #42           1m ago
//
// Each agent type from AgentType enum gets a persistent row.
// Active agents (with recent activity) show ● filled dot.
// Idle agents show ○ hollow dot.
//
// Updates via:
//   1. Full refresh from GET /api/agents (periodic polling)
//   2. Individual update from SignalR "AgentActivityRecorded" event
//
// Example:
//   var panel = new AgentPanel();
//   panel.SetAgents(activities);
//   panel.UpdateAgent(newActivity);
//
// WAL: AgentActivity has no persistent ID — keyed by AgentType for dedup.
// =============================================================================

using Terminal.Gui;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Cli.Panels;

public class AgentPanel : FrameView
{
    private readonly ListView _listView;
    private readonly Label _summaryLabel;
    private readonly Dictionary<AgentType, AgentActivityState> _agentStates = new();
    private List<string> _displayLines = new();

    public AgentPanel()
    {
        Title = "Agents / Subagents";
        BorderStyle = LineStyle.Single;

        _summaryLabel = new Label()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            Text = "Discovering agents..."
        };

        _listView = new ListView()
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            AllowsMarking = false,
            CanFocus = true
        };

        // Initialize all known agent types as idle
        foreach (var agentType in Enum.GetValues<AgentType>())
        {
            _agentStates[agentType] = new AgentActivityState
            {
                Type = agentType,
                IsActive = false,
                LastAction = "idle",
                LastSeen = null
            };
        }

        Add(_summaryLabel, _listView);
    }

    public void SetAgents(List<AgentActivity> activities)
    {
        // Reset all to idle first
        foreach (var key in _agentStates.Keys)
        {
            _agentStates[key] = _agentStates[key] with { IsActive = false, LastAction = "idle" };
        }

        // Apply latest activity per agent type
        foreach (var group in activities.GroupBy(a => a.AgentType))
        {
            var latest = group.OrderByDescending(a => a.Timestamp).First();
            var isRecent = (DateTime.UtcNow - latest.Timestamp).TotalMinutes < 10;

            _agentStates[group.Key] = new AgentActivityState
            {
                Type = group.Key,
                IsActive = isRecent,
                LastAction = latest.Action,
                Description = latest.Description,
                BranchName = latest.BranchName,
                Platform = latest.Platform,
                LastSeen = latest.Timestamp,
                SubagentCount = group.Count(a => (DateTime.UtcNow - a.Timestamp).TotalMinutes < 10)
            };
        }

        RebuildDisplay();
    }

    public void UpdateAgent(AgentActivity activity)
    {
        _agentStates[activity.AgentType] = new AgentActivityState
        {
            Type = activity.AgentType,
            IsActive = true,
            LastAction = activity.Action,
            Description = activity.Description,
            BranchName = activity.BranchName,
            Platform = activity.Platform,
            LastSeen = activity.Timestamp,
            SubagentCount = _agentStates.TryGetValue(activity.AgentType, out var existing)
                ? existing.SubagentCount + 1 : 1
        };

        RebuildDisplay();
    }

    private void RebuildDisplay()
    {
        var activeCount = _agentStates.Values.Count(s => s.IsActive);
        var totalSubagents = _agentStates.Values.Sum(s => s.SubagentCount);

        _summaryLabel.Text = $" {activeCount} active | {totalSubagents} subagents running";
        _summaryLabel.ColorScheme = new ColorScheme
        {
            Normal = new Terminal.Gui.Attribute(
                activeCount > 0 ? Color.Green : Color.Gray, Color.Black)
        };

        _displayLines = _agentStates.Values
            .OrderByDescending(s => s.IsActive)
            .ThenBy(s => s.Type)
            .Select(FormatAgentLine)
            .ToList();

        _listView.SetSource<string>(new System.Collections.ObjectModel.ObservableCollection<string>(_displayLines));
    }

    private static string FormatAgentLine(AgentActivityState state)
    {
        var icon = state.IsActive ? "●" : "○";
        var name = state.Type.ToString().PadRight(14);
        var action = (state.LastAction ?? "idle");
        action = action.Length > 25 ? action[..22] + "..." : action.PadRight(25);
        var ago = state.LastSeen.HasValue
            ? FormatTimeAgo(DateTime.UtcNow - state.LastSeen.Value)
            : "—";
        var branch = !string.IsNullOrEmpty(state.BranchName) ? $" [{state.BranchName}]" : "";
        var subs = state.SubagentCount > 1 ? $" ({state.SubagentCount} subs)" : "";

        return $" {icon} {name} {action} {ago}{branch}{subs}";
    }

    private static string FormatTimeAgo(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 60) return $"{(int)elapsed.TotalSeconds}s ago";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
        return $"{(int)elapsed.TotalDays}d ago";
    }
}

internal record AgentActivityState
{
    public AgentType Type { get; init; }
    public bool IsActive { get; init; }
    public string? LastAction { get; init; }
    public string? Description { get; init; }
    public string? BranchName { get; init; }
    public Platform? Platform { get; init; }
    public DateTime? LastSeen { get; init; }
    public int SubagentCount { get; init; }
}
