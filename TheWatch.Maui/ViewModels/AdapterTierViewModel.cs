using System.Collections.ObjectModel;
using TheWatch.Maui.Services;

namespace TheWatch.Maui.ViewModels;

/// <summary>
/// ViewModel for the Adapter Tier Switcher page.
/// Loads current adapter tier assignments from the Dashboard API
/// and allows toggling between Mock / Native / Live per slot.
/// </summary>
public partial class AdapterTierViewModel : ObservableObject
{
    private readonly IDashboardRelay _relay;
    private readonly ILogger<AdapterTierViewModel> _logger;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    public ObservableCollection<AdapterSlotItem> CloudProviders { get; } = new();
    public ObservableCollection<AdapterSlotItem> DataLayer { get; } = new();
    public ObservableCollection<AdapterSlotItem> Features { get; } = new();
    public ObservableCollection<AdapterSlotItem> AI { get; } = new();

    public AdapterTierViewModel(IDashboardRelay relay, ILogger<AdapterTierViewModel> logger)
    {
        _relay = relay;
        _logger = logger;

        IsLoading = false;
        StatusMessage = "Not loaded";
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Loading adapter registry...";

            var registry = await _relay.GetAdapterRegistryAsync();
            if (registry == null)
            {
                StatusMessage = "Failed to load -- is Dashboard API connected?";
                return;
            }

            PopulateGroup(CloudProviders, registry.CloudProviders);
            PopulateGroup(DataLayer, registry.DataLayer);
            PopulateGroup(Features, registry.Features);
            PopulateGroup(AI, registry.AI);

            var totalSlots = CloudProviders.Count + DataLayer.Count + Features.Count + AI.Count;
            var mockCount = CloudProviders.Concat(DataLayer).Concat(Features).Concat(AI)
                .Count(s => s.CurrentTier == "Mock");
            StatusMessage = $"{totalSlots} slots loaded -- {mockCount} on Mock";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load adapter registry");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task SwitchTierAsync(AdapterSlotItem slot)
    {
        if (slot == null) return;

        try
        {
            var newTier = slot.CurrentTier switch
            {
                "Mock" => "Native",
                "Native" => "Live",
                "Live" => "Mock",
                "Disabled" => "Mock",
                _ => "Mock"
            };

            StatusMessage = $"Switching {slot.Name} -> {newTier}...";
            var success = await _relay.SetAdapterTierAsync(slot.Name, newTier);

            if (success)
            {
                slot.CurrentTier = newTier;
                StatusMessage = $"{slot.Name} switched to {newTier}";
                _logger.LogInformation("Adapter switched: {Slot} -> {Tier}", slot.Name, newTier);
            }
            else
            {
                StatusMessage = $"Failed to switch {slot.Name}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to switch tier for {Slot}", slot.Name);
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ResetAllAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Resetting all adapters to Mock...";
            var success = await _relay.ResetAdapterTiersAsync();
            if (success)
            {
                await LoadAsync();
                StatusMessage = "All adapters reset to Mock";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset adapters");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static void PopulateGroup(ObservableCollection<AdapterSlotItem> collection, Dictionary<string, string>? slots)
    {
        collection.Clear();
        if (slots == null) return;
        foreach (var (name, tier) in slots.OrderBy(kv => kv.Key))
        {
            collection.Add(new AdapterSlotItem { Name = name, CurrentTier = tier });
        }
    }
}

/// <summary>
/// Observable item representing a single adapter slot.
/// </summary>
public partial class AdapterSlotItem : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string CurrentTier { get; set; }

    public AdapterSlotItem()
    {
        Name = "";
        CurrentTier = "Mock";
    }

    public string TierColor => CurrentTier switch
    {
        "Mock" => "#2E8B57",    // Green -- always safe
        "Native" => "#2563EB",   // Blue -- on-device
        "Live" => "#D4890E",     // Amber -- cloud
        "Disabled" => "#6B7280", // Gray
        _ => "#6B7280"
    };

    public string TierIcon => CurrentTier switch
    {
        "Mock" => "Mock",
        "Native" => "Native",
        "Live" => "Live",
        "Disabled" => "Disabled",
        _ => "Unknown"
    };
}
