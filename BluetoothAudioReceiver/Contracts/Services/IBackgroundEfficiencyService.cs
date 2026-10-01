namespace BluetoothAudioReceiver.Contracts.Services;

public enum ThrottleStatus
{
    Stopped = 0,
    BlacklistAndAllButWhitelist = 1
}

public interface IBackgroundEfficiencyService
{
    ThrottleStatus ThrottleStatus { get; }

    bool PauseThrottling { get; set; }

    IReadOnlySet<string> ProcessWhitelist { get; }
    IReadOnlySet<string> ProcessBlacklist { get; }

    event EventHandler<ThrottleStatus>? ThrottleStatusChanged;

    void Initialize();

    void ApplyAndSaveProcessWhitelist(string processWhitelistString);
    void ApplyProcessWhitelist(string processWhitelistString);
    void ApplyAndSaveProcessBlacklist(string processBlacklistString);
    void ApplyProcessBlacklist(string processBlacklistString);
}
