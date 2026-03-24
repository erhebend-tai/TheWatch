using TheWatch.Maui.Models;
using TheWatch.Maui.Services;
using TheWatch.Shared.Dtos;
using TheWatch.Shared.Enums;

namespace TheWatch.Maui.ViewModels;

public partial class SensorSimulatorViewModel : ObservableObject
{
    private readonly IDashboardRelay _dashboardRelay;
    private readonly ILogger<SensorSimulatorViewModel> _logger;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty]
    public partial SimulatedSensor Sensor { get; set; }

    [ObservableProperty]
    public partial double HeartRateValue { get; set; }

    [ObservableProperty]
    public partial double BloodOxygenValue { get; set; }

    [ObservableProperty]
    public partial double TemperatureValue { get; set; }

    [ObservableProperty]
    public partial double StressLevelValue { get; set; }

    [ObservableProperty]
    public partial bool IsFallDetected { get; set; }

    [ObservableProperty]
    public partial bool HasEcgAnomaly { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    public SensorSimulatorViewModel(IDashboardRelay dashboardRelay, ILogger<SensorSimulatorViewModel> logger, MainViewModel mainViewModel)
    {
        _dashboardRelay = dashboardRelay;
        _logger = logger;
        _mainViewModel = mainViewModel;

        Sensor = new();
        HeartRateValue = 75;
        BloodOxygenValue = 98;
        TemperatureValue = 37.0;
        StressLevelValue = 2;
        IsFallDetected = false;
        HasEcgAnomaly = false;
        IsLoading = false;
        StatusMessage = string.Empty;

        UpdateSensor();
    }

    [RelayCommand]
    public async Task PublishSensorReadingAsync()
    {
        try
        {
            if (!await _dashboardRelay.IsConnectedAsync())
            {
                StatusMessage = "Not connected to dashboard";
                return;
            }

            IsLoading = true;
            UpdateSensor();

            var eventDto = new SimulationEventDto(
                EventType: SimulationEventType.SensorReading,
                Payload: System.Text.Json.JsonSerializer.Serialize(Sensor.ToPayload()),
                Source: "MobileApp",
                Timestamp: DateTime.UtcNow,
                Latitude: 40.7128,
                Longitude: -74.0060
            );

            var success = await _dashboardRelay.SendSimulationEventAsync(eventDto);
            if (success)
            {
                StatusMessage = $"Sensor reading published: HR {Sensor.HeartRate} bpm, SpO2 {Sensor.BloodOxygen}%";
                _mainViewModel.IncrementPublished();
            }
            else
            {
                StatusMessage = "Failed to publish sensor reading";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing sensor reading");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task GenerateAnomalousReadingAsync()
    {
        try
        {
            HeartRateValue = Random.Shared.Next(140, 180);
            BloodOxygenValue = Random.Shared.Next(85, 92);
            TemperatureValue = Random.Shared.Next(38, 40);
            StressLevelValue = Random.Shared.Next(8, 10);
            HasEcgAnomaly = true;

            await PublishSensorReadingAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating anomalous reading");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task GenerateNormalReadingAsync()
    {
        try
        {
            HeartRateValue = Random.Shared.Next(60, 100);
            BloodOxygenValue = Random.Shared.Next(95, 100);
            TemperatureValue = 36.5 + Random.Shared.NextDouble();
            StressLevelValue = Random.Shared.Next(1, 4);
            IsFallDetected = false;
            HasEcgAnomaly = false;

            await PublishSensorReadingAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating normal reading");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ResetFormAsync()
    {
        HeartRateValue = 75;
        BloodOxygenValue = 98;
        TemperatureValue = 37.0;
        StressLevelValue = 2;
        IsFallDetected = false;
        HasEcgAnomaly = false;
        StatusMessage = string.Empty;
        UpdateSensor();
        await Task.CompletedTask;
    }

    private void UpdateSensor()
    {
        Sensor.HeartRate = (int)HeartRateValue;
        Sensor.BloodOxygen = (int)BloodOxygenValue;
        Sensor.BodyTemperature = TemperatureValue;
        Sensor.StressLevel = (int)StressLevelValue;
        Sensor.IsFallDetected = IsFallDetected;
        Sensor.HasEcgAnomaly = HasEcgAnomaly;
        Sensor.RecordedAt = DateTime.Now;
    }

    partial void OnHeartRateValueChanged(double value) => UpdateSensor();
    partial void OnBloodOxygenValueChanged(double value) => UpdateSensor();
    partial void OnTemperatureValueChanged(double value) => UpdateSensor();
    partial void OnStressLevelValueChanged(double value) => UpdateSensor();
    partial void OnIsFallDetectedChanged(bool value) => UpdateSensor();
    partial void OnHasEcgAnomalyChanged(bool value) => UpdateSensor();
}
