using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Platform.Overlay;

public static class OverlayThemeResources
{
    internal const string OverlayTypographyClass = "srv-overlay";
    private const string RavenWarningBrushResource = "RavenWarningBrush";

    private static readonly object ThemeWindowsLock = new();
    private static readonly List<WeakReference<Window>> ThemeWindows = [];
    private static readonly ConditionalWeakTable<Window, ScaleRegistration>
        ScaleRegistrations = new();
    private static readonly ConditionalWeakTable<Window, LegacyPresentationRegistration>
        LegacyPresentationRegistrations = new();
    private static readonly ConditionalWeakTable<Window, LayoutSettingsRegistration>
        LayoutSettingsRegistrations = new();
    private static readonly Dictionary<string, string> ResourceMappings =
        CreateResourceMappings(
            "Window",
            "Surface",
            "RaisedSurface",
            "Accent",
            "AccentMuted",
            "Text",
            "MutedText",
            "Border",
            "Success",
            "Warning",
            "Danger",
            "Primary",
            "PrimaryDim",
            "Secondary",
            "SecondaryDim",
            "DangerDim",
            "SuccessDim",
            "MenuGold",
            "BioConfirmed",
            "BioConfirmedDim",
            "BioPotential",
            "BioPrediction",
            "BioPredictionPotential",
            "BioGold",
            "BioGoldDim",
            "BioGoldFill",
            "BioGoldDimFill",
            "BioGalacticRegion",
            "BioGalacticRegionPotential",
            "BioUnknown",
            "BioUnknownGlyph",
            "BioHatch",
            "BioEmpty",
            "BioWhite",
            "BioConfirmedEdge",
            "BioConfirmedDimEdge",
            "BioPredictionEdge",
            "BioGoldEdge",
            "BioGoldDimEdge",
            "BioGalacticRegionEdge",
            "BioUnknownEdge",
            "ColoniseSurplus",
            "ColoniseSurplusDim",
            "ColoniseDeficit",
            "ColoniseDeficitDim",
            "ColoniseHighlight",
            "ColoniseItem",
            "ColoniseItemDim",
            "ColoniseRowHighlight",
            "FczCheckpoint",
            "FczCheckpointLocal",
            "FczPowerPost",
            "GuardianBackground",
            "GuardianHeader",
            "GuardianPrimary",
            "GuardianPrimaryDim",
            "GuardianSecondary",
            "GuardianSecondaryDim",
            "GuardianText",
            "GuardianMuted",
            "GuardianDanger",
            "GuardianSuccess",
            "GuardianWarning",
            "GuardianSurface");

    private static Dictionary<string, string> CreateResourceMappings(
        params string[] resourceNames) =>
        resourceNames.ToDictionary(
            name => $"Raven{name}Brush",
            name => $"RavenOverlay{name}Brush",
            StringComparer.Ordinal);

    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        TrackThemeWindow(window);
        ApplyThemeResources(window);
    }

    public static void Apply(
        Window window,
        LegacyOverlayLayout layout,
        string plotterName)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _ = OverlayLayoutCatalog.GetRequired(plotterName);
        Apply(window);
        ApplyLegacyFormFactor(window, plotterName);
        ApplyLegacyPresentation(window, plotterName);
        ApplyScale(window, layout, plotterName);
        ApplyOpacity(window, layout, plotterName);
        OverlayWindowRegistry.Shared.Register(window, plotterName);
        RegisterLayoutSettings(window, layout, plotterName);
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
        if (!window.Classes.Contains(OverlayTypographyClass))
        {
            window.Classes.Add(OverlayTypographyClass);
        }

        window.RequestedThemeVariant = ThemeVariant.Dark;
        var application = Application.Current;
        if (application is not null)
        {
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

        ApplySurfaceChrome(window);
    }

    private static void ApplySurfaceChrome(Window window)
    {
        // Editor previews own yellow folder-tab + body chrome in XAML.
        if (window is OverlayPositionPreviewWindow)
        {
            return;
        }

        var surface = window.Content switch
        {
            Border border => border,
            LayoutTransformControl { Child: Border border } => border,
            _ => null,
        };
        if (surface is null)
        {
            return;
        }

        _ = window.TryFindResource("RavenWindowBrush", out var windowBrush);
        _ = window.TryFindResource(RavenWarningBrushResource, out var warningBrush);
        ApplySurfaceChrome(
            surface,
            isEditorPreview: false,
            windowBrush as IBrush,
            warningBrush as IBrush);
    }

    internal static void ApplySurfaceChrome(
        Border surface,
        bool isEditorPreview,
        IBrush? windowBrush = null,
        IBrush? warningBrush = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        surface.Margin = new Thickness(isEditorPreview ? 1 : 0);
        surface.Padding = new Thickness(4);
        surface.Background = windowBrush ?? surface.Background;
        surface.BorderBrush = isEditorPreview ? warningBrush : null;
        surface.BorderThickness = new Thickness(isEditorPreview ? 2 : 0);
        surface.CornerRadius = new CornerRadius(5);
        if (!isEditorPreview)
        {
            surface.Opacity = 1d;
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

    private static void RegisterLayoutSettings(
        Window window,
        LegacyOverlayLayout layout,
        string plotterName)
    {
        if (LayoutSettingsRegistrations.TryGetValue(
                window,
                out var existing))
        {
            existing.Validate(layout, plotterName);
            return;
        }

        LayoutSettingsRegistrations.Add(
            window,
            new LayoutSettingsRegistration(window, layout, plotterName));
    }

    internal static void ApplyLegacyPresentation(
        Window window,
        string plotterName)
    {
        var definition = OverlayLayoutCatalog.Supported.FirstOrDefault(
            candidate => candidate.Name == plotterName);
        if (definition is null)
        {
            return;
        }

        // Shared presentation templates own their complete visual grammar.
        // Do not register the legacy LayoutUpdated normalizer: it would later
        // walk the editor preview shell and strip the folder tab's padding,
        // background, border, and corner radius after the window opens.
        if (OverlayRuntimePresentationFactory.UsesDedicatedHostChrome(plotterName)
            || GuardianOverlayPresentationFactory.IsSupported(plotterName))
        {
            ApplyDedicatedPresentationChrome(window);
            return;
        }

        var registration = LegacyPresentationRegistrations.GetValue(
            window,
            candidate => new LegacyPresentationRegistration(
                candidate,
                definition));
        registration.ApplyPresentation();
    }

    private static void ApplyDedicatedPresentationChrome(Window window)
    {
        // Editor previews keep their XAML folder-tab chrome; only live hosts
        // need the transparent dedicated presentation shell.
        if (window is OverlayPositionPreviewWindow)
        {
            return;
        }

        var surface = window.Content switch
        {
            Border border => border,
            LayoutTransformControl { Child: Border border } => border,
            _ => null,
        };
        if (surface is null)
        {
            return;
        }

        surface.Margin = new Thickness(0);
        surface.Padding = new Thickness(0);
        surface.Background = Brushes.Transparent;
        surface.BorderBrush = null;
        surface.BorderThickness = new Thickness(0);
        surface.CornerRadius = new CornerRadius(0);
        surface.Opacity = 1d;
    }

    internal static void NormalizeLegacyOverlayControl(
        Control control,
        Control rootSurface)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(rootSurface);
        if (ReferenceEquals(control, rootSurface))
        {
            return;
        }

        switch (control)
        {
            case TextBlock text:
                if (text.Classes.Contains("eyebrow"))
                {
                    text.IsVisible = false;
                }

                if (text.FontSize > 12d)
                {
                    text.FontSize = 12d;
                }

                break;
            case Border border when border.Classes.Contains("badge"):
                break;
            case Border border when IsAvaloniaCardChrome(border):
                border.Padding = new Thickness(0);
                border.Background = Brushes.Transparent;
                border.BorderBrush = null;
                border.BorderThickness = new Thickness(0);
                border.CornerRadius = new CornerRadius(0);
                break;
            case StackPanel stack when stack.Spacing > 3d:
                stack.Spacing = 3d;
                break;
            case Grid grid:
                if (grid.RowSpacing > 3d)
                {
                    grid.RowSpacing = 3d;
                }

                if (grid.ColumnSpacing > 5d)
                {
                    grid.ColumnSpacing = 5d;
                }

                break;
            case ProgressBar progress when progress.Height > 4d:
                progress.Height = 3d;
                break;
        }
    }

    internal static void ReplaceSurfaceContent(
        Border surface,
        StackPanel replacement)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(replacement);
        var original = surface.Child;
        surface.Child = null;
        if (original is not null)
        {
            replacement.Children.Add(original);
        }

        surface.Child = replacement;
    }

    private static bool IsAvaloniaCardChrome(Border border)
    {
        var padding = border.Padding;
        var corner = border.CornerRadius;
        return Math.Max(
                Math.Max(padding.Left, padding.Top),
                Math.Max(padding.Right, padding.Bottom)) >= 6d
            && Math.Max(
                Math.Max(corner.TopLeft, corner.TopRight),
                Math.Max(corner.BottomLeft, corner.BottomRight)) >= 5d;
    }

    internal static void ApplyLegacyFormFactor(
        Window window,
        string plotterName)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        var width = GetLegacyFormFactorWidth(plotterName);
        if (width is null)
        {
            return;
        }

        // Catalog width is a preferred floor, not a hard clip edge. Content-
        // driven hosts (WidthAndHeight) may grow so text is not truncated.
        if (double.IsNaN(window.MinWidth) || window.MinWidth <= 0)
        {
            window.MinWidth = width.Value;
        }

        if (window.SizeToContent is SizeToContent.WidthAndHeight
            or SizeToContent.Width)
        {
            if (!double.IsNaN(window.MaxWidth)
                && window.MaxWidth > 0
                && window.MaxWidth < width.Value)
            {
                window.MaxWidth = double.PositiveInfinity;
            }

            return;
        }

        if (window.MaxWidth < width.Value)
        {
            window.MaxWidth = width.Value;
        }

        window.Width = width.Value;
    }

    internal static double? GetLegacyFormFactorWidth(string plotterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        var definition = OverlayLayoutCatalog.Supported.FirstOrDefault(
            candidate => string.Equals(
                candidate.Name,
                plotterName,
                StringComparison.Ordinal));
        return definition?.PreviewSize.Width;
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

        // Respect content-driven hosts: only force the axes the window is not
        // already measuring from its presentation tree.
        if (window.SizeToContent is SizeToContent.Manual
            or SizeToContent.Height)
        {
            window.Width = Scale(registration.BaseWidth, factor);
        }

        if (window.SizeToContent is SizeToContent.Manual
            or SizeToContent.Width)
        {
            window.Height = Scale(registration.BaseHeight, factor);
        }

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

    private sealed class LegacyPresentationRegistration
    {
        private readonly Window window;
        private readonly OverlayLayoutDefinition definition;
        private readonly HashSet<Control> normalized =
            new(ReferenceEqualityComparer.Instance);
        private Border? rootSurface;
        private bool headerApplied;

        public LegacyPresentationRegistration(
            Window window,
            OverlayLayoutDefinition definition)
        {
            this.window = window;
            this.definition = definition;
            window.Opened += OnWindowOpened;
            window.LayoutUpdated += OnLayoutUpdated;
            window.Closed += OnWindowClosed;
        }

        public void ApplyPresentation()
        {
            rootSurface ??= ResolveRootSurface(window);
            if (rootSurface is null)
            {
                return;
            }

            // Shared presentation templates own their full visual grammar.
            // Do not inject headers or normalize spacing into them.
            if (OverlayRuntimePresentationFactory.IsSupported(definition.Name)
                || GuardianOverlayPresentationFactory.IsSupported(
                    definition.Name))
            {
                return;
            }

            ApplyHeader(rootSurface);
            NormalizeInitialTree(rootSurface);
            NormalizeRealizedControls();
        }

        private void ApplyHeader(Border surface)
        {
            if (headerApplied || definition.PreviewSize.Height < 50)
            {
                headerApplied = true;
                return;
            }

            headerApplied = true;
            if (ContainsTitle(surface, definition.DisplayName))
            {
                return;
            }

            var stack = new StackPanel { Spacing = 3d };
            stack.Classes.Add("legacy-runtime-surface");
            _ = window.TryFindResource(
                RavenWarningBrushResource,
                out var warning);
            stack.Children.Add(new TextBlock
            {
                Text = definition.DisplayName,
                FontSize = 12d,
                FontWeight = FontWeight.SemiBold,
                Foreground = warning as IBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            stack.Children.Add(new Border
            {
                Height = 1d,
                Margin = new Thickness(0, 1),
                Background = warning as IBrush,
                Opacity = 0.65d,
            });
            ReplaceSurfaceContent(surface, stack);
        }

        private void NormalizeInitialTree(Control control)
        {
            Normalize(control);
            switch (control)
            {
                case Border { Child: Control child }:
                    NormalizeInitialTree(child);
                    break;
                case Panel panel:
                    foreach (var childControl in panel.Children)
                    {
                        NormalizeInitialTree(childControl);
                    }

                    break;
                case ContentControl { Content: Control content }:
                    NormalizeInitialTree(content);
                    break;
            }
        }

        private void NormalizeRealizedControls()
        {
            foreach (var control in window
                         .GetVisualDescendants()
                         .OfType<Control>())
            {
                Normalize(control);
            }
        }

        private void Normalize(Control control)
        {
            if (!normalized.Add(control) || rootSurface is null)
            {
                return;
            }

            NormalizeLegacyOverlayControl(control, rootSurface);
        }

        private static bool ContainsTitle(Control control, string title)
        {
            if (control is TextBlock text
                && string.Equals(text.Text, title, StringComparison.Ordinal))
            {
                return true;
            }

            return control switch
            {
                Border { Child: Control child } => ContainsTitle(child, title),
                Panel panel => panel.Children.Any(child =>
                    ContainsTitle(child, title)),
                ContentControl { Content: Control content } =>
                    ContainsTitle(content, title),
                _ => false,
            };
        }

        private static Border? ResolveRootSurface(Window candidate)
        {
            return candidate.Content switch
            {
                Border border => border,
                LayoutTransformControl { Child: Border border } => border,
                _ => null,
            };
        }

        private void OnWindowOpened(object? sender, EventArgs eventArgs)
        {
            ApplyPresentation();
        }

        private void OnLayoutUpdated(object? sender, EventArgs eventArgs)
        {
            NormalizeRealizedControls();
        }

        private void OnWindowClosed(object? sender, EventArgs eventArgs)
        {
            window.Opened -= OnWindowOpened;
            window.LayoutUpdated -= OnLayoutUpdated;
            window.Closed -= OnWindowClosed;
            normalized.Clear();
        }
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

    private sealed class LayoutSettingsRegistration
    {
        private readonly Window window;
        private readonly LegacyOverlayLayout layout;
        private readonly string plotterName;
        private bool closed;

        public LayoutSettingsRegistration(
            Window window,
            LegacyOverlayLayout layout,
            string plotterName)
        {
            this.window = window;
            this.layout = layout;
            this.plotterName = plotterName;
            layout.Changed += OnLayoutChanged;
            window.Closed += OnWindowClosed;
        }

        public void Validate(
            LegacyOverlayLayout expectedLayout,
            string expectedPlotterName)
        {
            if (!ReferenceEquals(layout, expectedLayout)
                || !string.Equals(
                    plotterName,
                    expectedPlotterName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{window.GetType().Name} is already wired to "
                        + $"'{plotterName}' using a different overlay layout.");
            }
        }

        private void OnLayoutChanged(object? sender, EventArgs eventArgs)
        {
            if (closed)
            {
                return;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                Refresh();
                return;
            }

            Dispatcher.UIThread.Post(Refresh);
        }

        private void Refresh()
        {
            if (closed)
            {
                return;
            }

            ApplyScale(window, layout, plotterName);
            ApplyOpacity(window, layout, plotterName);
        }

        private void OnWindowClosed(object? sender, EventArgs eventArgs)
        {
            closed = true;
            layout.Changed -= OnLayoutChanged;
            window.Closed -= OnWindowClosed;
        }
    }
}
