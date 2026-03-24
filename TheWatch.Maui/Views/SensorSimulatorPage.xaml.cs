using TheWatch.Maui.ViewModels;

namespace TheWatch.Maui.Views;

public partial class SensorSimulatorPage : ContentPage
{
    public SensorSimulatorPage(SensorSimulatorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
