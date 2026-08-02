using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Controls;

public sealed partial class RouteBioTargetRow : UserControl
{
    public static readonly StyledProperty<bool> IsInteractiveProperty =
        AvaloniaProperty.Register<RouteBioTargetRow, bool>(
            nameof(IsInteractive),
            defaultValue: false);

    static RouteBioTargetRow()
    {
        IsInteractiveProperty.Changed.AddClassHandler<RouteBioTargetRow>(
            static (control, eventArgs) =>
            {
                control.CompletionCheckBox.IsHitTestVisible =
                    eventArgs.NewValue is true;
            });
    }

    public RouteBioTargetRow()
    {
        InitializeComponent();
        CompletionCheckBox.IsHitTestVisible = IsInteractive;
    }

    public event EventHandler<RouteBioCompletionRequestedEventArgs>?
        CompletionRequested;

    public bool IsInteractive
    {
        get => GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    private void CompletionCheckBox_Click(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (!IsInteractive
            || DataContext is not RouteBioTargetItemViewModel target
            || sender is not CheckBox checkBox)
        {
            return;
        }

        CompletionRequested?.Invoke(
            this,
            new RouteBioCompletionRequestedEventArgs(
                target,
                checkBox.IsChecked == true));
    }
}

public sealed record RouteBioCompletionRequestedEventArgs(
    RouteBioTargetItemViewModel Target,
    bool IsCompleted);
