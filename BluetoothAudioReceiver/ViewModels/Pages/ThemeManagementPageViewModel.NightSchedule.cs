using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class ThemeManagementPageViewModel
{
    [ObservableProperty]
    public partial TimeSpan? AutoDayNightNightStartTime { get; set; }

    [ObservableProperty]
    public partial TimeSpan? AutoDayNightNightEndTime { get; set; }

    [ObservableProperty]
    public partial bool AutoDayNightUseCustomNightSchedule { get; set; }

    [ObservableProperty]
    public partial string AutoDayNightNightScheduleText { get; set; } = "\u672A\u8BBE\u7F6E\u81EA\u5B9A\u4E49\u591C\u95F4\u65F6\u95F4";

    [RelayCommand]
    private async Task SaveNightScheduleAsync()
    {
        if (!_isInitialized)
        {
            return;
        }

        await _appSettingsService.SetAutoDayNightNightStartTimeAsync(AutoDayNightNightStartTime);
        await _appSettingsService.SetAutoDayNightNightEndTimeAsync(AutoDayNightNightEndTime);
        RefreshNightScheduleText();
        UpdateSunTimesTextAsync().Forget(nameof(ThemeManagementPageViewModel));
        _ = _autoDayNightService.RefreshAsync();
        GetNotificationService().Show(
            "\uE783",
            "\u81EA\u5B9A\u4E49\u591C\u95F4\u65F6\u95F4\u5DF2\u4FDD\u5B58",
            AutoDayNightNightScheduleText);
    }

    [RelayCommand]
    private async Task ClearNightScheduleAsync()
    {
        if (!_isInitialized)
        {
            return;
        }

        AutoDayNightNightStartTime = null;
        AutoDayNightNightEndTime = null;
        await _appSettingsService.SetAutoDayNightNightStartTimeAsync(null);
        await _appSettingsService.SetAutoDayNightNightEndTimeAsync(null);
        RefreshNightScheduleText();
        UpdateSunTimesTextAsync().Forget(nameof(ThemeManagementPageViewModel));
        _ = _autoDayNightService.RefreshAsync();
        GetNotificationService().Show(
            "\uE783",
            "\u81EA\u5B9A\u4E49\u591C\u95F4\u65F6\u95F4\u5DF2\u6E05\u7A7A",
            "\u5C06\u6062\u590D\u6309\u57CE\u5E02\u6216\u7CFB\u7EDF\u5B9A\u4F4D\u8BA1\u7B97");
    }

    partial void OnAutoDayNightUseCustomNightScheduleChanged(bool value)
    {
        if (!_isInitialized)
        {
            return;
        }

        _appSettingsService.SetAutoDayNightUseCustomNightScheduleAsync(value).Forget(nameof(ThemeManagementPageViewModel));
        _ = _autoDayNightService.RefreshAsync();
        UpdateSunTimesTextAsync().Forget(nameof(ThemeManagementPageViewModel));
    }

    private void LoadNightScheduleFromSettings()
    {
        AutoDayNightUseCustomNightSchedule = _appSettingsService.AutoDayNightUseCustomNightSchedule;
        AutoDayNightNightStartTime = ParseStoredNightTime(_appSettingsService.AutoDayNightNightStartTime);
        AutoDayNightNightEndTime = ParseStoredNightTime(_appSettingsService.AutoDayNightNightEndTime);
        RefreshNightScheduleText();
    }

    private void RefreshNightScheduleText()
    {
        if (AutoDayNightNightStartTime is not null && AutoDayNightNightEndTime is not null)
        {
            AutoDayNightNightScheduleText = $"\u5F53\u524D\u81EA\u5B9A\u4E49\uff1A{FormatNightTime(AutoDayNightNightStartTime.Value)} - {FormatNightTime(AutoDayNightNightEndTime.Value)}";
            return;
        }

        AutoDayNightNightScheduleText = "\u672A\u8BBE\u7F6E\u81EA\u5B9A\u4E49\u591C\u95F4\u65F6\u95F4";
    }

    private static TimeSpan? ParseStoredNightTime(string value)
    {
        return TimeSpan.TryParse(value, out var time) ? NormalizeNightTime(time) : null;
    }

    private static TimeSpan NormalizeNightTime(TimeSpan value)
    {
        var totalMinutes = ((int)value.TotalMinutes % (24 * 60) + (24 * 60)) % (24 * 60);
        return TimeSpan.FromMinutes(totalMinutes);
    }

    private static string FormatNightTime(TimeSpan time)
    {
        return NormalizeNightTime(time).ToString(@"hh\:mm");
    }

    private static INotificationService GetNotificationService()
    {
        return Ioc.Default.GetRequiredService<INotificationService>();
    }
}
