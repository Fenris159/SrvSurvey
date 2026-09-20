using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;

namespace SrvSurvey.Desktop.Platform.Overlay;

public enum OverlayTypographyRole
{
    Header,
    Title,
    Value,
    Body,
    Detail,
    Caption,
    Icons,
}

public sealed record OverlayTypographyScale(
    int Header,
    int Title,
    int Value,
    int Body,
    int Detail,
    int Caption,
    int Icons = 0
)
{
    public const int MinimumPercent = -50;
    public const int MaximumPercent = 100;
    public const int IncrementPercent = 5;

    public static OverlayTypographyScale Default { get; } = new(0, 0, 0, 0, 0, 0, 0);

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
            OverlayTypographyRole.Icons => Icons,
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
            OverlayTypographyRole.Icons => this with { Icons = normalized },
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
    private static readonly AttachedProperty<OverlayTypographyScale> ScaleProperty = AvaloniaProperty.RegisterAttached<
        Control,
        Control,
        OverlayTypographyScale
    >("Scale", defaultValue: OverlayTypographyScale.Default, inherits: true);

    private static readonly AttachedProperty<double> BaselineFontSizeProperty = AvaloniaProperty.RegisterAttached<
        Control,
        TextBlock,
        double
    >("BaselineFontSize", defaultValue: double.NaN);

    static OverlayTypographyResources()
    {
        ScaleProperty.Changed.AddClassHandler<TextBlock>(static (text, _) => ApplyToText(text));
        ScaleProperty.Changed.AddClassHandler<LayoutTransformControl>(static (control, _) => ApplyToIcon(control));
    }

    public static void Apply(Control root, OverlayTypographyScale? scale)
    {
        ArgumentNullException.ThrowIfNull(root);
        OverlayTypographyScale effective = scale ?? OverlayTypographyScale.Default;
        root.SetValue(ScaleProperty, effective);
        if (root is TextBlock rootText)
        {
            ApplyToText(rootText);
        }
        else if (root is LayoutTransformControl rootIcon)
        {
            ApplyToIcon(rootIcon);
        }

        foreach (Control control in root.GetLogicalDescendants().OfType<Control>())
        {
            if (control is TextBlock text)
            {
                ApplyToText(text);
            }
            else if (control is LayoutTransformControl icon)
            {
                ApplyToIcon(icon);
            }
        }

        root.InvalidateMeasure();
    }

    private static void ApplyToText(TextBlock text)
    {
        OverlayTypographyRole? role = GetRole(text.Classes);
        if (role is null)
        {
            return;
        }

        double baseline = text.GetValue(BaselineFontSizeProperty);
        if (!double.IsFinite(baseline) || baseline <= 0)
        {
            baseline = text.FontSize;
            text.SetValue(BaselineFontSizeProperty, baseline);
        }

        OverlayTypographyScale scale = text.GetValue(ScaleProperty);
        double factor = 1d + (scale.GetPercent(role.Value) / 100d);
        text.SetCurrentValue(TextBlock.FontSizeProperty, baseline * factor);
    }

    private static void ApplyToIcon(LayoutTransformControl control)
    {
        if (!control.Classes.Contains("type-icon"))
        {
            return;
        }

        OverlayTypographyScale scale = control.GetValue(ScaleProperty);
        double factor = 1d + (scale.Icons / 100d);
        control.SetCurrentValue(
            LayoutTransformControl.LayoutTransformProperty,
            scale.Icons == 0 ? null : new ScaleTransform(factor, factor)
        );
    }

    private static OverlayTypographyRole? GetRole(IReadOnlyList<string> classes)
    {
        if (classes.Contains("type-header"))
        {
            return OverlayTypographyRole.Header;
        }

        if (classes.Contains("type-title"))
        {
            return OverlayTypographyRole.Title;
        }

        if (classes.Contains("type-value"))
        {
            return OverlayTypographyRole.Value;
        }

        if (classes.Contains("type-body"))
        {
            return OverlayTypographyRole.Body;
        }

        if (classes.Contains("type-detail"))
        {
            return OverlayTypographyRole.Detail;
        }

        if (classes.Contains("type-caption"))
        {
            return OverlayTypographyRole.Caption;
        }

        return classes.Contains("type-icon") ? OverlayTypographyRole.Icons : null;
    }
}
