using TheWatch.Maui.Services;

namespace TheWatch.Maui.Views;

public partial class BlazorContainerPage : ContentPage
{
    private readonly ILifeSafetyService _lifeSafetyService;

    public BlazorContainerPage(ILifeSafetyService lifeSafetyService)
    {
        InitializeComponent();
        _lifeSafetyService = lifeSafetyService;
    }

    private void OnSosClicked(object sender, EventArgs e)
    {
        _lifeSafetyService.TriggerSos();
        DisplayAlert("SOS Triggered", "Emergency responders have been notified via the native shell.", "OK");
    }
}
