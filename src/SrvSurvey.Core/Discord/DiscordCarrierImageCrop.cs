using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Discord;

public readonly record struct DiscordCarrierCropRectangle(double X, double Y, double Width, double Height);

/// <summary>A fixed 16:9 frame described independently of the source image's resolution.</summary>
public sealed record DiscordCarrierImageCrop(double Zoom = 1, double CenterX = 0.5, double CenterY = 0.5)
{
    public const int OutputWidth = 1280;
    public const int OutputHeight = 720;
    public const double AspectRatio = (double)OutputWidth / OutputHeight;
    public const double MaximumZoom = 8;

    [JsonIgnore]
    public double EffectiveZoom => double.IsFinite(Zoom) ? Math.Clamp(Zoom, 1, MaximumZoom) : 1;

    public DiscordCarrierCropRectangle GetRectangle(int sourceWidth, int sourceHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        double width = Math.Min(sourceWidth, sourceHeight * AspectRatio) / EffectiveZoom;
        double height = width / AspectRatio;
        double x = Math.Clamp(
            double.IsFinite(CenterX) ? CenterX : 0.5,
            width / (2 * sourceWidth),
            1 - width / (2 * sourceWidth)
        );
        double y = Math.Clamp(
            double.IsFinite(CenterY) ? CenterY : 0.5,
            height / (2 * sourceHeight),
            1 - height / (2 * sourceHeight)
        );
        return new(x * sourceWidth - width / 2, y * sourceHeight - height / 2, width, height);
    }

    public DiscordCarrierImageCrop Constrain(int sourceWidth, int sourceHeight)
    {
        DiscordCarrierCropRectangle area = GetRectangle(sourceWidth, sourceHeight);
        return new(EffectiveZoom, (area.X + area.Width / 2) / sourceWidth, (area.Y + area.Height / 2) / sourceHeight);
    }
}
