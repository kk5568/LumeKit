using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using BluetoothAudioReceiver.Contracts.Services;
using Microsoft.UI.Xaml;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class HotkeyNotificationPageViewModel : ObservableRecipient, INavigationAware
{
    private const string CardsKey = "HotkeyNotification.CardsV1";
    private const string LegacyHotkeyKey = "HotkeyNotification.Key";
    private const string LegacyStatusKey = "HotkeyNotification.Status";

    private readonly ILocalSettingsService _localSettingsService;
    private readonly IAppSettingsService _appSettingsService;
    private readonly IAudioRouteHotkeyService _audioRouteHotkeyService;
    private readonly INotificationService _notificationService;
    private bool _cardsLoaded;
    private string _cardsSignature = string.Empty;

    [ObservableProperty]
    public partial Visibility EmptyStateVisibility { get; set; } = Visibility.Visible;

    public ObservableCollection<HotkeyNotificationCard> HotkeyCards { get; } = [];

    public HotkeyNotificationPageViewModel(
        ILocalSettingsService localSettingsService,
        IAppSettingsService appSettingsService,
        IAudioRouteHotkeyService audioRouteHotkeyService,
        INotificationService notificationService)
    {
        _localSettingsService = localSettingsService;
        _appSettingsService = appSettingsService;
        _audioRouteHotkeyService = audioRouteHotkeyService;
        _notificationService = notificationService;
        HotkeyCards.CollectionChanged += (_, _) => UpdatePageState();
    }

    public async Task OnNavigatedToAsync(object parameter)
    {
        if (!_cardsLoaded)
        {
            await LoadCardsAsync();
        }
        _audioRouteHotkeyService.RefreshNotificationHotkeys();
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    public void SetHotkeyCaptureSuspended(bool suspended)
    {
        _audioRouteHotkeyService.SetHotkeyCaptureSuspended(suspended);
    }

    public (bool IsValid, string ErrorMessage) ValidateHotkeyCandidate(string hotkey, string? ignoreCardId = null)
    {
        var normalized = BluetoothPageViewModel.NormalizeHotkey(hotkey);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return (false, "按键不能为空");
        }

        if (HotkeyCards.Any(card =>
                !string.Equals(card.Id, ignoreCardId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(BluetoothPageViewModel.NormalizeHotkey(card.Hotkey), normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return (false, "当前页面已存在相同快捷键");
        }

        if (string.Equals(BluetoothPageViewModel.NormalizeHotkey(_appSettingsService.InputHotkey), normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(BluetoothPageViewModel.NormalizeHotkey(_appSettingsService.OutputHotkey), normalized, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "该快捷键已被音频管理页占用");
        }

        return (true, string.Empty);
    }

    public async Task LoadCardsAsync()
    {
        if (_cardsLoaded)
        {
            return;
        }

        HotkeyCards.Clear();

        var configs = await _localSettingsService.ReadSettingAsync(CardsKey, new List<HotkeyNotificationCardConfig>()) ?? [];
        if (configs.Count == 0)
        {
            configs.Add(new HotkeyNotificationCardConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Hotkey = NormalizeOrDefault(_localSettingsService.ReadSetting<string>(LegacyHotkeyKey), HotkeyNotificationCard.DefaultHotkey),
                StatusText = NormalizeOrDefault(_localSettingsService.ReadSetting<string>(LegacyStatusKey), HotkeyNotificationCard.DefaultStatus)
            });
        }

        foreach (var config in configs)
        {
            HotkeyCards.Add(new HotkeyNotificationCard
            {
                Id = string.IsNullOrWhiteSpace(config.Id) ? Guid.NewGuid().ToString("N") : config.Id,
                Hotkey = NormalizeOrDefault(config.Hotkey, HotkeyNotificationCard.DefaultHotkey),
                StatusText = NormalizeOrDefault(config.StatusText, HotkeyNotificationCard.DefaultStatus),
                IsEnabled = config.IsEnabled
            });
        }

        var normalizedConfigs = HotkeyCards.Select(card => new HotkeyNotificationCardConfig
        {
            Id = card.Id,
            Hotkey = card.Hotkey,
            StatusText = card.StatusText,
            IsEnabled = card.IsEnabled
        }).ToList();
        var signature = BuildCardsSignature(normalizedConfigs);
        if (!string.Equals(signature, _cardsSignature, StringComparison.Ordinal))
        {
            _cardsSignature = signature;
            UpdateLegacyCompatibilitySettings();
            await SaveCardsAsync();
        }

        _cardsLoaded = true;
        UpdatePageState();
    }

    public async Task AddCardAsync(string hotkey, string status)
    {
        var card = new HotkeyNotificationCard
        {
            Id = Guid.NewGuid().ToString("N"),
            Hotkey = NormalizeOrDefault(hotkey, HotkeyNotificationCard.DefaultHotkey),
            StatusText = NormalizeOrDefault(status, HotkeyNotificationCard.DefaultStatus)
        };

        HotkeyCards.Add(card);
        await SaveCardsAsync();
        UpdateLegacyCompatibilitySettings();
        UpdatePageState();
        _audioRouteHotkeyService.RefreshNotificationHotkeys();
        _notificationService.ShowPreview(NotificationRequest.Hotkey(card.Hotkey, card.StatusText, bypassSuppression: true));
    }

    public async Task UpdateCardAsync(HotkeyNotificationCard card, string hotkey, string status)
    {
        card.Hotkey = NormalizeOrDefault(hotkey, HotkeyNotificationCard.DefaultHotkey);
        card.StatusText = NormalizeOrDefault(status, HotkeyNotificationCard.DefaultStatus);
        await SaveCardsAsync();
        UpdateLegacyCompatibilitySettings();
        UpdatePageState();
        _audioRouteHotkeyService.RefreshNotificationHotkeys();
        _notificationService.ShowPreview(NotificationRequest.Hotkey(card.Hotkey, card.StatusText, bypassSuppression: true));
    }

    public async Task DeleteCardAsync(HotkeyNotificationCard card)
    {
        _ = HotkeyCards.Remove(card);
        await SaveCardsAsync();
        UpdateLegacyCompatibilitySettings();
        UpdatePageState();
        _audioRouteHotkeyService.RefreshNotificationHotkeys();
    }

    private async Task SaveCardsAsync()
    {
        var configs = HotkeyCards.Select(card => new HotkeyNotificationCardConfig
        {
            Id = card.Id,
            Hotkey = card.Hotkey,
            StatusText = card.StatusText,
            IsEnabled = card.IsEnabled
        }).ToList();

        await _localSettingsService.SaveSettingAsync(CardsKey, configs);
    }

    private void UpdateLegacyCompatibilitySettings()
    {
        var first = HotkeyCards.FirstOrDefault();
        var hotkey = first?.Hotkey ?? HotkeyNotificationCard.DefaultHotkey;
        var status = first?.StatusText ?? HotkeyNotificationCard.DefaultStatus;

        _ = _localSettingsService.SaveSettingAsync(LegacyHotkeyKey, hotkey);
        _ = _localSettingsService.SaveSettingAsync(LegacyStatusKey, status);
    }

    private static string BuildCardsSignature(IEnumerable<HotkeyNotificationCardConfig> configs)
    {
        return string.Join('|', configs.Select(config =>
            $"{NormalizeOrDefault(config.Id, string.Empty)}\u001F{NormalizeOrDefault(config.Hotkey, string.Empty)}\u001F{NormalizeOrDefault(config.StatusText, string.Empty)}"));
    }

    private void UpdatePageState()
    {
        EmptyStateVisibility = HotkeyCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string NormalizeOrDefault(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim()
            .Replace("\\u002B", "+", StringComparison.OrdinalIgnoreCase)
            .Replace("\\uFF0B", "+", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class HotkeyNotificationCardConfig
    {
        public string Id { get; set; } = string.Empty;

        public string Hotkey { get; set; } = string.Empty;

        public string StatusText { get; set; } = string.Empty;

        public bool IsEnabled { get; set; } = true;
    }
}
