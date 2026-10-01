using Serilog;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace BluetoothAudioReceiver.Services;

public sealed class TaskSchedulerManager
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(TaskSchedulerManager));

    private const string SunriseTaskName = "LumeKit_AutoTheme_Sunrise";
    private const string SunsetTaskName = "LumeKit_AutoTheme_Sunset";

    public string LastError { get; private set; } = string.Empty;

    public Task ScheduleThemeTasksAsync(TimeSpan sunrise, TimeSpan sunset, CancellationToken token)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            throw new InvalidOperationException("Unable to resolve application executable path.");
        }

        CreateOrUpdateDailyTask(SunriseTaskName, sunrise, exePath, "--apply-system-theme light");
        CreateOrUpdateDailyTask(SunsetTaskName, sunset, exePath, "--apply-system-theme dark");
        LastError = string.Empty;
        return Task.CompletedTask;
    }

    public Task RemoveThemeTasksAsync()
    {
        DeleteTaskIfExists(SunriseTaskName);
        DeleteTaskIfExists(SunsetTaskName);
        return Task.CompletedTask;
    }

    public bool AreThemeTasksCreated()
    {
        return QueryTaskExists(SunriseTaskName) && QueryTaskExists(SunsetTaskName);
    }

    public ThemeTaskRuntimeInfo GetRuntimeInfo()
    {
        var sunrise = GetTaskRuntimeInfo(SunriseTaskName);
        var sunset = GetTaskRuntimeInfo(SunsetTaskName);
        return new ThemeTaskRuntimeInfo(sunrise, sunset);
    }

    public string GetRecentExecutionSummary(int recentDays)
    {
        try
        {
            return RunPowerShell($$"""
$ErrorActionPreference = 'Stop'
$startTime = (Get-Date).Date.AddDays(-{{recentDays - 1}})
$taskNames = @('\LumeKit_AutoTheme_Sunrise', '\LumeKit_AutoTheme_Sunset')
$events = Get-WinEvent -LogName 'Microsoft-Windows-TaskScheduler/Operational' -ErrorAction SilentlyContinue |
    Where-Object {
        $_.TimeCreated -ge $startTime -and
        $_.Id -in 107,102,201 -and
        ($taskNames -contains [string]$_.Properties[0].Value)
    } |
    Sort-Object TimeCreated -Descending |
    Select-Object -First 12

if (-not $events) {
    '最近3天无执行记录'
    return
}

$lines = foreach ($event in $events) {
    $taskPath = [string]$event.Properties[0].Value
    $taskLabel = if ($taskPath -like '*Sunrise') { '日出' } elseif ($taskPath -like '*Sunset') { '日落' } else { $taskPath }
    $result = switch ($event.Id) {
        102 { '完成' }
        107 { '触发' }
        201 { '动作启动' }
        default { "事件$($event.Id)" }
    }
    '{0:MM-dd HH:mm} {1} {2}' -f $event.TimeCreated, $taskLabel, $result
}

$lines -join [Environment]::NewLine
""");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to read recent task execution summary");
            return "最近3天暂无执行记录";
        }
    }

    private static void CreateOrUpdateDailyTask(string taskName, TimeSpan time, string exePath, string arguments)
    {
        var script = $@"
$ErrorActionPreference = 'Stop'
$taskName = '{EscapePs(taskName)}'
$exePath = '{EscapePs(exePath)}'
$arguments = '{EscapePs(arguments)}'
$action = New-ScheduledTaskAction -Execute $exePath -Argument $arguments -WorkingDirectory (Split-Path -Parent $exePath)
$triggerTime = Get-Date -Hour {time.Hours.ToString(CultureInfo.InvariantCulture)} -Minute {time.Minutes.ToString(CultureInfo.InvariantCulture)} -Second 0
$trigger = New-ScheduledTaskTrigger -Daily -At $triggerTime
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Force | Out-Null
";
        RunPowerShell(script);
    }

    private static void DeleteTaskIfExists(string taskName)
    {
        try
        {
            RunPowerShell($$"""
$ErrorActionPreference = 'Stop'
$taskName = '{{EscapePs(taskName)}}'
Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction Stop
""");
        }
        catch
        {
        }
    }

    private bool QueryTaskExists(string taskName)
    {
        try
        {
            var json = RunPowerShell($$"""
$ErrorActionPreference = 'Stop'
$taskName = '{{EscapePs(taskName)}}'
$task = Get-ScheduledTask -TaskName $taskName -ErrorAction Stop
[pscustomobject]@{ Exists = $true } | ConvertTo-Json -Compress
""");
            return !string.IsNullOrWhiteSpace(json);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    private ThemeTaskInfo GetTaskRuntimeInfo(string taskName)
    {
        try
        {
            var json = RunPowerShell($$"""
$ErrorActionPreference = 'Stop'
$taskName = '{{EscapePs(taskName)}}'
$task = Get-ScheduledTask -TaskName $taskName -ErrorAction Stop
$info = Get-ScheduledTaskInfo -TaskName $taskName -ErrorAction Stop
[pscustomobject]@{
    Exists = $true
    State = $task.State.ToString()
    NextRunTime = $info.NextRunTime
    LastRunTime = $info.LastRunTime
    LastTaskResult = $info.LastTaskResult
} | ConvertTo-Json -Compress
""");
            var dto = JsonSerializer.Deserialize(json, AppJsonContext.Default.TaskInfoDto);
            if (dto is null)
            {
                return new ThemeTaskInfo(false, null, null, null, "未找到", "任务信息为空");
            }

            return new ThemeTaskInfo(
                true,
                ParseTaskDateTime(dto.NextRunTime),
                ParseTaskDateTime(dto.LastRunTime),
                dto.LastTaskResult,
                string.IsNullOrWhiteSpace(dto.State) ? "未知" : dto.State,
                string.Empty);
        }
        catch (Exception ex)
        {
            return new ThemeTaskInfo(false, null, null, null, "未找到", ex.Message);
        }
    }

    private static string RunPowerShell(string script)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };

        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(script);

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            _log.Warning("PowerShell failed: code={Code}, output={Output}, error={Error}", process.ExitCode, output, error);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim());
        }

        return output.Trim();
    }

    private static string EscapePs(string value)
    {
        return value.Replace("'", "''");
    }

    private static DateTime? ParseTaskDateTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var localTime))
        {
            return localTime;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var invariantTime))
        {
            return invariantTime;
        }

        return null;
    }
}

public sealed record ThemeTaskRuntimeInfo(ThemeTaskInfo SunriseTask, ThemeTaskInfo SunsetTask);

public sealed record ThemeTaskInfo(
    bool Exists,
    DateTime? NextRunTime,
    DateTime? LastRunTime,
    int? LastTaskResult,
    string StateText,
    string ErrorMessage);

internal sealed class TaskInfoDto
{
    public bool Exists { get; set; }
    public string State { get; set; } = string.Empty;
    public string? NextRunTime { get; set; }
    public string? LastRunTime { get; set; }
    public int? LastTaskResult { get; set; }
}
