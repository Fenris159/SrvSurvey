using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop;

public sealed partial class StreamOverlayWindow : Window
{
    private IReadOnlyList<RenderTargetBitmap> frames = [];

    public StreamOverlayWindow()
    {
        InitializeComponent();
    }

    public void ReplaceFrames(
        IReadOnlyList<StreamOverlayRenderedFrame> renderedFrames)
    {
        ArgumentNullException.ThrowIfNull(renderedFrames);
        var previous = frames;
        frames = renderedFrames.Select(frame => frame.Bitmap).ToArray();
        OverlayCanvas.Children.Clear();
        foreach (var renderedFrame in renderedFrames)
        {
            var image = new Image
            {
                Source = renderedFrame.Bitmap,
                Width = renderedFrame.Projection.Width,
                Height = renderedFrame.Projection.Height,
                Opacity = renderedFrame.Opacity,
            };
            Canvas.SetLeft(image, renderedFrame.Projection.Left);
            Canvas.SetTop(image, renderedFrame.Projection.Top);
            OverlayCanvas.Children.Add(image);
        }

        foreach (var bitmap in previous)
        {
            bitmap.Dispose();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        ReplaceFrames([]);
        base.OnClosed(e);
    }
}

public sealed record StreamOverlayRenderedFrame(
    RenderTargetBitmap Bitmap,
    StreamOverlayFrame Projection,
    double Opacity = 1d);
