using Microsoft.Win32;
using BluetoothAudioReceiver.Contracts.Services;
using Serilog;

namespace BluetoothAudioReceiver.Services;

public sealed class InputAudioVolumeService(IAppSettingsService appSettingsService) : IInputAudioVolumeService, IDisposable
{
    private static readonly ILogger _log = Log.ForContext("SourceContext", nameof(InputAudioVolumeService));
    private readonly IAppSettingsService _appSettingsService = appSettingsService;

    private const string EqualizerApoRegistryPath = @"SOFTWARE\EqualizerAPO";
    private const string EqualizerApoConfigFileName = "config.txt";
    private const string ManagedIncludeFileName = "BluetoothAudioReceiver-input-gain.txt";
    private const string ManagedBlockStart = "# BluetoothAudioReceiver managed input gain start";
    private const string ManagedBlockEnd = "# BluetoothAudioReceiver managed input gain end";

    public Task ApplyCurrentVolumeAsync()
    {
        return ApplyVolumeAsync(_appSettingsService.InputVolumeIndex);
    }

    public Task ApplyVolumeAsync(int index)
    {
        var normalizedIndex = Math.Clamp(index, 0, 3);
        var gainFactor = IndexToGainFactor(normalizedIndex);
        var boostDb = (float)(20.0 * Math.Log10(gainFactor));

        try
        {
            var configDirectory = ResolveConfigDirectory();
            if (string.IsNullOrWhiteSpace(configDirectory))
            {
                _log.Warning("Equalizer APO config directory could not be resolved");
                return Task.CompletedTask;
            }

            Directory.CreateDirectory(configDirectory);

            var includePath = Path.Combine(configDirectory, ManagedIncludeFileName);
            File.WriteAllLines(includePath, [FormatPreampLine(boostDb)]);

            var configPath = Path.Combine(configDirectory, EqualizerApoConfigFileName);
            if (!File.Exists(configPath))
            {
                _log.Warning("Equalizer APO config file not found: {ConfigPath}", configPath);
                return Task.CompletedTask;
            }

            var configLines = File.ReadAllLines(configPath).ToList();
            UpsertManagedInclude(configLines);
            File.WriteAllLines(configPath, configLines);

            _log.Information(
                "Applied Equalizer APO software gain command: index={Index}, gainFactor={GainFactor:P0}, boostDb={BoostDb:F2}, config={ConfigPath}",
                normalizedIndex,
                gainFactor,
                boostDb,
                configPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.Warning(ex, "No permission to update Equalizer APO config. Run the app or installer with admin rights.");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to apply Equalizer APO software gain command");
        }

        return Task.CompletedTask;
    }

    private static float IndexToGainFactor(int index)
    {
        return index switch
        {
            0 => 1.2f,
            1 => 1.4f,
            2 => 1.6f,
            _ => 1.8f
        };
    }

    private static string FormatPreampLine(float boostDb)
    {
        var sign = boostDb >= 0 ? "+" : string.Empty;
        return $"Preamp: {sign}{boostDb:F2} dB";
    }

    private static void UpsertManagedInclude(IList<string> configLines)
    {
        var startIndex = -1;
        var endIndex = -1;

        for (var i = 0; i < configLines.Count; i++)
        {
            var line = configLines.ElementAt(i).Trim();
            if (string.Equals(line, ManagedBlockStart, StringComparison.Ordinal))
            {
                startIndex = i;
            }

            if (string.Equals(line, ManagedBlockEnd, StringComparison.Ordinal))
            {
                endIndex = i;
                break;
            }
        }

        var managedBlock = new[]
        {
            ManagedBlockStart,
            $"Include: {ManagedIncludeFileName}",
            ManagedBlockEnd
        };

        if (startIndex >= 0 && endIndex > startIndex)
        {
            var removalCount = endIndex - startIndex + 1;
            for (var i = 0; i < removalCount; i++)
            {
                configLines.RemoveAt(startIndex);
            }

            for (var i = managedBlock.Length - 1; i >= 0; i--)
            {
                configLines.Insert(startIndex, managedBlock[i]);
            }

            return;
        }

        var insertIndex = FindConfigInsertIndex(configLines);
        for (var i = managedBlock.Length - 1; i >= 0; i--)
        {
            configLines.Insert(insertIndex, managedBlock[i]);
        }
    }

    private static int FindConfigInsertIndex(IList<string> configLines)
    {
        var index = 0;
        while (index < configLines.Count)
        {
            var line = configLines[index].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                index++;
                continue;
            }

            break;
        }

        return index;
    }

    private static string? ResolveConfigDirectory()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(EqualizerApoRegistryPath);
            var registryPath = key?.GetValue("ConfigPath") as string;
            if (!string.IsNullOrWhiteSpace(registryPath))
            {
                return registryPath.Trim();
            }
        }
        catch
        {
            // ignore registry access failures and fall back to the default path
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            return null;
        }

        return Path.Combine(programFiles, "EqualizerAPO", "config");
    }

    public void Dispose()
    {
    }
}
