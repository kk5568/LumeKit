using BluetoothAudioReceiver.Automation.Models;
using BluetoothAudioReceiver.Automation;

namespace BluetoothAudioReceiver.Services;

internal static class AutomationOutputDeviceTriggerMatcher
{
    public static bool Matches(AutomationTriggerDefinition trigger, TriggerEvent triggerEvent)
    {
        if (!trigger.Parameters.TryGetValue("currentDeviceId", out var expectedDeviceId) ||
            string.IsNullOrWhiteSpace(expectedDeviceId))
        {
            return false;
        }

        var isExit = trigger.Parameters.TryGetValue("state", out var state) &&
                     string.Equals(state, "exited", StringComparison.OrdinalIgnoreCase);
        var currentDeviceId = triggerEvent.Payload.GetValueOrDefault("currentDeviceId");
        var previousDeviceId = triggerEvent.Payload.GetValueOrDefault("previousDeviceId");
        var currentMatches = string.Equals(
            DeviceIdentityHelper.NormalizeOutputEndpointId(currentDeviceId),
            DeviceIdentityHelper.NormalizeOutputEndpointId(expectedDeviceId),
            StringComparison.OrdinalIgnoreCase);

        if (!isExit)
        {
            return currentMatches;
        }

        return !currentMatches && string.Equals(
            DeviceIdentityHelper.NormalizeOutputEndpointId(previousDeviceId),
            DeviceIdentityHelper.NormalizeOutputEndpointId(expectedDeviceId),
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsExitEvent(AutomationTriggerDefinition trigger, TriggerEvent triggerEvent)
    {
        if (!string.Equals(
                AutomationTriggerCatalog.Normalize(trigger.TriggerType),
                AutomationTriggerCatalog.OutputDeviceChanged,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trigger.Parameters.TryGetValue("state", out var state) &&
            string.Equals(state, "exited", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedDeviceId = trigger.Parameters.GetValueOrDefault("currentDeviceId");
        var previousDeviceId = triggerEvent.Payload.GetValueOrDefault("previousDeviceId");
        var currentDeviceId = triggerEvent.Payload.GetValueOrDefault("currentDeviceId");
        return !string.IsNullOrWhiteSpace(expectedDeviceId) &&
               string.Equals(
                   DeviceIdentityHelper.NormalizeOutputEndpointId(previousDeviceId),
                   DeviceIdentityHelper.NormalizeOutputEndpointId(expectedDeviceId),
                   StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(
                   DeviceIdentityHelper.NormalizeOutputEndpointId(currentDeviceId),
                   DeviceIdentityHelper.NormalizeOutputEndpointId(expectedDeviceId),
                   StringComparison.OrdinalIgnoreCase);
    }
}
