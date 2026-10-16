using System;
using System.IO;

namespace BluetoothAudioReceiver.Infrastructure;

public static class Constants
{
    #region Program

    public const string BluetoothAudioReceiver = "BluetoothAudioReceiver";

    #endregion

    #region Startup

    public const string StartupTaskId = "StartAppOnLoginTask";

#if DEBUG
    // Debug builds own an independent autostart slot so they never fight the installed build over
    // the same Run value or scheduled task. With no entry created by default, debug starts OFF.
    private const string StartupIdentitySuffix = " (Debug)";
#else
    private const string StartupIdentitySuffix = "";
#endif

    // Names debug builds used before the per-build split. Only ever cleaned up, never created.
    public const string LegacyStartupRegistryKey = BluetoothAudioReceiver;

    public const string LegacyStartupLogonTaskName = $"{BluetoothAudioReceiver} Startup";

    public const string StartupRegistryKey = BluetoothAudioReceiver + StartupIdentitySuffix;

    public const string StartupLogonTaskName = $"{BluetoothAudioReceiver} Startup{StartupIdentitySuffix}";

    public const string StartupLogonTaskDesc = $"{BluetoothAudioReceiver} Auto Startup{StartupIdentitySuffix}";

    #endregion

    #region Resources

    public const string DefaultResourceFileName = "Resources";

    public static readonly string AppIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "speakericon3_256x256.ico");

    #endregion

    #region Settings & Logs

#if DEBUG
    public const string ApplicationDataFolder = "ApplicationData(Debug)";
#else
    public const string ApplicationDataFolder = "ApplicationData";
#endif

    public const string SettingsFolder = "Settings";

    public const string SettingsFile = "Settings.json";

    public const string LogsFolder = "Logs";

    #endregion
}
