using BluetoothAudioReceiver.Automation.Models;

namespace BluetoothAudioReceiver.Contracts.Services;

public interface IAutomationPipelineRepository
{
    IReadOnlyList<AutomationPipeline> Pipelines { get; }
    AutomationPipelineRepositoryLoadStatus LastLoadStatus { get; }
    Task InitializationTask { get; }

    event EventHandler? PipelinesChanged;

    Task ReplaceAllAsync(IEnumerable<AutomationPipeline> pipelines);
}
