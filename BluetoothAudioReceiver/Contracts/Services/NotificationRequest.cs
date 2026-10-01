using System.Collections.Generic;

namespace BluetoothAudioReceiver.Contracts.Services;

/// <summary>
/// Unified request model for the in-app notification surface.
/// </summary>
public sealed record NotificationRequest
{
    /// <summary>The layout profile used to render the notification.</summary>
    public NotificationLayoutProfile LayoutProfile { get; init; } = NotificationLayoutProfiles.Default;

    /// <summary>The left-side icon glyph. Falls back to the default icon when empty.</summary>
    public string? IconGlyph { get; init; }

    /// <summary>The main notification message.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>The secondary status text.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Whether the compact one-line layout should be used.</summary>
    public bool CompactLayout { get; init; }

    /// <summary>Optional width override for the notification window.</summary>
    public int? WindowWidthOverride { get; init; }

    /// <summary>Optional height override for the notification window.</summary>
    public int? WindowHeightOverride { get; init; }

    /// <summary>Whether to bypass notification suppression rules.</summary>
    public bool BypassSuppression { get; init; }

    /// <summary>Optional logical category used for queue deduplication.</summary>
    public string? Category { get; init; }

    /// <summary>Optional extra lines for automation-style notifications.</summary>
    public IReadOnlyList<string> DetailLines { get; init; } = [];

    /// <summary>Whether the automation layout should be used.</summary>
    public bool AutomationLayout { get; init; }

    /// <summary>Optional correlation id for tracing.</summary>
    public Guid? CorrelationId { get; init; }

    /// <summary>Creates a standard notification request.</summary>
    public static NotificationRequest Standard(
        string iconGlyph,
        string message,
        string status = "",
        NotificationLayoutProfile? layoutProfile = null,
        bool bypassSuppression = false,
        string? category = null,
        Guid? correlationId = null)
    {
        return new NotificationRequest
        {
            LayoutProfile = layoutProfile ?? NotificationLayoutProfiles.Default,
            IconGlyph = iconGlyph,
            Message = message,
            Status = status,
            BypassSuppression = bypassSuppression,
            Category = category,
            CorrelationId = correlationId
        };
    }

    /// <summary>Creates a hotkey notification request.</summary>
    public static NotificationRequest Hotkey(
        string hotkeyText,
        string status,
        NotificationLayoutProfile? layoutProfile = null,
        bool bypassSuppression = false,
        string? category = null)
    {
        return new NotificationRequest
        {
            LayoutProfile = layoutProfile ?? NotificationLayoutProfiles.Hotkey,
            IconGlyph = "\uE765",
            Message = hotkeyText,
            Status = status,
            CompactLayout = false,
            BypassSuppression = bypassSuppression,
            Category = category
        };
    }

    /// <summary>Creates an automation notification request.</summary>
    public static NotificationRequest Automation(
        string iconGlyph,
        string automationName,
        IReadOnlyList<string>? detailLines = null,
        bool bypassSuppression = false,
        string? category = null,
        Guid? correlationId = null)
    {
        return new NotificationRequest
        {
            LayoutProfile = NotificationLayoutProfiles.Default,
            IconGlyph = iconGlyph,
            Message = automationName,
            DetailLines = detailLines ?? [],
            AutomationLayout = false,
            BypassSuppression = bypassSuppression,
            Category = category,
            CorrelationId = correlationId
        };
    }
}
