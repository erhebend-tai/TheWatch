namespace TheWatch.Maui.Services;

public interface ILifeSafetyService
{
    void TriggerSos();
    void CancelSos();
    bool IsSosActive { get; }
}

public interface INativeDeviceBridge
{
    Task<string> GetDeviceIdentifierAsync();
    Task<double> GetBatteryLevelAsync();
    void Vibrate(int milliseconds);
    Task<bool> RequestPermissionsAsync();
}
