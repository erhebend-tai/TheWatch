using TheWatch.Maui.Models;
using TheWatch.Maui.Services;
using TheWatch.Shared.Dtos;
using TheWatch.Shared.Enums;

namespace TheWatch.Maui.ViewModels;

public partial class DeviceSimulatorViewModel : ObservableObject
{
    private readonly IDashboardRelay _dashboardRelay;
    private readonly ILogger<DeviceSimulatorViewModel> _logger;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty]
    public partial SimulatedDevice Device { get; set; }

    [ObservableProperty]
    public partial bool IsDeviceOnline { get; set; }

    [ObservableProperty]
    public partial double BatteryPercentage { get; set; }

    [ObservableProperty]
    public partial bool IsJailbroken { get; set; }

    [ObservableProperty]
    public partial bool IsRooted { get; set; }

    [ObservableProperty]
    public partial string[] DeviceTypeOptions { get; set; }

    [ObservableProperty]
    public partial int SelectedDeviceTypeIndex { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    public DeviceSimulatorViewModel(IDashboardRelay dashboardRelay, ILogger<DeviceSimulatorViewModel> logger, MainViewModel mainViewModel)
    {
        _dashboardRelay = dashboardRelay;
        _logger = logger;
        _mainViewModel = mainViewModel;

        Device = new();
        IsDeviceOnline = true;
        BatteryPercentage = 85;
        IsJailbroken = false;
        IsRooted = false;
        DeviceTypeOptions = ["Smartwatch", "Phone", "Tablet", "Wearable"];
        SelectedDeviceTypeIndex = 0;
        IsLoading = false;
        StatusMessage = string.Empty;

        UpdateDevice();
    }

    [RelayCommand]
    public async Task PublishDeviceStateAsync()
    {
        try
        {
            if (!await _dashboardRelay.IsConnectedAsync())
            {
                StatusMessage = "Not connected to dashboard";
                return;
            }

            IsLoading = true;
            UpdateDevice();

            var eventDto = new SimulationEventDto(
                EventType: SimulationEventType.DeviceStateChange,
                Payload: System.Text.Json.JsonSerializer.Serialize(Device.ToPayload()),
                Source: "MobileApp",
                Timestamp: DateTime.UtcNow,
                Latitude: Device.Latitude,
                Longitude: Device.Longitude
            );

            var success = await _dashboardRelay.SendSimulationEventAsync(eventDto);
            if (success)
            {
                StatusMessage = $"Device state published: {(IsDeviceOnline ? "Online" : "Offline")}, Battery {BatteryPercentage}%";
                _mainViewModel.IncrementPublished();
            }
            else
            {
                StatusMessage = "Failed to publish device state";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing device state");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task SimulateDeviceGoOfflineAsync()
    {
        IsDeviceOnline = false;
        BatteryPercentage = Random.Shared.Next(10, 30);
        await PublishDeviceStateAsync();
    }

    [RelayCommand]
    public async Task SimulateDeviceGoOnlineAsync()
    {
        IsDeviceOnline = true;
        BatteryPercentage = Random.Shared.Next(60, 100);
        await PublishDeviceStateAsync();
    }

    [RelayCommand]
    public async Task SimulateSecurityCompromiseAsync()
    {
        IsJailbroken = true;
        IsRooted = true;
        StatusMessage = "Device security compromised (jailbroken/rooted)";
        await PublishDeviceStateAsync();
    }

    [RelayCommand]
    public async Task SimulateSecurityRestoreAsync()
    {
        IsJailbroken = false;
        IsRooted = false;
        StatusMessage = "Device security restored";
        await PublishDeviceStateAsync();
    }

    [RelayCommand]
    public async Task ResetFormAsync()
    {
        IsDeviceOnline = true;
        BatteryPercentage = 85;
        IsJailbroken = false;
        IsRooted = false;
        SelectedDeviceTypeIndex = 0;
        StatusMessage = string.Empty;
        UpdateDevice();
        await Task.CompletedTask;
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
                Device.Latitude = location.Latitude;
                Device.Longitude = location.Longitude;
                StatusMessage = $"Location updated: {location.Latitude:F4}, {location.Longitude:F4}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting location");
            StatusMessage = $"Location error: {ex.Message}";
        }
    }

    private void UpdateDevice()
    {
        Device.IsOnline = IsDeviceOnline;
        Device.BatteryPercentage = (int)BatteryPercentage;
        Device.IsJailbroken = IsJailbroken;
        Device.IsRooted = IsRooted;
        Device.DeviceType = DeviceTypeOptions[SelectedDeviceTypeIndex];
        Device.UpdatedAt = DateTime.Now;
    }

    partial void OnIsDeviceOnlineChanged(bool value) => UpdateDevice();
    partial void OnBatteryPercentageChanged(double value) => UpdateDevice();
    partial void OnIsJailbrokenChanged(bool value) => UpdateDevice();
    partial void OnIsRootedChanged(bool value) => UpdateDevice();
    partial void OnSelectedDeviceTypeIndexChanged(int value) => UpdateDevice();
}
