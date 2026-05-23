using System.Collections.Specialized;

namespace BluetoothAudioReceiver.Contracts.Services;

public interface IAppNotificationService
{
    void Initialize();

    bool Show(string payload);

    void TryShow(string payload);

    void RunShow(string payload);

    void ShowNotification(string title, string body);

    NameValueCollection ParseArguments(string arguments);

    void Unregister();
}
