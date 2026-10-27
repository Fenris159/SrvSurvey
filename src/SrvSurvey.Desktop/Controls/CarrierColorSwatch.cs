using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.Controls;

/// <summary>Uses the same stable carrier hue in a bar or legend, adapting its brightness to the application theme.</summary>
public sealed class CarrierColorSwatch : Border
{
    public static readonly StyledProperty<long> MarketIdProperty = AvaloniaProperty.Register<CarrierColorSwatch, long>(
        nameof(MarketId)
    );

    public CarrierColorSwatch()
    {
        ActualThemeVariantChanged += (_, _) => UpdateColor();
        UpdateColor();
    }

    public long MarketId
    {
        get => GetValue(MarketIdProperty);
        set => SetValue(MarketIdProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarketIdProperty)
        {
            UpdateColor();
        }
    }

    private void UpdateColor() =>
        Background = new SolidColorBrush(
            CarrierColorPalette.GetColor(MarketId, ActualThemeVariant != ThemeVariant.Light)
        );
}
