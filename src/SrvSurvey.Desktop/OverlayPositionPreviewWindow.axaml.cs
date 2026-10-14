using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

internal readonly record struct OverlayPreviewPanelMetrics(PixelPoint OriginOffset, PixelSize PanelSize);

public sealed partial class OverlayPositionPreviewWindow : Window
{
    private double globalOpacity = 1d;
    private double? opacityOverride;
    private int globalScaleIndex;
    private int? scaleOverride;
    private double scaleRenderScaling = 1d;
    private double scaleFactor = 1d;
    private OverlayTypographyScale typographyScale = OverlayTypographyScale.Default;
    private readonly bool usesRuntimePresentation;
    private readonly IReadOnlyList<OverlayEditorPreviewStateDefinition> previewStates;
    private Control? runtimePresentation;
    private int previewStateIndex;
    private double resizeWidth;
    private double resizeHeight;
    private double minimumResizeWidth;
    private double minimumResizeHeight;

    /// <summary>Creates the default sample-data preview with shared presentation scaling.</summary>
    public OverlayPositionPreviewWindow()
    {
        InitializeComponent();
        Opened += OnRenderSurfaceChanged;
        ScalingChanged += OnRenderSurfaceChanged;
        Definition = OverlayLayoutCatalog.Supported[0];
        previewStates = OverlayRuntimePresentationFactory.GetEditorPreviewStates(Definition.Name);
        Preview = OverlayPositionPreviewViewModel.Create(Definition);
        DataContext = Preview;
        EnsureEditorFolderTab(Definition.DisplayName);
        usesRuntimePresentation = TryUseRuntimePresentation();
        ApplyContentSize();
    }

    /// <summary>Creates an editor preview for the same presentation used by the chosen live panel.</summary>
    public OverlayPositionPreviewWindow(OverlayLayoutDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        InitializeComponent();
        Opened += OnRenderSurfaceChanged;
        ScalingChanged += OnRenderSurfaceChanged;
        previewStates = OverlayRuntimePresentationFactory.GetEditorPreviewStates(Definition.Name);
        Preview = OverlayPositionPreviewViewModel.Create(definition);
        DataContext = Preview;
        EnsureEditorFolderTab(definition.DisplayName);
        usesRuntimePresentation = TryUseRuntimePresentation();
        ApplyContentSize();
        Title = $"SrvSurvey {definition.DisplayName} position preview overlay";
    }

    /// <summary>
    /// Forces the editor-only folder tab text/visibility from the catalog
    /// display name so identification never depends solely on bindings.
    /// </summary>
    private void EnsureEditorFolderTab(string displayName)
    {
        string label = string.IsNullOrWhiteSpace(displayName) ? Definition.Name : displayName.Trim();
        bool usesCenteredTopAnchor = string.Equals(
            Definition.Name,
            "PlotSurfaceMiningSurvey",
            StringComparison.Ordinal
        );
        EditorFolderTab.IsVisible = true;
        EditorFolderTab.HorizontalAlignment = usesCenteredTopAnchor
            ? Avalonia.Layout.HorizontalAlignment.Center
            : Avalonia.Layout.HorizontalAlignment.Left;
        EditorFolderTab.Margin = usesCenteredTopAnchor ? new Thickness(0) : new Thickness(10, 0, 16, 0);
        PreviewBody.CornerRadius = usesCenteredTopAnchor ? new CornerRadius(7) : new CornerRadius(0, 7, 7, 7);
        EditorFolderTabLabel.Text = label;
        ToolTip.SetTip(EditorFolderTab, label);
        UpdateEditorPreviewStateButton();
    }

    public OverlayLayoutDefinition Definition { get; }

    public OverlayPositionPreviewViewModel Preview { get; }

    internal Control? RuntimePresentation => runtimePresentation;

    /// <summary>Editor-only folder tab chrome (tests / diagnostics).</summary>
    internal Border EditorFolderTabControl => EditorFolderTab;

    /// <summary>Editor-only folder tab label (tests / diagnostics).</summary>
    internal TextBlock EditorFolderTabLabelControl => EditorFolderTabLabel;

    /// <summary>Editor-only simulated state cycle button.</summary>
    internal Button EditorFolderTabStateButtonControl => EditorFolderTabStateButton;

    /// <summary>Editor-only simulated state label.</summary>
    internal TextBlock EditorFolderTabStateLabelControl => EditorFolderTabStateLabel;

    internal int EditorPreviewStateCount => previewStates.Count;

    internal string CurrentEditorPreviewStateName => previewStates[previewStateIndex].DisplayName;

    /// <summary>Editor-only bordered preview body (tests / diagnostics).</summary>
    internal Border PreviewBodyControl => PreviewBody;

    public event EventHandler<OverlayPreviewSettingsRequestedEventArgs>? SettingsRequested;

    public event EventHandler<OverlayPreviewSizeChangedEventArgs>? PanelSizeChanged;

    public PixelSize GetExpectedPixelSize(double scaling)
    {
        double safeScaling = double.IsFinite(scaling) && scaling > 0 ? scaling : 1;
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
            double genericScale = safeScaling * scaleFactor;
            return new PixelSize(
                Math.Max(1, (int)Math.Ceiling(Preview.PreferredWidth * genericScale)),
                Math.Max(1, (int)Math.Ceiling(unscaledHeight * genericScale))
            );
        }

        double effectiveScale = safeScaling * scaleFactor;
        Size measured = MeasureRuntimePresentationSize();
        return new PixelSize(
            Math.Max(1, (int)Math.Ceiling(measured.Width * effectiveScale)),
            Math.Max(1, (int)Math.Ceiling(measured.Height * effectiveScale))
        );
    }

    public PixelSize GetCurrentPixelSize(double scaling)
    {
        double safeScaling = double.IsFinite(scaling) && scaling > 0 ? scaling : 1;
        return Bounds.Width > 0 && Bounds.Height > 0
            ? new PixelSize(
                Math.Max(1, (int)Math.Ceiling(Bounds.Width * safeScaling)),
                Math.Max(1, (int)Math.Ceiling(Bounds.Height * safeScaling))
            )
            : GetExpectedPixelSize(safeScaling);
    }

    internal OverlayPreviewPanelMetrics GetPanelMetrics(double scaling)
    {
        double safeScaling = double.IsFinite(scaling) && scaling > 0 ? scaling : 1d;
        Control panel = runtimePresentation ?? PreviewBody;
        Point? panelOrigin = panel.TranslatePoint(default, this);
        Point origin = panelOrigin ?? default;
        var originOffset = new PixelPoint(
            (int)Math.Round(origin.X * safeScaling),
            (int)Math.Round(origin.Y * safeScaling)
        );
        Point? panelExtent = panel.TranslatePoint(new Point(panel.Bounds.Width, panel.Bounds.Height), this);
        PixelSize panelSize;
        if (panelOrigin is { } start && panelExtent is { } end && end.X > start.X && end.Y > start.Y)
        {
            panelSize = new PixelSize(
                Math.Max(1, (int)Math.Ceiling((end.X - start.X) * safeScaling)),
                Math.Max(1, (int)Math.Ceiling((end.Y - start.Y) * safeScaling))
            );
        }
        else
        {
            double fallbackScale = safeScaling * scaleFactor;
            panelSize = new PixelSize(
                Math.Max(1, (int)Math.Ceiling(Definition.PreviewSize.Width * fallbackScale)),
                Math.Max(1, (int)Math.Ceiling(Definition.PreviewSize.Height * fallbackScale))
            );
        }

        return new OverlayPreviewPanelMetrics(originOffset, panelSize);
    }

    internal PixelPoint GetPanelScreenOrigin(double scaling)
    {
        PixelPoint offset = GetPanelMetrics(scaling).OriginOffset;
        return new PixelPoint(Position.X + offset.X, Position.Y + offset.Y);
    }

    internal PixelSize GetConfiguredPanelPixelSize(OverlayPanelSize size, double scaling)
    {
        ArgumentNullException.ThrowIfNull(size);
        double safeScaling = double.IsFinite(scaling) && scaling > 0 ? scaling : 1d;
        double effectiveScale = safeScaling * scaleFactor;
        return new PixelSize(
            Math.Max(1, (int)Math.Ceiling(size.Width * effectiveScale)),
            Math.Max(1, (int)Math.Ceiling(size.Height * effectiveScale))
        );
    }

    public void ConfigureScale(int globalIndex, int? overlayOverride, double renderScaling)
    {
        if (overlayOverride is { } value && !OverlayScaleCatalog.IsSupported(value))
        {
            throw new ArgumentOutOfRangeException(nameof(overlayOverride));
        }

        globalScaleIndex = OverlayScaleCatalog.NormalizeIndex(globalIndex);
        scaleOverride = overlayOverride;
        scaleRenderScaling = double.IsFinite(renderScaling) && renderScaling > 0 ? renderScaling : 1d;
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
        double opacity = opacityOverride ?? globalOpacity;
        // Dim the body with the preview opacity; keep the editor folder tab
        // fully readable for panel identification.
        PreviewBody.Opacity = opacity;
        PreviewSurface.Opacity = 1d;
    }

    public void ConfigureTypography(OverlayTypographyScale scale)
    {
        ArgumentNullException.ThrowIfNull(scale);
        typographyScale = scale;
        RefreshTypographyBaseline();
    }

    /// <summary>Sizes the shared panel and trims its editor tab without letting the label constrain manual resizing.</summary>
    public void ConfigureSize(OverlayPanelSize? size)
    {
        if (runtimePresentation is null)
        {
            PanelResizeThumb.IsVisible = false;
            return;
        }

        EditorFolderTab.MaxWidth = size is null
            ? double.PositiveInfinity
            : Math.Max(
                EditorFolderTab.MinWidth,
                size.Width
                    + PreviewBody.BorderThickness.Left
                    + PreviewBody.BorderThickness.Right
                    - EditorFolderTab.Margin.Left
                    - EditorFolderTab.Margin.Right
            );
        OverlayThemeResources.ApplyPanelSize(runtimePresentation, size);
        PreviewSurface.InvalidateMeasure();
        InvalidateMeasure();
    }

    /// <summary>Applies the live colonization settings to the shared presentation without replacing its sample plan.</summary>
    internal void ConfigureColonizationPreferences(ColonizationOverlayPreferences preferences)
    {
        if (runtimePresentation?.DataContext is ColonizationCommodityOverlayViewModel colonization)
        {
            colonization.ApplyPreferences(preferences);
        }
    }

    internal void RefreshTypographyBaseline()
    {
        if (runtimePresentation is not null)
        {
            OverlayTypographyResources.ApplyWhenAttached(runtimePresentation, typographyScale);
        }
    }

    private void ApplyContentSize()
    {
        if (usesRuntimePresentation)
        {
            // Match live hosts: the shared presentation (or the editor-only
            // folder tab when it is wider) owns the measured width. Keeping
            // the catalog width as a window floor leaves a transparent span
            // behind compact content that looks like a second panel when the
            // preview opacity is reduced.
            MinWidth = 1;
            MaxWidth = double.PositiveInfinity;
            MinHeight = 1;
            MaxHeight = double.PositiveInfinity;
            Width = double.NaN;
            Height = double.NaN;
            SizeToContent = SizeToContent.WidthAndHeight;
            return;
        }

        Width = Preview.PreferredWidth;
        MinWidth = Width;
        MaxWidth = Width;
        if (Preview.IsCompact)
        {
            Height = Definition.PreviewSize.Height;
            MinHeight = Height;
            MaxHeight = Height;
            SizeToContent = SizeToContent.Manual;
        }
    }

    private double MeasurePreviewContentHeight()
    {
        PreviewSurface.Measure(new Size(Preview.PreferredWidth, double.PositiveInfinity));
        return Math.Max(1d, PreviewSurface.DesiredSize.Height);
    }

    private Size MeasureRuntimePresentationSize()
    {
        var available = new Size(double.PositiveInfinity, double.PositiveInfinity);
        PreviewSurface.Measure(available);
        Size desired = PreviewSurface.DesiredSize;
        // Prefer live measured content; catalog width is only a soft floor when
        // the presentation actually wants that space (MinWidth on the host).
        double width = Math.Max(
            1d,
            double.IsFinite(desired.Width) && desired.Width > 0 ? desired.Width : Definition.PreviewSize.Width
        );
        if (double.IsFinite(MinWidth) && MinWidth > 0)
        {
            width = Math.Max(width, MinWidth);
        }

        double height = Math.Max(
            1d,
            double.IsFinite(desired.Height) && desired.Height > 0 ? desired.Height : Definition.PreviewSize.Height
        );
        return new Size(width, height);
    }

    internal void ApplyRuntimePresentationTheme()
    {
        if (usesRuntimePresentation)
        {
            OverlayThemeResources.ApplyLegacyPresentation(this, Definition.Name);
        }
    }

    private bool TryUseRuntimePresentation()
    {
        if (
            !OverlayRuntimePresentationFactory.TryCreate(
                Definition.Name,
                previewStateIndex,
                out Control? presentation,
                out _
            ) || presentation is null
        )
        {
            return false;
        }

        runtimePresentation = presentation;
        presentation.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        presentation.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        // Host the real shared template inside the yellow body; the folder
        // tab above remains editor-only chrome for identification.
        PreviewBody.Child = presentation;
        PreviewBody.Padding = new Thickness(0);
        PreviewBody.Background = Avalonia.Media.Brushes.Transparent;
        return true;
    }

    /// <summary>Changes sample content while the editor host preserves the panel's inward-facing corner.</summary>
    internal bool CycleEditorPreviewState()
    {
        if (previewStates.Count <= 1 || runtimePresentation is null)
        {
            return false;
        }

        previewStateIndex = (previewStateIndex + 1) % previewStates.Count;
        object? previousDataContext = runtimePresentation.DataContext;
        runtimePresentation.DataContext = OverlayRuntimePresentationFactory.CreateEditorDataContextOnly(
            Definition.Name,
            previewStateIndex
        );
        DisposeEditorDataContext(previousDataContext);
        UpdateEditorPreviewStateButton();
        runtimePresentation.InvalidateMeasure();
        PreviewSurface.InvalidateMeasure();
        InvalidateMeasure();
        return true;
    }

    private void UpdateEditorPreviewStateButton()
    {
        bool hasMultipleStates = previewStates.Count > 1;
        EditorFolderTabStateButton.IsVisible = hasMultipleStates;
        if (!hasMultipleStates)
        {
            EditorFolderTabStateLabel.Text = string.Empty;
            ToolTip.SetTip(EditorFolderTabStateButton, null);
            return;
        }

        OverlayEditorPreviewStateDefinition state = previewStates[previewStateIndex];
        OverlayEditorPreviewStateDefinition next = previewStates[(previewStateIndex + 1) % previewStates.Count];
        EditorFolderTabStateLabel.Text = $"{state.DisplayName} {previewStateIndex + 1}/{previewStates.Count}";
        ToolTip.SetTip(
            EditorFolderTabStateButton,
            $"Preview state: {state.DisplayName}. Click to show {next.DisplayName}."
        );
    }

    private void OnEditorFolderTabStateButtonClick(object? sender, RoutedEventArgs eventArgs)
    {
        _ = CycleEditorPreviewState();
        eventArgs.Handled = true;
    }

    /// <summary>Applies the selected scale against the preview's actual rendering monitor once attached.</summary>
    private void ApplyConfiguredScale()
    {
        int scaleIndex = scaleOverride ?? globalScaleIndex;
        double scaling = OverlayThemeResources.GetPresentationScaling(this, scaleRenderScaling);
        scaleFactor = OverlayScaleCatalog.GetRelativeScale(scaleIndex, scaling);
        OverlayThemeResources.ApplyScale(this, scaleIndex, scaling);
    }

    /// <summary>Resolves legacy and relative scale settings on the monitor actually rendering this preview.</summary>
    private void OnRenderSurfaceChanged(object? sender, EventArgs eventArgs) => ApplyConfiguredScale();

    private void OnPreviewSurfacePointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (!eventArgs.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            return;
        }

        SettingsRequested?.Invoke(this, new OverlayPreviewSettingsRequestedEventArgs(Definition.Name));
        eventArgs.Handled = true;
    }

    /// <summary>Captures the shared panel's unscaled dimensions at the start of a manual resize.</summary>
    private void OnPanelResizeStarted(object? sender, VectorEventArgs eventArgs)
    {
        if (runtimePresentation is null)
        {
            return;
        }

        // The folder tab can make the visible frame wider than its natural content.
        // Start from that frame so the first movement cannot disappear into an invisible width gap.
        resizeWidth = Math.Max(
            1d,
            PreviewBody.Bounds.Width - PreviewBody.BorderThickness.Left - PreviewBody.BorderThickness.Right
        );
        resizeHeight = Math.Max(
            1d,
            PreviewBody.Bounds.Height - PreviewBody.BorderThickness.Top - PreviewBody.BorderThickness.Bottom
        );
        minimumResizeWidth = Math.Min(120d, resizeWidth);
        minimumResizeHeight = Math.Min(48d, resizeHeight);
        eventArgs.Handled = true;
    }

    /// <summary>Uses the grip's already transformed local delta so every panel follows the pointer at any scale.</summary>
    private void OnPanelResizeDelta(object? sender, VectorEventArgs eventArgs)
    {
        if (runtimePresentation is null)
        {
            return;
        }

        resizeWidth = Math.Max(minimumResizeWidth, resizeWidth + eventArgs.Vector.X);
        resizeHeight = Math.Max(minimumResizeHeight, resizeHeight + eventArgs.Vector.Y);
        var updated = new OverlayPanelSize(Math.Round(resizeWidth), Math.Round(resizeHeight));
        ConfigureSize(updated);
        PanelSizeChanged?.Invoke(this, new OverlayPreviewSizeChangedEventArgs(Definition.Name, updated));
        eventArgs.Handled = true;
    }

    /// <summary>Consumes the completed resize gesture without forwarding it to panel movement.</summary>
    private static void OnPanelResizeCompleted(object? sender, VectorEventArgs eventArgs)
    {
        eventArgs.Handled = true;
    }

    private static void ValidateOpacity(double opacity, string parameterName)
    {
        if (!double.IsFinite(opacity) || opacity is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Overlay opacity must be from 0 to 1.");
        }
    }

    /// <summary>Detaches rendering callbacks and disposes the preview's owned sample data.</summary>
    protected override void OnClosed(EventArgs e)
    {
        Opened -= OnRenderSurfaceChanged;
        ScalingChanged -= OnRenderSurfaceChanged;
        DisposeEditorDataContext(runtimePresentation?.DataContext);
        runtimePresentation?.DataContext = null;
        base.OnClosed(e);
    }

    private static void DisposeEditorDataContext(object? dataContext)
    {
        if (dataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
