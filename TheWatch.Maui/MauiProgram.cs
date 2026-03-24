using CommunityToolkit.Maui;
using TheWatch.Maui.Services;
using TheWatch.Maui.ViewModels;
using TheWatch.Maui.Views;

namespace TheWatch.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-SemiBold.ttf", "OpenSansSemiBold");
            });

        // Register services
        builder.Services.AddSingleton<IDashboardRelay, DashboardRelay>();

        // Register ViewModels
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<AlertSimulatorViewModel>();
        builder.Services.AddSingleton<SensorSimulatorViewModel>();
        builder.Services.AddSingleton<DeviceSimulatorViewModel>();

        // Register Views
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<AlertSimulatorPage>();
        builder.Services.AddSingleton<SensorSimulatorPage>();
        builder.Services.AddSingleton<DeviceSimulatorPage>();
        builder.Services.AddSingleton<EventLogPage>();

        return builder.Build();
    }
}
