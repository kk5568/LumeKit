using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationTriggerBus : IAutomationTriggerBus
{
    public event EventHandler<TriggerEvent>? TriggerPublished;

    public void Publish(TriggerEvent triggerEvent)
    {
        ArgumentNullException.ThrowIfNull(triggerEvent);

        if (triggerEvent.CorrelationId == Guid.Empty)
        {
            triggerEvent = triggerEvent with { CorrelationId = Guid.NewGuid() };
        }

        TriggerPublished?.Invoke(this, triggerEvent);
    }
}
