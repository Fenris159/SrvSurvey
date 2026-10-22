using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.Controls;

/// <summary>Shows delivered, ready, and remaining cargo; nonzero segments retain a visible minimum width.</summary>
public sealed class ColonizationCargoProgressBar : Grid
{
    public static readonly StyledProperty<ColonizationNexusCargo?> CargoProperty = AvaloniaProperty.Register<
        ColonizationCargoProgressBar,
        ColonizationNexusCargo?
    >(nameof(Cargo));
    public static readonly StyledProperty<IBrush?> DeliveredBrushProperty = AvaloniaProperty.Register<
        ColonizationCargoProgressBar,
        IBrush?
    >(nameof(DeliveredBrush));
    public static readonly StyledProperty<IBrush?> ReadyBrushProperty = AvaloniaProperty.Register<
        ColonizationCargoProgressBar,
        IBrush?
    >(nameof(ReadyBrush));

    public ColonizationCargoProgressBar()
    {
        ColumnDefinitions = new ColumnDefinitions("0,0,*");
        var delivered = new Border { [!Border.BackgroundProperty] = this[!DeliveredBrushProperty] };
        var ready = new Border { [!Border.BackgroundProperty] = this[!ReadyBrushProperty] };
        SetColumn(ready, 1);
        Children.Add(delivered);
        Children.Add(ready);
    }

    public ColonizationNexusCargo? Cargo
    {
        get => GetValue(CargoProperty);
        set => SetValue(CargoProperty, value);
    }
    public IBrush? DeliveredBrush
    {
        get => GetValue(DeliveredBrushProperty);
        set => SetValue(DeliveredBrushProperty, value);
    }
    public IBrush? ReadyBrush
    {
        get => GetValue(ReadyBrushProperty);
        set => SetValue(ReadyBrushProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != CargoProperty)
        {
            return;
        }
        double total = Math.Max(0, Cargo?.Total ?? 0);
        double delivered = Math.Clamp(Cargo?.Delivered ?? 0, 0, total);
        double ready = Math.Clamp(Cargo?.Ready ?? 0, 0, total - delivered);
        double[] amounts = [delivered, ready, Math.Max(0, total - delivered - ready)];
        for (int index = 0; index < amounts.Length; index++)
        {
            ColumnDefinition column = ColumnDefinitions[index];
            column.MinWidth = amounts[index] > 0 ? 3 : 0;
            column.Width = new GridLength(amounts[index], GridUnitType.Star);
        }
        if (total == 0)
        {
            ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
        }
    }
}
