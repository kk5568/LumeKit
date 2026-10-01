using BluetoothAudioReceiver.ViewModels.Pages;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class ThemeManagementPage : Page
{
    public ThemeManagementPageViewModel ViewModel { get; }

    public ThemeManagementPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<ThemeManagementPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CitySourceToggle.IsOn = ViewModel.AutoDayNightUseCity;
        CustomScheduleSourceToggle.IsOn = ViewModel.AutoDayNightUseCustomNightSchedule;
    }

    private void OnCitySourceToggleToggled(object sender, RoutedEventArgs e)
    {
        if (ViewModel.AutoDayNightUseCity != CitySourceToggle.IsOn)
        {
            ViewModel.AutoDayNightUseCity = CitySourceToggle.IsOn;
        }
    }

    private void OnCustomScheduleSourceToggleToggled(object sender, RoutedEventArgs e)
    {
        if (ViewModel.AutoDayNightUseCustomNightSchedule != CustomScheduleSourceToggle.IsOn)
        {
            ViewModel.AutoDayNightUseCustomNightSchedule = CustomScheduleSourceToggle.IsOn;
        }
    }
}
