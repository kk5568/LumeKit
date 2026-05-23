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

    public const string StartupRegistryKey = BluetoothAudioReceiver;

    public const string StartupLogonTaskName = $"{BluetoothAudioReceiver} Startup";

    public const string StartupLogonTaskDesc = $"{BluetoothAudioReceiver} Auto Startup";

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
