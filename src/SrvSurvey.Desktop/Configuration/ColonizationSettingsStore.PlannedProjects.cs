using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.Configuration;

public sealed partial class ColonizationSettingsStore
{
    private readonly UiSettingsDocumentStore planningDocumentStore;

    internal IReadOnlyList<ColonizationSystemRecord> LoadPlannedSystems(string commander)
    {
        try
        {
            return planningDocumentStore
                    .Load()[commander.ToUpperInvariant()]
                    ?.Deserialize<ColonizationSystemRecord[]>()
                    ?.OfType<ColonizationSystemRecord>()
                    .Where(system => !string.IsNullOrWhiteSpace(system.Name) && system.Sites is not null)
                    .ToArray()
                ?? [];
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return [];
        }
    }

    internal void SavePlannedSystems(string commander, IEnumerable<ColonizationSystemRecord> systems)
    {
        planningDocumentStore.Update(root =>
            root[commander.ToUpperInvariant()] = JsonSerializer.SerializeToNode(systems.ToArray())
        );
    }

    internal IReadOnlyList<string> LoadHiddenPlannedProjects(string commander)
    {
        JsonObject root = documentStore.Load();
        return
            root[ColonizationSectionKey] is JsonObject section
            && section["HiddenPlannedProjects"] is JsonObject choices
            && choices[commander.ToUpperInvariant()] is JsonArray ids
            ? ids.OfType<JsonValue>()
                .Select(value => value.TryGetValue<string>(out string? id) ? id : null)
                .OfType<string>()
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray()
            : [];
    }

    internal void SaveHiddenPlannedProjects(string commander, IEnumerable<string> ids)
    {
        documentStore.Update(root =>
        {
            if (root[ColonizationSectionKey] is not JsonObject section)
            {
                section = [];
                root[ColonizationSectionKey] = section;
            }
            if (section["HiddenPlannedProjects"] is not JsonObject choices)
            {
                choices = [];
                section["HiddenPlannedProjects"] = choices;
            }
            choices[commander.ToUpperInvariant()] = new JsonArray(
                ids.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()
            );
            root[VersionKey] = 1;
        });
    }
}
