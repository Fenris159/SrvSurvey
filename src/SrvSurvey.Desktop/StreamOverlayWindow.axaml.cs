using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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

    public void ReplaceFrames(IReadOnlyList<StreamOverlayRenderedFrame> renderedFrames)
    {
        ArgumentNullException.ThrowIfNull(renderedFrames);
        IReadOnlyList<RenderTargetBitmap> previous = frames;
        frames = renderedFrames.Select(frame => frame.Bitmap).ToArray();
        OverlayCanvas.Children.Clear();
        foreach (StreamOverlayRenderedFrame renderedFrame in renderedFrames)
        {
            var image = new Image
            {
                Source = new PixelImage(renderedFrame.Bitmap),
                Width = renderedFrame.Projection.Width,
                Height = renderedFrame.Projection.Height,
                Opacity = renderedFrame.Opacity,
            };
            Canvas.SetLeft(image, renderedFrame.Projection.Left);
            Canvas.SetTop(image, renderedFrame.Projection.Top);
            OverlayCanvas.Children.Add(image);
        }

        foreach (RenderTargetBitmap bitmap in previous)
        {
            bitmap.Dispose();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        ReplaceFrames([]);
        base.OnClosed(e);
    }

    /// <summary>Samples the complete raster in pixels; its capture DPI has already been applied while rendering it.</summary>
    private sealed class PixelImage(RenderTargetBitmap bitmap) : IImage
    {
        public Size Size => new(bitmap.PixelSize.Width, bitmap.PixelSize.Height);

        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect) =>
            context.DrawImage(bitmap, sourceRect, destRect);
    }
}

public sealed record StreamOverlayRenderedFrame(
    RenderTargetBitmap Bitmap,
    StreamOverlayFrame Projection,
    double Opacity = 1d
);
