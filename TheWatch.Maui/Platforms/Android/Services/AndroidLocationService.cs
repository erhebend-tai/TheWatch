using Android.Gms.Location;
using TheWatch.Maui.Services;
using TheWatch.Models.Geo;

namespace TheWatch.Maui.Platforms.Android.Services;

public class AndroidLocationService : ILocationService
{
    private FusedLocationProviderClient? _fusedLocationClient;
    private LocationCallback? _locationCallback;

    public event EventHandler<LocationChangedEventArgs>? LocationChanged;

    public AndroidLocationService()
    {
        // We will initialize the client and callback when updates are requested
    }

    public async Task<GeoCoordinates?> GetLastKnownLocationAsync()
    {
        var permissionStatus = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (permissionStatus != PermissionStatus.Granted)
        {
            permissionStatus = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (permissionStatus != PermissionStatus.Granted) return null;
        }

        var client = GetFusedLocationProviderClient();
        var location = await client.GetLastLocationAsync();

        return location != null ? new GeoCoordinates(location.Latitude, location.Longitude) : null;
    }

    public async Task StartLocationUpdatesAsync()
    {
        var backgroundPermission = await Permissions.CheckStatusAsync<Permissions.LocationAlways>();
        if (backgroundPermission != PermissionStatus.Granted)
        {
             backgroundPermission = await Permissions.RequestAsync<Permissions.LocationAlways>();
             if(backgroundPermission != PermissionStatus.Granted) return;
        }

        var client = GetFusedLocationProviderClient();
        
        if (_locationCallback == null)
        {
            _locationCallback = new LocationCallback((location) => {
                var geoCoords = new GeoCoordinates(location.Latitude, location.Longitude);
                LocationChanged?.Invoke(this, new LocationChangedEventArgs(geoCoords));
            });
        }

        var locationRequest = new LocationRequest.Builder(Priority.PriorityHighAccuracy, 5000) // 5 seconds
            .SetWaitForAccurateLocation(false)
            .SetMinUpdateIntervalMillis(2000) // 2 seconds
            .Build();

        await client.RequestLocationUpdatesAsync(locationRequest, _locationCallback, Looper.MainLooper);
        System.Diagnostics.Debug.WriteLine("[NATIVE-LOCATION] Android location updates started.");
    }

    public void StopLocationUpdates()
    {
        if (_fusedLocationClient != null && _locationCallback != null)
        {
            _fusedLocationClient.RemoveLocationUpdatesAsync(_locationCallback);
            System.Diagnostics.Debug.WriteLine("[NATIVE-LOCATION] Android location updates stopped.");
        }
    }
    
    private FusedLocationProviderClient GetFusedLocationProviderClient()
    {
        if (_fusedLocationClient == null)
        {
            _fusedLocationClient = LocationServices.GetFusedLocationProviderClient(MauiApplication.Current.ApplicationContext);
        }
        return _fusedLocationClient;
    }
}

public class LocationCallback : Android.Gms.Location.LocationCallback
{
    private readonly Action<Location> _callback;

    public LocationCallback(Action<Location> callback)
    {
        _callback = callback;
    }

    public override void OnLocationResult(LocationResult result)
    {
        base.OnLocationResult(result);
        var lastLocation = result.Locations.LastOrDefault();
        if (lastLocation != null)
        {
            _callback?.Invoke(lastLocation);
        }
    }
}
