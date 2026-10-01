using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using BluetoothAudioReceiver.Core.Extensions;
using Serilog;
using System.Threading;
using Windows.UI.ViewManagement;

namespace BluetoothAudioReceiver.Services;

internal class ThemeSelectorService(IAppSettingsService appSettingsService, ThemeManager themeManager) : IThemeSelectorService, IDisposable
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(ThemeSelectorService));
    public ElementTheme Theme => _appSettingsService.Theme;

    public event EventHandler<ElementTheme>? ThemeChanged;

    private readonly IAppSettingsService _appSettingsService = appSettingsService;
    private readonly ThemeManager _themeManager = themeManager;
    private UISettings? _systemSettings;
    private DispatcherQueue? _dispatcherQueue;
    private int _refreshQueued;

    public async Task SetThemeAsync(ElementTheme theme)
    {
        await ApplyThemeAsync(theme, persistTheme: true);
    }

    public async Task SetSystemThemeAsync(ElementTheme theme)
    {
        await ApplyThemeAsync(theme, persistTheme: false);
    }

    public async Task RefreshThemeFromSystemAsync()
    {
        if (Theme != ElementTheme.Default)
        {
            return;
        }

        var actualTheme = GetSystemTheme();
        if (App.MainWindow is not null)
        {
            await ApplyRequestedThemeAsync(App.MainWindow, actualTheme);
        }

        await WindowsExtensions.GetAllWindows().EnqueueOrInvokeAsync(
            async window => await ApplyRequestedThemeAsync(window, actualTheme),
            Microsoft.UI.Dispatching.DispatcherQueuePriority.High);

        ThemeChanged?.Invoke(this, actualTheme);
    }

    public void InitializeSystemThemeMonitoring(DispatcherQueue dispatcherQueue)
    {
        if (_systemSettings is not null)
        {
            return;
        }

        _dispatcherQueue = dispatcherQueue;
        _systemSettings = new UISettings();
        _systemSettings.ColorValuesChanged += OnSystemColorValuesChanged;
        _log.Information("System theme monitoring initialized.");
    }

    private void OnSystemColorValuesChanged(UISettings sender, object args)
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) != 0)
        {
            _log.Debug("System theme change ignored because a refresh is already queued.");
            return;
        }

        var dispatcherQueue = _dispatcherQueue;
        if (dispatcherQueue is null ||
            !dispatcherQueue.TryEnqueue(
                DispatcherQueuePriority.High,
                async () =>
                {
                    try
                    {
                        await RefreshThemeFromSystemAsync();
                        _log.Information("System theme change processed. Theme={Theme}", GetActualTheme());
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "Failed to process system theme change.");
                    }
                    finally
                    {
                        Volatile.Write(ref _refreshQueued, 0);
                    }
                }))
        {
            Volatile.Write(ref _refreshQueued, 0);
            _log.Warning("System theme change could not be queued on the UI thread.");
        }
    }

    private async Task ApplyThemeAsync(ElementTheme theme, bool persistTheme)
    {
        await _themeManager.ApplyThemeAsync(theme);

        var actualTheme = ResolveActualTheme(theme);
        if (App.MainWindow is not null)
        {
            await ApplyRequestedThemeAsync(App.MainWindow, actualTheme);
        }

        await WindowsExtensions.GetAllWindows().EnqueueOrInvokeAsync(
            async window => await ApplyRequestedThemeAsync(window, actualTheme),
            Microsoft.UI.Dispatching.DispatcherQueuePriority.High);

        if (persistTheme)
        {
            await _appSettingsService.SetThemeAsync(theme);
        }

        ThemeChanged?.Invoke(this, actualTheme);
    }

    public async Task SetRequestedThemeAsync(Window window)
    {
        await ApplyRequestedThemeAsync(window, GetActualTheme());
    }

    private static Task ApplyRequestedThemeAsync(Window window, ElementTheme theme)
    {
        var dispatcherQueue = window.DispatcherQueue;
        if (dispatcherQueue is null)
        {
            ThemeHelper.SetRequestedThemeAsync(window, theme);
            return Task.CompletedTask;
        }

        if (dispatcherQueue.HasThreadAccess)
        {
            ThemeHelper.SetRequestedThemeAsync(window, theme);
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    ThemeHelper.SetRequestedThemeAsync(window, theme);
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }))
        {
            tcs.SetException(new InvalidOperationException("Failed to enqueue theme update on window dispatcher."));
        }

        return tcs.Task;
    }

    public bool IsDarkTheme()
    {
        return GetActualTheme() == ElementTheme.Dark;
    }

    public ElementTheme GetSystemTheme()
    {
        return _themeManager.GetCurrentTheme();
    }

    public ElementTheme GetActualTheme()
    {
        return Theme switch
        {
            ElementTheme.Dark => ElementTheme.Dark,
            ElementTheme.Light => ElementTheme.Light,
            _ => GetSystemTheme()
        };
    }

    private ElementTheme ResolveActualTheme(ElementTheme theme)
    {
        return theme switch
        {
            ElementTheme.Dark => ElementTheme.Dark,
            ElementTheme.Light => ElementTheme.Light,
            _ => GetSystemTheme()
        };
    }

    public void Dispose()
    {
        if (_systemSettings is not null)
        {
            _systemSettings.ColorValuesChanged -= OnSystemColorValuesChanged;
            _systemSettings = null;
        }

        _dispatcherQueue = null;
    }
}
