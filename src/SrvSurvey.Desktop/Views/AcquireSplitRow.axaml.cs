using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace SrvSurvey.Desktop.Views;

public sealed partial class AcquireSplitRow : UserControl
{
    private double lastLockedWidth;

    public static readonly StyledProperty<bool> LockColumnWidthsProperty = AvaloniaProperty.Register<
        AcquireSplitRow,
        bool
    >(nameof(LockColumnWidths));

    public static readonly StyledProperty<double> FirstAnchorYProperty = AvaloniaProperty.Register<
        AcquireSplitRow,
        double
    >(nameof(FirstAnchorY), double.PositiveInfinity);

    public static readonly StyledProperty<IEnumerable?> LinesProperty = AvaloniaProperty.Register<
        AcquireSplitRow,
        IEnumerable?
    >(nameof(Lines));

    public static readonly StyledProperty<IDataTemplate?> TargetTemplateProperty = AvaloniaProperty.Register<
        AcquireSplitRow,
        IDataTemplate?
    >(nameof(TargetTemplate));

    public static readonly StyledProperty<IDataTemplate?> LineTemplateProperty = AvaloniaProperty.Register<
        AcquireSplitRow,
        IDataTemplate?
    >(nameof(LineTemplate));

    public AcquireSplitRow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateColumnWidths();
        TargetBorder.SizeChanged += (_, _) =>
        {
            if (TargetBorder.Bounds.Height > 0)
            {
                FirstAnchorY = TargetBorder.Bounds.Height / 2;
            }
        };
    }

    public double FirstAnchorY
    {
        get => GetValue(FirstAnchorYProperty);
        private set => SetValue(FirstAnchorYProperty, value);
    }

    public bool LockColumnWidths
    {
        get => GetValue(LockColumnWidthsProperty);
        set
        {
            SetValue(LockColumnWidthsProperty, value);
            UpdateColumnWidths();
        }
    }

    public IEnumerable? Lines
    {
        get => GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    public IDataTemplate? TargetTemplate
    {
        get => GetValue(TargetTemplateProperty);
        set => SetValue(TargetTemplateProperty, value);
    }

    public IDataTemplate? LineTemplate
    {
        get => GetValue(LineTemplateProperty);
        set => SetValue(LineTemplateProperty, value);
    }

    private void UpdateColumnWidths()
    {
        if (
            !LockColumnWidths
            || Bounds.Width <= 0
            || SplitGrid is null
            || Math.Abs(Bounds.Width - lastLockedWidth) < 0.1
        )
        {
            return;
        }

        double width = Bounds.Width;
        lastLockedWidth = width;
        SplitGrid.ColumnDefinitions[0].Width = new GridLength(width * 0.42);
        SplitGrid.ColumnDefinitions[1].Width = new GridLength(width * 0.04);
        SplitGrid.ColumnDefinitions[2].Width = new GridLength(width * 0.54);
    }
}
