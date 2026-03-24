using System.Net.Http.Json;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Dtos;
using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Web.Services;

/// <summary>
/// Typed HTTP client for the Dashboard API.
/// </summary>
public class DashboardApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DashboardApiClient> _logger;

    public DashboardApiClient(HttpClient httpClient, ILogger<DashboardApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    // Milestones
    public async Task<List<MilestoneDto>> GetMilestonesAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<List<MilestoneDto>>("api/milestones");
            return response ?? new List<MilestoneDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching milestones");
            return new List<MilestoneDto>();
        }
    }

    public async Task<MilestoneProgressDto?> GetMilestoneProgressAsync(string milestoneId)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<MilestoneProgressDto>($"api/milestones/{milestoneId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching milestone progress");
            return null;
        }
    }

    // Work Items
    public async Task<List<WorkItemDto>> GetWorkItemsAsync(
        string? milestone = null,
        string? agent = null,
        string? platform = null,
        string? status = null)
    {
        try
        {
            var queryParts = new List<string>();
            if (!string.IsNullOrEmpty(milestone)) queryParts.Add($"milestone={milestone}");
            if (!string.IsNullOrEmpty(agent)) queryParts.Add($"agent={agent}");
            if (!string.IsNullOrEmpty(platform)) queryParts.Add($"platform={platform}");
            if (!string.IsNullOrEmpty(status)) queryParts.Add($"status={status}");

            var query = queryParts.Any() ? "?" + string.Join("&", queryParts) : "";
            var response = await _httpClient.GetFromJsonAsync<List<WorkItemDto>>($"api/workitems{query}");
            return response ?? new List<WorkItemDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching work items");
            return new List<WorkItemDto>();
        }
    }

    // Agents
    public async Task<List<object>> GetAgentsAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<List<object>>("api/agents");
            return response ?? new List<object>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching agents");
            return new List<object>();
        }
    }

    public async Task<List<AgentActivityDto>> GetRecentActivityAsync(int limit = 50)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<List<AgentActivityDto>>($"api/agents/activity/recent?limit={limit}");
            return response ?? new List<AgentActivityDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching recent activity");
            return new List<AgentActivityDto>();
        }
    }

    // Builds
    public async Task<List<BuildStatusDto>> GetBuildsAsync(string? platform = null)
    {
        try
        {
            var query = !string.IsNullOrEmpty(platform) ? $"?platform={platform}" : "";
            var response = await _httpClient.GetFromJsonAsync<List<BuildStatusDto>>($"api/builds{query}");
            return response ?? new List<BuildStatusDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching builds");
            return new List<BuildStatusDto>();
        }
    }

    public async Task<object?> GetBuildStatsAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<object>("api/builds/stats/summary");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching build stats");
            return null;
        }
    }

    // Simulation
    public async Task<List<SimulationEventDto>> GetSimulationLogAsync(int limit = 100)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<List<SimulationEventDto>>($"api/simulation/log?limit={limit}");
            return response ?? new List<SimulationEventDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching simulation log");
            return new List<SimulationEventDto>();
        }
    }

    public async Task<SimulationEventDto?> PublishSimulationEventAsync(SimulationEventDto eventDto)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/simulation/events", eventDto);
            return await response.Content.ReadFromJsonAsync<SimulationEventDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing simulation event");
            return null;
        }
    }

    public async Task<object?> GetSimulationStatsAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<object>("api/simulation/stats");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching simulation stats");
            return null;
        }
    }

    // Features
    public async Task<List<FeatureImplementation>> GetFeaturesAsync(FeatureCategory? category = null, FeatureStatus? status = null)
    {
        try
        {
            var queryParts = new List<string>();
            if (category is not null) queryParts.Add($"category={category}");
            if (status is not null) queryParts.Add($"status={status}");
            var query = queryParts.Any() ? "?" + string.Join("&", queryParts) : "";
            return await _httpClient.GetFromJsonAsync<List<FeatureImplementation>>($"api/features{query}") ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching features");
            return new();
        }
    }

    public async Task<object?> GetFeatureStatsAsync()
    {
        try { return await _httpClient.GetFromJsonAsync<object>("api/features/stats"); }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching feature stats"); return null; }
    }

    public async Task<FeatureImplementation?> UpsertFeatureAsync(FeatureImplementation feature)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/features", feature);
            return await response.Content.ReadFromJsonAsync<FeatureImplementation>();
        }
        catch (Exception ex) { _logger.LogError(ex, "Error upserting feature"); return null; }
    }

    // DevWork
    public async Task<List<DevWorkLog>> GetDevWorkLogsAsync(int limit = 50)
    {
        try { return await _httpClient.GetFromJsonAsync<List<DevWorkLog>>($"api/devwork/logs?limit={limit}") ?? new(); }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching devwork logs"); return new(); }
    }

    public async Task<DevWorkLog?> LogDevWorkAsync(DevWorkLog log)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/devwork/logs", log);
            return await response.Content.ReadFromJsonAsync<DevWorkLog>();
        }
        catch (Exception ex) { _logger.LogError(ex, "Error logging devwork"); return null; }
    }

    // Swarm Inventory (Firestore-backed)
    public async Task<List<TheWatch.Shared.Domain.Ports.SwarmFileRecord>> GetSwarmFilesAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<List<TheWatch.Shared.Domain.Ports.SwarmFileRecord>>(
                "api/swarm-inventory/files") ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Firestore swarm files unavailable, will use static fallback");
            return [];
        }
    }

    public async Task<List<TheWatch.Shared.Domain.Ports.SwarmSupervisorRecord>> GetSwarmSupervisorsAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<List<TheWatch.Shared.Domain.Ports.SwarmSupervisorRecord>>(
                "api/swarm-inventory/supervisors") ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Firestore swarm supervisors unavailable, will use static fallback");
            return [];
        }
    }

    public async Task SeedSwarmInventoryAsync(
        List<TheWatch.Shared.Domain.Ports.SwarmFileRecord> files,
        List<TheWatch.Shared.Domain.Ports.SwarmSupervisorRecord> supervisors)
    {
        try
        {
            await _httpClient.PostAsJsonAsync("api/swarm-inventory/seed", new { Files = files, Supervisors = supervisors });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to seed swarm inventory");
        }
    }

    // Health
    public async Task<object?> GetAggregatedHealthAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<object>("api/health");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching aggregated health");
            return null;
        }
    }
}
