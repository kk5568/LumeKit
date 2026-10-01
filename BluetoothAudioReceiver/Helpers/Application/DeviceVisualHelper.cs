namespace BluetoothAudioReceiver.Helpers.Application;

public static class DeviceVisualHelper
{
    public const string SpeakerGlyph = "\uE767";
    public const string HeadsetGlyph = "\uE7F6";
    public const string TabletGlyph = "\uE70A";
    public const string PhoneGlyph = "\uE8EA";
    public const string SpeakerLabel = "\u626C\u58F0\u5668";
    public const string HeadsetLabel = "\u8033\u673A";
    public const string TabletLabel = "\u5E73\u677F";
    public const string PhoneLabel = "\u624B\u673A";

    public static string ResolveGlyph(string? rawName)
    {
        var name = string.IsNullOrWhiteSpace(rawName) ? string.Empty : rawName.Trim();
        var lower = name.ToLowerInvariant();

        var isTablet = name.Contains("\u5E73\u677F", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("pad")
            || lower.Contains("tab")
            || lower.Contains("tablet");
        var isHeadset = name.Contains("\u8033\u673A", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("headphone")
            || lower.Contains("headset")
            || lower.Contains("earbud")
            || lower.Contains("earbuds")
            || lower.Contains("buds")
            || lower.Contains("airpods");
        var isPhoneKeyword = name.Contains("\u624B\u673A", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("phone")
            || lower.Contains("iphone");
        var isPhoneBrandModel = (lower.Contains("xiaomi")
            || lower.Contains("redmi")
            || lower.Contains("huawei")
            || lower.Contains("honor")
            || lower.Contains("oppo")
            || lower.Contains("vivo"))
            && !isTablet
            && !isHeadset;

        if (isHeadset)
        {
            return HeadsetGlyph;
        }

        if (isTablet)
        {
            return TabletGlyph;
        }

        if (isPhoneKeyword || isPhoneBrandModel)
        {
            return PhoneGlyph;
        }

        return SpeakerGlyph;
    }

    public static string ResolveCategoryLabel(string? rawName)
    {
        var name = string.IsNullOrWhiteSpace(rawName) ? string.Empty : rawName.Trim();
        var lower = name.ToLowerInvariant();

        var isTablet = name.Contains("\u5E73\u677F", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("pad")
            || lower.Contains("tab")
            || lower.Contains("tablet");
        var isHeadset = name.Contains("\u8033\u673A", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("headphone")
            || lower.Contains("headset")
            || lower.Contains("earbud")
            || lower.Contains("earbuds")
            || lower.Contains("buds")
            || lower.Contains("airpods");
        var isPhoneKeyword = name.Contains("\u624B\u673A", StringComparison.OrdinalIgnoreCase)
            || lower.Contains("phone")
            || lower.Contains("iphone");
        var isPhoneBrandModel = (lower.Contains("xiaomi")
            || lower.Contains("redmi")
            || lower.Contains("huawei")
            || lower.Contains("honor")
            || lower.Contains("oppo")
            || lower.Contains("vivo"))
            && !isTablet
            && !isHeadset;

        if (isHeadset)
        {
            return HeadsetLabel;
        }

        if (isTablet)
        {
            return TabletLabel;
        }

        if (isPhoneKeyword || isPhoneBrandModel)
        {
            return PhoneLabel;
        }

        return SpeakerLabel;
    }

    public static string ResolveDisplayName(string? rawName, string fallback = "\u672A\u77E5\u8BBE\u5907")
    {
        return string.IsNullOrWhiteSpace(rawName) ? fallback : rawName.Trim();
    }
}
