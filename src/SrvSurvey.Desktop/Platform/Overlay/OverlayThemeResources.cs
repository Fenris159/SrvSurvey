using Avalonia;
using Avalonia.Controls;

namespace SrvSurvey.Desktop.Platform.Overlay;

public static class OverlayThemeResources
{
    private static readonly IReadOnlyDictionary<string, string> ResourceMappings =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RavenWindowBrush"] = "RavenOverlayWindowBrush",
            ["RavenSurfaceBrush"] = "RavenOverlaySurfaceBrush",
            ["RavenRaisedSurfaceBrush"] = "RavenOverlayRaisedSurfaceBrush",
            ["RavenAccentBrush"] = "RavenOverlayAccentBrush",
            ["RavenAccentMutedBrush"] = "RavenOverlayAccentMutedBrush",
            ["RavenTextBrush"] = "RavenOverlayTextBrush",
            ["RavenMutedTextBrush"] = "RavenOverlayMutedTextBrush",
            ["RavenBorderBrush"] = "RavenOverlayBorderBrush",
            ["RavenSuccessBrush"] = "RavenOverlaySuccessBrush",
            ["RavenWarningBrush"] = "RavenOverlayWarningBrush",
            ["RavenDangerBrush"] = "RavenOverlayDangerBrush",
        };

    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        foreach (var mapping in ResourceMappings)
        {
            if (application.Resources.TryGetResource(
                    mapping.Value,
                    application.ActualThemeVariant,
                    out var value))
            {
                window.Resources[mapping.Key] = value;
            }
        }
    }

    public static void Apply(
        Window window,
        LegacyOverlayLayout layout,
        string plotterName)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Apply(window);
        ApplyOpacity(window, layout, plotterName);
        OverlayWindowRegistry.Shared.Register(window, plotterName);
    }

    public static void ApplyOpacity(
        Window window,
        LegacyOverlayLayout layout,
        string plotterName)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        var opacity = layout.GetOpacity(plotterName) ?? 1d;
        if (Math.Abs(window.Opacity - opacity) > 0.0001d)
        {
            window.Opacity = opacity;
        }
    }
}
