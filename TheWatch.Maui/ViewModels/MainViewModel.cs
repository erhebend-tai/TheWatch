using System.Collections.ObjectModel;
using TheWatch.Maui.Services;
using TheWatch.Shared.Dtos;

namespace TheWatch.Maui.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IDashboardRelay _dashboardRelay;
    private readonly ILogger<MainViewModel> _logger;

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial string ConnectionStatus { get; set; }

    [ObservableProperty]
    public partial int TotalEventsPublished { get; set; }

    [ObservableProperty]
    public partial int TotalEventsReceived { get; set; }

    [ObservableProperty]
    public partial string LastEventTime { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public ObservableCollection<SimulationEventDto> RecentEvents { get; } = new();

    public MainViewModel(IDashboardRelay dashboardRelay, ILogger<MainViewModel> logger)
    {
        _dashboardRelay = dashboardRelay;
        _logger = logger;

        IsConnected = false;
        ConnectionStatus = "Disconnected";
        TotalEventsPublished = 0;
        TotalEventsReceived = 0;
        LastEventTime = "Never";
        IsLoading = false;

        _dashboardRelay.ConnectionStatusChanged += (s, status) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ConnectionStatus = status;
                IsConnected = status == "Connected";
            });
        };

        _dashboardRelay.EventReceived += (s, eventDto) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                RecentEvents.Insert(0, eventDto);
                if (RecentEvents.Count > 20)
                {
                    RecentEvents.RemoveAt(RecentEvents.Count - 1);
                }
                TotalEventsReceived++;
                LastEventTime = DateTime.Now.ToString("HH:mm:ss");
            });
        };
    }

    [RelayCommand]
    public async Task ConnectAsync()
    {
        try
        {
            IsLoading = true;
            var apiUrl = DeviceInfo.Platform == DevicePlatform.WinUI
                ? "https://localhost:5001"
                : "http://10.0.2.2:5000";

            var result = await _dashboardRelay.ConnectAsync(apiUrl);
            if (result)
            {
                IsConnected = true;
                ConnectionStatus = "Connected";
            }
            else
            {
                ConnectionStatus = "Connection failed";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to dashboard");
            ConnectionStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task DisconnectAsync()
    {
        try
        {
            IsLoading = true;
            await _dashboardRelay.DisconnectAsync();
            IsConnected = false;
            ConnectionStatus = "Disconnected";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting from dashboard");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ClearEventsAsync()
    {
        RecentEvents.Clear();
        TotalEventsPublished = 0;
        TotalEventsReceived = 0;
        LastEventTime = "Never";
    }

    public void IncrementPublished()
    {
        TotalEventsPublished++;
        LastEventTime = DateTime.Now.ToString("HH:mm:ss");
    }
}
