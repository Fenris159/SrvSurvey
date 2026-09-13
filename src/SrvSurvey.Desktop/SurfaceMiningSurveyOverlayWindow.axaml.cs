using Avalonia;
using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop;

public sealed partial class SurfaceMiningSurveyOverlayWindow : Window
{
    private double previousPixelWidth;

    public SurfaceMiningSurveyOverlayWindow()
        : this(null) { }

    public SurfaceMiningSurveyOverlayWindow(MineMapViewModel? viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        LayoutUpdated += OnLayoutUpdated;
    }

    private void OnLayoutUpdated(object? sender, EventArgs eventArgs)
    {
        var scaling = double.IsFinite(RenderScaling) && RenderScaling > 0 ? RenderScaling : 1d;
        var currentPixelWidth = Bounds.Width * scaling;
        if (!(currentPixelWidth > 0))
        {
            return;
        }

        if (previousPixelWidth > 0 && Math.Abs(currentPixelWidth - previousPixelWidth) >= 1)
        {
            Position = new PixelPoint(
                Position.X + (int)Math.Round((previousPixelWidth - currentPixelWidth) / 2d),
                Position.Y
            );
        }

        previousPixelWidth = currentPixelWidth;
    }
}
