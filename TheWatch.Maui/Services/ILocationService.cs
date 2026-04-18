using TheWatch.Models.Geo;

namespace TheWatch.Maui.Services
{
    public interface ILocationService
    {
        Task<GeoCoordinates?> GetLastKnownLocationAsync();
        Task StartLocationUpdatesAsync();
        void StopLocationUpdates();

        event EventHandler<LocationChangedEventArgs> LocationChanged;
    }

    public class LocationChangedEventArgs : EventArgs
    {
        public GeoCoordinates Location { get; }

        public LocationChangedEventArgs(GeoCoordinates location)
        {
            Location = location;
        }
    }
}
