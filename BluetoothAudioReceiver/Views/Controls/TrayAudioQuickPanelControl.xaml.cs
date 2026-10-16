using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Devices.Enumeration;
using Windows.UI.ViewManagement;
using BluetoothAudioReceiver.Core.Helpers;

namespace BluetoothAudioReceiver.Views.Controls;

/// <summary>
/// 托盘左键音频切换浮层：按「输入设备 / 输出设备」两段显示卡片，一行 3 张。
/// 卡片：第一行 图标 + 类型大字（并排），第二行 设备名称；选中态=蓝色描边 + 右上角蓝点。
/// 外观沿用 DeskBox 浮层设计值（见 Styles/DeskBoxMenuResources.xaml）。
/// </summary>
public sealed partial class TrayAudioQuickPanelControl : UserControl
{
    // 供 NotificationService 判断托盘浮层是否正在显示（对应原来的 TrayQuickPanelWindow.IsPanelVisible）。
    public static bool IsOpen { get; internal set; }

    private const int CardsPerRow = 1;
    private const double CardHeight = 46;
    private const double CardCornerRadius = 6;
    private const double CardSpacing = 8;
    private const double CardBorderThickness = 1.5;      // 未选中的灰色描边
    private const double CardActiveBorderThickness = 2;  // 选中：蓝色描边（右上角蓝点已去掉）
    private const double CardContentMargin = 6.6;
    private const double CardIconFontSize = 18;
    private const double CardTypeSpacing = 6.6;
    private const double CardTitleFontSize = 12;
    private const double CardNameFontSize = 12.375;
    private const double SectionHeaderFontSize = 12;
    private const double SectionIconFontSize = 14;

    private const string InputSectionTitle = "\u8F93\u5165\u8BBE\u5907";   // 输入设备
    private const string OutputSectionTitle = "\u8F93\u51FA\u8BBE\u5907";  // 输出设备
    private const string InputSectionGlyph = "\uE720";                     // Microphone
    private const string OutputSectionGlyph = "\uE767";                    // Speaker
    private const string MicrophoneGlyph = "\uE720";
    private const string DisplayGlyph = "\uE7F4";
    private const string DefaultDeviceLabel = "\u97F3\u9891\u8BBE\u5907";  // 音频设备

    private const string InputHotkeyBindingsKey = "InputHotkeyBindingsV2";
    private const string OutputHotkeyBindingsKey = "OutputHotkeyBindingsV2";

    // 选中描边/蓝点统一用系统强调蓝。
    private static readonly global::Windows.UI.Color AccentColor =
        global::Windows.UI.Color.FromArgb(0xFF, 0x00, 0x78, 0xD4);

    // 颜色与 Styles/DeskBoxMenuResources.xaml 的 Widget* 主题值保持一致。
    private static readonly CardPalette DarkPalette = new(
        global::Windows.UI.Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF),
        global::Windows.UI.Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF),
        global::Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF),
        global::Windows.UI.Color.FromArgb(0xFF, 0xF5, 0xF5, 0xF5),
        global::Windows.UI.Color.FromArgb(0xFF, 0xA5, 0xA5, 0xA5));

    private static readonly CardPalette LightPalette = new(
        global::Windows.UI.Color.FromArgb(0x0F, 0x00, 0x00, 0x00),
        global::Windows.UI.Color.FromArgb(0x16, 0x00, 0x00, 0x00),
        global::Windows.UI.Color.FromArgb(0x1A, 0x00, 0x00, 0x00),
        global::Windows.UI.Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A),
        global::Windows.UI.Color.FromArgb(0xFF, 0x5A, 0x5A, 0x5A));

    private readonly IAudioPlaybackConnectionService _connectionService;
    private readonly IAppSettingsService _appSettingsService;
    private readonly IAudioRouteHotkeyService _audioRouteHotkeyService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly UISettings _uiSettings = new();
    private readonly Dictionary<Border, CardItem> _cardMap = [];

    private CardPalette _palette = DarkPalette;
    private string _currentOutputSelectionId = string.Empty;
    private bool _isSwitchingByClick;

    // 面板打开期间把两段卡片的顺序定住：点卡片刷新只换描边，卡片不再窜位，关掉重开才重排。
    private List<string>? _frozenInputOrder;
    private List<string>? _frozenOutputOrder;

    /// <summary>点「×」时触发，由宿主编排关闭浮层。</summary>
    public event EventHandler? CloseRequested;

    public TrayAudioQuickPanelControl()
    {
        InitializeComponent();
        _connectionService = Ioc.Default.GetRequiredService<IAudioPlaybackConnectionService>();
        _appSettingsService = Ioc.Default.GetRequiredService<IAppSettingsService>();
        _audioRouteHotkeyService = Ioc.Default.GetRequiredService<IAudioRouteHotkeyService>();
        _localSettingsService = Ioc.Default.GetRequiredService<ILocalSettingsService>();

        CloseButton.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        Loaded += (_, _) => ApplyTheme();
    }

    /// <summary>重新读取输入/输出设备并重画卡片。</summary>
    public async Task RefreshAsync()
    {
        ApplyTheme();

        var inputDevicesTask = _connectionService.FindAllInputRouteDevicesAsync();
        var outputDevicesTask = _audioRouteHotkeyService.GetOutputDevicesAsync();
        var defaultOutputTask = _audioRouteHotkeyService.GetCurrentDefaultOutputEndpointIdAsync();
        var inputIdsTask = GetSelectedDeviceIdsAsync(
            InputHotkeyBindingsKey,
            _appSettingsService.PreferredInputDeviceId,
            isInput: true);
        var outputIdsTask = GetSelectedDeviceIdsAsync(
            OutputHotkeyBindingsKey,
            _appSettingsService.PreferredOutputDeviceId,
            isInput: false);

        await Task.WhenAll(
            inputDevicesTask,
            outputDevicesTask,
            defaultOutputTask,
            inputIdsTask,
            outputIdsTask);

        var inputLookup = BuildInputDeviceLookup(inputDevicesTask.Result);
        var outputLookup = BuildOutputDeviceLookup(outputDevicesTask.Result);

        var currentDefaultOutputEndpointId = NormalizeOutputSelectionId(defaultOutputTask.Result);
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
                .Select(device => device.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);

        // 输入段以「系统当前能看到的输入设备」为准：同一条绑定里既记了原生设备路径、
        // 又记了物理编号，按物理编号去重后一台设备只出一张卡。
        var inputCards = new List<CardItem>();
        var rememberedInputIds = new HashSet<string>(inputIdsTask.Result, StringComparer.OrdinalIgnoreCase);
        var showAllInputs = rememberedInputIds.Count == 0;   // 没绑定也没首选时，显示当前能连的输入设备
        var seenInputDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var inputDevices = inputDevicesTask.Result
            .Where(device => !string.IsNullOrWhiteSpace(device.Id))
            .GroupBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());

        foreach (var device in inputDevices)
        {
            var name = ResolveDeviceName(device.Id, inputLookup);
            if (string.IsNullOrWhiteSpace(name) ||
                (!showAllInputs && !MatchesAnyId(device, rememberedInputIds)))
            {
                continue;
            }

            var identity = DeviceIdentityHelper.GetPhysicalDeviceId(device);
            if (string.IsNullOrWhiteSpace(identity))
            {
                identity = device.Id;
            }

            if (!seenInputDevices.Add(identity))
            {
                continue;
            }

            var (label, glyph) = ResolveCardVisual(name, isInput: true);
            inputCards.Add(new CardItem(
                device.Id,
                label,
                name,
                glyph,
                MatchesAnyId(device, connectedIds),
                IsInput: true));
        }

        var outputCards = new List<CardItem>();
        foreach (var deviceId in outputIdsTask.Result)
        {
            var name = ResolveDeviceName(deviceId, outputLookup);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var (label, glyph) = ResolveCardVisual(name, isInput: false);
            outputCards.Add(new CardItem(
                deviceId,
                label,
                name,
                glyph,
                string.Equals(deviceId, currentOutputDeviceId, StringComparison.OrdinalIgnoreCase),
                IsInput: false));
        }

        var recentIds = DevicePriorityHelper.GetRecentDeviceIds(_localSettingsService);
        var orderedInputs = OrderCards(inputCards, recentIds);
        var orderedOutputs = OrderCards(outputCards, recentIds);

        // 打开后的第一次刷新把顺序定住；之后点卡片触发的刷新只更新描边，卡片不再窜位。
        var frozenInputs = _frozenInputOrder ??= orderedInputs.Select(card => card.DeviceId).ToList();
        var frozenOutputs = _frozenOutputOrder ??= orderedOutputs.Select(card => card.DeviceId).ToList();

        RenderSections(
            ApplyFrozenOrder(orderedInputs, frozenInputs),
            ApplyFrozenOrder(orderedOutputs, frozenOutputs));
    }

    private static IReadOnlyList<CardItem> OrderCards(
        IReadOnlyList<CardItem> cards,
        IReadOnlyList<string> recentIds)
    {
        return DevicePriorityHelper.OrderDevices(
            cards,
            card => card.DeviceId,
            card => card.DeviceName,
            recentIds,
            card => card.IsActive);
    }

    /// <summary>每次打开面板前调用：清掉上次定住的顺序，重新按最新优先级排。</summary>
    public void ResetOrderSnapshot()
    {
        _frozenInputOrder = null;
        _frozenOutputOrder = null;
    }

    /// <summary>把刷新出来的卡片按打开时定住的顺序摆；期间新出现的设备排到该段末尾。</summary>
    private static IReadOnlyList<CardItem> ApplyFrozenOrder(
        IReadOnlyList<CardItem> cards,
        List<string> frozenOrder)
    {
        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < frozenOrder.Count; index++)
        {
            positions.TryAdd(frozenOrder[index], index);
        }

        return cards
            .Select((card, index) => (
                Card: card,
                Index: index,
                Position: positions.TryGetValue(card.DeviceId, out var position) ? position : int.MaxValue))
            .OrderBy(entry => entry.Position)
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Card)
            .ToList();
    }

    private void RenderSections(IReadOnlyList<CardItem> inputCards, IReadOnlyList<CardItem> outputCards)
    {
        SectionsPanel.Children.Clear();
        _cardMap.Clear();
        SectionsPanel.Children.Add(CreateSection(InputSectionTitle, InputSectionGlyph, inputCards));
        SectionsPanel.Children.Add(CreateSection(OutputSectionTitle, OutputSectionGlyph, outputCards));
    }

    private StackPanel CreateSection(string title, string glyph, IReadOnlyList<CardItem> cards)
    {
        var section = new StackPanel { Spacing = (double)CardSpacing };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };
        header.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = SectionIconFontSize,
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = SectionHeaderFontSize,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        section.Children.Add(header);

        if (cards.Count == 0)
        {
            section.Children.Add(new TextBlock
            {
                Text = "\u6682\u65E0\u8BBE\u5907",   // 暂无设备
                FontSize = CardNameFontSize,
                Foreground = new SolidColorBrush(_palette.SecondaryText)
            });
            return section;
        }

        for (var index = 0; index < cards.Count; index += CardsPerRow)
        {
            var row = new Grid { ColumnSpacing = CardSpacing };
            for (var column = 0; column < CardsPerRow; column++)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });
            }

            for (var column = 0; column < CardsPerRow && index + column < cards.Count; column++)
            {
                var card = CreateCard(cards[index + column]);
                Grid.SetColumn(card, column);
                row.Children.Add(card);
            }

            section.Children.Add(row);
        }

        return section;
    }

    private Border CreateCard(CardItem item)
    {
        var card = new Border
        {
            Height = CardHeight,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(CardCornerRadius),
            BorderThickness = new Thickness(item.IsActive ? CardActiveBorderThickness : CardBorderThickness),
            Background = new SolidColorBrush(_palette.CardFill),
            BorderBrush = new SolidColorBrush(item.IsActive ? AccentColor : _palette.CardStroke)
        };
        _cardMap[card] = item;
        ToolTipService.SetToolTip(card, item.DeviceName);

        // 一排一个的卡片：只有一行 —— 设备图标 + 设备名称。
        var content = new Grid
        {
            Margin = new Thickness(CardContentMargin, 4, CardContentMargin, 4),
            ColumnSpacing = CardTypeSpacing,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var textBrush = new SolidColorBrush(item.IsActive ? _palette.PrimaryText : _palette.SecondaryText);

        content.Children.Add(new FontIcon
        {
            Glyph = item.Glyph,
            FontSize = CardIconFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = textBrush
        });

        // 设备名称：紧跟图标，单行显示；选中时与图标同色。
        var name = new TextBlock
        {
            Text = item.DeviceName,
            FontSize = CardNameFontSize,
            TextAlignment = TextAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = textBrush
        };
        Grid.SetColumn(name, 1);
        content.Children.Add(name);

        card.Child = content;
        card.PointerEntered += (_, _) => card.Background = new SolidColorBrush(_palette.CardHover);
        card.PointerExited += (_, _) => card.Background = new SolidColorBrush(_palette.CardFill);
        card.Tapped += OnCardTapped;
        return card;
    }

    private async void OnCardTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not Border card ||
            !_cardMap.TryGetValue(card, out var item) ||
            _isSwitchingByClick)
        {
            return;
        }

        _isSwitchingByClick = true;
        try
        {
            if (IsCardCurrentlyActive(item))
            {
                await DeactivateAsync(item);
            }
            else
            {
                await ActivateAsync(item);
            }

            await RefreshAsync();
        }
        catch
        {
            // 设备可能在切换过程中被移除，忽略并保持面板可用。
        }
        finally
        {
            _isSwitchingByClick = false;
        }
    }

    private async Task DeactivateAsync(CardItem item)
    {
        if (item.IsInput)
        {
            var connectedTarget = FindConnectedDevice(item.DeviceId);
            if (connectedTarget is not null)
            {
                await _connectionService.DisconnectAsync(connectedTarget);
            }

            if (string.Equals(_appSettingsService.PreferredInputDeviceId, item.DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                await _appSettingsService.SetPreferredInputDeviceIdAsync(FindNextConnectedDeviceId(item.DeviceId));
            }

            return;
        }

        if (string.Equals(_appSettingsService.PreferredOutputDeviceId, item.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            await _appSettingsService.SetPreferredOutputDeviceIdAsync(string.Empty);
        }
    }

    private async Task ActivateAsync(CardItem item)
    {
        if (item.IsInput)
        {
            var device = await FindAudioDeviceAsync(item.DeviceId);
            if (device is not null && await _connectionService.ConnectAsync(device))
            {
                await _appSettingsService.SetPreferredInputDeviceIdAsync(item.DeviceId);
                await DevicePriorityHelper.MarkDeviceAsUsedAsync(_localSettingsService, device.Id);
            }

            return;
        }

        var currentInput = _appSettingsService.PreferredInputDeviceId ?? string.Empty;
        await _audioRouteHotkeyService.UpdateTargetsAsync(currentInput, item.DeviceId);
        _audioRouteHotkeyService.UpdateTargetCandidates([], [item.DeviceId]);
        // 面板里手动切换一律不弹悬浮通知（通知会顶掉这个浮窗）。
        await _audioRouteHotkeyService.TriggerOutputRouteAsync(suppressNotification: true);
    }

    private bool IsCardCurrentlyActive(CardItem item)
    {
        if (item.IsInput)
        {
            return FindConnectedDevice(item.DeviceId) is not null;
        }

        return string.Equals(_currentOutputSelectionId, item.DeviceId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 由独立窗口承载时调用：让浮层铺满窗口，圆角和描边交给窗口外壳，
    /// 卡片画法、配色、交互和以前完全一样。
    /// </summary>
    public void FillWindowSurface()
    {
        SurfaceRoot.Width = double.NaN;
        SurfaceRoot.MaxHeight = double.PositiveInfinity;
        SurfaceRoot.CornerRadius = new CornerRadius(0);
        SurfaceRoot.BorderThickness = new Thickness(0);
        SurfaceRoot.HorizontalAlignment = HorizontalAlignment.Stretch;
        SurfaceRoot.VerticalAlignment = VerticalAlignment.Stretch;
    }

    /// <summary>
    /// 由公共浮窗（DeskBoxFloatingWindow）承载时调用：把自己的底色、圆角和描边交还给窗口外壳，
    /// 让材质、圆角、阴影和进出场动画由浮窗统一提供；卡片画法、配色和交互不变。
    /// 宽度保留自己的值，浮窗按这个宽度算窗口尺寸和位置。
    /// </summary>
    public void PrepareForHost()
    {
        SurfaceRoot.Background = null;
        SurfaceRoot.BorderThickness = new Thickness(0);
        SurfaceRoot.CornerRadius = new CornerRadius(0);
    }

    private void ApplyTheme()
    {
        var theme = ResolveCurrentTheme();
        _palette = theme == ElementTheme.Dark ? DarkPalette : LightPalette;
        SurfaceRoot.RequestedTheme = theme;
    }

    private ElementTheme ResolveCurrentTheme()
    {
        if (_appSettingsService.Theme != ElementTheme.Default)
        {
            return _appSettingsService.Theme;
        }

        var background = _uiSettings.GetColorValue(UIColorType.Background);
        var luminance = (0.2126 * background.R) + (0.7152 * background.G) + (0.0722 * background.B);
        return luminance < 128 ? ElementTheme.Dark : ElementTheme.Light;
    }

    private static Dictionary<string, string> BuildInputDeviceLookup(IEnumerable<DeviceInformation> devices)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in devices.Where(device => !string.IsNullOrWhiteSpace(device.Id)))
        {
            var name = string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name;
            lookup[device.Id] = name;

            var physicalId = DeviceIdentityHelper.GetPhysicalDeviceId(device);
            if (!string.IsNullOrWhiteSpace(physicalId))
            {
                lookup.TryAdd(physicalId, name);
                lookup.TryAdd(DeviceIdentityHelper.BuildSelectionId(physicalId, true), name);
            }
        }

        return lookup;
    }

    private static Dictionary<string, string> BuildOutputDeviceLookup(IEnumerable<AudioOutputDeviceItem> devices)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in devices.Where(device => !string.IsNullOrWhiteSpace(device.Id)))
        {
            lookup[device.Id] = string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name;
        }

        return lookup;
    }

    private static string? ResolveDeviceName(string deviceId, Dictionary<string, string> lookup)
    {
        if (lookup.TryGetValue(deviceId, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return LooksLikeRawDevicePath(name) ? null : name;
        }

        return null;
    }

    /// <summary>枚举设备的各种 id 形态：原生设备路径、物理编号、输入/输出角色选择 id。</summary>
    private static IEnumerable<string> EnumerateDeviceIds(DeviceInformation device)
    {
        if (!string.IsNullOrWhiteSpace(device.Id))
        {
            yield return device.Id;
        }

        var physicalId = DeviceIdentityHelper.GetPhysicalDeviceId(device);
        if (string.IsNullOrWhiteSpace(physicalId))
        {
            yield break;
        }

        yield return physicalId;
        yield return DeviceIdentityHelper.BuildSelectionId(physicalId, true);
        yield return DeviceIdentityHelper.BuildSelectionId(physicalId, false);
    }

    /// <summary>设备的任意一种 id 形态命中给定集合，即算命中。</summary>
    private static bool MatchesAnyId(DeviceInformation device, HashSet<string> ids)
    {
        if (ids.Count == 0)
        {
            return false;
        }

        foreach (var id in EnumerateDeviceIds(device))
        {
            if (!string.IsNullOrWhiteSpace(id) && ids.Contains(id))
            {
                return true;
            }
        }

        return false;
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

    /// <summary>按设备名猜类型，返回卡片大字标签和图标；猜不出时用「音频设备」。</summary>
    private static (string Label, string Glyph) ResolveCardVisual(string rawName, bool isInput)
    {
        var name = string.IsNullOrWhiteSpace(rawName) ? string.Empty : rawName.Trim();
        var lower = name.ToLowerInvariant();

        var isTablet = name.Contains("\u5E73\u677F", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("pad")
            || lower.Contains("tablet");
        var isHeadset = name.Contains("\u8033\u673A", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("headphone")
            || lower.Contains("headset")
            || lower.Contains("earbud")
            || lower.Contains("earbuds")
            || lower.Contains("buds")
            || lower.Contains("airpods");
        var isPhoneKeyword = name.Contains("\u624B\u673A", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("phone")
            || lower.Contains("iphone");
        var isPhoneBrandModel = (lower.Contains("xiaomi")
            || lower.Contains("redmi")
            || lower.Contains("huawei")
            || lower.Contains("honor")
            || lower.Contains("oppo")
            || lower.Contains("vivo"))
            && !isTablet
            && !isHeadset;
        var isDisplay = name.Contains("\u663E\u793A\u5668", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("monitor")
            || lower.Contains("display")
            || lower.Contains("hdmi");

        if (isHeadset)
        {
            return (isInput ? "\u8033\u9EA6" : DeviceVisualHelper.HeadsetLabel, DeviceVisualHelper.HeadsetGlyph);
        }

        if (isTablet)
        {
            return (DeviceVisualHelper.TabletLabel, DeviceVisualHelper.TabletGlyph);
        }

        if (isPhoneKeyword || isPhoneBrandModel)
        {
            return (DeviceVisualHelper.PhoneLabel, DeviceVisualHelper.PhoneGlyph);
        }

        if (isInput)
        {
            if (lower.Contains("line in") || name.Contains("\u7EBF\u8DEF", StringComparison.OrdinalIgnoreCase))
            {
                return ("\u7EBF\u8DEF\u8F93\u5165", MicrophoneGlyph);   // 线路输入
            }

            if (lower.Contains("virtual") || name.Contains("\u865A\u62DF", StringComparison.OrdinalIgnoreCase))
            {
                return ("\u865A\u62DF\u9EA6\u514B\u98CE", MicrophoneGlyph);  // 虚拟麦克风
            }

            if (lower.Contains("mic") || name.Contains("\u9EA6\u514B\u98CE", StringComparison.OrdinalIgnoreCase))
            {
                return ("\u9EA6\u514B\u98CE", MicrophoneGlyph);   // 麦克风
            }

            return (DefaultDeviceLabel, MicrophoneGlyph);
        }

        if (isDisplay)
        {
            return ("\u663E\u793A\u5668", DisplayGlyph);   // 显示器
        }

        return (DeviceVisualHelper.SpeakerLabel, DeviceVisualHelper.SpeakerGlyph);
    }

    private static string? ResolveCurrentOutputDeviceId(
        List<AudioOutputDeviceItem> outputDevices,
        string currentDefaultOutputEndpointId)
    {
        if (outputDevices.Count == 0 || string.IsNullOrWhiteSpace(currentDefaultOutputEndpointId))
        {
            return null;
        }

        foreach (var device in outputDevices)
        {
            if (string.Equals(NormalizeOutputSelectionId(device.EndpointId), currentDefaultOutputEndpointId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizeOutputSelectionId(device.Id), currentDefaultOutputEndpointId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizeOutputSelectionId(device.PhysicalId), currentDefaultOutputEndpointId, StringComparison.OrdinalIgnoreCase))
            {
                return device.Id;
            }
        }

        return null;
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

    /// <summary>取面板要显示的设备集合：优先用已绑定快捷键的设备，其次用主界面选的设备。</summary>
    private async Task<List<string>> GetSelectedDeviceIdsAsync(string key, string? fallbackDeviceId, bool isInput)
    {
        var deviceIds = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var configs = await _localSettingsService.ReadSettingAsync(key, Array.Empty<HotkeyBindingConfig>())
                ?? Array.Empty<HotkeyBindingConfig>();

            foreach (var config in configs)
            {
                var ids = (config.DeviceIds ?? []).Concat(
                    (config.DevicePhysicalIds ?? []).Select(physicalId =>
                        DeviceIdentityHelper.BuildSelectionId(physicalId, isInput)));
                foreach (var id in ids)
                {
                    if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                    {
                        continue;
                    }

                    deviceIds.Add(id);
                }

                if ((config.DeviceIds is null || config.DeviceIds.Count == 0) &&
                    !string.IsNullOrWhiteSpace(config.DeviceId) &&
                    seen.Add(config.DeviceId!))
                {
                    deviceIds.Add(config.DeviceId!);
                }
            }
        }
        catch
        {
            // 快捷键绑定读不出来时退回下面的首选设备。
        }

        if (deviceIds.Count == 0 && !string.IsNullOrWhiteSpace(fallbackDeviceId))
        {
            deviceIds.Add(fallbackDeviceId);
        }

        return deviceIds;
    }

    private DeviceInformation? FindConnectedDevice(string deviceId)
    {
        var connectedDevices = _connectionService.ConnectedDevices;
        for (var index = 0; index < connectedDevices.Count; index++)
        {
            foreach (var id in EnumerateDeviceIds(connectedDevices[index]))
            {
                if (!string.IsNullOrWhiteSpace(id) &&
                    string.Equals(id, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return connectedDevices[index];
                }
            }
        }

        return null;
    }

    private string FindNextConnectedDeviceId(string excludedDeviceId)
    {
        var connectedDevices = _connectionService.ConnectedDevices;
        for (var index = 0; index < connectedDevices.Count; index++)
        {
            var id = connectedDevices[index].Id;
            if (!string.IsNullOrWhiteSpace(id) &&
                !string.Equals(id, excludedDeviceId, StringComparison.OrdinalIgnoreCase))
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

    private sealed record CardPalette(
        global::Windows.UI.Color CardFill,
        global::Windows.UI.Color CardStroke,
        global::Windows.UI.Color CardHover,
        global::Windows.UI.Color PrimaryText,
        global::Windows.UI.Color SecondaryText);

    private sealed record CardItem(
        string DeviceId,
        string TypeLabel,
        string DeviceName,
        string Glyph,
        bool IsActive,
        bool IsInput);

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
}
