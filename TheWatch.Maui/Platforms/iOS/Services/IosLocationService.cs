using CoreLocation;
using System.Text.Json;
using TheWatch.Maui.Services;
using TheWatch.Models.Geo;
using TheWatch.Shared.Models.Sync;

namespace TheWatch.Maui.Platforms.iOS.Services;

public class IosLocationService : ILocationService
{
    private readonly CLLocationManager _locationManager;
    private readonly SyncTaskStore? _syncTaskStore;

    public event EventHandler<LocationChangedEventArgs>? LocationChanged;

    public IosLocationService()
    {
        _locationManager = new CLLocationManager
        {
            DesiredAccuracy = CLLocation.AccuracyBest,
            PausesLocationUpdatesAutomatically = false, // Critical for continuous tracking
            AllowsBackgroundLocationUpdates = true,    // The key for background operation
        };

        _syncTaskStore = MauiApplication.Current.Services.GetService<SyncTaskStore>();

        _locationManager.LocationsUpdated += (sender, e) =>
        {
            var location = e.Locations.LastOrDefault();
            if (location != null)
            {
                var geoCoords = new GeoCoordinates(location.Coordinate.Latitude, location.Coordinate.Longitude);
                LocationChanged?.Invoke(this, new LocationChangedEventArgs(geoCoords));

                if (_syncTaskStore != null)
                {
                    var syncTask = new SyncTask
                    {
                        DataType = "GeoLocation",
                        Payload = JsonSerializer.Serialize(geoCoords)
                    };
                    _syncTaskStore.AddTaskAsync(syncTask);
                    System.Diagnostics.Debug.WriteLine($"[NATIVE-LOCATION-BG] Enqueued location: {geoCoords.Latitude}, {geoCoords.Longitude}");
                }
            }
        };
    }

    public async Task<GeoCoordinates?> GetLastKnownLocationAsync()
    {
        // This is not the primary way we'll get location on iOS for SOS,
        // but it's useful for one-off requests.
        var status = await CheckAndRequestPermission();
        if (status != CLAuthorizationStatus.AuthorizedAlways)
        {
            return null;
        }

        var location = _locationManager.Location;
        return location != null ? new GeoCoordinates(location.Coordinate.Latitude, location.Coordinate.Longitude) : null;
    }

    public async Task StartLocationUpdatesAsync()
    {
        var status = await CheckAndRequestPermission();
        if (status == CLAuthorizationStatus.AuthorizedAlways)
        {
            _locationManager.StartUpdatingLocation();
            System.Diagnostics.Debug.WriteLine("[NATIVE-LOCATION] iOS location updates started.");
        }
    }

    public void StopLocationUpdates()
    {
        _locationManager.StopUpdatingLocation();
        System.Diagnostics.Debug.WriteLine("[NATIVE-LOCATION] iOS location updates stopped.");
    }

    private async Task<CLAuthorizationStatus> CheckAndRequestPermission()
    {
        var status = _locationManager.AuthorizationStatus;
        if (status == CLAuthorizationStatus.NotDetermined)
        {
            // Request Always authorization
            _locationManager.RequestAlwaysAuthorization();
            
            // We need to wait for the user's response. This is a simplified
            // version. A real app would use a more robust mechanism to wait.
            await Task.Delay(1000); 
        }
        
        return _locationManager.AuthorizationStatus;
    }
}
