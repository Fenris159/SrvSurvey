using Avalonia;
using Avalonia.Controls;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.Platform.Overlay;

public enum OverlayTypographyRole
{
    Header,
    Title,
    Value,
    Body,
    Detail,
    Caption,
}

public sealed record OverlayTypographyScale(int Header, int Title, int Value, int Body, int Detail, int Caption)
{
    public const int MinimumPercent = -50;
    public const int MaximumPercent = 100;
    public const int IncrementPercent = 5;

    public static OverlayTypographyScale Default { get; } = new(0, 0, 0, 0, 0, 0);

    public bool IsDefault => this == Default;

    public int GetPercent(OverlayTypographyRole role) =>
        role switch
        {
            OverlayTypographyRole.Header => Header,
            OverlayTypographyRole.Title => Title,
            OverlayTypographyRole.Value => Value,
            OverlayTypographyRole.Body => Body,
            OverlayTypographyRole.Detail => Detail,
            OverlayTypographyRole.Caption => Caption,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

    public OverlayTypographyScale WithPercent(OverlayTypographyRole role, double percent)
    {
        int normalized = Normalize(percent);
        return role switch
        {
            OverlayTypographyRole.Header => this with { Header = normalized },
            OverlayTypographyRole.Title => this with { Title = normalized },
            OverlayTypographyRole.Value => this with { Value = normalized },
            OverlayTypographyRole.Body => this with { Body = normalized },
            OverlayTypographyRole.Detail => this with { Detail = normalized },
            OverlayTypographyRole.Caption => this with { Caption = normalized },
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
    }

    public static int Normalize(double percent)
    {
        if (!double.IsFinite(percent))
        {
            return 0;
        }

        double clamped = Math.Clamp(percent, MinimumPercent, MaximumPercent);
        return (int)Math.Round(clamped / IncrementPercent, MidpointRounding.AwayFromZero) * IncrementPercent;
    }

    public static bool IsValid(int percent) =>
        percent is >= MinimumPercent and <= MaximumPercent && percent % IncrementPercent == 0;
}

internal static class OverlayTypographyResources
{
    private static readonly IReadOnlyDictionary<OverlayTypographyRole, (string Key, double DefaultSize)> Roles =
        new Dictionary<OverlayTypographyRole, (string, double)>
        {
            [OverlayTypographyRole.Header] = ("RavenOverlayHeaderFontSize", OverlayTypographySettings.Default.Header),
            [OverlayTypographyRole.Title] = ("RavenOverlayTitleFontSize", OverlayTypographySettings.Default.Title),
            [OverlayTypographyRole.Value] = ("RavenOverlayValueFontSize", OverlayTypographySettings.Default.Value),
            [OverlayTypographyRole.Body] = ("RavenOverlayBodyFontSize", OverlayTypographySettings.Default.Body),
            [OverlayTypographyRole.Detail] = ("RavenOverlayDetailFontSize", OverlayTypographySettings.Default.Detail),
            [OverlayTypographyRole.Caption] = (
                "RavenOverlayCaptionFontSize",
                OverlayTypographySettings.Default.Caption
            ),
        };

    public static void Apply(Control root, OverlayTypographyScale? scale)
    {
        ArgumentNullException.ThrowIfNull(root);
        OverlayTypographyScale effective = scale ?? OverlayTypographyScale.Default;
        foreach (KeyValuePair<OverlayTypographyRole, (string Key, double DefaultSize)> entry in Roles)
        {
            double baseline = GetBaseline(entry.Value.Key, entry.Value.DefaultSize);
            double factor = 1d + (effective.GetPercent(entry.Key) / 100d);
            root.Resources[entry.Value.Key] = baseline * factor;
        }

        root.InvalidateMeasure();
    }

    private static double GetBaseline(string key, double fallback)
    {
        Application? application = Application.Current;
        if (
            application is not null
            && application.Resources.TryGetResource(key, application.ActualThemeVariant, out object? value)
            && value is double size
            && double.IsFinite(size)
            && size > 0
        )
        {
            return size;
        }

        return fallback;
    }
}
