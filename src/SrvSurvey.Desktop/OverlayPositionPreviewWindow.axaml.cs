using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class OverlayPositionPreviewWindow : Window
{
    private bool updatingOpacityControls;
    private double globalOpacity = 1d;
    private double? opacityOverride;

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

    public PixelSize GetExpectedPixelSize(double scaling) =>
        Preview.GetEstimatedPixelSize(scaling);

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

    private void UpdateOpacityDisplay()
    {
        var effectiveOpacity = opacityOverride ?? globalOpacity;
        PreviewSurface.Opacity = effectiveOpacity;
        OpacityOverrideValueText.Text = $"{effectiveOpacity * 100d:N0}%";
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
