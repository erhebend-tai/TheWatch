using TheWatch.Maui.ViewModels;

namespace TheWatch.Maui.Views;

public partial class AdapterTierPage : ContentPage
{
    public AdapterTierPage(AdapterTierViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is AdapterTierViewModel vm)
        {
            await vm.LoadAsync();
        }
    }
}
