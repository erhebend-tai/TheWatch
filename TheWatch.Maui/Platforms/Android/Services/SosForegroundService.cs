using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using System.Text.Json;
using TheWatch.Maui.Services;
using TheWatch.Shared.Models.Sync;

namespace TheWatch.Maui.Platforms.Android.Services;

[Service]
public class SosForegroundService : Service
{
    public const string ActionStart = "START_SOS_SERVICE";
    public const string ActionStop = "STOP_SOS_SERVICE";
    private const string ChannelId = "TheWatchSosChannel";
    private const int NotificationId = 101;

    private ILocationService? _locationService;
    private SyncTaskStore? _syncTaskStore;

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        // Resolve services from the DI container
        _locationService = MauiApplication.Current.Services.GetService<ILocationService>();
        _syncTaskStore = MauiApplication.Current.Services.GetService<SyncTaskStore>();

        if (_locationService != null)
        {
            _locationService.LocationChanged += OnLocationChanged;
        }
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ActionStart)
        {
            CreateNotificationChannel();
            var notification = new NotificationCompat.Builder(this, ChannelId)
                .SetContentTitle("TheWatch SOS Active")
                .SetContentText("Your location is being shared with emergency responders.")
                .SetSmallIcon(Resource.Mipmap.appicon) // Ensure you have this resource
                .SetOngoing(true)
                .Build();

            StartForeground(NotificationId, notification);
            _locationService?.StartLocationUpdatesAsync();

        }
        else if (intent?.Action == ActionStop)
        {
            _locationService?.StopLocationUpdates();
            StopForeground(true);
            StopSelfResult(startId);
        }

        return StartCommandResult.Sticky;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        // We have a new location. Enqueue it for synchronization.
        System.Diagnostics.Debug.WriteLine($"[NATIVE-LOCATION-BG] New location: {e.Location.Latitude}, {e.Location.Longitude}");

        if (_syncTaskStore != null)
        {
            var syncTask = new SyncTask
            {
                DataType = "GeoLocation",
                Payload = JsonSerializer.Serialize(e.Location)
            };
            _syncTaskStore.AddTaskAsync(syncTask);
        }
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;

        var channel = new NotificationChannel(ChannelId, "TheWatch SOS Service", NotificationImportance.High)
        {
            Description = "Persistent notification for active SOS alerts."
        };

        var manager = (NotificationManager)GetSystemService(NotificationService);
        manager.CreateNotificationChannel(channel);
    }

    public override void OnDestroy()
    {
        if (_locationService != null)
        {
            _locationService.LocationChanged -= OnLocationChanged;
            _locationService.StopLocationUpdates();
        }
        base.OnDestroy();
    }
}
