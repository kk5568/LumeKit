using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal interface IAutomationObservationCollector
{
    Task<AutomationDynamicContext> CollectAsync(
        AutomationDynamicContext previous,
        ObservationRequest request,
        CancellationToken cancellationToken);
}

internal sealed class AutomationObservationCollector : IAutomationObservationCollector
{
    private readonly AutomationObservationGetterSelector _getterSelector;
    private readonly Func<CancellationToken> _getCancellationToken;

    public AutomationObservationCollector(
        AutomationObservationGetterSelector getterSelector,
        Func<CancellationToken> getCancellationToken)
    {
        _getterSelector = getterSelector;
        _getCancellationToken = getCancellationToken;
    }

    public async Task<AutomationDynamicContext> CollectAsync(
        AutomationDynamicContext previous,
        ObservationRequest request,
        CancellationToken cancellationToken)
    {
        var current = previous with { CapturedAt = DateTimeOffset.Now };
        foreach (var plan in _getterSelector.BuildPlan(request.Items))
        {
            current = await plan.Getter.CaptureAsync(
                current,
                plan.Items,
                _getCancellationToken());
        }

        return current;
    }
}

internal interface IAutomationObservationProcessor
{
    Task ProcessAsync(
        AutomationDynamicContext current,
        CancellationToken cancellationToken);
}

internal interface IAutomationObservationDispatcher
{
    void Dispatch(
        AutomationDynamicContext previous,
        AutomationDynamicContext current,
        ObservationRequest request);
}

internal sealed class AutomationObservationEventDispatcher : IAutomationObservationDispatcher
{
    private readonly Action<AutomationDynamicContext, AutomationDynamicContext, ObservationRequest> _dispatch;

    public AutomationObservationEventDispatcher(
        Action<AutomationDynamicContext, AutomationDynamicContext, ObservationRequest> dispatch)
    {
        _dispatch = dispatch;
    }

    public void Dispatch(
        AutomationDynamicContext previous,
        AutomationDynamicContext current,
        ObservationRequest request)
        => _dispatch(previous, current, request);
}
