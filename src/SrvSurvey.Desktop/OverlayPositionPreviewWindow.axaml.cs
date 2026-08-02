using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class OverlayPositionPreviewWindow : Window
{
    private static readonly OverlayScaleOption[]
        IndividualScaleOptions = OverlayScaleCatalog.Options
            .Where(option => option.AbsoluteScale is not null)
            .OrderBy(option => option.AbsoluteScale)
            .ToArray();
    private bool updatingOpacityControls;
    private bool updatingScaleControls;
    private double globalOpacity = 1d;
    private double? opacityOverride;
    private int globalScaleIndex;
    private int? scaleOverride;
    private double scaleRenderScaling = 1d;
    private double scaleFactor = 1d;

    public OverlayPositionPreviewWindow()
    {
        InitializeComponent();
        Definition = OverlayLayoutCatalog.Supported[0];
        Preview = OverlayPositionPreviewViewModel.Create(Definition);
        DataContext = Preview;
        ApplyContentSize();
    }

    public OverlayPositionPreviewWindow(OverlayLayoutDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        InitializeComponent();
        Preview = OverlayPositionPreviewViewModel.Create(definition);
        DataContext = Preview;
        ApplyContentSize();
        Title = $"{definition.DisplayName} position preview";
    }

    public OverlayLayoutDefinition Definition { get; }

    public OverlayPositionPreviewViewModel Preview { get; }

    public event EventHandler<OverlayPreviewOpacityChangedEventArgs>?
        OpacityOverrideChanged;

    public event EventHandler<OverlayPreviewScaleChangedEventArgs>?
        ScaleOverrideChanged;

    public PixelSize GetExpectedPixelSize(double scaling)
    {
        var safeScaling = double.IsFinite(scaling) && scaling > 0
            ? scaling
            : 1;
        return Preview.GetEstimatedPixelSize(safeScaling * scaleFactor);
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

    private void ApplyContentSize()
    {
        Width = Preview.PreferredWidth;
        MinWidth = Width;
        MaxWidth = Width;
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

        updatingScaleControls = true;
        globalScaleIndex = OverlayScaleCatalog.NormalizeIndex(globalIndex);
        scaleOverride = overlayOverride;
        scaleRenderScaling = double.IsFinite(renderScaling) && renderScaling > 0
            ? renderScaling
            : 1d;
        UseGlobalScaleCheckBox.IsChecked = scaleOverride is null;
        ScaleOverrideSlider.IsEnabled = scaleOverride is not null;
        ScaleOverrideSlider.Value = GetScaleOptionOrdinal(
            scaleOverride ?? GetIndividualFallback(globalScaleIndex).Index);
        ApplyConfiguredScale();
        UpdateScaleDisplay();
        updatingScaleControls = false;
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

    public void ConfigureOpacity(double global, double? overlayOverride)
    {
        ValidateOpacity(global, nameof(global));
        if (overlayOverride is not null)
        {
            ValidateOpacity(overlayOverride.Value, nameof(overlayOverride));
        }

        updatingOpacityControls = true;
        globalOpacity = global;
        opacityOverride = overlayOverride;
        UseGlobalOpacityCheckBox.IsChecked = overlayOverride is null;
        OpacityOverrideSlider.IsEnabled = overlayOverride is not null;
        OpacityOverrideSlider.Value = (overlayOverride ?? global) * 100d;
        UpdateOpacityDisplay();
        updatingOpacityControls = false;
    }

    private void OnUseGlobalOpacityChanged(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (updatingOpacityControls)
        {
            return;
        }

        opacityOverride = UseGlobalOpacityCheckBox.IsChecked == true
            ? null
            : OpacityOverrideSlider.Value / 100d;
        OpacityOverrideSlider.IsEnabled = opacityOverride is not null;
        UpdateOpacityDisplay();
        OpacityOverrideChanged?.Invoke(
            this,
            new OverlayPreviewOpacityChangedEventArgs(
                Definition.Name,
                opacityOverride));
    }

    private void OnOpacityOverrideValueChanged(
        object? sender,
        RangeBaseValueChangedEventArgs eventArgs)
    {
        if (updatingOpacityControls || opacityOverride is null)
        {
            return;
        }

        opacityOverride = eventArgs.NewValue / 100d;
        UpdateOpacityDisplay();
        OpacityOverrideChanged?.Invoke(
            this,
            new OverlayPreviewOpacityChangedEventArgs(
                Definition.Name,
                opacityOverride));
    }

    private void OnUseGlobalScaleChanged(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (updatingScaleControls)
        {
            return;
        }

        scaleOverride = UseGlobalScaleCheckBox.IsChecked == true
            ? null
            : GetScaleOption(
                (int)Math.Round(ScaleOverrideSlider.Value)).Index;
        ScaleOverrideSlider.IsEnabled = scaleOverride is not null;
        ApplyConfiguredScale();
        UpdateScaleDisplay();
        ScaleOverrideChanged?.Invoke(
            this,
            new OverlayPreviewScaleChangedEventArgs(
                Definition.Name,
                scaleOverride));
    }

    private void OnScaleOverrideValueChanged(
        object? sender,
        RangeBaseValueChangedEventArgs eventArgs)
    {
        if (updatingScaleControls || scaleOverride is null)
        {
            return;
        }

        var option = GetScaleOption((int)Math.Round(eventArgs.NewValue));
        if (scaleOverride == option.Index)
        {
            return;
        }

        scaleOverride = option.Index;
        ApplyConfiguredScale();
        UpdateScaleDisplay();
        ScaleOverrideChanged?.Invoke(
            this,
            new OverlayPreviewScaleChangedEventArgs(
                Definition.Name,
                scaleOverride));
    }

    private void UpdateOpacityDisplay()
    {
        var effectiveOpacity = opacityOverride ?? globalOpacity;
        PreviewSurface.Opacity = effectiveOpacity;
        OpacityOverrideValueText.Text = $"{effectiveOpacity * 100d:N0}%";
    }

    private void UpdateScaleDisplay()
    {
        var option = OverlayScaleCatalog.Options.Single(candidate =>
            candidate.Index == (scaleOverride ?? globalScaleIndex));
        ScaleOverrideValueText.Text = option.AbsoluteScale is { } scale
            ? scale.ToString("0%")
            : "OS";
    }

    private static OverlayScaleOption GetScaleOption(int ordinal)
    {
        return IndividualScaleOptions[Math.Clamp(
            ordinal,
            0,
            IndividualScaleOptions.Length - 1)];
    }

    private static int GetScaleOptionOrdinal(int scaleIndex)
    {
        for (var index = 0; index < IndividualScaleOptions.Length; index++)
        {
            if (IndividualScaleOptions[index].Index == scaleIndex)
            {
                return index;
            }
        }

        var scale = OverlayScaleCatalog.Options
            .FirstOrDefault(option => option.Index == scaleIndex)
            ?.AbsoluteScale
            ?? 1d;
        return IndividualScaleOptions
            .Select((option, index) => new
            {
                index,
                distance = Math.Abs(option.AbsoluteScale!.Value - scale),
            })
            .MinBy(entry => entry.distance)!
            .index;
    }

    private static OverlayScaleOption GetIndividualFallback(int scaleIndex)
    {
        return GetScaleOption(GetScaleOptionOrdinal(scaleIndex));
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
