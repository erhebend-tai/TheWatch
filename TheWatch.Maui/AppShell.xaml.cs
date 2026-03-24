using TheWatch.Maui.Views;

namespace TheWatch.Maui;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute(nameof(AlertSimulatorPage), typeof(AlertSimulatorPage));
        Routing.RegisterRoute(nameof(SensorSimulatorPage), typeof(SensorSimulatorPage));
        Routing.RegisterRoute(nameof(DeviceSimulatorPage), typeof(DeviceSimulatorPage));
        Routing.RegisterRoute(nameof(EventLogPage), typeof(EventLogPage));
    }
}
