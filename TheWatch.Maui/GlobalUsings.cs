// WAL: Global using directives for CommunityToolkit.Mvvm source generators.
// Without these, ObservableObject, [ObservableProperty], and [RelayCommand]
// attributes will not resolve at compile time.
//
// Example — ViewModel using these:
//   public partial class MyViewModel : ObservableObject
//   {
//       [ObservableProperty]
//       public partial string Name { get; set; }
//
//       [RelayCommand]
//       public async Task DoWorkAsync() { ... }
//   }

global using CommunityToolkit.Mvvm.ComponentModel;
global using CommunityToolkit.Mvvm.Input;
global using Microsoft.Extensions.Logging;
