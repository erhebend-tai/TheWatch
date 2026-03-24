using TheWatch.Maui.ViewModels;

namespace TheWatch.Maui.Views;

public partial class AlertSimulatorPage : ContentPage
{
    public AlertSimulatorPage(AlertSimulatorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
