using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.Contracts.ViewModels;
using BluetoothAudioReceiver.Core.Contracts.Services;
using BluetoothAudioReceiver.Core.Helpers;
using BluetoothAudioReceiver.Core.Models;
using BluetoothAudioReceiver.Models.Application;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BluetoothAudioReceiver.Services;
using Serilog;
using Windows.Devices.Enumeration;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class BluetoothPageViewModel : ObservableObject, INavigationAware
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(BluetoothPageViewModel));

    private const string InputHotkeyBindingsKey = "InputHotkeyBindingsV2";
    private const string OutputHotkeyBindingsKey = "OutputHotkeyBindingsV2";

    private readonly IAudioPlaybackConnectionService _connectionService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IAppSettingsService _appSettingsService;
    private readonly IAudioRouteHotkeyService _audioRouteHotkeyService;
    private readonly InputAudioVolumeService _inputAudioVolumeService;
    private readonly LocalSettingsKeys _localSettingsKeys;

    private readonly Dictionary<string, DeviceChoice> _inputChoiceMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeviceChoice> _outputChoiceMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeviceChoice> _unifiedChoiceMap = new(StringComparer.OrdinalIgnoreCase);
    private bool _devicesLoaded;
    private bool _hotkeyCardsLoaded;

    [ObservableProperty]
    public partial bool NotificationEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial int InputVolumeIndex { get; set; } = 3;

    [ObservableProperty]
    public partial string BindingStatusText { get; set; } = string.Empty;

    public ObservableCollection<HotkeyBindingCard> HotkeyCards { get; } = [];
    public ObservableCollection<HotkeyBindingCard> InputHotkeyCards { get; } = [];
    public ObservableCollection<HotkeyBindingCard> OutputHotkeyCards { get; } = [];

    public BluetoothPageViewModel(
        IAudioPlaybackConnectionService connectionService,
        ILocalSettingsService localSettingsService,
        IAppSettingsService appSettingsService,
        IAudioRouteHotkeyService audioRouteHotkeyService,
        InputAudioVolumeService inputAudioVolumeService,
        LocalSettingsKeys localSettingsKeys)
    {
        _connectionService = connectionService;
        _localSettingsService = localSettingsService;
        _appSettingsService = appSettingsService;
        _audioRouteHotkeyService = audioRouteHotkeyService;
        _inputAudioVolumeService = inputAudioVolumeService;
        _localSettingsKeys = localSettingsKeys;

        _connectionService.DeviceConnected += OnDeviceConnected;
    }

    public async Task OnNavigatedToAsync(object parameter)
    {
        InputVolumeIndex = _appSettingsService.InputVolumeIndex;
        NotificationEnabled = _appSettingsService.NotificationEnabled;

        await EnsureAllDevicesLoadedAsync();
        if (!_hotkeyCardsLoaded)
        {
            await LoadHotkeyCardsAsync();
            _hotkeyCardsLoaded = true;
        }
    }
    public async Task OnNavigatedFromAsync()
    {
        var ids = _connectionService.ConnectedDevices.Select(d => d.Id).ToList();
        await _localSettingsService.SaveSettingAsync(_localSettingsKeys.BluetoothConnectedDeviceIdsKey, ids);
    }

    partial void OnNotificationEnabledChanged(bool value)
    {
        _appSettingsService.SetNotificationEnabledAsync(value).Forget(nameof(BluetoothPageViewModel));
    }

    partial void OnInputVolumeIndexChanged(int value)
    {
        _appSettingsService.SetInputVolumeIndexAsync(value).Forget(nameof(BluetoothPageViewModel));
        _inputAudioVolumeService.ApplyVolumeAsync(value).Forget(nameof(BluetoothPageViewModel));
    }

    public static string NormalizeHotkey(string? hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return string.Empty;
        }

        var tokens = hotkey
            .Replace("\\u002B", "+", StringComparison.OrdinalIgnoreCase)
            .Replace("\\uFF0B", "+", StringComparison.OrdinalIgnoreCase)
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
        await EnsureAllDevicesLoadedAsync(forceReload: true);
    }

    public async Task EnsureAllDevicesLoadedAsync(bool forceReload = false)
    {
        try
        {
            if (_devicesLoaded && !forceReload)
            {
                return;
            }

            var inputTask = _connectionService.FindAllInputRouteDevicesAsync();
            var outputTask = _audioRouteHotkeyService.GetOutputDevicesAsync();
            var retainedOutputTask = LoadRetainedOutputDevicesAsync();
            await Task.WhenAll(inputTask, outputTask);

            _inputChoiceMap.Clear();
            foreach (var device in inputTask.Result)
            {
                if (string.IsNullOrWhiteSpace(device.Id))
                {
                    continue;
                }

                var physicalId = DeviceIdentityHelper.GetPhysicalDeviceId(device);
                var choice = new DeviceChoice(
                    device.Id,
                    string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name,
                    true,
                    true,
                    physicalId);
                AddInputChoice(
                    choice,
                    device.Id,
                    physicalId);
            }

            await LoadRetainedInputDevicesAsync();

            _outputChoiceMap.Clear();
            foreach (var device in outputTask.Result)
            {
                if (string.IsNullOrWhiteSpace(device.Id))
                {
                    continue;
                }

                var physicalId = string.IsNullOrWhiteSpace(device.PhysicalId) ? device.Id : device.PhysicalId;
                var choice = new DeviceChoice(
                    device.Id,
                    string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name,
                    false,
                    true,
                    physicalId);
                AddOutputChoice(choice, device.EndpointId, physicalId);
            }

            await retainedOutputTask;

            RebuildUnifiedChoiceMap();
            _devicesLoaded = true;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "EnsureAllDevicesLoadedAsync failed");
        }
    }
    public IReadOnlyList<DeviceChoice> GetUnifiedDeviceChoices()
    {
        var connectedIds = new HashSet<string>(
            _connectionService.ConnectedDevices
                .Select(d => d.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);
        var recentIds = DevicePriorityHelper.GetRecentDeviceIds(_localSettingsService);
        var preferredOutputId = _appSettingsService.PreferredOutputDeviceId ?? string.Empty;
        var choices = _unifiedChoiceMap.Values
            .DistinctBy(choice => choice.Id)
            .ToList();

        var ordered = DevicePriorityHelper.OrderDevices(
            choices,
            choice => choice.Id,
            choice => choice.Name,
            recentIds,
            choice => choice.IsInput
                ? connectedIds.Contains(choice.Id)
                : string.Equals(choice.Id, preferredOutputId, StringComparison.OrdinalIgnoreCase));

        return ordered;
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

        card.PropertyChanged += OnHotkeyCardPropertyChanged;
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
        card.PropertyChanged -= OnHotkeyCardPropertyChanged;
        _ = InputHotkeyCards.Remove(card);
        _ = OutputHotkeyCards.Remove(card);
        RebuildHotkeyCards();
        await SaveHotkeyCardsAsync();
    }

    public void SetHotkeyCaptureSuspended(bool suspended)
    {
        _audioRouteHotkeyService.SetHotkeyCaptureSuspended(suspended);
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
                Hotkey = NormalizeHotkey(config.Hotkey),
                IsEnabled = config.IsEnabled
            };
            card.PropertyChanged += OnHotkeyCardPropertyChanged;
            ApplySelectionToCard(card, config.DeviceIds ?? [], config.DeviceNames ?? []);
            InputHotkeyCards.Add(card);
        }

        foreach (var config in outputConfigs)
        {
            var card = new HotkeyBindingCard
            {
                Id = string.IsNullOrWhiteSpace(config.Id) ? Guid.NewGuid().ToString("N") : config.Id,
                IsInputBinding = false,
                Hotkey = NormalizeHotkey(config.Hotkey),
                IsEnabled = config.IsEnabled
            };
            card.PropertyChanged += OnHotkeyCardPropertyChanged;
            ApplySelectionToCard(card, config.DeviceIds ?? [], config.DeviceNames ?? []);
            OutputHotkeyCards.Add(card);
        }

        RebuildHotkeyCards();
    }

    private async void OnHotkeyCardPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HotkeyBindingCard.IsEnabled))
        {
            await SaveHotkeyCardsAsync();
        }
    }

    private async Task SaveHotkeyCardsAsync()
    {
        var inputConfigs = InputHotkeyCards.Select(ToConfig).ToList();
        var outputConfigs = OutputHotkeyCards.Select(ToConfig).ToList();

        await _localSettingsService.SaveSettingAsync(InputHotkeyBindingsKey, inputConfigs);
        await _localSettingsService.SaveSettingAsync(OutputHotkeyBindingsKey, outputConfigs);
        _audioRouteHotkeyService.RefreshUserHotkeys();
    }

    private HotkeyBindingConfig ToConfig(HotkeyBindingCard card)
    {
        return new HotkeyBindingConfig
        {
            Id = card.Id,
            Hotkey = card.Hotkey,
            IsEnabled = card.IsEnabled,
            DeviceIds = card.SelectedDeviceIds.ToList(),
            DevicePhysicalIds = card.SelectedDeviceIds
                .Select(id => _unifiedChoiceMap.TryGetValue(id, out var choice) ? choice.PhysicalId : id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            DeviceNames = card.SelectedDeviceDisplayNames.ToList()
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

    private void ApplySelectionToCard(HotkeyBindingCard card, IReadOnlyList<string> selectedIds, IReadOnlyList<string>? fallbackNames = null)
    {
        var normalizedIds = selectedIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        card.SetSelectedDeviceIds(normalizedIds);

        var names = new List<string>();
        var lines = new List<DeviceLineItem>();
        for (var i = 0; i < normalizedIds.Count; i++)
        {
            var id = normalizedIds[i];
            var fallbackName = fallbackNames != null && i < fallbackNames.Count ? fallbackNames[i] : string.Empty;
            if (!_unifiedChoiceMap.TryGetValue(id, out var choice))
            {
                choice = FindChoiceByPhysicalId(id);
            }

            if (choice == null)
            {
                var name = string.IsNullOrWhiteSpace(fallbackName) ? id : fallbackName;
                names.Add(name);
                lines.Add(new DeviceLineItem
                {
                    Name = name,
                    Glyph = DeviceVisualHelper.ResolveGlyph(name),
                    IsAvailable = false
                });
                continue;
            }

            names.Add(choice.Name);
            lines.Add(new DeviceLineItem
            {
                Name = choice.Name,
                Glyph = DeviceVisualHelper.ResolveGlyph(choice.Name),
                IsAvailable = choice.IsAvailable
            });
        }

        if (names.Count == 0)
        {
            names.Add("No device selected");
            lines.Add(new DeviceLineItem { Name = "No device selected", Glyph = DeviceVisualHelper.SpeakerGlyph, IsAvailable = true });
        }

        card.SetDeviceNameLines(names);
        card.SetDeviceLines(lines);
        card.SetSelectedDeviceDisplayNames(names);
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

    private void AddInputChoice(DeviceChoice choice, string? deviceId, string? physicalId)
    {
        if (string.IsNullOrWhiteSpace(choice.Id))
        {
            return;
        }

        _inputChoiceMap[choice.Id] = choice;

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            _inputChoiceMap[deviceId] = choice;
        }

        if (!string.IsNullOrWhiteSpace(physicalId))
        {
            _inputChoiceMap[physicalId] = choice;

            var selectionId = DeviceIdentityHelper.BuildSelectionId(physicalId, isInput: true);
            if (!string.IsNullOrWhiteSpace(selectionId))
            {
                _inputChoiceMap[selectionId] = choice;
            }
        }
    }

    private void AddOutputChoice(DeviceChoice choice, string? endpointId, string? physicalId)
    {
        if (string.IsNullOrWhiteSpace(choice.Id))
        {
            return;
        }

        _outputChoiceMap[choice.Id] = choice;

        if (!string.IsNullOrWhiteSpace(endpointId))
        {
            _outputChoiceMap[endpointId] = choice;
        }

        if (string.IsNullOrWhiteSpace(physicalId))
        {
            return;
        }

        _outputChoiceMap[physicalId] = choice;

        var selectionId = DeviceIdentityHelper.BuildSelectionId(physicalId, isInput: false);
        if (!string.IsNullOrWhiteSpace(selectionId))
        {
            _outputChoiceMap[selectionId] = choice;
        }
    }

    private async Task LoadRetainedInputDevicesAsync()
    {
        try
        {
            var inputConfigs = await _localSettingsService.ReadSettingAsync(InputHotkeyBindingsKey, new List<HotkeyBindingConfig>()) ?? [];
            var connectedDeviceIds = await _localSettingsService.ReadSettingAsync(_localSettingsKeys.BluetoothConnectedDeviceIdsKey, new List<string>()) ?? [];
            var retainedIds = inputConfigs
                .SelectMany(config => (config.DeviceIds ?? []).Concat(config.DevicePhysicalIds ?? []))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            retainedIds.AddRange(
                connectedDeviceIds.Where(id => !string.IsNullOrWhiteSpace(id)));

            var preferredInputId = _appSettingsService.PreferredInputDeviceId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(preferredInputId))
            {
                retainedIds.Add(preferredInputId);
            }

            foreach (var retainedId in retainedIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (_inputChoiceMap.ContainsKey(retainedId))
                {
                    continue;
                }

                try
                {
                    var candidateIds = new List<string> { retainedId };
                    if (DeviceIdentityHelper.TryParseSelectionId(retainedId, out _, out var physicalDeviceId) &&
                        !string.IsNullOrWhiteSpace(physicalDeviceId))
                    {
                        candidateIds.Add(physicalDeviceId);
                    }

                    DeviceInformation? device = null;
                    foreach (var candidateId in candidateIds.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        device = await DeviceInformation.CreateFromIdAsync(candidateId);
                        if (device is not null && !string.IsNullOrWhiteSpace(device.Id))
                        {
                            break;
                        }
                    }

                    var displayName = device == null || string.IsNullOrWhiteSpace(device.Id)
                        ? retainedId
                        : string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name;
                    var physicalId = device == null
                        ? (DeviceIdentityHelper.TryParseSelectionId(retainedId, out _, out var retainedPhysicalId) ? retainedPhysicalId : retainedId)
                        : DeviceIdentityHelper.GetPhysicalDeviceId(device);

                    AddInputChoice(
                        new DeviceChoice(retainedId, displayName, true, false, physicalId),
                        device?.Id ?? retainedId,
                        physicalId);
                }
                catch
                {
                    // ignore unresolved retained entries
                }
            }
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "LoadRetainedInputDevicesAsync failed");
        }
    }

    private async Task LoadRetainedOutputDevicesAsync()
    {
        try
        {
            var outputConfigs = await _localSettingsService.ReadSettingAsync(OutputHotkeyBindingsKey, new List<HotkeyBindingConfig>()) ?? [];
            var connectedDeviceIds = await _localSettingsService.ReadSettingAsync(_localSettingsKeys.BluetoothConnectedDeviceIdsKey, new List<string>()) ?? [];
            var retainedIds = outputConfigs
                .SelectMany(config => (config.DeviceIds ?? []).Concat(config.DevicePhysicalIds ?? []))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            retainedIds.AddRange(
                connectedDeviceIds.Where(id => !string.IsNullOrWhiteSpace(id)));

            var preferredOutputId = _appSettingsService.PreferredOutputDeviceId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(preferredOutputId))
            {
                retainedIds.Add(preferredOutputId);
            }

            foreach (var retainedId in retainedIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (_outputChoiceMap.ContainsKey(retainedId))
                {
                    continue;
                }

                try
                {
                    var candidateIds = new List<string> { retainedId };
                    if (DeviceIdentityHelper.TryParseSelectionId(retainedId, out _, out var physicalDeviceId) &&
                        !string.IsNullOrWhiteSpace(physicalDeviceId))
                    {
                        candidateIds.Add(physicalDeviceId);
                    }

                    DeviceInformation? device = null;
                    foreach (var candidateId in candidateIds.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        device = await DeviceInformation.CreateFromIdAsync(candidateId);
                        if (device is not null && !string.IsNullOrWhiteSpace(device.Id))
                        {
                            break;
                        }
                    }

                    var displayName = device == null || string.IsNullOrWhiteSpace(device.Id)
                        ? retainedId
                        : string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name;
                    var physicalId = device == null
                        ? (DeviceIdentityHelper.TryParseSelectionId(retainedId, out _, out var retainedPhysicalId) ? retainedPhysicalId : retainedId)
                        : DeviceIdentityHelper.GetPhysicalDeviceId(device);
                    AddOutputChoice(
                        new DeviceChoice(retainedId, displayName, false, false, physicalId),
                        device?.Id ?? retainedId,
                        physicalId);
                }
                catch
                {
                    // ignore unresolved retained entries
                }
            }
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "LoadRetainedOutputDevicesAsync failed");
        }
    }

    private async void OnDeviceConnected(object? sender, DeviceInformation e)
    {
        await DevicePriorityHelper.MarkDeviceAsUsedAsync(_localSettingsService, e.Id);
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

    private static string GetRuntimeDeviceId(string selectionId)
    {
        return DeviceIdentityHelper.TryParseSelectionId(selectionId, out _, out var physicalDeviceId)
            ? physicalDeviceId
            : selectionId;
    }

    private static string NormalizeSelectionId(string deviceId, bool isInput)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return string.Empty;
        }

        if (DeviceIdentityHelper.TryParseSelectionId(deviceId, out _, out var physicalDeviceId))
        {
            return DeviceIdentityHelper.BuildSelectionId(physicalDeviceId, isInput);
        }

        return DeviceIdentityHelper.BuildSelectionId(deviceId, isInput);
    }

    private DeviceChoice? FindChoiceByPhysicalId(string id)
    {
        var physicalId = DeviceIdentityHelper.TryParseSelectionId(id, out _, out var parsedPhysicalId)
            ? parsedPhysicalId
            : id;

        return _unifiedChoiceMap.Values.FirstOrDefault(choice =>
            string.Equals(choice.PhysicalId, physicalId, StringComparison.OrdinalIgnoreCase));
    }

    public sealed record DeviceChoice(string Id, string Name, bool IsInput, bool IsAvailable, string PhysicalId);

    private sealed class HotkeyBindingConfig
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("hotkey")]
        public string Hotkey { get; set; } = string.Empty;

        [JsonPropertyName("isEnabled")]
        public bool IsEnabled { get; set; } = true;

        [JsonPropertyName("deviceIds")]
        public List<string> DeviceIds { get; set; } = [];

        [JsonPropertyName("devicePhysicalIds")]
        public List<string> DevicePhysicalIds { get; set; } = [];

        [JsonPropertyName("deviceNames")]
        public List<string> DeviceNames { get; set; } = [];
    }
}






