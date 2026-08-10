using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

internal readonly record struct OverlayPreviewPanelMetrics(
    PixelPoint OriginOffset,
    PixelSize PanelSize);

public sealed partial class OverlayPositionPreviewWindow : Window
{
    private double globalOpacity = 1d;
    private double? opacityOverride;
    private int globalScaleIndex;
    private int? scaleOverride;
    private double scaleRenderScaling = 1d;
    private double scaleFactor = 1d;
    private readonly bool usesRuntimePresentation;
    private readonly IReadOnlyList<OverlayEditorPreviewStateDefinition>
        previewStates;
    private Control? runtimePresentation;
    private int previewStateIndex;

    public OverlayPositionPreviewWindow()
    {
        InitializeComponent();
        Definition = OverlayLayoutCatalog.Supported[0];
        previewStates = OverlayRuntimePresentationFactory
            .GetEditorPreviewStates(Definition.Name);
        Preview = OverlayPositionPreviewViewModel.Create(Definition);
        DataContext = Preview;
        EnsureEditorFolderTab(Definition.DisplayName);
        usesRuntimePresentation = TryUseRuntimePresentation();
        ApplyContentSize();
    }

    public OverlayPositionPreviewWindow(OverlayLayoutDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        InitializeComponent();
        previewStates = OverlayRuntimePresentationFactory
            .GetEditorPreviewStates(Definition.Name);
        Preview = OverlayPositionPreviewViewModel.Create(definition);
        DataContext = Preview;
        EnsureEditorFolderTab(definition.DisplayName);
        usesRuntimePresentation = TryUseRuntimePresentation();
        ApplyContentSize();
        Title = $"{definition.DisplayName} position preview";
    }

    /// <summary>
    /// Forces the editor-only folder tab text/visibility from the catalog
    /// display name so identification never depends solely on bindings.
    /// </summary>
    private void EnsureEditorFolderTab(string displayName)
    {
        var label = string.IsNullOrWhiteSpace(displayName)
            ? Definition.Name
            : displayName.Trim();
        EditorFolderTab.IsVisible = true;
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
    internal Button EditorFolderTabStateButtonControl =>
        EditorFolderTabStateButton;

    /// <summary>Editor-only simulated state label.</summary>
    internal TextBlock EditorFolderTabStateLabelControl =>
        EditorFolderTabStateLabel;

    internal int EditorPreviewStateCount => previewStates.Count;

    internal string CurrentEditorPreviewStateName =>
        previewStates[previewStateIndex].DisplayName;

    /// <summary>Editor-only bordered preview body (tests / diagnostics).</summary>
    internal Border PreviewBodyControl => PreviewBody;

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
        var measured = MeasureRuntimePresentationSize();
        return new PixelSize(
            Math.Max(
                1,
                (int)Math.Ceiling(measured.Width * effectiveScale)),
            Math.Max(
                1,
                (int)Math.Ceiling(measured.Height * effectiveScale)));
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

    internal OverlayPreviewPanelMetrics GetPanelMetrics(double scaling)
    {
        var safeScaling = double.IsFinite(scaling) && scaling > 0
            ? scaling
            : 1d;
        var panel = runtimePresentation ?? PreviewBody;
        var panelOrigin = panel.TranslatePoint(default, this);
        var origin = panelOrigin ?? default;
        var originOffset = new PixelPoint(
            (int)Math.Round(origin.X * safeScaling),
            (int)Math.Round(origin.Y * safeScaling));
        var panelExtent = panel.TranslatePoint(
            new Point(panel.Bounds.Width, panel.Bounds.Height),
            this);
        PixelSize panelSize;
        if (panelOrigin is { } start
            && panelExtent is { } end
            && end.X > start.X
            && end.Y > start.Y)
        {
            panelSize = new PixelSize(
                Math.Max(
                    1,
                    (int)Math.Ceiling((end.X - start.X) * safeScaling)),
                Math.Max(
                    1,
                    (int)Math.Ceiling((end.Y - start.Y) * safeScaling)));
        }
        else
        {
            var fallbackScale = safeScaling * scaleFactor;
            panelSize = new PixelSize(
                Math.Max(
                    1,
                    (int)Math.Ceiling(
                        Definition.PreviewSize.Width * fallbackScale)),
                Math.Max(
                    1,
                    (int)Math.Ceiling(
                        Definition.PreviewSize.Height * fallbackScale)));
        }

        return new OverlayPreviewPanelMetrics(originOffset, panelSize);
    }

    internal PixelPoint GetPanelScreenOrigin(double scaling)
    {
        var offset = GetPanelMetrics(scaling).OriginOffset;
        return new PixelPoint(
            Position.X + offset.X,
            Position.Y + offset.Y);
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
        var opacity = opacityOverride ?? globalOpacity;
        // Dim the body with the preview opacity; keep the editor folder tab
        // fully readable for panel identification.
        PreviewBody.Opacity = opacity;
        PreviewSurface.Opacity = 1d;
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
        PreviewSurface.Measure(new Size(
            Preview.PreferredWidth,
            double.PositiveInfinity));
        return Math.Max(1d, PreviewSurface.DesiredSize.Height);
    }

    private Size MeasureRuntimePresentationSize()
    {
        var available = new Size(
            double.PositiveInfinity,
            double.PositiveInfinity);
        PreviewSurface.Measure(available);
        var desired = PreviewSurface.DesiredSize;
        // Prefer live measured content; catalog width is only a soft floor when
        // the presentation actually wants that space (MinWidth on the host).
        var width = Math.Max(
            1d,
            double.IsFinite(desired.Width) && desired.Width > 0
                ? desired.Width
                : Definition.PreviewSize.Width);
        if (double.IsFinite(MinWidth) && MinWidth > 0)
        {
            width = Math.Max(width, MinWidth);
        }

        var height = Math.Max(
            1d,
            double.IsFinite(desired.Height) && desired.Height > 0
                ? desired.Height
                : Definition.PreviewSize.Height);
        return new Size(width, height);
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
                previewStateIndex,
                out var presentation,
                out _)
            || presentation is null)
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

    internal bool CycleEditorPreviewState()
    {
        if (previewStates.Count <= 1 || runtimePresentation is null)
        {
            return false;
        }

        previewStateIndex = (previewStateIndex + 1) % previewStates.Count;
        var previousDataContext = runtimePresentation.DataContext;
        runtimePresentation.DataContext = OverlayRuntimePresentationFactory
            .CreateEditorDataContextOnly(
                Definition.Name,
                previewStateIndex);
        DisposeEditorDataContext(previousDataContext);
        UpdateEditorPreviewStateButton();
        runtimePresentation.InvalidateMeasure();
        PreviewSurface.InvalidateMeasure();
        InvalidateMeasure();
        return true;
    }

    private void UpdateEditorPreviewStateButton()
    {
        var hasMultipleStates = previewStates.Count > 1;
        EditorFolderTabStateButton.IsVisible = hasMultipleStates;
        if (!hasMultipleStates)
        {
            EditorFolderTabStateLabel.Text = string.Empty;
            ToolTip.SetTip(EditorFolderTabStateButton, null);
            return;
        }

        var state = previewStates[previewStateIndex];
        var next = previewStates[(previewStateIndex + 1) % previewStates.Count];
        EditorFolderTabStateLabel.Text =
            $"{state.DisplayName} {previewStateIndex + 1}/{previewStates.Count}";
        ToolTip.SetTip(
            EditorFolderTabStateButton,
            $"Preview state: {state.DisplayName}. Click to show {next.DisplayName}.");
    }

    private void OnEditorFolderTabStateButtonClick(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        _ = CycleEditorPreviewState();
        eventArgs.Handled = true;
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

    protected override void OnClosed(EventArgs e)
    {
        DisposeEditorDataContext(runtimePresentation?.DataContext);
        if (runtimePresentation is not null)
        {
            runtimePresentation.DataContext = null;
        }
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
