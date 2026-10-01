using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WinUIEx;
using HWND = global::Windows.Win32.Foundation.HWND;
using PInvoke = global::Windows.Win32.PInvoke;
using SET_WINDOW_POS_FLAGS = global::Windows.Win32.UI.WindowsAndMessaging.SET_WINDOW_POS_FLAGS;
using Windows.Devices.Enumeration;
using Windows.UI.ViewManagement;
using BluetoothAudioReceiver.Core.Helpers;
using BluetoothAudioReceiver.Models.Application;

namespace BluetoothAudioReceiver.Views.Windows;

public sealed partial class TrayQuickPanelWindow : WindowEx
{
    public static bool IsPanelVisible { get; private set; }
    // ========================= 可直接修改的设计参数（统一入口） =========================
    private const double TitleFontSize = 14; // 标题字号（音频管理）
    private const double HeaderHintFontSize = 11; // 标题后说明文字字号
    private static readonly Thickness HeaderSectionPadding = new(40, 10, 10, 10); // 标题区外边距（左,上,右,下）
    private const double HeaderDividerHorizontalInset = 15; // 标题下分界线左右缩进；调小可加长，调大可缩短，允许负值
    private const double HeaderDividerTopSpacing = 4; // 标题下分界线上边距
    private const double HeaderDividerBottomSpacing = 6; // 标题下分界线下边距
    private const double HeaderDividerHeight = 1; // 标题下分界线高度
    private const double RowFontSize = 12; // 设备名称字号
    private const double HotkeyFontSize = 12; // 快捷键字号
    private const double DotRadius = 4; // 左侧绿点半径（视觉半径）
    private static readonly Thickness RowPadding = new(6, 5, 6, 5); // 每行内容内边距（左,上,右,下）
    private const double SectionSpacing = 10; // 区块间距（预留参数）
    private const int WindowWidth = 650; // 浮窗宽度
    private const int WindowHeight = 400; // 浮窗高度
    private const double DotColumnWidth = 20; // 圆点列宽
    private const double IconColumnWidth = 30; // 左侧图标列宽
    private const double NameColumnWidth = 170; // 设备名称列宽
    private const double HotkeyColumnWidth = 220; // 快捷键列宽（左移20%）
    private const double DeviceColumnsSpacing = 0; // 圆点+图标+设备名称+快捷键 四列之间的间距
    private const double DeviceRowsSpacing = 0; // 设备行与设备行之间的间距（StackPanel.Spacing）
    private const double HotkeyColumnMinWidth = 120; // 快捷键列最小宽度保护（避免被挤掉）

    private const byte RowBackgroundAlpha = 20; // 每行灰色圆角矩形背景透明度（0-255）
    private static readonly global::Windows.UI.Color RowBackgroundColor = global::Windows.UI.Color.FromArgb(RowBackgroundAlpha, 180, 180, 180); // 每行灰色圆角矩形背景色（悬浮态）
    private static readonly global::Windows.UI.Color RowIdleBackgroundColor = global::Windows.UI.Color.FromArgb(0, 180, 180, 180); // 每行默认背景色（非悬浮）
    private const double RowCornerRadius = 6; // 每行灰色圆角矩形圆角半径
    private static readonly Thickness RowOuterMargin = new(4, 2, 4, 2); // 每行外边距（左右边距/上下行距，较之前减少约40%）

    private const byte HotkeyBadgeBackgroundAlpha = 20; // 快捷键徽章背景透明度（0-255）
    private static readonly global::Windows.UI.Color HotkeyBadgeBackgroundColor = global::Windows.UI.Color.FromArgb(HotkeyBadgeBackgroundAlpha, 150, 150, 150); // 快捷键徽章背景色
    private const double HotkeyBadgeCornerRadius = 6; // 快捷键徽章圆角半径
    private static readonly Thickness HotkeyBadgePadding = new(12, 4, 12, 4); // 快捷键徽章内边距（左,上,右,下）
    private const string HotkeyPlaceholder = "--"; // 快捷键为空时占位文本

    private static readonly global::Windows.UI.Color DotActiveColor = global::Windows.UI.Color.FromArgb(255, 108, 203, 95); // 绿点激活颜色
    private static readonly global::Windows.UI.Color DotInactiveColor = global::Windows.UI.Color.FromArgb(0, 128, 128, 128); // 未选中不显示圆点（透明）
    // ============================================================================

    // 浮窗贴边安全距离
    private const int ScreenMargin = 10;
    private const string InputHotkeyBindingsKey = "InputHotkeyBindingsV2";
    private const string OutputHotkeyBindingsKey = "OutputHotkeyBindingsV2";

    private readonly DispatcherQueueTimer _topMostRetryTimer;
    private readonly DispatcherQueueTimer _outsideClickTimer;
    private bool _lastLeftDown;
    private bool _lastRightDown;
    private DateTimeOffset _ignoreOutsideClickUntil = DateTimeOffset.MinValue;
    private readonly UISettings _uiSettings;
    private ElementTheme _currentTheme = ElementTheme.Default;
    private readonly IThemeSelectorService _themeSelectorService;

    private readonly IAudioPlaybackConnectionService _connectionService;
    private readonly IAppSettingsService _appSettingsService;
    private readonly IAudioRouteHotkeyService _audioRouteHotkeyService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly Dictionary<string, List<Ellipse>> _dotMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Border, RowMeta> _rowMap = [];
    private readonly List<DeviceRowSnapshot> _lastRenderedRows = [];
    private string _lastSummarySignature = string.Empty;
    private string _currentOutputSelectionId = string.Empty;
    private bool _isSwitchingByClick;

    public TrayQuickPanelWindow()
    {
        InitializeComponent();
        _connectionService = Ioc.Default.GetRequiredService<IAudioPlaybackConnectionService>();
        _appSettingsService = Ioc.Default.GetRequiredService<IAppSettingsService>();
        _audioRouteHotkeyService = Ioc.Default.GetRequiredService<IAudioRouteHotkeyService>();
        _localSettingsService = Ioc.Default.GetRequiredService<ILocalSettingsService>();
        _themeSelectorService = Ioc.Default.GetRequiredService<IThemeSelectorService>();
        _uiSettings = new UISettings();
        _uiSettings.ColorValuesChanged += Settings_ColorValuesChanged;
        _themeSelectorService.ThemeChanged += OnThemeChanged;
        _currentTheme = ResolveCurrentTheme();
        Closed += OnWindowClosed;

        IsTitleBarVisible = false;
        IsMinimizable = false;
        IsMaximizable = false;
        IsResizable = false;
        IsShownInSwitchers = false;

        try { SystemBackdrop = new MicaBackdrop(); } catch { }

        ApplyDesignTokens();
        EnsureAlwaysOnTopPresenter();

        _topMostRetryTimer = DispatcherQueue.CreateTimer();
        _topMostRetryTimer.Interval = TimeSpan.FromMilliseconds(120);
        _topMostRetryTimer.IsRepeating = false;
        _topMostRetryTimer.Tick += (_, _) =>
        {
            EnsureAlwaysOnTopPresenter();
            EnsureTopMostNoActivate();
        };

        _outsideClickTimer = DispatcherQueue.CreateTimer();
        _outsideClickTimer.Interval = TimeSpan.FromMilliseconds(30);
        _outsideClickTimer.IsRepeating = true;
        _outsideClickTimer.Tick += (_, _) => CheckOutsideClickToClose();

        // 按下快捷键并执行成功后，浮窗列表刷新一次（不再做每秒轮询刷新）
        _audioRouteHotkeyService.DebugMessage += OnHotkeyDebugMessage;
        _connectionService.DeviceConnected += OnDeviceConnectionChanged;
        _connectionService.DeviceDisconnected += OnDeviceConnectionChanged;
    }

    private void Settings_ColorValuesChanged(UISettings sender, object args)
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, () =>
        {
            if (_appSettingsService.Theme == ElementTheme.Default)
            {
                _currentTheme = ResolveCurrentTheme();
            }
            ApplyDesignTokens();
            if (Visible)
            {
                RefreshDotIndicatorsOnly();
            }
        });
    }


    private void OnThemeChanged(object? sender, ElementTheme theme)
    {
        _currentTheme = theme == ElementTheme.Default ? ResolveCurrentTheme() : theme;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, () =>
        {
            ApplyDesignTokens();
            if (Visible)
            {
                RefreshDotIndicatorsOnly();
            }
        });
    }
    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _uiSettings.ColorValuesChanged -= Settings_ColorValuesChanged;
        _themeSelectorService.ThemeChanged -= OnThemeChanged;
        Closed -= OnWindowClosed;
    }

    public void TogglePanel()
    {
        if (Visible)
        {
            _outsideClickTimer.Stop();
            WindowExtensions.Hide(this);
            IsPanelVisible = false;
            return;
        }

        _ = ShowPanelNearCursorAsync();
    }

    public async Task ShowPanelNearCursorAsync()
    {
        MoveNearCursorFast();

        // Preload device rows before showing the window to avoid visible layout jumps.
        try
        {
            await RefreshDeviceSummaryAsync();
        }
        catch
        {
            if (_lastRenderedRows.Count > 0 && DeviceListPanel.Children.Count == 0)
            {
                RenderDeviceRows(_lastRenderedRows);
            }
        }

        if (!Visible) Activate(); else AppWindow.Show();
        IsPanelVisible = true;

        EnsureAlwaysOnTopPresenter();
        _topMostRetryTimer.Stop();
        _topMostRetryTimer.Start();
        _ignoreOutsideClickUntil = DateTimeOffset.Now.AddMilliseconds(300);

        _lastLeftDown = IsMouseButtonDown(0x01);
        _lastRightDown = IsMouseButtonDown(0x02);
        _outsideClickTimer.Start();

        FocusFirstRow();
    }

    private void MoveNearCursorFast()
    {
        var area = DisplayArea.Primary;
        if (area is null) return;

        var work = area.WorkArea;
        var panelWidth = Math.Min(WindowWidth, Math.Max(WindowWidth, work.Width - (ScreenMargin * 2)));
        var panelHeight = Math.Min(WindowHeight, Math.Max(260, work.Height - (ScreenMargin * 2)));
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(panelWidth, panelHeight));

        if (!GetCursorPos(out var cursor)) return;

        var x = cursor.X + 4;
        var y = cursor.Y - panelHeight + 24;

        if (x < work.X + ScreenMargin) x = work.X + ScreenMargin;
        if (y < work.Y + ScreenMargin) y = work.Y + ScreenMargin;
        if (x + panelWidth > work.X + work.Width - ScreenMargin) x = work.X + work.Width - panelWidth - ScreenMargin;
        if (y + panelHeight > work.Y + work.Height - ScreenMargin) y = work.Y + work.Height - panelHeight - ScreenMargin;

        AppWindow.Move(new global::Windows.Graphics.PointInt32(x, y));
    }

    private void ApplyDesignTokens()
    {
        Width = WindowWidth;
        Height = WindowHeight;
        RootCard.RequestedTheme = _currentTheme;
        HeaderSectionBorder.Padding = HeaderSectionPadding;
        InputSectionTitle.FontSize = TitleFontSize;
        InputSectionTitle.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        InputSectionTitle.Foreground = CreatePrimaryTextBrush();
        HeaderHintText.FontSize = HeaderHintFontSize;
        HeaderHintText.Foreground = CreateSecondaryTextBrush();
        HeaderDivider.Height = HeaderDividerHeight;
        HeaderDivider.Margin = BuildHeaderDividerMargin();
        DeviceListPanel.Spacing = DeviceRowsSpacing;
    }

    private static Thickness BuildHeaderDividerMargin()
        => new(HeaderDividerHorizontalInset, HeaderDividerTopSpacing, HeaderDividerHorizontalInset, HeaderDividerBottomSpacing);

    private SolidColorBrush CreatePrimaryTextBrush()
    {
        var isDark = GetEffectiveTheme() == ElementTheme.Dark;
        return new SolidColorBrush(isDark
            ? global::Windows.UI.Color.FromArgb(255, 245, 245, 245)
            : global::Windows.UI.Color.FromArgb(255, 20, 20, 20));
    }

    private SolidColorBrush CreateSecondaryTextBrush()
    {
        var isDark = GetEffectiveTheme() == ElementTheme.Dark;
        return new SolidColorBrush(isDark
            ? global::Windows.UI.Color.FromArgb(255, 200, 200, 200)
            : global::Windows.UI.Color.FromArgb(255, 75, 75, 75));
    }

    private static SolidColorBrush CreateInactiveRowTextBrush()
        => new(global::Windows.UI.Color.FromArgb(255, 145, 145, 145));


    private ElementTheme ResolveCurrentTheme()
    {
        if (_appSettingsService.Theme != ElementTheme.Default)
        {
            return _appSettingsService.Theme;
        }

        return IsSystemDarkTheme() ? ElementTheme.Dark : ElementTheme.Light;
    }

    private ElementTheme GetEffectiveTheme()
    {
        if (_currentTheme != ElementTheme.Default)
        {
            return _currentTheme;
        }

        return IsSystemDarkTheme() ? ElementTheme.Dark : ElementTheme.Light;
    }

    private bool IsSystemDarkTheme()
    {
        var background = _uiSettings.GetColorValue(UIColorType.Background);
        var luminance = (0.2126 * background.R) + (0.7152 * background.G) + (0.0722 * background.B);
        return luminance < 128;
    }

    private async Task RefreshDeviceSummaryAsync()
    {
        var inputDevicesTask = _connectionService.FindAllInputRouteDevicesAsync();
        var outputDevicesTask = _audioRouteHotkeyService.GetOutputDevicesAsync();
        var currentDefaultOutputEndpointTask = _audioRouteHotkeyService.GetCurrentDefaultOutputEndpointIdAsync();
        var inputEntries = await GetSelectedDeviceEntriesFromHotkeyCardsAsync(
            InputHotkeyBindingsKey,
            _appSettingsService.PreferredInputDeviceId,
            NormalizeHotkey(_appSettingsService.InputHotkey, "Alt+1"));
        var outputEntries = await GetSelectedDeviceEntriesFromHotkeyCardsAsync(
            OutputHotkeyBindingsKey,
            _appSettingsService.PreferredOutputDeviceId,
            NormalizeHotkey(_appSettingsService.OutputHotkey, "Alt+2"));

        await Task.WhenAll(inputDevicesTask, outputDevicesTask, currentDefaultOutputEndpointTask);
        var inputDeviceLookup = inputDevicesTask.Result
            .Where(device => !string.IsNullOrWhiteSpace(device.Id))
            .GroupBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => string.IsNullOrWhiteSpace(group.First().Name) ? group.Key : group.First().Name,
                StringComparer.OrdinalIgnoreCase);
        foreach (var device in inputDevicesTask.Result.Where(device => !string.IsNullOrWhiteSpace(device.Id)))
        {
            var name = string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name;
            var physicalId = DeviceIdentityHelper.GetPhysicalDeviceId(device);
            if (!string.IsNullOrWhiteSpace(physicalId))
            {
                inputDeviceLookup.TryAdd(physicalId, name);
                inputDeviceLookup.TryAdd(DeviceIdentityHelper.BuildSelectionId(physicalId, true), name);
            }
        }
        var outputDeviceLookup = outputDevicesTask.Result
            .Where(device => !string.IsNullOrWhiteSpace(device.Id))
            .GroupBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => string.IsNullOrWhiteSpace(group.First().Name) ? group.Key : group.First().Name,
                StringComparer.OrdinalIgnoreCase);

        var currentDefaultOutputEndpointId = NormalizeOutputSelectionId(currentDefaultOutputEndpointTask.Result);
        var outputDevices = outputDevicesTask.Result
            .Where(device => !string.IsNullOrWhiteSpace(device.Id))
            .GroupBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var currentOutputDeviceId = ResolveCurrentOutputDeviceId(outputDevices, currentDefaultOutputEndpointId)
            ?? _appSettingsService.PreferredOutputDeviceId
            ?? string.Empty;
        _currentOutputSelectionId = currentOutputDeviceId;

        var connectedIds = new HashSet<string>(
            _connectionService.ConnectedDevices
                .Select(d => d.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);
        var preferredInputId = _appSettingsService.PreferredInputDeviceId ?? string.Empty;
        var rows = new List<DeviceRowSnapshot>();

        foreach (var entry in inputEntries)
        {
            var name = ResolveDeviceName(entry.DeviceId, inputDeviceLookup);
            if (!string.IsNullOrWhiteSpace(name))
            {
                var isConnected = connectedIds.Contains(entry.DeviceId);
                var (displayName, glyph) = ResolveVisual(name);
                rows.Add(new DeviceRowSnapshot(entry.DeviceId, displayName, entry.Hotkey, glyph, isConnected, true));
            }
        }

        foreach (var entry in outputEntries)
        {
            var name = ResolveDeviceName(entry.DeviceId, outputDeviceLookup);
            if (!string.IsNullOrWhiteSpace(name))
            {
                var isConnected = string.Equals(entry.DeviceId, currentOutputDeviceId, StringComparison.OrdinalIgnoreCase);
                var (displayName, glyph) = ResolveVisual(name);
                rows.Add(new DeviceRowSnapshot(entry.DeviceId, displayName, entry.Hotkey, glyph, isConnected, false));
            }
        }
        var recentIds = DevicePriorityHelper.GetRecentDeviceIds(_localSettingsService);
        var orderedRows = DevicePriorityHelper.OrderDevices(
            rows,
            row => row.DeviceId,
            row => row.DeviceName,
            recentIds,
            row => row.IsSelected);

        var signature = BuildDeviceSummarySignature(orderedRows);
        if (string.Equals(signature, _lastSummarySignature, StringComparison.Ordinal))
        {
            if (DeviceListPanel.Children.Count > 0)
            {
                RefreshDotIndicatorsOnly();
            }
            return;
        }

        _lastSummarySignature = signature;
        RenderDeviceRows(orderedRows);
    }

    private static string BuildDeviceSummarySignature(IEnumerable<DeviceRowSnapshot> rows)
    {
        return string.Join('|', rows.Select(row =>
            $"{row.DeviceId}:{row.DeviceName}:{row.Hotkey}:{row.IconGlyph}:{row.IsInput}"));
    }

    private static string? ResolveDeviceName(string deviceId, Dictionary<string, string> lookup)
    {
        if (lookup.TryGetValue(deviceId, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            if (LooksLikeRawDevicePath(name))
            {
                return null;
            }

            return name;
        }

        return null;
    }

    private static bool LooksLikeRawDevicePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        return text.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)
            || text.Contains("MMDEVAPI", StringComparison.OrdinalIgnoreCase)
            || text.Contains("SWD#", StringComparison.OrdinalIgnoreCase);
    }

    private void RenderDeviceRows(IEnumerable<DeviceRowSnapshot> rows)
    {
        DeviceListPanel.Children.Clear();
        _dotMap.Clear();
        _rowMap.Clear();
        _lastRenderedRows.Clear();

        foreach (var row in rows)
        {
            _lastRenderedRows.Add(row);
            DeviceListPanel.Children.Add(CreateDeviceRow(row.DeviceId, row.DeviceName, row.Hotkey, row.IconGlyph, row.IsSelected, row.IsInput));
        }
    }

    private Border CreateDeviceRow(string deviceId, string deviceName, string hotkey, string iconGlyph, bool isSelected, bool isInput)
    {
        var row = new Border
        {
            Background = new SolidColorBrush(RowIdleBackgroundColor),
            Padding = RowPadding,
            Margin = RowOuterMargin,
            CornerRadius = new CornerRadius(RowCornerRadius)
        };
        row.PointerEntered += (_, _) => row.Background = new SolidColorBrush(RowBackgroundColor);
        row.PointerExited += (_, _) => row.Background = new SolidColorBrush(RowIdleBackgroundColor);
        row.Tapped += OnDeviceRowTapped;
        _rowMap[row] = new RowMeta(deviceId, isInput);

        var grid = new Grid { ColumnSpacing = DeviceColumnsSpacing };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DotColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IconColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NameColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HotkeyColumnWidth), MinWidth = HotkeyColumnMinWidth });

        var dot = new Ellipse
        {
            Width = DotRadius * 2,
            Height = DotRadius * 2,
            Fill = new SolidColorBrush(isSelected ? DotActiveColor : DotInactiveColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (!_dotMap.TryGetValue(deviceId, out var dots))
        {
            dots = new List<Ellipse>();
            _dotMap[deviceId] = dots;
        }
        dots.Add(dot);

        var leftIcon = new FontIcon
        {
            Glyph = iconGlyph,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
            FontSize = 16,
            Foreground = CreateInactiveRowTextBrush(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(leftIcon, 1);

        var name = new TextBlock
        {
            Text = deviceName,
            FontSize = RowFontSize,
            MaxWidth = NameColumnWidth,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = CreateInactiveRowTextBrush(),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(name, 2);

        var hk = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(hotkey) ? HotkeyPlaceholder : hotkey,
            FontSize = HotkeyFontSize,
            Foreground = CreateInactiveRowTextBrush(),
            TextAlignment = TextAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        var hkBadge = new Border
        {
            Background = new SolidColorBrush(HotkeyBadgeBackgroundColor),
            CornerRadius = new CornerRadius(HotkeyBadgeCornerRadius),
            Padding = HotkeyBadgePadding,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = hk
        };
        Grid.SetColumn(hkBadge, 3);

        grid.Children.Add(dot);
        grid.Children.Add(leftIcon);
        grid.Children.Add(name);
        grid.Children.Add(hkBadge);
        row.Child = grid;
        ApplyRowActiveState(row, isSelected);
        return row;
    }

    private async void OnDeviceRowTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is not Border row || !_rowMap.TryGetValue(row, out var meta) || _isSwitchingByClick) return;
        _isSwitchingByClick = true;
        try
        {
            if (IsRowCurrentlyActive(meta))
            {
                if (meta.IsInput)
                {
                    var connectedTarget = FindConnectedDevice(meta.DeviceId);
                    if (connectedTarget != null)
                    {
                        await _connectionService.DisconnectAsync(connectedTarget);
                    }

                    if (string.Equals(_appSettingsService.PreferredInputDeviceId, meta.DeviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        await _appSettingsService.SetPreferredInputDeviceIdAsync(FindNextConnectedDeviceId(meta.DeviceId));
                    }
                }
                else
                {
                    if (string.Equals(_appSettingsService.PreferredOutputDeviceId, meta.DeviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        await _appSettingsService.SetPreferredOutputDeviceIdAsync(string.Empty);
                    }
                }
            }
            else
            {
                if (meta.IsInput)
                {
                    var device = await FindAudioDeviceAsync(meta.DeviceId);
                    if (device != null && await _connectionService.ConnectAsync(device))
                    {
                        await _appSettingsService.SetPreferredInputDeviceIdAsync(meta.DeviceId);
                        await DevicePriorityHelper.MarkDeviceAsUsedAsync(_localSettingsService, device.Id);
                    }
                }
                else
                {
                    var currentInput = _appSettingsService.PreferredInputDeviceId ?? string.Empty;
                    await _audioRouteHotkeyService.UpdateTargetsAsync(currentInput, meta.DeviceId);
                    _audioRouteHotkeyService.UpdateTargetCandidates([], [meta.DeviceId]);
                    await _audioRouteHotkeyService.TriggerOutputRouteAsync();
                }
            }

            await RefreshDeviceSummaryAsync();
        }
        catch { }
        finally
        {
            _isSwitchingByClick = false;
        }
    }

    private void RefreshDotIndicatorsOnly()
    {
        var connectedIds = BuildConnectedDeviceIdSet();
        var preferredInputId = _appSettingsService.PreferredInputDeviceId ?? string.Empty;

        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(preferredInputId) &&
            connectedIds.Contains(preferredInputId))
        {
            active.Add(preferredInputId);
        }
        foreach (var connectedId in connectedIds)
        {
            active.Add(connectedId);
        }
        if (!string.IsNullOrWhiteSpace(_currentOutputSelectionId))
        {
            active.Add(_currentOutputSelectionId);
        }

        foreach (var pair in _dotMap)
        {
            var isActive = active.Contains(pair.Key);
            foreach (var dot in pair.Value)
            {
                dot.Fill = new SolidColorBrush(isActive ? DotActiveColor : DotInactiveColor);
            }
        }

        foreach (var pair in _rowMap)
        {
            var row = pair.Key;
            var meta = pair.Value;
            ApplyRowActiveState(row, IsRowCurrentlyActive(meta));
        }
    }

    private void ApplyRowActiveState(Border row, bool isActive)
    {
        if (row.Child is not Grid grid)
        {
            return;
        }

        var activePrimary = CreatePrimaryTextBrush();
        var activeSecondary = CreateSecondaryTextBrush();
        var inactive = CreateInactiveRowTextBrush();

        foreach (var child in grid.Children)
        {
            if (child is TextBlock tb)
            {
                var column = Grid.GetColumn(tb);
                tb.Foreground = column == 2
                    ? (isActive ? activePrimary : inactive)
                    : (isActive ? activeSecondary : inactive);
            }
            else if (child is FontIcon icon)
            {
                icon.Foreground = isActive ? activeSecondary : inactive;
            }
            else if (child is Border badge && badge.Child is TextBlock hk)
            {
                badge.Background = new SolidColorBrush(HotkeyBadgeBackgroundColor);
                hk.Foreground = isActive ? activeSecondary : inactive;
            }
        }
    }

    private static (string DisplayName, string Glyph) ResolveVisual(string rawName)
    {
        var name = string.IsNullOrWhiteSpace(rawName) ? "未知设备" : rawName.Trim();
        var lower = name.ToLowerInvariant();

        var isTablet = name.Contains("平板", StringComparison.OrdinalIgnoreCase) || lower.Contains("pad") || lower.Contains("tablet");
        var isHeadset = name.Contains("耳机", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("headphone")
            || lower.Contains("headset")
            || lower.Contains("earbud")
            || lower.Contains("earbuds")
            || lower.Contains("buds")
            || lower.Contains("airpods");
        var isPhoneKeyword = name.Contains("手机", StringComparison.OrdinalIgnoreCase) || lower.Contains("phone") || lower.Contains("iphone");
        var isPhoneBrandModel = (lower.Contains("xiaomi") || lower.Contains("redmi") || lower.Contains("huawei") || lower.Contains("honor") || lower.Contains("oppo") || lower.Contains("vivo"))
            && !isTablet
            && !isHeadset;

        if (isHeadset)
        {
            return (name, DeviceVisualHelper.HeadsetGlyph);
        }

        if (isTablet)
        {
            return (name, DeviceVisualHelper.TabletGlyph);
        }

        if (isPhoneKeyword || isPhoneBrandModel)
        {
            return (name, DeviceVisualHelper.PhoneGlyph);
        }

        return (name, DeviceVisualHelper.SpeakerGlyph);
    }

    private bool IsRowCurrentlyActive(RowMeta meta)
    {
        if (meta.IsInput)
        {
            var connectedDevices = _connectionService.ConnectedDevices;
            for (var i = 0; i < connectedDevices.Count; i++)
            {
                if (string.Equals(connectedDevices[i].Id, meta.DeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        return string.Equals(_currentOutputSelectionId, meta.DeviceId, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveCurrentOutputDeviceId(
        List<AudioOutputDeviceItem> outputDevices,
        string currentDefaultOutputEndpointId)
    {
        if (outputDevices.Count == 0 || string.IsNullOrWhiteSpace(currentDefaultOutputEndpointId))
        {
            return null;
        }

        for (var i = 0; i < outputDevices.Count; i++)
        {
            var device = outputDevices[i];
            if (string.Equals(NormalizeOutputSelectionId(device.EndpointId), currentDefaultOutputEndpointId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizeOutputSelectionId(device.Id), currentDefaultOutputEndpointId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizeOutputSelectionId(device.PhysicalId), currentDefaultOutputEndpointId, StringComparison.OrdinalIgnoreCase))
            {
                return device.Id;
            }
        }

        return null;
    }

    private async Task<List<SelectedDeviceEntry>> GetSelectedDeviceEntriesFromHotkeyCardsAsync(string key, string fallbackDeviceId, string fallbackHotkey)
    {
        try
        {
            var configs = await _localSettingsService.ReadSettingAsync(key, Array.Empty<HotkeyBindingConfig>()) ?? Array.Empty<HotkeyBindingConfig>();
            var entries = new List<SelectedDeviceEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var config in configs.Where(c => c.DeviceIds is { Count: > 0 } || c.DevicePhysicalIds is { Count: > 0 }))
            {
                var hotkey = NormalizeHotkey(config.Hotkey, fallbackHotkey);
                foreach (var id in config.DeviceIds!.Concat(
                             config.DevicePhysicalIds!.Select(physicalId =>
                                  DeviceIdentityHelper.BuildSelectionId(physicalId, key == InputHotkeyBindingsKey))))
                {
                    if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) continue;
                    entries.Add(new SelectedDeviceEntry(id, hotkey, true));
                }
            }

            // 兼容旧数据：只有单个 DeviceId 的情况
            foreach (var config in configs.Where(c => (c.DeviceIds == null || c.DeviceIds.Count == 0) && !string.IsNullOrWhiteSpace(c.DeviceId)))
            {
                var hotkey = NormalizeHotkey(config.Hotkey, fallbackHotkey);
                var id = config.DeviceId!;
                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) continue;
                entries.Add(new SelectedDeviceEntry(id, hotkey, true));
            }

            if (entries.Count > 0) return entries;
        }
        catch { }

        return string.IsNullOrWhiteSpace(fallbackDeviceId)
            ? []
            : [new SelectedDeviceEntry(fallbackDeviceId, fallbackHotkey, false)];
    }

    private void EnsureTopMostNoActivate()
    {
        try
        {
            var hwnd = new HWND(this.GetWindowHandle());
            PInvoke.SetWindowPos(hwnd, new HWND(-1), 0, 0, 0, 0,
                SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE |
                SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        }
        catch { }
    }

    private void EnsureAlwaysOnTopPresenter()
    {
        try
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsAlwaysOnTop = true;
        }
        catch { }
    }

    private void CheckOutsideClickToClose()
    {
        if (!Visible) { _outsideClickTimer.Stop(); return; }
        if (DateTimeOffset.Now < _ignoreOutsideClickUntil) return;

        var leftDown = IsMouseButtonDown(0x01);
        var rightDown = IsMouseButtonDown(0x02);
        var leftClicked = leftDown && !_lastLeftDown;
        var rightClicked = rightDown && !_lastRightDown;
        _lastLeftDown = leftDown;
        _lastRightDown = rightDown;

        if (!leftClicked && !rightClicked) return;
        if (!GetCursorPos(out var cursor)) return;

        var pos = AppWindow.Position;
        var size = AppWindow.Size;
        var inside = cursor.X >= pos.X && cursor.X <= pos.X + size.Width &&
                     cursor.Y >= pos.Y && cursor.Y <= pos.Y + size.Height;

        if (!inside)
        {
            _outsideClickTimer.Stop();
            WindowExtensions.Hide(this);
            IsPanelVisible = false;
        }
    }

    private void OnHotkeyDebugMessage(string message)
    {
        if (!Visible) return;
        if (!message.Contains("切换成功", StringComparison.Ordinal)) return;

        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(80);
            RefreshDotIndicatorsOnly();
        });
    }

    private void OnDeviceConnectionChanged(object? sender, DeviceInformation _)
    {
        if (!Visible) return;
        DispatcherQueue.TryEnqueue(() => RefreshDotIndicatorsOnly());
    }

    private void FocusFirstRow()
    {
        if (DeviceListPanel.Children.Count == 0) return;
        if (DeviceListPanel.Children[0] is UIElement first)
        {
            first.Focus(FocusState.Programmatic);
        }
    }

    private static bool IsMouseButtonDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private async Task DisconnectInputCoreAsync()
    {
        try
        {
            var preferredId = _appSettingsService.PreferredInputDeviceId;
            var connectedDevices = _connectionService.ConnectedDevices;
            DeviceInformation? target = null;

            if (!string.IsNullOrWhiteSpace(preferredId))
            {
                for (var i = 0; i < connectedDevices.Count; i++)
                {
                    if (string.Equals(connectedDevices[i].Id, preferredId, StringComparison.OrdinalIgnoreCase))
                    {
                        target = connectedDevices[i];
                        break;
                    }
                }
            }

            target ??= connectedDevices.Count > 0 ? connectedDevices[0] : null;

            if (target != null) await _connectionService.DisconnectAsync(target);
        }
        catch { }
        finally { await RefreshDeviceSummaryAsync(); }
    }

    private static void MuteOutputCore()
    {
        keybd_event(0xAD, 0, 0, 0);
        keybd_event(0xAD, 0, 2, 0);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint lpPoint);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll")]
    private static partial void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    private sealed class HotkeyBindingConfig
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("hotkey")]
        public string Hotkey { get; set; } = string.Empty;

        [JsonPropertyName("deviceIds")]
        public List<string> DeviceIds { get; set; } = new();

        [JsonPropertyName("devicePhysicalIds")]
        public List<string> DevicePhysicalIds { get; set; } = new();

        [JsonPropertyName("deviceId")]
        public string? DeviceId { get; set; }
    }

    private sealed record DeviceRowSnapshot(string DeviceId, string DeviceName, string Hotkey, string IconGlyph, bool IsSelected, bool IsInput);
    private sealed record SelectedDeviceEntry(string DeviceId, string Hotkey, bool IsSelected);
    private sealed record RowMeta(string DeviceId, bool IsInput);

    private static string NormalizeHotkey(string? hotkey, string fallback)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return fallback;
        }

        return hotkey
            .Replace("\\u002B", "+", StringComparison.OrdinalIgnoreCase)
            .Replace("\\uFF0B", "+", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeInputSelectionId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return string.Empty;
        }

        if (DeviceIdentityHelper.TryParseSelectionId(id, out var isInput, out var physicalId))
        {
            return isInput ? DeviceIdentityHelper.BuildSelectionId(physicalId, true) : string.Empty;
        }

        return DeviceIdentityHelper.BuildSelectionId(id, true);
    }

    private static string NormalizeOutputSelectionId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return string.Empty;
        }

        if (DeviceIdentityHelper.TryParseSelectionId(id, out _, out var physicalId))
        {
            return DeviceIdentityHelper.BuildSelectionId(physicalId, false);
        }

        return DeviceIdentityHelper.BuildSelectionId(id, false);
    }

    private static string NormalizeLoadedDeviceId(string deviceId, bool isInput)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return string.Empty;
        }

        if (isInput)
        {
            return NormalizeInputSelectionId(deviceId);
        }

        return NormalizeOutputSelectionId(deviceId);
    }

    private DeviceInformation? FindConnectedDevice(string deviceId)
    {
        var connectedDevices = _connectionService.ConnectedDevices;
        for (var i = 0; i < connectedDevices.Count; i++)
        {
            if (string.Equals(connectedDevices[i].Id, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                return connectedDevices[i];
            }
        }

        return null;
    }

    private string FindNextConnectedDeviceId(string excludedDeviceId)
    {
        var connectedDevices = _connectionService.ConnectedDevices;
        for (var i = 0; i < connectedDevices.Count; i++)
        {
            var id = connectedDevices[i].Id;
            if (!string.IsNullOrWhiteSpace(id) && !string.Equals(id, excludedDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                return id;
            }
        }

        return string.Empty;
    }

    private async Task<DeviceInformation?> FindAudioDeviceAsync(string deviceId)
    {
        var devices = await _connectionService.FindAllAudioDevicesAsync();
        foreach (var device in devices)
        {
            if (string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                return device;
            }
        }

        return null;
    }

    private HashSet<string> BuildConnectedDeviceIdSet()
    {
        var connectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connectedDevices = _connectionService.ConnectedDevices;
        for (var i = 0; i < connectedDevices.Count; i++)
        {
            var id = connectedDevices[i].Id;
            if (!string.IsNullOrWhiteSpace(id))
            {
                connectedIds.Add(id);
            }
        }

        return connectedIds;
    }
}








