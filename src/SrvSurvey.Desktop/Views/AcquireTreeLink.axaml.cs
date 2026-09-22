using Avalonia;
using Avalonia.Controls;

namespace SrvSurvey.Desktop.Views;

public sealed partial class AcquireTreeLink : UserControl
{
    public static readonly StyledProperty<string> KindProperty = AvaloniaProperty.Register<AcquireTreeLink, string>(
        nameof(Kind),
        "Single"
    );

    static AcquireTreeLink()
    {
        KindProperty.Changed.AddClassHandler<AcquireTreeLink>((link, _) => link.ApplyKind());
    }

    public AcquireTreeLink()
    {
        InitializeComponent();
    }

    public string Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
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
        bool branch = Kind is "Next" or "Last";
        Grid.SetColumn(HorizontalLine, branch ? 1 : 0);
        Grid.SetColumnSpan(HorizontalLine, branch ? 1 : 2);
        int verticalRow = Kind == "First" ? 1 : 0;
        Grid.SetRow(VerticalLine, verticalRow);
        Grid.SetRowSpan(VerticalLine, Kind is "First" or "Last" ? 1 : 2);
    }
}
