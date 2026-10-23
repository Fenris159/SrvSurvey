using System.Text.Json;
using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed partial class ColonizationSettingsStore
{
    private readonly UiSettingsDocumentStore shoppingDocumentStore;

    /// <summary>Keeps market snapshots separate from frequently updated UI preferences.</summary>
    internal ColonizationShoppingSession? LoadShopping(string commander)
    {
        try
        {
            JsonObject root = shoppingDocumentStore.Load();
            if (root[VersionKey]?.GetValue<int>() != 1)
            {
                return null;
            }

            ColonizationShoppingSession? session = root[commander.ToUpperInvariant()]
                ?.Deserialize<ColonizationShoppingSession>();
            return
                session?.ProjectIds is not null
                && session.ProjectIds.All(id => !string.IsNullOrWhiteSpace(id))
                && (session.Search is null || session.Search.IsValid)
                ? session
                : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    internal void SaveShopping(string commander, ColonizationShoppingSession session)
    {
        shoppingDocumentStore.Update(root =>
        {
            root[VersionKey] = 1;
            root[commander.ToUpperInvariant()] = JsonSerializer.SerializeToNode(session);
        });
    }
}
