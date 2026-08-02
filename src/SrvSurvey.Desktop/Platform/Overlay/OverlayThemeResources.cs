using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Platform.Overlay;

public static class OverlayThemeResources
{
    private static readonly object ThemeWindowsLock = new();
    private static readonly List<WeakReference<Window>> ThemeWindows = [];
    private static readonly ConditionalWeakTable<Window, ScaleRegistration>
        ScaleRegistrations = new();
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
            ["RavenPrimaryBrush"] = "RavenOverlayPrimaryBrush",
            ["RavenPrimaryDimBrush"] = "RavenOverlayPrimaryDimBrush",
            ["RavenSecondaryBrush"] = "RavenOverlaySecondaryBrush",
            ["RavenSecondaryDimBrush"] = "RavenOverlaySecondaryDimBrush",
            ["RavenDangerDimBrush"] = "RavenOverlayDangerDimBrush",
            ["RavenSuccessDimBrush"] = "RavenOverlaySuccessDimBrush",
            ["RavenMenuGoldBrush"] = "RavenOverlayMenuGoldBrush",
            ["RavenBioGoldBrush"] = "RavenOverlayBioGoldBrush",
            ["RavenBioGoldDimBrush"] = "RavenOverlayBioGoldDimBrush",
            ["RavenBioUnknownBrush"] = "RavenOverlayBioUnknownBrush",
            ["RavenBioHatchBrush"] = "RavenOverlayBioHatchBrush",
            ["RavenBioWhiteBrush"] = "RavenOverlayBioWhiteBrush",
            ["RavenBioPredictionBrush"] = "RavenOverlayBioPredictionBrush",
            ["RavenColoniseSurplusBrush"] = "RavenOverlayColoniseSurplusBrush",
            ["RavenColoniseSurplusDimBrush"] = "RavenOverlayColoniseSurplusDimBrush",
            ["RavenColoniseDeficitBrush"] = "RavenOverlayColoniseDeficitBrush",
            ["RavenColoniseDeficitDimBrush"] = "RavenOverlayColoniseDeficitDimBrush",
            ["RavenColoniseHighlightBrush"] = "RavenOverlayColoniseHighlightBrush",
            ["RavenColoniseItemBrush"] = "RavenOverlayColoniseItemBrush",
            ["RavenColoniseItemDimBrush"] = "RavenOverlayColoniseItemDimBrush",
            ["RavenFczCheckpointBrush"] = "RavenOverlayFczCheckpointBrush",
            ["RavenFczCheckpointLocalBrush"] = "RavenOverlayFczCheckpointLocalBrush",
            ["RavenFczPowerPostBrush"] = "RavenOverlayFczPowerPostBrush",
        };

    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        TrackThemeWindow(window);
        ApplyThemeResources(window);
    }

    public static void RefreshAll()
    {
        Window[] windows;
        lock (ThemeWindowsLock)
        {
            windows = ThemeWindows
                .Select(reference => reference.TryGetTarget(out var window)
                    ? window
                    : null)
                .Where(window => window is not null)
                .Cast<Window>()
                .ToArray();
            ThemeWindows.RemoveAll(reference => !reference.TryGetTarget(out _));
        }

        foreach (var window in windows)
        {
            ApplyThemeResources(window);
        }
    }

    private static void ApplyThemeResources(Window window)
    {
        // Overlay controls keep a stable native style and never inherit a
        // light/dark switch from the application shell.
        window.RequestedThemeVariant = ThemeVariant.Dark;
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

    private static void TrackThemeWindow(Window window)
    {
        lock (ThemeWindowsLock)
        {
            for (var index = ThemeWindows.Count - 1; index >= 0; index--)
            {
                if (!ThemeWindows[index].TryGetTarget(out var target))
                {
                    ThemeWindows.RemoveAt(index);
                    continue;
                }

                if (ReferenceEquals(target, window))
                {
                    return;
                }
            }

            ThemeWindows.Add(new WeakReference<Window>(window));
        }
    }

    public static void Apply(
        Window window,
        LegacyOverlayLayout layout,
        string plotterName)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Apply(window);
        ApplyScale(window, layout, plotterName);
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
        ApplyScale(window, layout, plotterName);
        var opacity = layout.GetOpacity(plotterName) ?? 1d;
        if (Math.Abs(window.Opacity - opacity) > 0.0001d)
        {
            window.Opacity = opacity;
        }
    }

    public static void ApplyScale(
        Window window,
        LegacyOverlayLayout layout)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(layout);
        var scaleIndex = OverlayWindowRegistry.Shared.TryGetPlotterName(
            window,
            out var plotterName)
                ? layout.GetScaleIndex(plotterName)
                : layout.ScaleIndex;
        ApplyScale(window, scaleIndex, window.RenderScaling);
    }

    public static void ApplyScale(
        Window window,
        LegacyOverlayLayout layout,
        string plotterName)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        ApplyScale(
            window,
            layout.GetScaleIndex(plotterName),
            window.RenderScaling);
    }

    public static void ApplyScale(
        Window window,
        int scaleIndex,
        double renderScaling)
    {
        ArgumentNullException.ThrowIfNull(window);
        var registration = GetOrCreateScaleRegistration(window);
        if (registration is null)
        {
            return;
        }

        var factor = OverlayScaleCatalog.GetRelativeScale(
            scaleIndex,
            renderScaling);
        if (Math.Abs(registration.AppliedFactor - factor) <= 0.0001d)
        {
            return;
        }

        registration.Container.LayoutTransform = new ScaleTransform(
            factor,
            factor);
        window.MinWidth = Scale(registration.BaseMinWidth, factor);
        window.MinHeight = Scale(registration.BaseMinHeight, factor);
        window.MaxWidth = Scale(registration.BaseMaxWidth, factor);
        window.MaxHeight = Scale(registration.BaseMaxHeight, factor);
        window.Width = Scale(registration.BaseWidth, factor);
        window.Height = Scale(registration.BaseHeight, factor);
        registration.AppliedFactor = factor;
    }

    public static void SetBaseSize(
        Window window,
        LegacyOverlayLayout layout,
        double width,
        double height)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(layout);
        if (!double.IsFinite(width) || width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (!double.IsFinite(height) || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        var registration = GetOrCreateScaleRegistration(window)
            ?? throw new InvalidOperationException(
                "The overlay window content cannot be scaled.");
        if (Math.Abs(registration.BaseWidth - width) <= 0.0001d
            && Math.Abs(registration.BaseHeight - height) <= 0.0001d)
        {
            ApplyScale(window, layout);
            return;
        }

        registration.BaseWidth = width;
        registration.BaseHeight = height;
        registration.AppliedFactor = double.NaN;
        ApplyScale(window, layout);
    }

    private static ScaleRegistration? GetOrCreateScaleRegistration(Window window)
    {
        if (ScaleRegistrations.TryGetValue(window, out var existing))
        {
            return existing;
        }

        if (window.Content is not Control content)
        {
            return null;
        }

        var registration = new ScaleRegistration(
            new LayoutTransformControl(),
            window.Width,
            window.Height,
            window.MinWidth,
            window.MinHeight,
            window.MaxWidth,
            window.MaxHeight);
        window.Content = null;
        registration.Container.Child = content;
        window.Content = registration.Container;
        ScaleRegistrations.Add(window, registration);
        return registration;
    }

    private static double Scale(double value, double factor)
    {
        return double.IsNaN(value) ? value : value * factor;
    }

    private sealed class ScaleRegistration(
        LayoutTransformControl container,
        double baseWidth,
        double baseHeight,
        double baseMinWidth,
        double baseMinHeight,
        double baseMaxWidth,
        double baseMaxHeight)
    {
        public LayoutTransformControl Container { get; } = container;

        public double BaseWidth { get; set; } = baseWidth;

        public double BaseHeight { get; set; } = baseHeight;

        public double BaseMinWidth { get; } = baseMinWidth;

        public double BaseMinHeight { get; } = baseMinHeight;

        public double BaseMaxWidth { get; } = baseMaxWidth;

        public double BaseMaxHeight { get; } = baseMaxHeight;

        public double AppliedFactor { get; set; } = double.NaN;
    }
}
