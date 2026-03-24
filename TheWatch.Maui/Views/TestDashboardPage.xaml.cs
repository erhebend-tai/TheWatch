using TheWatch.Maui.ViewModels;

namespace TheWatch.Maui.Views;

public partial class TestDashboardPage : ContentPage
{
    public TestDashboardPage(TestDashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is TestDashboardViewModel vm)
        {
            await vm.LoadSuitesAsync();
            await vm.LoadHistoryAsync();
        }
    }
}
