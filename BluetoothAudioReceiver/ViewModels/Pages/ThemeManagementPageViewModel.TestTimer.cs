using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class ThemeManagementPageViewModel
{
    private bool _themeTestEnabled;
    private TimeSpan? _themeTestTime;
    private bool _themeTestUseDarkMode = true;
    private int _themeTestTargetThemeIndex = 1;
    private string _themeTestStatusText = "未设置测试定时器";

    public ICommand StartThemeTestTimerCommand => new RelayCommand(StartThemeTestTimer);

    public ICommand CancelThemeTestTimerCommand => new RelayCommand(CancelThemeTestTimer);

    public ICommand StartThirtySecondThemeFlipTestCommand => new RelayCommand(StartThirtySecondThemeFlipTest);

    public IReadOnlyList<string> ThemeTestTargetThemeOptions { get; } = ["浅色", "深色"];

    public bool ThemeTestEnabled
    {
        get => _themeTestEnabled;
        set => SetProperty(ref _themeTestEnabled, value);
    }

    public TimeSpan? ThemeTestTime
    {
        get => _themeTestTime;
        set => SetProperty(ref _themeTestTime, value);
    }

    public bool ThemeTestUseDarkMode
    {
        get => _themeTestUseDarkMode;
        set
        {
            if (SetProperty(ref _themeTestUseDarkMode, value))
            {
                _themeTestTargetThemeIndex = value ? 1 : 0;
            }
        }
    }

    public int ThemeTestTargetThemeIndex
    {
        get => _themeTestTargetThemeIndex;
        set
        {
            if (SetProperty(ref _themeTestTargetThemeIndex, value))
            {
                ThemeTestUseDarkMode = value == 1;
            }
        }
    }

    public string ThemeTestStatusText
    {
        get => _themeTestStatusText;
        set => SetProperty(ref _themeTestStatusText, value);
    }

    private void StartThemeTestTimer()
    {
        if (ThemeTestTime is null)
        {
            ThemeTestStatusText = "请先设置测试时间";
            return;
        }

        ThemeTestUseDarkMode = ThemeTestTargetThemeIndex == 1;
        _themeTestTimerService.Schedule(ThemeTestTime.Value, ThemeTestUseDarkMode);
        ThemeTestEnabled = _themeTestTimerService.Enabled;
        ThemeTestStatusText = _themeTestTimerService.StatusText;
    }

    private void CancelThemeTestTimer()
    {
        _themeTestTimerService.Cancel();
        ThemeTestEnabled = _themeTestTimerService.Enabled;
        ThemeTestStatusText = _themeTestTimerService.StatusText;
    }

    private void StartThirtySecondThemeFlipTest()
    {
        _themeTestTimerService.ScheduleOppositeThemeAfterThirtySeconds();
        ThemeTestEnabled = _themeTestTimerService.Enabled;
        ThemeTestTime = _themeTestTimerService.ScheduledTime;
        ThemeTestUseDarkMode = _themeTestTimerService.UseDarkMode;
        ThemeTestStatusText = _themeTestTimerService.StatusText;
    }
}
