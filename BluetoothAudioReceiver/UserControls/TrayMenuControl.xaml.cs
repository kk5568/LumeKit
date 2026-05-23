using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Windows.Devices.Enumeration;
using BluetoothAudioReceiver.Views.Windows;

namespace BluetoothAudioReceiver.UserControls;

public sealed partial class TrayMenuControl : UserControl
{
    public TrayMenuControlViewModel ViewModel { get; } = new();
    private TrayQuickPanelWindow? _quickPanelWindow;

    public TrayMenuControl()
    {
        InitializeComponent();
        _quickPanelWindow = new TrayQuickPanelWindow();

        var connectionService = Ioc.Default.GetRequiredService<IAudioPlaybackConnectionService>();
        connectionService.DeviceConnected += OnDeviceConnected;
        connectionService.DeviceDisconnected += OnDeviceDisconnected;

        UpdateConnectionState(connectionService);
    }

    private void UpdateConnectionState(IAudioPlaybackConnectionService connectionService)
    {
        var connectedDevices = connectionService.ConnectedDevices;
        if (connectedDevices.Count > 0)
        {
            var deviceNames = string.Join(", ", connectedDevices.Select(d => d.Name));
            ViewModel.IsConnected = true;
            ViewModel.ConnectedDeviceName = deviceNames;
            ViewModel.TrayIconToolTip = $"蓝牙音频接收器 - 已连接: {deviceNames}";
        }
        else
        {
            ViewModel.IsConnected = false;
            ViewModel.ConnectedDeviceName = null;
            ViewModel.TrayIconToolTip = "蓝牙音频接收器 - 未连接";
        }
    }

    private void OnDeviceConnected(object? sender, DeviceInformation device)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            var connectionService = Ioc.Default.GetRequiredService<IAudioPlaybackConnectionService>();
            UpdateConnectionState(connectionService);
        });
    }

    private void OnDeviceDisconnected(object? sender, DeviceInformation device)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            var connectionService = Ioc.Default.GetRequiredService<IAudioPlaybackConnectionService>();
            UpdateConnectionState(connectionService);
        });
    }

    [RelayCommand]
    private void ShowWindow()
    {
        App.MainWindow.Show();
        App.MainWindow.BringToFront();
    }

    [RelayCommand]
    private void RestartApp()
    {
        DisposeTrayIconControl();
        App.RestartApplication();
    }

    [RelayCommand]
    private void ShowQuickPanel()
    {
        _quickPanelWindow ??= new TrayQuickPanelWindow();
        _quickPanelWindow.TogglePanel();
    }

    [RelayCommand]
    private void ExitApp()
    {
        App.MainWindow.Hide();
        DisposeTrayIconControl();
#if TRAY_ICON
        App.CanCloseWindow = true;
#endif
        App.MainWindow.Close();
    }

    private void DisposeTrayIconControl()
    {
        try
        {
            TrayIconControl.Dispose();
        }
        catch
        {
            // ignore
        }
    }
}

public partial class TrayMenuControlViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string TrayIconToolTip { get; set; } = "蓝牙音频接收器 - 未连接";

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial string? ConnectedDeviceName { get; set; }

    [RelayCommand]
    private async Task ToggleConnectionAsync()
    {
        var connectionService = Ioc.Default.GetRequiredService<IAudioPlaybackConnectionService>();

        if (IsConnected)
        {
            var devices = connectionService.ConnectedDevices.ToList();
            foreach (var device in devices)
            {
                await connectionService.DisconnectAsync(device);
            }
        }
        else
        {
            var localSettingsService = Ioc.Default.GetRequiredService<ILocalSettingsService>();
            var savedIds = await localSettingsService.ReadSettingAsync<List<string>>("BluetoothConnectedDeviceIds");
            if (savedIds?.Count > 0)
            {
                await connectionService.ReconnectPreviousDevicesAsync(savedIds);
            }
        }
    }
}
