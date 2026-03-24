using TheWatch.Maui.Models;
using TheWatch.Maui.Services;
using TheWatch.Shared.Dtos;
using TheWatch.Shared.Enums;

namespace TheWatch.Maui.ViewModels;

public partial class AlertSimulatorViewModel : ObservableObject
{
    private readonly IDashboardRelay _dashboardRelay;
    private readonly ILogger<AlertSimulatorViewModel> _logger;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty]
    public partial SimulatedAlert Alert { get; set; }

    [ObservableProperty]
    public partial string[] SeverityOptions { get; set; }

    [ObservableProperty]
    public partial int SelectedSeverityIndex { get; set; }

    [ObservableProperty]
    public partial string[] TriggerOptions { get; set; }

    [ObservableProperty]
    public partial int SelectedTriggerIndex { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    public AlertSimulatorViewModel(IDashboardRelay dashboardRelay, ILogger<AlertSimulatorViewModel> logger, MainViewModel mainViewModel)
    {
        _dashboardRelay = dashboardRelay;
        _logger = logger;
        _mainViewModel = mainViewModel;

        Alert = new();
        SeverityOptions = ["Low", "Medium", "High", "Critical"];
        SelectedSeverityIndex = 3;
        TriggerOptions = ["Manual", "Automatic", "Scheduled", "User-initiated"];
        SelectedTriggerIndex = 0;
        IsLoading = false;
        StatusMessage = string.Empty;

        Alert.AlertSeverity = SeverityOptions[SelectedSeverityIndex];
        Alert.TriggerType = TriggerOptions[SelectedTriggerIndex];
    }

    [RelayCommand]
    public async Task PublishAlertAsync()
    {
        try
        {
            if (!await _dashboardRelay.IsConnectedAsync())
            {
                StatusMessage = "Not connected to dashboard";
                return;
            }

            IsLoading = true;
            Alert.AlertSeverity = SeverityOptions[SelectedSeverityIndex];
            Alert.TriggerType = TriggerOptions[SelectedTriggerIndex];

            var eventDto = new SimulationEventDto(
                EventType: SimulationEventType.SOSTrigger,
                Payload: System.Text.Json.JsonSerializer.Serialize(Alert.ToPayload()),
                Source: "MobileApp",
                Timestamp: DateTime.UtcNow,
                Latitude: Alert.Latitude,
                Longitude: Alert.Longitude
            );

            var success = await _dashboardRelay.SendSimulationEventAsync(eventDto);
            if (success)
            {
                StatusMessage = $"Alert published: {Alert.AlertSeverity} severity";
                _mainViewModel.IncrementPublished();
                await ResetFormAsync();
            }
            else
            {
                StatusMessage = "Failed to publish alert";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing alert");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task GetCurrentLocationAsync()
    {
        try
        {
            var request = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(10));
            var location = await Geolocation.GetLocationAsync(request);

            if (location != null)
            {
                Alert.Latitude = location.Latitude;
                Alert.Longitude = location.Longitude;
                StatusMessage = $"Location updated: {location.Latitude:F4}, {location.Longitude:F4}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting location");
            StatusMessage = $"Location error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ResetFormAsync()
    {
        Alert = new SimulatedAlert();
        SelectedSeverityIndex = 3;
        SelectedTriggerIndex = 0;
        StatusMessage = string.Empty;
        await Task.CompletedTask;
    }

    partial void OnSelectedSeverityIndexChanged(int value)
    {
        if (value >= 0 && value < SeverityOptions.Length)
        {
            Alert.AlertSeverity = SeverityOptions[value];
        }
    }

    partial void OnSelectedTriggerIndexChanged(int value)
    {
        if (value >= 0 && value < TriggerOptions.Length)
        {
            Alert.TriggerType = TriggerOptions[value];
        }
    }
}
