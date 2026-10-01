using BluetoothAudioReceiver.Automation.Models;

namespace BluetoothAudioReceiver.Contracts.Services;

public interface IAutomationSystemContextHub
{
    AutomationStaticContext StaticContext { get; }

    AutomationDynamicContext DynamicContext { get; }

    SystemContext Current { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task UpdateAsync(SystemContext context, CancellationToken cancellationToken = default);

    Task UpdateDynamicAsync(AutomationDynamicContext context, CancellationToken cancellationToken = default);
}
