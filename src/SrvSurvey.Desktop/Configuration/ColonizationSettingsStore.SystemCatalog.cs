using System.Text.Json;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.Configuration;

internal sealed record ColonizationSystemCatalog(
    DateTimeOffset LastAttemptUtc,
    DateTimeOffset? RefreshedAtUtc,
    IReadOnlyList<ColonizationArchitectSystem> Systems
);

public sealed partial class ColonizationSettingsStore
{
    private readonly UiSettingsDocumentStore systemCatalogDocumentStore;

    internal ColonizationSystemCatalog? LoadSystemCatalog(string commander)
    {
        try
        {
            ColonizationSystemCatalog? catalog = systemCatalogDocumentStore
                .Load()[commander.ToUpperInvariant()]
                ?.Deserialize<ColonizationSystemCatalog>();
            return catalog?.Systems is null
                ? null
                : catalog with
                {
                    Systems = catalog
                        .Systems.OfType<ColonizationArchitectSystem>()
                        .Where(system => !string.IsNullOrWhiteSpace(system.Name) && system.SystemAddress >= 0)
                        .DistinctBy(system => system.Name, StringComparer.OrdinalIgnoreCase)
                        .OrderBy(system => system.Name, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    internal void SaveSystemCatalog(string commander, ColonizationSystemCatalog catalog) =>
        systemCatalogDocumentStore.Update(root =>
            root[commander.ToUpperInvariant()] = JsonSerializer.SerializeToNode(catalog)
        );
}
