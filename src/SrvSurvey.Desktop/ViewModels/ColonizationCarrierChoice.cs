using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed record ColonizationCarrierChoice(long MarketId, string Label)
{
    internal static IReadOnlyList<ColonizationCarrierChoice> CreateOptions(
        IEnumerable<ColonizationFleetCarrier> carriers
    ) =>
        new[] { new ColonizationCarrierChoice(0, "ALL") }
            .Concat(
                carriers
                    .Where(carrier => carrier.MarketId > 0)
                    .DistinctBy(carrier => carrier.MarketId)
                    .OrderBy(carrier => carrier.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(carrier => carrier.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(carrier => new ColonizationCarrierChoice(
                        carrier.MarketId,
                        string.IsNullOrWhiteSpace(carrier.DisplayName) || carrier.DisplayName == carrier.Name
                            ? carrier.Name
                            : $"{carrier.DisplayName} ({carrier.Name})"
                    ))
            )
            .ToArray();
}
