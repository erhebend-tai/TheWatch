using CommunityToolkit.Maui;
using TheWatch.Maui.Services;
using TheWatch.Maui.ViewModels;
using TheWatch.Maui.Views;
using TheWatch.Models.Geo;

#if ANDROID
using TheWatch.Maui.Platforms.Android.Services;
#elif IOS
using TheWatch.Maui.Platforms.iOS.Services;
#endif

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

        // ── Blazor WebView Integration ──────────────────────────────
        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        // ── Platform Specific Services ──────────────────────────────
#if ANDROID
        builder.Services.AddSingleton<ILifeSafetyService, AndroidLifeSafetyService>();
        builder.Services.AddSingleton<ILocationService, AndroidLocationService>();
        builder.Services.AddSingleton<IPhraseRecognitionService, AndroidPhraseRecognitionService>();
#elif IOS
        builder.Services.AddSingleton<ILifeSafetyService, IosLifeSafetyService>();
        builder.Services.AddSingleton<ILocationService, IosLocationService>();
        builder.Services.AddSingleton<IPhraseRecognitionService, IosPhraseRecognitionService>();
#else
        // Mock for other platforms
        builder.Services.AddSingleton<ILifeSafetyService, MockLifeSafetyService>();
        builder.Services.AddSingleton<ILocationService, MockLocationService>();
        builder.Services.AddSingleton<IPhraseRecognitionService, MockPhraseRecognitionService>();
#endif

        // ── Shared Services ──────────────────────────────────────────
        builder.Services.AddSingleton<IDashboardRelay, DashboardRelay>();
        builder.Services.AddSingleton<SyncTaskStore>();
        builder.Services.AddSingleton<ISyncEngine, SyncEngine>();


        // ── ViewModels ───────────────────────────────────────────────
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<AlertSimulatorViewModel>();
        builder.Services.AddSingleton<SensorSimulatorViewModel>();
        builder.Services.AddSingleton<DeviceSimulatorViewModel>();

        // ── Views ───────────────────────────────────────────────────
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<AlertSimulatorPage>();
        builder.Services.AddSingleton<SensorSimulatorPage>();
        builder.Services.AddSingleton<DeviceSimulatorPage>();
        builder.Services.AddSingleton<EventLogPage>();

        var app = builder.Build();

        // Start the sync engine
        var syncEngine = app.Services.GetRequiredService<ISyncEngine>();
        syncEngine.Start();

        return app;
    }
}

// ── Mock Services for Desktop ────────────────────────────────

public class MockLifeSafetyService : ILifeSafetyService
{
    public bool IsSosActive => false;
    public void TriggerSos() { System.Diagnostics.Debug.WriteLine("[MOCK-SOS] Triggered"); }
    public void CancelSos() { System.Diagnostics.Debug.WriteLine("[MOCK-SOS] Canceled"); }
}

public class MockLocationService : ILocationService
{
    public event EventHandler<LocationChangedEventArgs> LocationChanged;

    public Task<GeoCoordinates?> GetLastKnownLocationAsync()
    {
        return Task.FromResult<GeoCoordinates?>(new GeoCoordinates(40.7128, -74.0060)); // Default to NYC
    }

    public Task StartLocationUpdatesAsync()
    {
        System.Diagnostics.Debug.WriteLine("[MOCK-LOCATION] Start updates");
        return Task.CompletedTask;
    }

    public void StopLocationUpdates()
    {
        System.Diagnostics.Debug.WriteLine("[MOCK-LOCATION] Stop updates");
    }
}

public class MockPhraseRecognitionService : IPhraseRecognitionService
{
    public event EventHandler<PhraseRecognizedEventArgs> PhraseRecognized;

    public Task<bool> RequestPermission() => Task.FromResult(true);
    public void StartListening(string[] phrases) => System.Diagnostics.Debug.WriteLine("[MOCK-PHRASE] Start listening");
    public void StopListening() => System.Diagnostics.Debug.WriteLine("[MOCK-PHRASE] Stop listening");
}
