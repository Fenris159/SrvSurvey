using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SrvSurvey.Core.Guardian;

namespace SrvSurvey.Desktop.Controls;

public sealed class GuardianArtifactGlyphControl : Control
{
    public static readonly StyledProperty<string?> ArtifactCodeProperty =
        AvaloniaProperty.Register<GuardianArtifactGlyphControl, string?>(
            nameof(ArtifactCode));

    static GuardianArtifactGlyphControl()
    {
        AffectsRender<GuardianArtifactGlyphControl>(ArtifactCodeProperty);
    }

    public string? ArtifactCode
    {
        get => GetValue(ArtifactCodeProperty);
        set => SetValue(ArtifactCodeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!TryResolveType(ArtifactCode, out var type))
        {
            return;
        }

        var style = GuardianLegacyMapDrawing.GetPointStyle(
            type,
            GuardianPoiStatus.Present);
        var fill = new SolidColorBrush(style.Fill);
        var pen = new Pen(new SolidColorBrush(style.Stroke), 2);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        if (type == GuardianPoiType.Relic)
        {
            var geometry = new StreamGeometry();
            var points = GuardianLegacyMapDrawing.CreateGlyphPoints(
                type,
                center,
                0,
                0.65);
            using var geometryContext = geometry.Open();
            geometryContext.BeginFigure(points[0], isFilled: true);
            foreach (var point in points.Skip(1))
            {
                geometryContext.LineTo(point);
            }

            geometryContext.EndFigure(isClosed: true);
            context.DrawGeometry(fill, pen, geometry);
            return;
        }

        context.DrawEllipse(fill, pen, center, 5, 5);
    }

    private static bool TryResolveType(
        string? code,
        out GuardianPoiType type)
    {
        type = code?.ToLowerInvariant() switch
        {
            "ca" or "casket" => GuardianPoiType.Casket,
            "or" or "orb" => GuardianPoiType.Orb,
            "re" or "relic" => GuardianPoiType.Relic,
            "ta" or "tablet" => GuardianPoiType.Tablet,
            "to" or "totem" => GuardianPoiType.Totem,
            "ur" or "urn" => GuardianPoiType.Urn,
            _ => GuardianPoiType.Unknown,
        };
        return type != GuardianPoiType.Unknown;
    }
}
