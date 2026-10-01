namespace BluetoothAudioReceiver.Contracts.Services;

public interface IPowerProvider
{
    Task<string?> GetActiveSchemeAsync(CancellationToken cancellationToken = default);

    Task<bool> SetActiveSchemeAsync(string schemeGuid, CancellationToken cancellationToken = default);
}
