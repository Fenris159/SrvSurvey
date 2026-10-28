using Avalonia.Media;

namespace SrvSurvey.Desktop.Theming;

/// <summary>Personal swatches used only by explicit overlay editor brush overrides.</summary>
internal static class OverlayCustomBrushes
{
    public static IReadOnlyDictionary<string, Color> Defaults { get; } =
        new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["custom.1"] = Color.Parse("#FF70C5"),
            ["custom.2"] = Color.Parse("#53E0B6"),
            ["custom.3"] = Color.Parse("#F6C75A"),
            ["custom.4"] = Color.Parse("#60B8FF"),
            ["custom.5"] = Color.Parse("#B79AFF"),
        };
}
