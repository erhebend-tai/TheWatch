using TheWatch.Maui.ViewModels;

namespace TheWatch.Maui.Views;

public partial class EventLogPage : ContentPage
{
    public EventLogPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
