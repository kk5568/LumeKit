using BluetoothAudioReceiver.Automation.Models;

namespace BluetoothAudioReceiver.Contracts.Services;

public interface IAutomationTriggerBus
{
    event EventHandler<TriggerEvent>? TriggerPublished;

    void Publish(TriggerEvent triggerEvent);
}
