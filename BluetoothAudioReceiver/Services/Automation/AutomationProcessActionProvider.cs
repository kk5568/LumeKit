using System.Diagnostics;
using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationProcessActionProvider : IAutomationActionProvider
{
    public AutomationActionCapability Capability { get; } = new(
        AutomationSupportCatalog.ActionProcessControl,
        "程序控制",
        AutomationActionProviderKind.Process,
        AutomationActionResourceScope.Process,
        SupportsRestore: false,
        RequiresAdministrator: false,
        IsAsync: false,
        AllowsConcurrent: true);

    public string ActionType => "process-control";

    public Task ExecuteAsync(AutomationActionDefinition action, TriggerEvent triggerEvent, AutomationExecutionContext executionContext, SystemContext context, CancellationToken cancellationToken = default)
    {
        var (operation, target) = AutomationActionParameterHelper.ReadProcessOperation(action.ParametersJson);
        if (string.Equals(operation, "close", StringComparison.OrdinalIgnoreCase))
        {
            var processName = target;
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    process.Kill(entireProcessTree: false);
                }
                catch
                {
                    // best effort
                }
            }

            return Task.CompletedTask;
        }

        var fileName = string.Equals(operation, "launch", StringComparison.OrdinalIgnoreCase)
            ? target
            : string.Empty;

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("程序路径未设置");
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"程序启动失败: {fileName}", exception);
        }

        return Task.CompletedTask;
    }
}
