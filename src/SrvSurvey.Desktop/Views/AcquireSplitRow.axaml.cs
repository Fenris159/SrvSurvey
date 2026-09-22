using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace SrvSurvey.Desktop.Views;

public sealed partial class AcquireSplitRow : UserControl
{
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

    public AcquireSplitRow() => InitializeComponent();

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
}
