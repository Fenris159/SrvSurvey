using Avalonia;
using Avalonia.Controls;

namespace SrvSurvey.Desktop.Views;

public sealed partial class AcquireTreeLink : UserControl
{
    public static readonly StyledProperty<string> KindProperty = AvaloniaProperty.Register<AcquireTreeLink, string>(
        nameof(Kind),
        "Single"
    );

    public static readonly StyledProperty<double> FirstAnchorYProperty = AvaloniaProperty.Register<
        AcquireTreeLink,
        double
    >(nameof(FirstAnchorY), double.PositiveInfinity);

    static AcquireTreeLink()
    {
        KindProperty.Changed.AddClassHandler<AcquireTreeLink>((link, _) => link.ApplyKind());
        FirstAnchorYProperty.Changed.AddClassHandler<AcquireTreeLink>((link, _) => link.ApplyKind());
    }

    public AcquireTreeLink()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ApplyKind();
    }

    public string Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double FirstAnchorY
    {
        get => GetValue(FirstAnchorYProperty);
        set => SetValue(FirstAnchorYProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyKind();
    }

    private void ApplyKind()
    {
        if (VerticalLine is null || HorizontalLine is null)
        {
            return;
        }

        VerticalLine.IsVisible = Kind is not "Single";
        bool first = Kind == "First";
        LinkGrid.RowDefinitions[0].Height = first
            ? new GridLength(Math.Min(Bounds.Height / 2, FirstAnchorY))
            : new GridLength(1, GridUnitType.Star);
        LinkGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
        bool branch = Kind is "Next" or "Last";
        Grid.SetColumn(HorizontalLine, branch ? 1 : 0);
        Grid.SetColumnSpan(HorizontalLine, branch ? 1 : 2);
        Grid.SetRowSpan(HorizontalLine, first ? 1 : 2);
        HorizontalLine.VerticalAlignment = first
            ? Avalonia.Layout.VerticalAlignment.Bottom
            : Avalonia.Layout.VerticalAlignment.Center;
        int verticalRow = first ? 1 : 0;
        Grid.SetRow(VerticalLine, verticalRow);
        Grid.SetRowSpan(VerticalLine, first || Kind == "Last" ? 1 : 2);
    }
}
