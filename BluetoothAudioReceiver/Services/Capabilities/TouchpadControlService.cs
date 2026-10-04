using System.Diagnostics;
using BluetoothAudioReceiver.Contracts.Services;
using Serilog;

namespace BluetoothAudioReceiver.Services;

internal sealed class TouchpadControlService : ITouchpadControlService
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(TouchpadControlService));

    public async Task<bool?> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        var script = """
$ErrorActionPreference = 'Stop'
$devices = Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object {
    $_.FriendlyName -and
    (
        $_.FriendlyName -match '(?i)touchpad|clickpad|trackpad|precision touchpad'
    )
}
if (-not $devices) {
    Write-Output 'NO_TOUCHPAD_FOUND'
    return
}
$enabled = $devices | Where-Object { $_.Status -eq 'OK' } | Select-Object -First 1
if ($enabled) {
    Write-Output 'TOUCHPAD_ENABLED'
} else {
    Write-Output 'TOUCHPAD_DISABLED'
}
""";

        var result = await RunPowerShellAsync(script, cancellationToken);
        if (result.ExitCode != 0)
        {
            return null;
        }

        if (result.StandardOutput.Contains("TOUCHPAD_ENABLED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (result.StandardOutput.Contains("TOUCHPAD_DISABLED", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    public async Task<bool> EnableAsync(CancellationToken cancellationToken = default)
    {
        var script = """
$ErrorActionPreference = 'Stop'
$devices = Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object {
    $_.FriendlyName -and
    (
        $_.FriendlyName -match '(?i)touchpad|clickpad|trackpad|precision touchpad'
    )
}
if (-not $devices) {
    Write-Output 'NO_TOUCHPAD_FOUND'
    return
}
foreach ($device in $devices) {
    Enable-PnpDevice -InstanceId $device.InstanceId -Confirm:$false -ErrorAction Stop | Out-Null
}
Write-Output 'TOUCHPAD_ENABLED'
""";

        var result = await RunPowerShellAsync(script, cancellationToken);
        if (result.ExitCode != 0)
        {
            _log.Warning("Touchpad enable command failed. ExitCode={ExitCode}, Error={Error}", result.ExitCode, result.StandardError);
            return false;
        }

        if (result.StandardOutput.Contains("TOUCHPAD_ENABLED", StringComparison.OrdinalIgnoreCase))
        {
            _log.Information("Touchpad enable command succeeded");
            return true;
        }

        _log.Warning("Touchpad enable command completed without enabling a touchpad. Output={Output}", result.StandardOutput);
        return false;
    }

    public async Task<bool> DisableAsync(CancellationToken cancellationToken = default)
    {
        var script = """
$ErrorActionPreference = 'Stop'
$devices = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object {
    $_.Status -eq 'OK' -and
    $_.FriendlyName -and
    (
        $_.FriendlyName -match '(?i)touchpad|clickpad|trackpad|precision touchpad'
    )
}
if (-not $devices) {
    Write-Output 'NO_TOUCHPAD_FOUND'
    return
}
foreach ($device in $devices) {
    Disable-PnpDevice -InstanceId $device.InstanceId -Confirm:$false -ErrorAction Stop | Out-Null
}
Write-Output 'TOUCHPAD_DISABLED'
""";

        var result = await RunPowerShellAsync(script, cancellationToken);
        if (result.ExitCode != 0)
        {
            _log.Warning("Touchpad disable command failed. ExitCode={ExitCode}, Error={Error}", result.ExitCode, result.StandardError);
            return false;
        }

        if (result.StandardOutput.Contains("TOUCHPAD_DISABLED", StringComparison.OrdinalIgnoreCase))
        {
            _log.Information("Touchpad disable command succeeded");
            return true;
        }

        _log.Warning("Touchpad disable command completed without disabling a touchpad. Output={Output}", result.StandardOutput);
        return false;
    }

    private static async Task<PowerShellResult> RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(script);

        process.Start();
        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new PowerShellResult(
            process.ExitCode,
            (await stdOutTask).Trim(),
            (await stdErrTask).Trim());
    }

    private sealed record PowerShellResult(int ExitCode, string StandardOutput, string StandardError);
}
