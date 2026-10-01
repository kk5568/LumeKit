using System.Linq;
using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Contracts.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BluetoothAudioReceiver.ViewModels.Pages;

public partial class AutomationGameListPageViewModel : ObservableRecipient
{
    private static readonly TimeSpan AutoSaveDelay = TimeSpan.FromMilliseconds(600);

    private readonly IAppSettingsService _settingsService;
    private CancellationTokenSource? _autoSaveCts;
    private bool _isLoading;

    [ObservableProperty]
    public partial string DefaultProgramsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GameProgramsText { get; set; } = string.Empty;

    public string TitleText => "程序清单";
    public string OverviewText => "维护自动化里可复用的程序清单。触发器里的程序启动和程序退出会直接选择这里的清单。";
    public string DefaultListTitleText => "默认程序清单";
    public string DefaultListInstructionText => "放常用程序，每行一个 exe，支持注释行。";
    public string GameListTitleText => "游戏程序清单";
    public string GameListInstructionText => "只放游戏本体程序，一行一个，例如 cs2.exe、valorant.exe。不要放启动平台、启动器、加速器。";
    public string SaveButtonText => "保存";
    public string RestoreButtonText => "恢复默认";
    public string DefaultProgramsPreviewText => $"当前默认程序清单共 {CountLines(DefaultProgramsText)} 项。";
    public string GameProgramsPreviewText => $"当前游戏程序清单共 {CountLines(GameProgramsText)} 项。";

    public AutomationGameListPageViewModel(IAppSettingsService settingsService)
    {
        _settingsService = settingsService;
        LoadLists();
    }

    [RelayCommand]
    private void SaveGameList()
    {
        var serialized = BuildSerializedLists(DefaultProgramsText, GameProgramsText);
        _settingsService.SetAutomationGameListAsync(serialized).GetAwaiter().GetResult();
        LoadLists();
    }

    [RelayCommand]
    private void RestoreToDefaultGameList()
    {
        var serialized = BuildSerializedLists(
            _settingsService.BackgroundEfficiencyProcessBlacklist,
            string.Empty);
        _settingsService.SetAutomationGameListAsync(serialized).GetAwaiter().GetResult();
        LoadLists();
    }

    [RelayCommand]
    private void ScanInstalledPrograms()
    {
        var lists = AutomationInstalledProgramScanner.BuildInstalledProgramLists(_settingsService.BackgroundEfficiencyProcessBlacklist);
        DefaultProgramsText = AutomationProgramListCatalog.SerializeExecutableLines(
            lists.First(list => list.Id == AutomationProgramListCatalog.DefaultProgramsListId).Entries);
        GameProgramsText = AutomationProgramListCatalog.SerializeExecutableLines(
            lists.First(list => list.Id == AutomationProgramListCatalog.GameProgramsListId).Entries);
        SaveGameList();
    }

    public void AddProgramToDefaultList(string executable)
    {
        DefaultProgramsText = AppendProgram(DefaultProgramsText, executable);
    }

    public void AddProgramToGameList(string executable)
    {
        GameProgramsText = AppendProgram(GameProgramsText, executable);
    }

    partial void OnDefaultProgramsTextChanged(string value)
    {
        OnPropertyChanged(nameof(DefaultProgramsPreviewText));
        ScheduleAutoSave();
    }

    partial void OnGameProgramsTextChanged(string value)
    {
        OnPropertyChanged(nameof(GameProgramsPreviewText));
        ScheduleAutoSave();
    }

    private void LoadLists()
    {
        _isLoading = true;
        var lists = AutomationProgramListCatalog.ParseLists(
            _settingsService.AutomationGameList,
            _settingsService.BackgroundEfficiencyProcessBlacklist);

        DefaultProgramsText = AutomationProgramListCatalog.SerializeExecutableLines(
            lists.First(list => list.Id == AutomationProgramListCatalog.DefaultProgramsListId).Entries);
        GameProgramsText = AutomationProgramListCatalog.SerializeExecutableLines(
            lists.First(list => list.Id == AutomationProgramListCatalog.GameProgramsListId).Entries);

        OnPropertyChanged(nameof(DefaultProgramsPreviewText));
        OnPropertyChanged(nameof(GameProgramsPreviewText));
        _isLoading = false;
    }

    private void ScheduleAutoSave()
    {
        if (_isLoading)
        {
            return;
        }

        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();

        var cts = new CancellationTokenSource();
        _autoSaveCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(AutoSaveDelay, cts.Token);
                var serialized = BuildSerializedLists(DefaultProgramsText, GameProgramsText);
                await _settingsService.SetAutomationGameListAsync(serialized);
            }
            catch (OperationCanceledException)
            {
            }
        }, cts.Token);
    }

    private static string AppendProgram(string existingText, string executable)
    {
        var entries = AutomationProgramListCatalog.ParseExecutableLines(existingText).ToList();
        entries.Add(AutomationProgramListCatalog.CreateEntryFromExecutable(executable));
        return AutomationProgramListCatalog.SerializeExecutableLines(entries);
    }

    private static string BuildSerializedLists(string defaultProgramsText, string gameProgramsText)
    {
        return AutomationProgramListCatalog.SerializeLists(
        [
            new AutomationProgramListDefinition(
                AutomationProgramListCatalog.DefaultProgramsListId,
                AutomationProgramListCatalog.DefaultProgramsListName,
                AutomationProgramListCatalog.ParseExecutableLines(defaultProgramsText)),
            new AutomationProgramListDefinition(
                AutomationProgramListCatalog.GameProgramsListId,
                AutomationProgramListCatalog.GameProgramsListName,
                AutomationProgramListCatalog.ParseExecutableLines(gameProgramsText))
        ]);
    }

    private static int CountLines(string? value)
        => AutomationProgramListCatalog.ParseExecutableLines(value).Count;
}
