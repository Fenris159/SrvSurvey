using Avalonia;
using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Controls;

/// <summary>Draws proportional carrier shares against the full build requirement, leaving the unready portion unfilled.</summary>
public sealed class CarrierReadinessBar : Grid
{
    public static readonly StyledProperty<IReadOnlyList<PreviewCarrierReadiness>?> CarriersProperty =
        AvaloniaProperty.Register<CarrierReadinessBar, IReadOnlyList<PreviewCarrierReadiness>?>(nameof(Carriers));
    public static readonly StyledProperty<long> MaximumProperty = AvaloniaProperty.Register<CarrierReadinessBar, long>(
        nameof(Maximum)
    );

    /// <summary>Initializes an empty bar with an unfilled remainder column.</summary>
    public CarrierReadinessBar() => Rebuild();

    public IReadOnlyList<PreviewCarrierReadiness>? Carriers
    {
        get => GetValue(CarriersProperty);
        set => SetValue(CarriersProperty, value);
    }
    public long Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Rebuilds proportional columns when the carrier shares or full requirement change.</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CarriersProperty || change.Property == MaximumProperty)
        {
            Rebuild();
        }
    }

    /// <summary>Caps visible shares at the current requirement and leaves unused width unfilled.</summary>
    private void Rebuild()
    {
        Children.Clear();
        ColumnDefinitions.Clear();
        long remaining = Math.Max(0, Maximum);
        foreach (PreviewCarrierReadiness carrier in Carriers ?? [])
        {
            long amount = Math.Clamp(carrier.Ready ?? 0, 0, remaining);
            if (amount == 0)
            {
                continue;
            }
            var segment = new CarrierColorSwatch { MarketId = carrier.MarketId };
            ToolTip.SetTip(segment, carrier.LegendText);
            SetColumn(segment, ColumnDefinitions.Count);
            ColumnDefinitions.Add(new ColumnDefinition(new GridLength(amount, GridUnitType.Star)));
            Children.Add(segment);
            remaining -= amount;
        }
        ColumnDefinitions.Add(
            new ColumnDefinition(new GridLength(Children.Count == 0 ? 1 : remaining, GridUnitType.Star))
        );
    }
}
