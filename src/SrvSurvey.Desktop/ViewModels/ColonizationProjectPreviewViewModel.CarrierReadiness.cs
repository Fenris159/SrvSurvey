using System.Globalization;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationProjectPreviewViewModel
{
    public IReadOnlyList<PreviewCarrierReadiness> ReadyCarriers { get; private set; } = [];

    private void ApplyCarrierReadiness(ColonizationProjectPreview snapshot) =>
        ReadyCarriers = snapshot
            .GetCarrierReadiness()
            .Select(item => new PreviewCarrierReadiness(item.Carrier.MarketId, CarrierLabel(item.Carrier), item.Ready))
            .ToArray();

    private static string CarrierLabel(ColonizationPreviewCarrier carrier)
    {
        if (string.IsNullOrWhiteSpace(carrier.Name))
        {
            return string.IsNullOrWhiteSpace(carrier.DisplayName)
                ? carrier.MarketId.ToString(CultureInfo.InvariantCulture)
                : carrier.DisplayName;
        }
        return string.IsNullOrWhiteSpace(carrier.DisplayName) || carrier.Name == carrier.DisplayName
            ? carrier.Name
            : carrier.Label;
    }
}

/// <summary>Names and quantities accompany every color so carrier readiness is understandable without relying on hue alone.</summary>
public sealed record PreviewCarrierReadiness(long MarketId, string Label, long? Ready)
{
    public string QuantityText =>
        Ready is { } quantity ? $"{quantity.ToString("N0", CultureInfo.CurrentCulture)} t" : "Unknown";
    public string LegendText => $"{Label} · {QuantityText}";
}
