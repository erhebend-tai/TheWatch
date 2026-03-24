using TheWatch.Maui.ViewModels;

namespace TheWatch.Maui.Views;

public partial class DeviceSimulatorPage : ContentPage
{
    public DeviceSimulatorPage(DeviceSimulatorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
