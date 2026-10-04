using System.Diagnostics;
using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal sealed class PowerProvider : IPowerProvider
{
    public async Task<string?> GetActiveSchemeAsync(CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = "/getactivescheme",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var start = output.IndexOf(':');
        if (start < 0)
        {
            return null;
        }

        var text = output[(start + 1)..].Trim();
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    }

    public async Task<bool> SetActiveSchemeAsync(string schemeGuid, CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = $"/setactive {schemeGuid}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0;
    }
}
