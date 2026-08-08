using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class OverlayPositionPreviewWindow : Window
{
    private double globalOpacity = 1d;
    private double? opacityOverride;
    private int globalScaleIndex;
    private int? scaleOverride;
    private double scaleRenderScaling = 1d;
    private double scaleFactor = 1d;
    private readonly bool usesRuntimePresentation;
    private Control? runtimePresentation;

    public OverlayPositionPreviewWindow()
    {
        InitializeComponent();
        Definition = OverlayLayoutCatalog.Supported[0];
        Preview = OverlayPositionPreviewViewModel.Create(Definition);
        DataContext = Preview;
        usesRuntimePresentation = TryUseRuntimePresentation();
        ApplyContentSize();
    }

    public OverlayPositionPreviewWindow(OverlayLayoutDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        InitializeComponent();
        Preview = OverlayPositionPreviewViewModel.Create(definition);
        DataContext = Preview;
        usesRuntimePresentation = TryUseRuntimePresentation();
        ApplyContentSize();
        Title = $"{definition.DisplayName} position preview";
    }

    public OverlayLayoutDefinition Definition { get; }

    public OverlayPositionPreviewViewModel Preview { get; }

    internal Control? RuntimePresentation => runtimePresentation;

    public event EventHandler<OverlayPreviewSettingsRequestedEventArgs>?
        SettingsRequested;

    public PixelSize GetExpectedPixelSize(double scaling)
    {
        var safeScaling = double.IsFinite(scaling) && scaling > 0
            ? scaling
            : 1;
        if (!usesRuntimePresentation)
        {
            double unscaledHeight;
            if (Preview.IsCompact)
            {
                unscaledHeight = Definition.PreviewSize.Height;
            }
            else if (Preview.IsRouteBio)
            {
                unscaledHeight = Preview.EstimatedHeight;
            }
            else
            {
                unscaledHeight = MeasurePreviewContentHeight();
            }
            var genericScale = safeScaling * scaleFactor;
            return new PixelSize(
                Math.Max(
                    1,
                    (int)Math.Ceiling(
                        Preview.PreferredWidth * genericScale)),
                Math.Max(
                    1,
                    (int)Math.Ceiling(unscaledHeight * genericScale)));
        }

        var effectiveScale = safeScaling * scaleFactor;
        return new PixelSize(
            Math.Max(
                1,
                (int)Math.Ceiling(
                    Definition.PreviewSize.Width * effectiveScale)),
            Math.Max(
                1,
                (int)Math.Ceiling(
                    Definition.PreviewSize.Height * effectiveScale)));
    }

    public PixelSize GetCurrentPixelSize(double scaling)
    {
        var safeScaling = double.IsFinite(scaling) && scaling > 0
            ? scaling
            : 1;
        return Bounds.Width > 0 && Bounds.Height > 0
            ? new PixelSize(
                Math.Max(1, (int)Math.Ceiling(Bounds.Width * safeScaling)),
                Math.Max(1, (int)Math.Ceiling(Bounds.Height * safeScaling)))
            : GetExpectedPixelSize(safeScaling);
    }

    public void ConfigureScale(
        int globalIndex,
        int? overlayOverride,
        double renderScaling)
    {
        if (overlayOverride is { } value
            && !OverlayScaleCatalog.IsSupported(value))
        {
            throw new ArgumentOutOfRangeException(nameof(overlayOverride));
        }

        globalScaleIndex = OverlayScaleCatalog.NormalizeIndex(globalIndex);
        scaleOverride = overlayOverride;
        scaleRenderScaling = double.IsFinite(renderScaling) && renderScaling > 0
            ? renderScaling
            : 1d;
        ApplyConfiguredScale();
    }

    public void ConfigureOpacity(double global, double? overlayOverride)
    {
        ValidateOpacity(global, nameof(global));
        if (overlayOverride is not null)
        {
            ValidateOpacity(overlayOverride.Value, nameof(overlayOverride));
        }

        globalOpacity = global;
        opacityOverride = overlayOverride;
        PreviewSurface.Opacity = opacityOverride ?? globalOpacity;
    }

    private void ApplyContentSize()
    {
        Width = usesRuntimePresentation
            ? Definition.PreviewSize.Width
            : Preview.PreferredWidth;
        MinWidth = Width;
        MaxWidth = Width;
        if (usesRuntimePresentation || Preview.IsCompact)
        {
            Height = Definition.PreviewSize.Height;
            MinHeight = Height;
            MaxHeight = Height;
            SizeToContent = SizeToContent.Manual;
        }
    }

    private double MeasurePreviewContentHeight()
    {
        PreviewSurface.Measure(new Size(
            Preview.PreferredWidth,
            double.PositiveInfinity));
        return Math.Max(1d, PreviewSurface.DesiredSize.Height);
    }

    internal void ApplyRuntimePresentationTheme()
    {
        if (usesRuntimePresentation)
        {
            OverlayThemeResources.ApplyLegacyPresentation(
                this,
                Definition.Name);
        }
    }

    private bool TryUseRuntimePresentation()
    {
        if (!OverlayRuntimePresentationFactory.TryCreate(
                Definition.Name,
                out var presentation,
                out _)
            || presentation is null)
        {
            return false;
        }

        runtimePresentation = presentation;
        // Host the real shared template; outer PreviewSurface keeps the
        // editor-only yellow drag border around it.
        PreviewSurface.Child = presentation;
        PreviewSurface.Padding = new Thickness(0);
        PreviewSurface.Background = Avalonia.Media.Brushes.Transparent;
        return true;
    }

    private void ApplyConfiguredScale()
    {
        var scaleIndex = scaleOverride ?? globalScaleIndex;
        scaleFactor = OverlayScaleCatalog.GetRelativeScale(
            scaleIndex,
            scaleRenderScaling);
        OverlayThemeResources.ApplyScale(
            this,
            scaleIndex,
            scaleRenderScaling);
    }

    private void OnPreviewSurfacePointerPressed(
        object? sender,
        PointerPressedEventArgs eventArgs)
    {
        if (!eventArgs.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            return;
        }

        SettingsRequested?.Invoke(
            this,
            new OverlayPreviewSettingsRequestedEventArgs(Definition.Name));
        eventArgs.Handled = true;
    }

    private static void ValidateOpacity(double opacity, string parameterName)
    {
        if (!double.IsFinite(opacity) || opacity is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Overlay opacity must be from 0 to 1.");
        }
    }
}
