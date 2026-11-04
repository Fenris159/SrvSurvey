using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SrvSurvey.Desktop.Controls;

/// <summary>Calibration spinner with wheel input and temporary keyboard precision modifiers.</summary>
public sealed class VrCalibrationNumberInput : NumericUpDown
{
    public static readonly StyledProperty<decimal> StepProperty = AvaloniaProperty.Register<
        VrCalibrationNumberInput,
        decimal
    >(nameof(Step), 1m);

    public VrCalibrationNumberInput()
    {
        AddHandler(PointerPressedEvent, (_, args) => UpdateIncrement(args.KeyModifiers), RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, (_, args) => UpdateIncrement(args.KeyModifiers), RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
    }

    protected override Type StyleKeyOverride => typeof(NumericUpDown);

    public decimal Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    private void UpdateIncrement(KeyModifiers modifiers)
    {
        decimal multiplier = modifiers.HasFlag(KeyModifiers.Control) ? 5m : 1m;
        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            multiplier = 0.1m;
        }
        Increment = Step * multiplier;
    }

    private void OnWheel(object? sender, PointerWheelEventArgs args)
    {
        if (args.Delta.Y == 0 || !IsEnabled || !AllowSpin)
        {
            return;
        }
        UpdateIncrement(args.KeyModifiers);
        Value = Math.Clamp((Value ?? 0) + Math.Sign(args.Delta.Y) * Increment, Minimum, Maximum);
        args.Handled = true;
    }
}
