using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BluetoothAudioReceiver.Contracts.Services;
using BluetoothAudioReceiver.Infrastructure;
using BluetoothAudioReceiver.ViewModels.Pages;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace BluetoothAudioReceiver.Views.Pages;

public sealed partial class AutomationEditorPage : Page
{
    private const string NotConfiguredText = "未设置";
    private static readonly string RouteLogPath = BuildAutomationEditorRouteLogPath();

    public AutomationBuilderPageViewModel ViewModel { get; }

    private readonly IAudioRouteHotkeyService _hotkeyService;
    private Action<VirtualKey, VirtualKeyModifiers>? _shortcutCaptureHandler;
    private AutomationNode? _capturingHotkeyNode;

    public AutomationEditorPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<AutomationBuilderPageViewModel>();
        _hotkeyService = Ioc.Default.GetRequiredService<IAudioRouteHotkeyService>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadEditorAsync(e.Parameter as Guid?).Forget(nameof(AutomationEditorPage));
        WriteAutomationEditorRouteSnapshot();
    }

    private async Task LoadEditorAsync(Guid? pipelineId)
    {
        await Task.WhenAll(
            ViewModel.RefreshOutputDevicesAsync(),
            ViewModel.RefreshInputDevicesAsync());

        ViewModel.LoadPipeline(pipelineId);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        StopShortcutCapture();
        base.OnNavigatedFrom(e);
    }

    private static string BuildAutomationEditorRouteLogPath()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Constants.BluetoothAudioReceiver,
            Constants.ApplicationDataFolder,
            Constants.LogsFolder);

        Directory.CreateDirectory(logDirectory);
        return Path.Combine(logDirectory, "automation-editor-route.log");
    }

    private void WriteAutomationEditorRouteSnapshot()
    {
        using var writer = new StreamWriter(RouteLogPath, append: false);
        writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Automation editor route snapshot");

        foreach (var section in ViewModel.Sections)
        {
            writer.WriteLine($"Section={AutomationDisplayTextConverter.ToDisplayText(section.HeaderNode.Title)}");

            foreach (var node in section.ActionNodes)
            {
                var title = AutomationDisplayTextConverter.ToDisplayText(node.Title);
                var visualType = AutomationActionVisualVisibilityConverter.ResolveVisualType(node);
                writer.WriteLine(
                    $"  title={title}, rawTitle={node.Title}, kind={node.Kind}, parameterKind={node.ParameterKind}, visualType={visualType}, subtitle={node.Subtitle}, rawValue={node.RawValue}");
            }
        }
    }

    private void AddSyntax_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.AddSyntaxNode();
    }

    private async void ReplaceSyntax_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not AutomationNode node)
        {
            return;
        }

        var selectedSyntax = await ShowSyntaxPickerAsync(node.Title);
        if (!string.IsNullOrWhiteSpace(selectedSyntax))
        {
            node.Title = selectedSyntax;
        }
    }

    private async void AddAction_Click(object sender, RoutedEventArgs e)
    {
        var selectedAction = await ShowActionPickerAsync();
        if (!string.IsNullOrWhiteSpace(selectedAction))
        {
            ViewModel.AddActionNode(selectedAction);
        }
    }

    private async void AddSectionAction_Click(object sender, RoutedEventArgs e)
    {
        var selectedAction = await ShowActionPickerAsync();
        if (string.IsNullOrWhiteSpace(selectedAction))
        {
            return;
        }

        if (sender is FrameworkElement element && element.DataContext is AutomationSectionViewModel section)
        {
            ViewModel.InsertActionNodeAfter(section.HeaderNode, selectedAction);
            return;
        }

        ViewModel.AddActionNode(selectedAction);
    }

    private async void AddInlineSectionItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not AutomationSectionViewModel section)
        {
            return;
        }

        var sectionTitle = AutomationDisplayTextConverter.ToDisplayText(section.HeaderNode.Title);
        if (sectionTitle == "如果")
        {
            var selectedTrigger = await ShowTriggerPickerAsync();
            if (!string.IsNullOrWhiteSpace(selectedTrigger))
            {
                ViewModel.InsertTriggerNodeAfter(section.HeaderNode);
                var insertedNode = ViewModel.Nodes
                    .SkipWhile(node => !ReferenceEquals(node, section.HeaderNode))
                    .Skip(1)
                    .FirstOrDefault();

                if (insertedNode is not null)
                {
                    insertedNode.Title = selectedTrigger;
                }
            }

            return;
        }

        if (sectionTitle == "条件")
        {
            ViewModel.InsertConditionNodeAfter(section.HeaderNode);
        }
    }

    private async void AddTrigger_Click(object sender, RoutedEventArgs e)
    {
        var selectedTrigger = await ShowTriggerPickerAsync();
        if (string.IsNullOrWhiteSpace(selectedTrigger))
        {
            return;
        }

        if (sender is FrameworkElement element && element.DataContext is AutomationSectionViewModel section)
        {
            ViewModel.InsertTriggerNodeAfter(section.HeaderNode);
            var insertedNode = ViewModel.Nodes.SkipWhile(node => !ReferenceEquals(node, section.HeaderNode)).Skip(1).FirstOrDefault();
            if (insertedNode is not null)
            {
                insertedNode.Title = selectedTrigger;
            }
            return;
        }

        ViewModel.AddTriggerNode(selectedTrigger);
    }

    private async void ReplaceAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not AutomationNode node)
        {
            return;
        }

        if (AutomationNodeHelpers.IsTriggerNode(node))
        {
            var selectedTrigger = await ShowTriggerPickerAsync(node.Title);
            if (!string.IsNullOrWhiteSpace(selectedTrigger))
            {
                node.Title = selectedTrigger;
            }

            return;
        }

        var selectedAction = await ShowActionPickerAsync(node.Title);
        if (!string.IsNullOrWhiteSpace(selectedAction))
        {
            node.Title = selectedAction;
        }
    }

    private async void SaveAutomation_Click(object sender, RoutedEventArgs e)
    {
        if (await ViewModel.SaveCurrentPipelineAsync())
        {
            await CloseEditorAsync();
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "保存失败",
            Content = string.IsNullOrWhiteSpace(ViewModel.LastSaveErrorMessage)
                ? "当前自动化配置不完整，请检查后再试。"
                : ViewModel.LastSaveErrorMessage,
            CloseButtonText = "知道了",
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
    }

    private async void CancelAutomation_Click(object sender, RoutedEventArgs e)
    {
        await CloseEditorAsync();
    }

    private async void DeleteAutomation_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasPersistedPipeline)
        {
            await CloseEditorAsync();
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "删除自动化",
            Content = $"确认删除“{ViewModel.AutomationName}”吗？",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary &&
            await ViewModel.DeleteCurrentPipelineAsync())
        {
            await CloseEditorAsync();
        }
    }

    private void RemoveNode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is AutomationNode node)
        {
            ViewModel.RemoveNode(node);
        }
    }

    private void MoveNodeUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is AutomationNode node)
        {
            ViewModel.MoveNodeUp(node);
        }
    }

    private void MoveNodeDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is AutomationNode node)
        {
            ViewModel.MoveNodeDown(node);
        }
    }

    private void ActionList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (sender.DataContext is AutomationSectionViewModel section)
        {
            ViewModel.ReorderSectionActions(section);
        }
    }

    private void ParameterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox ||
            comboBox.DataContext is not AutomationNode node ||
            comboBox.SelectedItem is not string selected)
        {
            return;
        }

        node.Subtitle = selected;

        if (node.Title == AutomationTokens.AudioOutput)
        {
            node.RawValue = ViewModel.GetOutputDeviceValue(selected);
            return;
        }

        if (AutomationDisplayTextConverter.ToDisplayText(node.Title) == "输入音频")
        {
            node.RawValue = ViewModel.GetInputDeviceValue(selected);
            return;
        }

        if (AutomationDisplayTextConverter.ToDisplayText(node.Title) == "蓝牙已连接")
        {
            node.RawValue = ViewModel.GetBluetoothTriggerDeviceValue(selected);
            return;
        }

        if (AutomationDisplayTextConverter.ToDisplayText(node.Title) == "输出设备变化")
        {
            node.RawValue = ViewModel.GetOutputDeviceValue(selected);
            return;
        }

        var nodeTitle = AutomationDisplayTextConverter.ToDisplayText(node.Title);
        if (nodeTitle is "程序启动" or "程序退出")
        {
            node.RawValue = ViewModel.ResolveAutomationProgramListValue(selected);
            return;
        }

        if (AutomationBuilderPageViewModel.RequiresExecutablePicker(node))
        {
            node.RawValue = string.Equals(selected, AutomationTokens.NotSelectedProgram, StringComparison.Ordinal)
                ? string.Empty
                : selected;
            return;
        }

        node.RawValue = selected;
    }

    private void PerformanceWindowMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox ||
            comboBox.DataContext is not AutomationNode node ||
            comboBox.SelectedItem is not string selected)
        {
            return;
        }

        node.PerformanceWindowMode = selected;
    }

    private void PerformanceGpuComparison_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox ||
            comboBox.DataContext is not AutomationNode node ||
            comboBox.SelectedItem is not string selected)
        {
            return;
        }

        node.PerformanceGpuComparison = selected;
    }

    private void PerformanceGpuUsage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox ||
            comboBox.DataContext is not AutomationNode node ||
            comboBox.SelectedItem is not string selected)
        {
            return;
        }

        node.PerformanceGpuUsage = selected;
    }

    private void ParameterSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is Slider slider && slider.DataContext is AutomationNode node)
        {
            node.Subtitle = ((int)e.NewValue).ToString();
            node.RawValue = node.Subtitle;
        }
    }

    private void InlineText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not AutomationNode node)
        {
            return;
        }

        node.RawValue = textBox.Text.Trim();
        if (node.ParameterKind == AutomationParameterKind.Text)
        {
            node.Subtitle = string.IsNullOrWhiteSpace(node.RawValue) ? NotConfiguredText : node.RawValue;
        }
    }

    private void SaveNotificationText_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not AutomationNode node)
        {
            return;
        }

        node.RawValue = node.RawValue.Trim();
        node.Subtitle = string.IsNullOrWhiteSpace(node.RawValue) ? NotConfiguredText : node.RawValue;
    }

    private void ClearNotificationText_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not AutomationNode node)
        {
            return;
        }

        node.RawValue = string.Empty;
        node.Subtitle = NotConfiguredText;
    }

    private async void PickExecutableForNode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not AutomationNode node)
        {
            return;
        }

        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");
        picker.FileTypeFilter.Add(".bat");
        picker.FileTypeFilter.Add(".cmd");
        picker.SuggestedStartLocation = PickerLocationId.Desktop;

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            ViewModel.UpdateNodeExecutablePath(node, file.Path);
        }
    }

    private void RecordHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not AutomationNode node)
        {
            return;
        }

        StopShortcutCapture();
        _capturingHotkeyNode = node;
        node.RawValue = string.IsNullOrWhiteSpace(node.RawValue) ? string.Empty : node.RawValue.Trim();
        node.Subtitle = "请按下快捷键";
        _shortcutCaptureHandler = OnShortcutCaptureKeyDown;
        _hotkeyService.HotkeyCaptureKeyDown += _shortcutCaptureHandler;
        _hotkeyService.SetHotkeyCaptureSuspended(true);
    }

    private void ClearCustomHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not AutomationNode node)
        {
            return;
        }

        if (!string.Equals(AutomationDisplayTextConverter.ToDisplayText(node.Title), "自定义快捷键", StringComparison.Ordinal))
        {
            return;
        }

        if (ReferenceEquals(_capturingHotkeyNode, node))
        {
            StopShortcutCapture();
        }

        node.RawValue = string.Empty;
        node.Subtitle = NotConfiguredText;
    }

    private void OnShortcutCaptureKeyDown(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        if (_capturingHotkeyNode is null)
        {
            return;
        }

        if (key == VirtualKey.Escape)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _capturingHotkeyNode.RawValue = string.Empty;
                _capturingHotkeyNode.Subtitle = NotConfiguredText;
                StopShortcutCapture();
            });
            return;
        }

        if (IsModifierKey(key))
        {
            return;
        }

        var hotkey = TryBuildHotkeyText(key, modifiers);
        if (hotkey is null)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            _capturingHotkeyNode.RawValue = hotkey;
            _capturingHotkeyNode.Subtitle = hotkey;
            StopShortcutCapture();
        });
    }

    private void StopShortcutCapture()
    {
        if (_shortcutCaptureHandler is not null)
        {
            _hotkeyService.HotkeyCaptureKeyDown -= _shortcutCaptureHandler;
            _hotkeyService.SetHotkeyCaptureSuspended(false);
            _shortcutCaptureHandler = null;
        }

        _capturingHotkeyNode = null;
    }

    private async Task<string?> ShowSyntaxPickerAsync(string? currentSyntax = null)
    {
        var options = ViewModel.SyntaxOptions
            .Select(item => new PickerOption(item, AutomationDisplayTextConverter.ToDisplayText(item)))
            .ToList();

        return await ShowPickerDialogAsync("选择语法", "语法", options, currentSyntax);
    }

    private async Task<string?> ShowActionPickerAsync(string? currentAction = null)
    {
        var options = ViewModel.ActionOptions
            .Select(item => new PickerOption(item, AutomationDisplayTextConverter.ToDisplayText(item)))
            .ToList();

        return await ShowPickerDialogAsync("选择动作", "动作", options, currentAction);
    }

    private async Task<string?> ShowTriggerPickerAsync(string? currentTrigger = null)
    {
        var options = ViewModel.TriggerOptions
            .Select(item => new PickerOption(item, AutomationDisplayTextConverter.ToDisplayText(item)))
            .ToList();

        return await ShowPickerDialogAsync("选择触发器", "触发器", options, currentTrigger);
    }

    private async Task<string?> ShowPickerDialogAsync(
        string title,
        string header,
        IReadOnlyList<PickerOption> options,
        string? currentValue)
    {
        if (options.Count == 0)
        {
            return null;
        }

        var comboBox = new ComboBox
        {
            Header = header,
            MinWidth = 280,
            ItemsSource = options,
            DisplayMemberPath = nameof(PickerOption.Display),
            SelectedItem = options.FirstOrDefault(item => string.Equals(item.Value, currentValue, StringComparison.Ordinal)) ?? options[0]
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = comboBox
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? (comboBox.SelectedItem as PickerOption)?.Value : null;
    }

    private static string? TryBuildHotkeyText(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        var keyText = ToHotkeyKeyText(key);
        if (keyText is null)
        {
            return null;
        }

        var parts = new List<string>();
        if (modifiers.HasFlag(VirtualKeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(VirtualKeyModifiers.Menu)) parts.Add("Alt");
        if (modifiers.HasFlag(VirtualKeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(VirtualKeyModifiers.Windows)) parts.Add("Win");
        parts.Add(keyText);
        return string.Join("+", parts);
    }

    private static string? ToHotkeyKeyText(VirtualKey key)
    {
        if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
        {
            return ((char)('0' + (int)(key - VirtualKey.Number0))).ToString();
        }

        if (key >= VirtualKey.A && key <= VirtualKey.Z)
        {
            return key.ToString().ToUpperInvariant();
        }

        if (key >= VirtualKey.F1 && key <= VirtualKey.F24)
        {
            return key.ToString().ToUpperInvariant();
        }

        return key switch
        {
            VirtualKey.Space => "Space",
            VirtualKey.Tab => "Tab",
            VirtualKey.Enter => "Enter",
            VirtualKey.Home => "Home",
            VirtualKey.End => "End",
            VirtualKey.PageUp => "PageUp",
            VirtualKey.PageDown => "PageDown",
            VirtualKey.Insert => "Insert",
            VirtualKey.Delete => "Delete",
            VirtualKey.Left => "Left",
            VirtualKey.Right => "Right",
            VirtualKey.Up => "Up",
            VirtualKey.Down => "Down",
            _ => key.ToString().ToUpperInvariant()
        };
    }

    private static bool IsModifierKey(VirtualKey key)
    {
        return key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.LeftWindows or VirtualKey.RightWindows;
    }

    private Task CloseEditorAsync()
    {
        if (Frame?.CanGoBack == true)
        {
            Frame.GoBack();
            return Task.CompletedTask;
        }

        Frame?.Navigate(typeof(AutomationListPage));
        return Task.CompletedTask;
    }
}

public sealed partial class AutomationNodeKindVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var kind = value?.ToString();
        var target = parameter?.ToString();
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(target))
        {
            return Visibility.Collapsed;
        }

        return string.Equals(kind, target, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed partial class AutomationCountVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var count = value is int intValue ? intValue : 0;
        var mode = parameter?.ToString();

        return mode switch
        {
            "Zero" when count == 0 => Visibility.Visible,
            "NonZero" when count > 0 => Visibility.Visible,
            _ => Visibility.Collapsed
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed partial class AutomationNodeWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => string.Equals(value?.ToString(), nameof(AutomationNodeKind.Syntax), StringComparison.OrdinalIgnoreCase) ? 176d : double.NaN;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed partial class AutomationActionVisualVisibilityConverter : IValueConverter
{
    private static readonly object _routeLogSync = new();
    private static readonly string _routeLogPath = BuildRouteLogPath();

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not AutomationNode node)
        {
            return Visibility.Collapsed;
        }

        var requested = parameter?.ToString();
        var title = AutomationDisplayTextConverter.ToDisplayText(node.Title);
        var visualType = GetVisualType(node, title);
        var isVisible = string.Equals(visualType, requested, StringComparison.Ordinal);

        WriteRouteLog($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] title={title}, rawTitle={node.Title}, kind={node.Kind}, parameterKind={node.ParameterKind}, requested={requested}, visualType={visualType}, visible={isVisible}, subtitle={node.Subtitle}, rawValue={node.RawValue}");

        return isVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static string BuildRouteLogPath()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Constants.BluetoothAudioReceiver,
            Constants.ApplicationDataFolder,
            Constants.LogsFolder);

        Directory.CreateDirectory(logDirectory);
        return Path.Combine(logDirectory, "automation-editor-route.log");
    }

    private static void WriteRouteLog(string line)
    {
        lock (_routeLogSync)
        {
            File.AppendAllText(_routeLogPath, line + Environment.NewLine);
        }
    }

    internal static string ResolveVisualType(AutomationNode node)
    {
        var title = AutomationDisplayTextConverter.ToDisplayText(node.Title);
        return GetVisualType(node, title);
    }

    private static string GetVisualType(AutomationNode node, string title)
    {
        if (AutomationBuilderPageViewModel.RequiresExecutablePicker(node))
        {
            return "Program";
        }

        return title switch
        {
            "音频输出" => "AudioOutput",
            "通知" => "Notification",
            "GHelper 快捷键" => "GHelper",
            "Win11 系统快捷键" => "Combo",
            "自定义快捷键" => "Text",
            _ when node.ParameterKind == AutomationParameterKind.Text => "Text",
            _ when node.ParameterKind == AutomationParameterKind.Combo => "Combo",
            _ => "None"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed partial class AutomationLaunchProgramVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not AutomationNode node)
        {
            return Visibility.Collapsed;
        }

        return AutomationDisplayTextConverter.ToDisplayText(node.Title) == "启动程序"
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed partial class AutomationSectionKindVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var title = AutomationDisplayTextConverter.ToDisplayText(value?.ToString());
        var requested = parameter?.ToString();
        var isAction = string.Equals(title, "执行", StringComparison.Ordinal);

        var visible = requested switch
        {
            "Action" => isAction,
            "Inline" => !isAction,
            _ => false
        };

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed partial class AutomationActionTitleVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var title = AutomationDisplayTextConverter.ToDisplayText(value?.ToString());
        var requestedTitles = parameter?.ToString()?
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];

        return requestedTitles.Any(item => string.Equals(item, title, StringComparison.Ordinal))
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed partial class AutomationActionIconGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return AutomationDisplayTextConverter.ToDisplayText(value?.ToString()) switch
        {
            "调整亮度" => "\uE706",
            "调整音量" => "\uE767",
            "输入音频" => "\uE720",
            "音频输出" => "\uE8D6",
            "深色模式" => "\uE708",
            "电源模式" => "\uE945",
            "触摸板" => "\uE7F8",
            "启动程序" or "关闭程序" => "\uE8B7",
            "打开网页" => "\uE774",
            "GHelper 快捷键" or "Win11 系统快捷键" or "自定义快捷键" => "\uE765",
            "通知" => "\uE7F4",
            "等待" => "\uE916",
            "记录当前状态" => "\uE7C3",
            _ => "\uE8F1"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed partial class AutomationTriggerLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value?.ToString() switch
        {
            var title when title == AutomationTokens.PowerState => "电源状态",
            _ => "应用"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed partial class AutomationTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is string text ? ToDisplayText(text) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();

    public static string ToDisplayText(string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? string.Empty : text;
    }
}

public sealed partial class AutomationShortcutOptionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var text = value?.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalizedGHelper = BluetoothAudioReceiver.Automation.GHelperShortcutCatalog.NormalizeSelectionText(text);
        if (normalizedGHelper.Contains('|', StringComparison.Ordinal))
        {
            return normalizedGHelper;
        }

        var normalizedWindows = BluetoothAudioReceiver.Automation.WindowsShortcutCatalog.NormalizeSelectionText(text);
        if (normalizedWindows.Contains('|', StringComparison.Ordinal))
        {
            return normalizedWindows;
        }

        return AutomationTextConverter.ToDisplayText(text);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed record PickerOption(string Value, string Display);

internal static class AutomationNodeHelpers
{
    public static bool IsTriggerNode(AutomationNode? node)
    {
        return node is not null &&
               node.Kind == AutomationNodeKind.Action &&
               AutomationBuilderPageViewModel.IsSupportedTriggerTitle(node.Title);
    }
}

internal static class AutomationTokens
{
    public const string ProcessStarted = "程序启动";
    public const string ProcessStopped = "程序退出";
    public const string PowerState = "电源状态";
    public const string Brightness = "调整亮度";
    public const string Volume = "调整音量";
    public const string LaunchProgram = "启动程序";
    public const string CloseProgram = "关闭程序";
    public const string OpenUrl = "打开网页";
    public const string PowerMode = "电源模式";
    public const string Theme = "深色模式";
    public const string Wait = "等待";
    public const string GHelperHotkey = "GHelper 快捷键";
    public const string WindowsHotkey = "Win11 系统快捷键";
    public const string CustomHotkey = "自定义快捷键";
    public const string Notification = "通知";
    public const string AudioOutput = "音频输出";
    public const string DisableTouchpad = "关闭触摸板";
    public const string EnableTouchpad = "开启触摸板";
    public const string If = "如果";
    public const string And = "并且";
    public const string Or = "或者";
    public const string Else = "否则";
    public const string Execute = "执行";
    public const string Stop = "停止";
    public const string End = "结束";
    public const string Battery = "电池";
    public const string PluggedIn = "插电";
    public const string Light = "浅色";
    public const string FollowSystem = "跟随系统";
    public const string Dark = "深色";
    public const string PowerSaver = "节能";
    public const string Performance = "性能";
    public const string Balanced = "平衡";
    public const string NotSelectedProgram = "未选择程序";
}
