using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.Contracts.ViewModels;
using BluetoothAudioReceiver.Core.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class BackgroundEfficiencyPageViewModel : ObservableRecipient, INavigationAware, IDisposable
{
    private readonly ImageSource _throttlingIcon = new BitmapImage(new Uri("ms-appx:///Assets/InApp/CheckButton.png"));
    private readonly string _throttlingDescription = "Home_Throttling_Description".GetLocalizedString();

    private readonly ImageSource _throttlingPausedIcon = new BitmapImage(new Uri("ms-appx:///Assets/InApp/PauseButton.png"));
    private readonly string _throttlingPausedDescription = "Home_ThrottlingPaused_Description".GetLocalizedString();

    private readonly string _titleText = "BackgroundEfficiency_Title".GetLocalizedString();
    private readonly string _overviewText = "BackgroundEfficiency_OverviewText".GetLocalizedString();
    private readonly string _rulesTitleText = "BackgroundEfficiency_RulesTitle".GetLocalizedString();
    private readonly string _whitelistTitleText = "BackgroundEfficiency_WhitelistTitle".GetLocalizedString();
    private readonly string _whitelistLabelText = "BackgroundEfficiency_WhitelistLabel".GetLocalizedString();
    private readonly string _blacklistTitleText = "BackgroundEfficiency_BlacklistTitle".GetLocalizedString();
    private readonly string _blacklistLabelText = "BackgroundEfficiency_BlacklistLabel".GetLocalizedString();
    private readonly string _currentStatusLabelText = "BackgroundEfficiency_CurrentStatusLabel".GetLocalizedString();
    private readonly string _masterSwitchLabelText = "BackgroundEfficiency_MasterSwitchLabel".GetLocalizedString();
    private readonly string _onLabelText = "BackgroundEfficiency_OnLabel".GetLocalizedString();
    private readonly string _whitelistInstructionText = "BackgroundEfficiency_WhitelistInstruction".GetLocalizedString();
    private readonly string _blacklistInstructionText = "BackgroundEfficiency_BlacklistInstruction".GetLocalizedString();
    private readonly string _ruleLineOneText = "BackgroundEfficiency_RuleLineOne".GetLocalizedString();
    private readonly string _ruleLineTwoText = "BackgroundEfficiency_RuleLineTwo".GetLocalizedString();
    private readonly string _ruleLineThreeText = "BackgroundEfficiency_RuleLineThree".GetLocalizedString();
    private readonly string _ruleLineFourText = "BackgroundEfficiency_RuleLineFour".GetLocalizedString();
    private readonly string _saveButtonText = "BackgroundEfficiency_SaveButton".GetLocalizedString();
    private readonly string _restoreButtonText = "BackgroundEfficiency_RestoreButton".GetLocalizedString();

    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private readonly IBackgroundEfficiencyService _backgroundEfficiencyService;
    private readonly IAppSettingsService _settingsService;

    [ObservableProperty]
    public partial ImageSource? StatusIcon { get; set; }

    [ObservableProperty]
    public partial string? StatusDescription { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProcessWhitelistModified))]
    public partial string ProcessWhitelistString { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProcessBlacklistModified))]
    public partial string ProcessBlacklistString { get; set; } = string.Empty;

    public string WhitelistCountText { get; private set; } = string.Empty;
    public string WhitelistPreviewText { get; private set; } = string.Empty;
    public string BlacklistCountText { get; private set; } = string.Empty;
    public string BlacklistPreviewText { get; private set; } = string.Empty;

    public bool PauseThrottling
    {
        get => _backgroundEfficiencyService.PauseThrottling;
        set => SetProperty(PauseThrottling, value, x => _backgroundEfficiencyService.PauseThrottling = x);
    }

    public bool MasterSwitchEnabled
    {
        get => !PauseThrottling;
        set
        {
            if (MasterSwitchEnabled != value)
            {
                PauseThrottling = !value;
                OnPropertyChanged(nameof(MasterSwitchEnabled));
            }
        }
    }

    public bool ProcessWhitelistModified => NormalizeLineEndings(ProcessWhitelistString) != NormalizeLineEndings(_settingsService.BackgroundEfficiencyProcessWhitelist);
    public bool ProcessBlacklistModified => NormalizeLineEndings(ProcessBlacklistString) != NormalizeLineEndings(_settingsService.BackgroundEfficiencyProcessBlacklist);

    public string TitleText => _titleText;
    public string OverviewText => _overviewText;
    public string RulesTitleText => _rulesTitleText;
    public string WhitelistTitleText => _whitelistTitleText;
    public string WhitelistLabelText => _whitelistLabelText;
    public string BlacklistTitleText => _blacklistTitleText;
    public string BlacklistLabelText => _blacklistLabelText;
    public string CurrentStatusLabelText => _currentStatusLabelText;
    public string MasterSwitchLabelText => _masterSwitchLabelText;
    public string OnLabelText => _onLabelText;
    public string WhitelistInstructionText => _whitelistInstructionText;
    public string BlacklistInstructionText => _blacklistInstructionText;
    public string RuleLineOneText => _ruleLineOneText;
    public string RuleLineTwoText => _ruleLineTwoText;
    public string RuleLineThreeText => _ruleLineThreeText;
    public string RuleLineFourText => _ruleLineFourText;
    public string SaveButtonText => _saveButtonText;
    public string RestoreButtonText => _restoreButtonText;

    public BackgroundEfficiencyPageViewModel()
        : this(
            CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<IBackgroundEfficiencyService>(),
            CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<IAppSettingsService>())
    {
    }

    public BackgroundEfficiencyPageViewModel(IBackgroundEfficiencyService backgroundEfficiencyService, IAppSettingsService settingsService)
    {
        _backgroundEfficiencyService = backgroundEfficiencyService;
        _settingsService = settingsService;
        UpdateStatusOnUi(_backgroundEfficiencyService.ThrottleStatus);
        LoadCurrentSettingsText();
        UpdateListSnapshot();
        _backgroundEfficiencyService.ThrottleStatusChanged += BackgroundEfficiencyService_ThrottleStatusChanged;
    }

    private void LoadCurrentSettingsText()
    {
        ProcessWhitelistString = _settingsService.BackgroundEfficiencyProcessWhitelist;
        ProcessBlacklistString = _settingsService.BackgroundEfficiencyProcessBlacklist;
    }

    private void UpdateStatusOnUi(ThrottleStatus throttleStatus)
    {
        if (throttleStatus == ThrottleStatus.Stopped)
        {
            StatusIcon = _throttlingPausedIcon;
            StatusDescription = _throttlingPausedDescription;
            return;
        }

        StatusIcon = _throttlingIcon;
        StatusDescription = _throttlingDescription;
    }

    private void UpdateListSnapshot()
    {
        WhitelistCountText = _backgroundEfficiencyService.ProcessWhitelist.Count.ToString();
        WhitelistPreviewText = string.Format("BackgroundEfficiency_WhitelistPreviewHidden".GetLocalizedString(), WhitelistCountText);
        BlacklistCountText = _backgroundEfficiencyService.ProcessBlacklist.Count.ToString();
        BlacklistPreviewText = string.Format("BackgroundEfficiency_BlacklistPreview".GetLocalizedString(), BlacklistCountText);
        OnPropertyChanged(nameof(WhitelistCountText));
        OnPropertyChanged(nameof(WhitelistPreviewText));
        OnPropertyChanged(nameof(BlacklistCountText));
        OnPropertyChanged(nameof(BlacklistPreviewText));
        OnPropertyChanged(nameof(ProcessWhitelistModified));
        OnPropertyChanged(nameof(ProcessBlacklistModified));
    }

    private void BackgroundEfficiencyService_ThrottleStatusChanged(object? sender, ThrottleStatus e)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            UpdateStatusOnUi(e);
            OnPropertyChanged(nameof(PauseThrottling));
            OnPropertyChanged(nameof(MasterSwitchEnabled));
            return;
        }

        _dispatcherQueue.TryEnqueue(() =>
        {
            UpdateStatusOnUi(e);
            OnPropertyChanged(nameof(PauseThrottling));
            OnPropertyChanged(nameof(MasterSwitchEnabled));
        });
    }

    public void Initialize()
    {
        _backgroundEfficiencyService.Initialize();
        UpdateStatusOnUi(_backgroundEfficiencyService.ThrottleStatus);
        LoadCurrentSettingsText();
        UpdateListSnapshot();
        OnPropertyChanged(nameof(PauseThrottling));
        OnPropertyChanged(nameof(MasterSwitchEnabled));
    }

    [RelayCommand]
    private void SaveProcessWhitelist()
    {
        _backgroundEfficiencyService.ApplyAndSaveProcessWhitelist(ProcessWhitelistString);
        LoadCurrentSettingsText();
        UpdateListSnapshot();
    }

    [RelayCommand]
    private void RestoreToDefaultProcessWhitelist()
    {
        _backgroundEfficiencyService.ApplyAndSaveProcessWhitelist("DefaultProcessWhitelist".GetLocalizedString());
        LoadCurrentSettingsText();
        UpdateListSnapshot();
    }

    [RelayCommand]
    private void SaveProcessBlacklist()
    {
        _backgroundEfficiencyService.ApplyAndSaveProcessBlacklist(ProcessBlacklistString);
        LoadCurrentSettingsText();
        UpdateListSnapshot();
    }

    [RelayCommand]
    private void RestoreToDefaultProcessBlacklist()
    {
        _backgroundEfficiencyService.ApplyAndSaveProcessBlacklist("DefaultProcessBlacklist".GetLocalizedString());
        LoadCurrentSettingsText();
        UpdateListSnapshot();
    }

    private static string NormalizeLineEndings(string? value)
    {
        return (value ?? string.Empty).ReplaceLineEndings();
    }

    public void Dispose()
    {
        _backgroundEfficiencyService.ThrottleStatusChanged -= BackgroundEfficiencyService_ThrottleStatusChanged;
    }

    public Task OnNavigatedToAsync(object parameter)
    {
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync()
    {
        return Task.CompletedTask;
    }
}
