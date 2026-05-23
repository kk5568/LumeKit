using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.Contracts.ViewModels;
using BluetoothAudioReceiver.Core.Contracts.Services;
using BluetoothAudioReceiver.Core.Models;
using BluetoothAudioReceiver.Models.Application;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Serilog;
using Windows.Devices.Enumeration;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class BluetoothPageViewModel : ObservableObject, INavigationAware
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(BluetoothPageViewModel));

    private const string SavedDeviceIdsKey = "BluetoothConnectedDeviceIds";
    private const string InputHotkeyBindingsKey = "InputHotkeyBindingsV2";
    private const string OutputHotkeyBindingsKey = "OutputHotkeyBindingsV2";

    private readonly IAudioPlaybackConnectionService _connectionService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IAppSettingsService _appSettingsService;
    private readonly IAudioRouteHotkeyService _audioRouteHotkeyService;

    private readonly Dictionary<string, DeviceChoice> _inputChoiceMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeviceChoice> _outputChoiceMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeviceChoice> _unifiedChoiceMap = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    public partial bool AutoConnect { get; set; } = true;

    [ObservableProperty]
    public partial bool NotificationEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial string InputRouteStatusText { get; set; } = "Input: not connected";

    [ObservableProperty]
    public partial string OutputRouteStatusText { get; set; } = "Output: not selected";

    [ObservableProperty]
    public partial Visibility InputConnectedVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility OutputConnectedVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial string BindingStatusText { get; set; } = string.Empty;

    public ObservableCollection<HotkeyBindingCard> HotkeyCards { get; } = [];
    public ObservableCollection<HotkeyBindingCard> InputHotkeyCards { get; } = [];
    public ObservableCollection<HotkeyBindingCard> OutputHotkeyCards { get; } = [];

    public string AutoConnectText => AutoConnect ? "On" : "Off";

    public BluetoothPageViewModel(
        IAudioPlaybackConnectionService connectionService,
        ILocalSettingsService localSettingsService,
        IAppSettingsService appSettingsService,
        IAudioRouteHotkeyService audioRouteHotkeyService)
    {
        _connectionService = connectionService;
        _localSettingsService = localSettingsService;
        _appSettingsService = appSettingsService;
        _audioRouteHotkeyService = audioRouteHotkeyService;

        _connectionService.DeviceConnected += OnDeviceConnected;
        _connectionService.DeviceDisconnected += OnDeviceDisconnected;
    }

    public async void OnNavigatedTo(object parameter)
    {
        AutoConnect = _appSettingsService.AutoConnect;
        NotificationEnabled = _appSettingsService.NotificationEnabled;

        await EnsureDevicesLoadedAsync(true);
        await EnsureDevicesLoadedAsync(false);
        await LoadHotkeyCardsAsync();
        RefreshRouteStatus();
    }

    public async void OnNavigatedFrom()
    {
        var ids = _connectionService.ConnectedDevices.Select(d => d.Id).ToList();
        await _localSettingsService.SaveSettingAsync(SavedDeviceIdsKey, ids);
    }

    partial void OnAutoConnectChanged(bool value)
    {
        OnPropertyChanged(nameof(AutoConnectText));
        _ = _appSettingsService.SetAutoConnectAsync(value);
    }

    partial void OnNotificationEnabledChanged(bool value)
    {
        _ = _appSettingsService.SetNotificationEnabledAsync(value);
    }

    public static string NormalizeHotkey(string? hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return string.Empty;
        }

        var tokens = hotkey
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeHotkeyToken)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (tokens.Count == 0)
        {
            return string.Empty;
        }

        var ordered = new List<string>(5);
        AppendIfExists("Ctrl");
        AppendIfExists("Alt");
        AppendIfExists("Shift");
        AppendIfExists("Win");
        foreach (var token in tokens)
        {
            if (!IsModifierToken(token))
            {
                ordered.Add(token);
            }
        }

        return string.Join("+", ordered.Distinct(StringComparer.OrdinalIgnoreCase));

        void AppendIfExists(string value)
        {
            if (tokens.Any(t => string.Equals(t, value, StringComparison.OrdinalIgnoreCase)))
            {
                ordered.Add(value);
            }
        }
    }

    public async Task EnsureDevicesLoadedAsync(bool isInput)
    {
        try
        {
            if (isInput)
            {
                var inputDevices = await _connectionService.FindAllAudioDevicesAsync();
                _inputChoiceMap.Clear();

                foreach (var device in inputDevices)
                {
                    if (string.IsNullOrWhiteSpace(device.Id))
                    {
                        continue;
                    }

                    _inputChoiceMap[device.Id] = new DeviceChoice(
                        device.Id,
                        string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name,
                        true);
                }
            }
            else
            {
                var outputDevices = await _audioRouteHotkeyService.GetOutputDevicesAsync();
                _outputChoiceMap.Clear();
                foreach (var device in outputDevices)
                {
                    if (string.IsNullOrWhiteSpace(device.Id))
                    {
                        continue;
                    }

                    _outputChoiceMap[device.Id] = new DeviceChoice(
                        device.Id,
                        string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name,
                        false);
                }
            }

            RebuildUnifiedChoiceMap();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "EnsureDevicesLoadedAsync failed: {RouteType}", isInput ? "Input" : "Output");
        }
    }

    public IReadOnlyList<DeviceChoice> GetUnifiedDeviceChoices()
    {
        return _unifiedChoiceMap.Values
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public bool TryResolveBindingType(IReadOnlyList<string> selectedIds, out bool isInput, out string error)
    {
        isInput = true;
        error = string.Empty;

        var ids = selectedIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids.Count == 0)
        {
            error = "Please select at least one device.";
            return false;
        }

        var inputHits = ids.Count(id => _inputChoiceMap.ContainsKey(id));
        var outputHits = ids.Count(id => _outputChoiceMap.ContainsKey(id));

        if (inputHits == ids.Count && outputHits == 0)
        {
            isInput = true;
            return true;
        }

        if (outputHits == ids.Count && inputHits == 0)
        {
            isInput = false;
            return true;
        }

        error = "Input and output devices cannot be mixed in one hotkey card.";
        return false;
    }

    public async Task AddHotkeyCardAsync(bool isInput, string hotkey, IReadOnlyList<string> selectedIds)
    {
        var normalized = NormalizeHotkey(hotkey);
        var card = new HotkeyBindingCard
        {
            Id = Guid.NewGuid().ToString("N"),
            IsInputBinding = isInput,
            Hotkey = normalized
        };

        ApplySelectionToCard(card, selectedIds);
        if (isInput)
        {
            InputHotkeyCards.Add(card);
        }
        else
        {
            OutputHotkeyCards.Add(card);
        }

        RebuildHotkeyCards();
        await SaveHotkeyCardsAsync();
    }

    public async Task UpdateHotkeyCardAsync(HotkeyBindingCard card, string hotkey, IReadOnlyList<string> selectedIds)
    {
        card.Hotkey = NormalizeHotkey(hotkey);
        ApplySelectionToCard(card, selectedIds);
        await SaveHotkeyCardsAsync();
    }

    public async Task DeleteHotkeyCardAsync(HotkeyBindingCard card)
    {
        _ = InputHotkeyCards.Remove(card);
        _ = OutputHotkeyCards.Remove(card);
        RebuildHotkeyCards();
        await SaveHotkeyCardsAsync();
    }

    public void SetHotkeyCaptureSuspended(bool suspended)
    {
        _audioRouteHotkeyService.SetHotkeyCaptureSuspended(suspended);
    }

    [RelayCommand]
    private async Task DisconnectInputDeviceAsync()
    {
        try
        {
            var connected = _connectionService.ConnectedDevices.ToList();
            foreach (var device in connected)
            {
                await _connectionService.DisconnectAsync(device);
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "DisconnectInputDeviceAsync failed");
        }
        finally
        {
            RefreshRouteStatus();
        }
    }

    [RelayCommand]
    private async Task DisconnectOutputDeviceAsync()
    {
        try
        {
            await _appSettingsService.SetPreferredOutputDeviceIdAsync(string.Empty);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "DisconnectOutputDeviceAsync failed");
        }
        finally
        {
            RefreshRouteStatus();
        }
    }

    private async Task LoadHotkeyCardsAsync()
    {
        InputHotkeyCards.Clear();
        OutputHotkeyCards.Clear();

        var inputConfigs = await _localSettingsService.ReadSettingAsync(InputHotkeyBindingsKey, new List<HotkeyBindingConfig>()) ?? [];
        var outputConfigs = await _localSettingsService.ReadSettingAsync(OutputHotkeyBindingsKey, new List<HotkeyBindingConfig>()) ?? [];

        foreach (var config in inputConfigs)
        {
            var card = new HotkeyBindingCard
            {
                Id = string.IsNullOrWhiteSpace(config.Id) ? Guid.NewGuid().ToString("N") : config.Id,
                IsInputBinding = true,
                Hotkey = NormalizeHotkey(config.Hotkey)
            };
            ApplySelectionToCard(card, config.DeviceIds ?? []);
            InputHotkeyCards.Add(card);
        }

        foreach (var config in outputConfigs)
        {
            var card = new HotkeyBindingCard
            {
                Id = string.IsNullOrWhiteSpace(config.Id) ? Guid.NewGuid().ToString("N") : config.Id,
                IsInputBinding = false,
                Hotkey = NormalizeHotkey(config.Hotkey)
            };
            ApplySelectionToCard(card, config.DeviceIds ?? []);
            OutputHotkeyCards.Add(card);
        }

        RebuildHotkeyCards();
    }

    private async Task SaveHotkeyCardsAsync()
    {
        var inputConfigs = InputHotkeyCards.Select(ToConfig).ToList();
        var outputConfigs = OutputHotkeyCards.Select(ToConfig).ToList();

        await _localSettingsService.SaveSettingAsync(InputHotkeyBindingsKey, inputConfigs);
        await _localSettingsService.SaveSettingAsync(OutputHotkeyBindingsKey, outputConfigs);
    }

    private static HotkeyBindingConfig ToConfig(HotkeyBindingCard card)
    {
        return new HotkeyBindingConfig
        {
            Id = card.Id,
            Hotkey = card.Hotkey,
            DeviceIds = card.SelectedDeviceIds.ToList()
        };
    }

    private void RebuildHotkeyCards()
    {
        HotkeyCards.Clear();
        foreach (var card in InputHotkeyCards)
        {
            HotkeyCards.Add(card);
        }

        foreach (var card in OutputHotkeyCards)
        {
            HotkeyCards.Add(card);
        }
    }

    private void ApplySelectionToCard(HotkeyBindingCard card, IReadOnlyList<string> selectedIds)
    {
        var normalizedIds = selectedIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        card.SetSelectedDeviceIds(normalizedIds);

        var names = new List<string>();
        var lines = new List<DeviceLineItem>();
        foreach (var id in normalizedIds)
        {
            if (!_unifiedChoiceMap.TryGetValue(id, out var choice))
            {
                continue;
            }

            names.Add(choice.Name);
            lines.Add(new DeviceLineItem
            {
                Name = choice.Name,
                Glyph = ResolveDeviceGlyph(choice.Name)
            });
        }

        if (names.Count == 0)
        {
            names.Add("No device selected");
            lines.Add(new DeviceLineItem { Name = "No device selected", Glyph = "\uE7F5" });
        }

        card.SetDeviceNameLines(names);
        card.SetDeviceLines(lines);
        card.DeviceNamesText = string.Join(" / ", names);
    }

    private void RebuildUnifiedChoiceMap()
    {
        _unifiedChoiceMap.Clear();

        foreach (var pair in _inputChoiceMap)
        {
            _unifiedChoiceMap[pair.Key] = pair.Value;
        }

        foreach (var pair in _outputChoiceMap)
        {
            if (!_unifiedChoiceMap.ContainsKey(pair.Key))
            {
                _unifiedChoiceMap[pair.Key] = pair.Value;
            }
        }

        foreach (var card in HotkeyCards)
        {
            ApplySelectionToCard(card, card.SelectedDeviceIds);
        }
    }

    private void RefreshRouteStatus()
    {
        var connected = _connectionService.ConnectedDevices.ToList();
        if (connected.Count == 0)
        {
            InputRouteStatusText = "Input: not connected";
            InputConnectedVisibility = Visibility.Collapsed;
        }
        else
        {
            var inputText = string.Join(" + ", connected.Select(d => string.IsNullOrWhiteSpace(d.Name) ? d.Id : d.Name));
            InputRouteStatusText = $"Input: {inputText}";
            InputConnectedVisibility = Visibility.Visible;
        }

        var outputId = _appSettingsService.PreferredOutputDeviceId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(outputId))
        {
            OutputRouteStatusText = "Output: not selected";
            OutputConnectedVisibility = Visibility.Collapsed;
        }
        else
        {
            if (_outputChoiceMap.TryGetValue(outputId, out var output))
            {
                OutputRouteStatusText = $"Output: {output.Name}";
            }
            else
            {
                OutputRouteStatusText = "Output: selected";
            }

            OutputConnectedVisibility = Visibility.Visible;
        }
    }

    private void OnDeviceConnected(object? sender, DeviceInformation e)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() => RefreshRouteStatus());
    }

    private void OnDeviceDisconnected(object? sender, DeviceInformation e)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() => RefreshRouteStatus());
    }

    private static string NormalizeHotkeyToken(string token)
    {
        var t = token.Trim();
        if (string.Equals(t, "control", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "ctrl", StringComparison.OrdinalIgnoreCase))
        {
            return "Ctrl";
        }

        if (string.Equals(t, "menu", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "alt", StringComparison.OrdinalIgnoreCase))
        {
            return "Alt";
        }

        if (string.Equals(t, "shift", StringComparison.OrdinalIgnoreCase))
        {
            return "Shift";
        }

        if (string.Equals(t, "windows", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "win", StringComparison.OrdinalIgnoreCase))
        {
            return "Win";
        }

        return t.ToUpperInvariant();
    }

    private static bool IsModifierToken(string token)
    {
        return string.Equals(token, "Ctrl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "Alt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "Shift", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "Win", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDeviceGlyph(string deviceName)
    {
        var name = (deviceName ?? string.Empty).ToLowerInvariant();
        var isTablet = name.Contains("平板") || name.Contains("pad") || name.Contains("tablet");
        var isPhoneKeyword = name.Contains("手机") || name.Contains("phone") || name.Contains("iphone");
        var isPhoneBrandModel = (name.Contains("xiaomi") || name.Contains("redmi") || name.Contains("huawei") || name.Contains("honor") || name.Contains("oppo") || name.Contains("vivo"))
            && !isTablet;

        if (isPhoneKeyword || isPhoneBrandModel)
        {
            return "\uE8EA";
        }

        if (isTablet)
        {
            return "\uE70A";
        }

        if (name.Contains("headphone") || name.Contains("headset") || name.Contains("耳机"))
        {
            return "\uE7F6";
        }

        if (name.Contains("monitor") || name.Contains("display") || name.Contains("显示器"))
        {
            return "\uE7F8";
        }

        return "\uE7F5";
    }

    public sealed record DeviceChoice(string Id, string Name, bool IsInput);

    private sealed class HotkeyBindingConfig
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("hotkey")]
        public string Hotkey { get; set; } = string.Empty;

        [JsonPropertyName("deviceIds")]
        public List<string> DeviceIds { get; set; } = [];
    }
}
