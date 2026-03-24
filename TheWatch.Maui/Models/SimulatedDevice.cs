using TheWatch.Shared.Enums;

namespace TheWatch.Maui.Models;

public class SimulatedDevice
{
    public string DeviceId { get; set; } = Guid.NewGuid().ToString();
    public SimulationEventType EventType { get; set; } = SimulationEventType.DeviceStateChange;
    public bool IsOnline { get; set; } = true;
    public double Latitude { get; set; } = 40.7128;
    public double Longitude { get; set; } = -74.0060;
    public int BatteryPercentage { get; set; } = 85;
    public bool IsJailbroken { get; set; } = false;
    public bool IsRooted { get; set; } = false;
    public string DeviceType { get; set; } = "Smartwatch";
    public string Description { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public Dictionary<string, object> ToPayload()
    {
        return new Dictionary<string, object>
        {
            { "deviceId", DeviceId },
            { "isOnline", IsOnline },
            { "latitude", Latitude },
            { "longitude", Longitude },
            { "batteryPercentage", BatteryPercentage },
            { "isJailbroken", IsJailbroken },
            { "isRooted", IsRooted },
            { "deviceType", DeviceType },
            { "description", Description },
            { "timestamp", UpdatedAt }
        };
    }
}
