namespace BluetoothAudioReceiver.Services;

public enum RuntimeModule
{
    SystemAwareness,
    Scheduler,
    AutomationManagement,
    ActionExecution,
    InterfaceAndSettings,
    BackgroundIntegration
}

public enum RuntimeHealthState
{
    NotRunning,
    Normal,
    Warning,
    Error
}

public sealed record RuntimeModuleHealth(
    RuntimeModule Module,
    RuntimeHealthState State,
    string? Message = null,
    DateTimeOffset? OccurredAt = null);

public sealed class RuntimeHealthService
{
    private readonly object _sync = new();
    private readonly Dictionary<RuntimeModule, RuntimeModuleHealth> _states =
        Enum.GetValues<RuntimeModule>().ToDictionary(
            module => module,
            module => new RuntimeModuleHealth(module, RuntimeHealthState.NotRunning));

    public event EventHandler? Changed;

    public IReadOnlyList<RuntimeModuleHealth> GetSnapshot()
    {
        lock (_sync)
        {
            return _states.Values.ToArray();
        }
    }

    public void ReportNormal(RuntimeModule module)
        => Update(new RuntimeModuleHealth(module, RuntimeHealthState.Normal));

    public void ReportWarning(RuntimeModule module, string message)
        => Update(new RuntimeModuleHealth(module, RuntimeHealthState.Warning, message, DateTimeOffset.Now));

    public void ReportError(RuntimeModule module, string message)
        => Update(new RuntimeModuleHealth(module, RuntimeHealthState.Error, message, DateTimeOffset.Now));

    private void Update(RuntimeModuleHealth next)
    {
        var changed = false;
        lock (_sync)
        {
            if (_states[next.Module] != next)
            {
                _states[next.Module] = next;
                changed = true;
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
