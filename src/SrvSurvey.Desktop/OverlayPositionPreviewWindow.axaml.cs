using Avalonia;
using Avalonia.Controls;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class OverlayPositionPreviewWindow : Window
{
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
}
