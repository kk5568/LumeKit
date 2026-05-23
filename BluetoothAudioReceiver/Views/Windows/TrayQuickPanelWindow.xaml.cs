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

namespace BluetoothAudioReceiver.Views.Windows;

public sealed partial class TrayQuickPanelWindow : WindowEx
{
    public static bool IsPanelVisible { get; private set; }
    // ========================= 可直接修改的设计参数（统一入口） =========================
    private const double TitleFontSize = 13; // 标题字号（音频管理）
    private const double RowFontSize = 12; // 设备名称字号
    private const double HotkeyFontSize = 12; // 快捷键字号
    private const double FooterHintFontSize = 11; // 底部说明文字字号
    private const string FooterHintTextValue = "点击设备可连接/断开"; // 底部说明文案
    private static readonly Thickness FooterHintPanelMargin = new(20, 10, 12, 10); // 底部说明区外边距（左,上,右,下）
    private const double FooterHintPanelSpacing = 6; // 底部说明区元素间距
    private static readonly Thickness FooterHintDividerMargin = new(0, 0, 0, 0); // 底部分割线外边距
    private const byte FooterHintTextAlpha = 200; // 底部说明文字透明度（0-255）
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

    private const byte RowBackgroundAlpha = 40; // 每行灰色圆角矩形背景透明度（0-255）
    private static readonly global::Windows.UI.Color RowBackgroundColor = global::Windows.UI.Color.FromArgb(RowBackgroundAlpha, 180, 180, 180); // 每行灰色圆角矩形背景色（悬浮态）
    private static readonly global::Windows.UI.Color RowIdleBackgroundColor = global::Windows.UI.Color.FromArgb(0, 180, 180, 180); // 每行默认背景色（非悬浮）
    private const double RowCornerRadius = 6; // 每行灰色圆角矩形圆角半径
    private static readonly Thickness RowOuterMargin = new(4, 2, 4, 2); // 每行外边距（左右边距/上下行距，较之前减少约40%）

    private const byte HotkeyBadgeBackgroundAlpha = 60; // 快捷键徽章背景透明度（0-255）
    private static readonly global::Windows.UI.Color HotkeyBadgeBackgroundColor = global::Windows.UI.Color.FromArgb(HotkeyBadgeBackgroundAlpha, 150, 150, 150); // 快捷键徽章背景色
    private static readonly global::Windows.UI.Color HotkeyBadgeInactiveBackgroundColor = global::Windows.UI.Color.FromArgb(80, 120, 120, 120); // 未选中行快捷键背景
    private const double HotkeyBadgeCornerRadius = 6; // 快捷键徽章圆角半径
    private static readonly Thickness HotkeyBadgePadding = new(8, 4, 8, 4); // 快捷键徽章内边距（左,上,右,下）
    private const string HotkeyPlaceholder = "--"; // 快捷键为空时占位文本

    private static readonly global::Windows.UI.Color DotActiveColor = global::Windows.UI.Color.FromArgb(255, 108, 203, 95); // 绿点激活颜色
    private static readonly global::Windows.UI.Color DotInactiveColor = global::Windows.UI.Color.FromArgb(0, 128, 128, 128); // 未选中不显示圆点（透明）
    // ============================================================================

    private const int ScreenMargin = 10;
    private const string InputHotkeyBindingsKey = "InputHotkeyBindingsV2";
    private const string OutputHotkeyBindingsKey = "OutputHotkeyBindingsV2";

    private readonly DispatcherQueueTimer _topMostRetryTimer;
    private readonly DispatcherQueueTimer _outsideClickTimer;
    private bool _lastLeftDown;
    private bool _lastRightDown;
    private DateTimeOffset _ignoreOutsideClickUntil = DateTimeOffset.MinValue;

    private readonly IAudioPlaybackConnectionService _connectionService;
    private readonly IAppSettingsService _appSettingsService;
    private readonly IAudioRouteHotkeyService _audioRouteHotkeyService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly Dictionary<string, List<Ellipse>> _dotMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Border, RowMeta> _rowMap = [];
    private bool _isSwitchingByClick;

    public TrayQuickPanelWindow()
    {
        InitializeComponent();

        IsTitleBarVisible = false;
        IsMinimizable = false;
        IsMaximizable = false;
        IsResizable = false;
        IsShownInSwitchers = false;

        try { SystemBackdrop = new MicaBackdrop(); } catch { }

        _connectionService = Ioc.Default.GetRequiredService<IAudioPlaybackConnectionService>();
        _appSettingsService = Ioc.Default.GetRequiredService<IAppSettingsService>();
        _audioRouteHotkeyService = Ioc.Default.GetRequiredService<IAudioRouteHotkeyService>();
        _localSettingsService = Ioc.Default.GetRequiredService<ILocalSettingsService>();

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

    public Task ShowPanelNearCursorAsync()
    {
        MoveNearCursorFast();
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

        // 先显示，再异步刷新列表，避免等待设备枚举导致首开延迟
        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await RefreshDeviceSummaryAsync();
            }
            catch { }
        });

        return Task.CompletedTask;
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
        RootCard.RequestedTheme = _appSettingsService.Theme == ElementTheme.Default
            ? (Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light)
            : _appSettingsService.Theme;
        InputSectionTitle.FontSize = TitleFontSize;
        InputSectionTitle.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        InputSectionTitle.Foreground = CreatePrimaryTextBrush();
        DeviceListPanel.Spacing = DeviceRowsSpacing;

        FooterHintPanel.Margin = FooterHintPanelMargin;
        FooterHintPanel.Spacing = FooterHintPanelSpacing;
        FooterHintDivider.Margin = FooterHintDividerMargin;
        FooterHintText.Text = FooterHintTextValue;
        FooterHintText.FontSize = FooterHintFontSize;
        FooterHintText.Foreground = new SolidColorBrush(global::Windows.UI.Color.FromArgb(
            FooterHintTextAlpha,
            160, 160, 160));
    }

    private Brush CreatePrimaryTextBrush()
    {
        var isDark = RootCard.RequestedTheme == ElementTheme.Dark;
        return new SolidColorBrush(isDark
            ? global::Windows.UI.Color.FromArgb(255, 245, 245, 245)
            : global::Windows.UI.Color.FromArgb(255, 20, 20, 20));
    }

    private Brush CreateSecondaryTextBrush()
    {
        var isDark = RootCard.RequestedTheme == ElementTheme.Dark;
        return new SolidColorBrush(isDark
            ? global::Windows.UI.Color.FromArgb(255, 200, 200, 200)
            : global::Windows.UI.Color.FromArgb(255, 75, 75, 75));
    }

    private static Brush CreateInactiveRowTextBrush()
        => new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 145, 145, 145));

    private async Task RefreshDeviceSummaryAsync()
    {
        DeviceListPanel.Children.Clear();
        _dotMap.Clear();
        _rowMap.Clear();

        var inputEntries = await GetSelectedDeviceEntriesFromHotkeyCardsAsync(
            InputHotkeyBindingsKey,
            _appSettingsService.PreferredInputDeviceId,
            NormalizeHotkey(_appSettingsService.InputHotkey, "Alt+1"));
        var outputEntries = await GetSelectedDeviceEntriesFromHotkeyCardsAsync(
            OutputHotkeyBindingsKey,
            _appSettingsService.PreferredOutputDeviceId,
            NormalizeHotkey(_appSettingsService.OutputHotkey, "Alt+2"));

        var connectedIds = new HashSet<string>(
            _connectionService.ConnectedDevices
                .Select(d => d.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);
        var preferredInputId = _appSettingsService.PreferredInputDeviceId ?? string.Empty;
        var preferredOutputId = _appSettingsService.PreferredOutputDeviceId ?? string.Empty;
        foreach (var entry in inputEntries)
        {
            var name = await GetInputDeviceDisplayNameAsync(entry.DeviceId);
            if (!string.IsNullOrWhiteSpace(name))
            {
                var isConnected = connectedIds.Contains(entry.DeviceId) &&
                                  string.Equals(entry.DeviceId, preferredInputId, StringComparison.OrdinalIgnoreCase);
                var visual = ResolveVisual(name);
                DeviceListPanel.Children.Add(CreateDeviceRow(entry.DeviceId, visual.DisplayName, entry.Hotkey, visual.Glyph, isSelected: isConnected, isInput: true));
            }
        }

        foreach (var entry in outputEntries)
        {
            var name = await GetOutputDeviceDisplayNameAsync(entry.DeviceId);
            if (!string.IsNullOrWhiteSpace(name))
            {
                var isConnected = string.Equals(entry.DeviceId, preferredOutputId, StringComparison.OrdinalIgnoreCase);
                var visual = ResolveVisual(name);
                DeviceListPanel.Children.Add(CreateDeviceRow(entry.DeviceId, visual.DisplayName, entry.Hotkey, visual.Glyph, isSelected: isConnected, isInput: false));
            }
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
            dots = [];
            _dotMap[deviceId] = dots;
        }
        dots.Add(dot);

        var leftIcon = new TextBlock
        {
            Text = iconGlyph,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"),
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
            Background = new SolidColorBrush(HotkeyBadgeInactiveBackgroundColor),
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
                    var connectedTarget = _connectionService.ConnectedDevices.FirstOrDefault(d =>
                        string.Equals(d.Id, meta.DeviceId, StringComparison.OrdinalIgnoreCase));
                    if (connectedTarget != null)
                    {
                        await _connectionService.DisconnectAsync(connectedTarget);
                    }

                    if (string.Equals(_appSettingsService.PreferredInputDeviceId, meta.DeviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        await _appSettingsService.SetPreferredInputDeviceIdAsync(string.Empty);
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
                    var currentOutput = _appSettingsService.PreferredOutputDeviceId ?? string.Empty;
                    _audioRouteHotkeyService.UpdateTargets(meta.DeviceId, currentOutput);
                    _audioRouteHotkeyService.UpdateTargetCandidates([meta.DeviceId], []);
                    await _audioRouteHotkeyService.TriggerInputRouteAsync();
                }
                else
                {
                    var currentInput = _appSettingsService.PreferredInputDeviceId ?? string.Empty;
                    _audioRouteHotkeyService.UpdateTargets(currentInput, meta.DeviceId);
                    _audioRouteHotkeyService.UpdateTargetCandidates([], [meta.DeviceId]);
                    await _audioRouteHotkeyService.TriggerOutputRouteAsync();
                }
            }

            RefreshDotIndicatorsOnly();
        }
        catch { }
        finally
        {
            _isSwitchingByClick = false;
        }
    }

    private void RefreshDotIndicatorsOnly()
    {
        var connectedIds = new HashSet<string>(
            _connectionService.ConnectedDevices
                .Select(d => d.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);
        var preferredInputId = _appSettingsService.PreferredInputDeviceId ?? string.Empty;
        var preferredOutputId = _appSettingsService.PreferredOutputDeviceId ?? string.Empty;

        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(preferredInputId) && connectedIds.Contains(preferredInputId))
        {
            active.Add(preferredInputId);
        }
        if (!string.IsNullOrWhiteSpace(preferredOutputId))
        {
            active.Add(preferredOutputId);
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
            else if (child is Border badge && badge.Child is TextBlock hk)
            {
                badge.Background = new SolidColorBrush(isActive ? HotkeyBadgeBackgroundColor : HotkeyBadgeInactiveBackgroundColor);
                hk.Foreground = isActive ? activeSecondary : inactive;
            }
        }
    }

    private static (string DisplayName, string Glyph) ResolveVisual(string rawName)
    {
        var name = string.IsNullOrWhiteSpace(rawName) ? "未知设备" : rawName.Trim();
        var lower = name.ToLowerInvariant();

        var isTablet = name.Contains("平板", StringComparison.OrdinalIgnoreCase) || lower.Contains("pad") || lower.Contains("tablet");
        var isPhoneKeyword = name.Contains("手机", StringComparison.OrdinalIgnoreCase) || lower.Contains("phone") || lower.Contains("iphone");
        var isPhoneBrandModel = (lower.Contains("xiaomi") || lower.Contains("redmi") || lower.Contains("huawei") || lower.Contains("honor") || lower.Contains("oppo") || lower.Contains("vivo"))
            && !isTablet;

        if (isTablet)
        {
            return (name, "\uE70A");
        }

        if (isPhoneKeyword || isPhoneBrandModel)
        {
            return (name, "\uE8EA");
        }

        if (name.Contains("扬声器", StringComparison.OrdinalIgnoreCase) || lower.Contains("speaker") || lower.Contains("realtek"))
        {
            return (name, "\uE7F5");
        }

        if (name.Contains("耳机", StringComparison.OrdinalIgnoreCase) || lower.Contains("headphone"))
        {
            return (name, "\uE720");
        }

        if (name.Contains("显示器", StringComparison.OrdinalIgnoreCase) || lower.Contains("hdmi") || lower.Contains("monitor"))
        {
            return (name, "\uE7F8");
        }

        // 未明确识别类型时，保持原始设备名
        return (name, "\uE7F5");
    }

    private bool IsRowCurrentlyActive(RowMeta meta)
    {
        if (meta.IsInput)
        {
            return string.Equals(_appSettingsService.PreferredInputDeviceId, meta.DeviceId, StringComparison.OrdinalIgnoreCase)
                && _connectionService.ConnectedDevices.Any(d => string.Equals(d.Id, meta.DeviceId, StringComparison.OrdinalIgnoreCase));
        }

        return string.Equals(_appSettingsService.PreferredOutputDeviceId, meta.DeviceId, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<List<SelectedDeviceEntry>> GetSelectedDeviceEntriesFromHotkeyCardsAsync(string key, string fallbackDeviceId, string fallbackHotkey)
    {
        try
        {
            var configs = await _localSettingsService.ReadSettingAsync(key, new List<HotkeyBindingConfig>()) ?? [];
            var entries = new List<SelectedDeviceEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var config in configs.Where(c => c.DeviceIds is { Count: > 0 }))
            {
                var hotkey = NormalizeHotkey(config.Hotkey, fallbackHotkey);
                foreach (var id in config.DeviceIds)
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
                if (!seen.Add(id)) continue;
                entries.Add(new SelectedDeviceEntry(id, hotkey, true));
            }

            if (entries.Count > 0) return entries;
        }
        catch { }

        return string.IsNullOrWhiteSpace(fallbackDeviceId)
            ? []
            : [new SelectedDeviceEntry(fallbackDeviceId, fallbackHotkey, false)];
    }

    private async Task<string> GetOutputDeviceDisplayNameAsync(string preferredOutputId)
    {
        try
        {
            var devices = await _audioRouteHotkeyService.GetOutputDevicesAsync();
            var found = devices.FirstOrDefault(d => string.Equals(d.Id, preferredOutputId, StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(found?.Name) ? preferredOutputId : found.Name;
        }
        catch { return preferredOutputId; }
    }

    private async Task<string> GetInputDeviceDisplayNameAsync(string preferredInputId)
    {
        try
        {
            var allInputs = await _connectionService.FindAllAudioDevicesAsync();
            var found = allInputs.FirstOrDefault(d => string.Equals(d.Id, preferredInputId, StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(found?.Name) ? preferredInputId : found.Name;
        }
        catch { return preferredInputId; }
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
            var target = _connectionService.ConnectedDevices.FirstOrDefault(d =>
                string.Equals(d.Id, preferredId, StringComparison.OrdinalIgnoreCase))
                ?? _connectionService.ConnectedDevices.FirstOrDefault();

            if (target != null) await _connectionService.DisconnectAsync(target);
        }
        catch { }
        finally { await RefreshDeviceSummaryAsync(); }
    }

    private void MuteOutputCore()
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
        public List<string> DeviceIds { get; set; } = [];

        [JsonPropertyName("deviceId")]
        public string? DeviceId { get; set; }
    }

    private sealed record SelectedDeviceEntry(string DeviceId, string Hotkey, bool IsSelected);
    private sealed record RowMeta(string DeviceId, bool IsInput);

    private static string NormalizeHotkey(string? hotkey, string fallback)
        => string.IsNullOrWhiteSpace(hotkey) ? fallback : hotkey;
}



