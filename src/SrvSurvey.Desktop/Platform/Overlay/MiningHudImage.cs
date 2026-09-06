using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Normalizes capture resolution while retaining HUD colors.</summary>
internal sealed class MiningHudImage : IFssPixelSource
{
    private readonly FssRgbPixel[] colors;
    public int Width { get; }
    public int Height { get; }
    public double Radius { get; }
    internal MiningHudImage(IFssPixelSource source, double circleWidth)
    {
        Width = MiningDetectionSettings.GetWorkingWidth(circleWidth);
        Height = Math.Max(1, (int)Math.Round(source.Height * Width / (double)source.Width));
        Radius = Width * circleWidth / 2;
        colors = new FssRgbPixel[Width * Height];
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var p = source.GetPixel(Math.Min(source.Width - 1, (int)(x * source.Width / (double)Width)),
                    Math.Min(source.Height - 1, (int)(y * source.Height / (double)Height)));
                colors[y * Width + x] = p;
            }
    }
    public FssRgbPixel GetPixel(int x, int y)
    {
        return colors[y * Width + x];
    }
}
