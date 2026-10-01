using BluetoothAudioReceiver.Automation;
using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Contracts.Services;

namespace BluetoothAudioReceiver.Services;

internal sealed class AutomationNotificationActionProvider : IAutomationActionProvider
{
    private readonly INotificationService _notificationService;
    private readonly IAppSettingsService _appSettingsService;

    public AutomationNotificationActionProvider(INotificationService notificationService, IAppSettingsService appSettingsService)
    {
        _notificationService = notificationService;
        _appSettingsService = appSettingsService;
    }

    public AutomationActionCapability Capability { get; } = new(
        AutomationSupportCatalog.ActionShowNotification,
        "Show Notification",
        AutomationActionProviderKind.Notification,
        AutomationActionResourceScope.Notification,
        SupportsRestore: false,
        RequiresAdministrator: false,
        IsAsync: false,
        AllowsConcurrent: true);

    public string ActionType => "show-notification";

    public Task ExecuteAsync(
        AutomationActionDefinition action,
        TriggerEvent triggerEvent,
        AutomationExecutionContext executionContext,
        SystemContext context,
        CancellationToken cancellationToken = default)
    {
        var message = AutomationActionParameterHelper.ReadValue(action.ParametersJson);

        if (string.IsNullOrWhiteSpace(message))
        {
            return Task.CompletedTask;
        }

        if (!_appSettingsService.AutomationExecutionNotificationEnabled)
        {
            return Task.CompletedTask;
        }

        _notificationService.Show(NotificationRequest.Standard(
            "\uE7BA",
            message,
            layoutProfile: NotificationLayoutProfiles.Compact,
            correlationId: executionContext.CorrelationId));

        return Task.CompletedTask;
    }
}
