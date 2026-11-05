using System.Text.Json;
using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed partial class ColonizationSettingsStore
{
    private readonly UiSettingsDocumentStore shoppingDocumentStore;
    private const string CarrierSelectionsKey = "carrierSelections";

    /// <summary>Keeps market snapshots separate from frequently updated UI preferences.</summary>
    internal ColonizationShoppingSession? LoadShopping(string commander)
    {
        return ReadShopping(shoppingDocumentStore.Load(), commander.ToUpperInvariant());
    }

    /// <summary>Loads carrier preferences even after the last project tag has removed the saved search.</summary>
    internal ColonizationCarrierSelectionState? LoadShoppingCarrierSelection(string commander)
    {
        return ReadCarrierSelection(shoppingDocumentStore.Load(), commander.ToUpperInvariant());
    }

    /// <summary>Rejects malformed or unsupported search snapshots without preventing a fresh workspace.</summary>
    private static ColonizationShoppingSession? ReadShopping(JsonObject root, string commanderKey)
    {
        try
        {
            if (root[VersionKey]?.GetValue<int>() != 1)
            {
                return null;
            }

            ColonizationShoppingSession? session = root[commanderKey]?.Deserialize<ColonizationShoppingSession>();
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

    /// <summary>Atomically retains the newest carrier preference before replacing or clearing its search snapshot.</summary>
    internal void SaveShopping(string commander, ColonizationShoppingSession session)
    {
        shoppingDocumentStore.Update(root =>
        {
            string key = commander.ToUpperInvariant();
            ColonizationCarrierSelectionState? selection = ReadCarrierSelection(root, key);
            if (
                session.Search is { IsValid: true } search
                && search.CarrierSelectionRevision >= (selection?.Revision ?? 0)
            )
            {
                selection = new(
                    search.SelectedCarrierMarketId,
                    search.SelectedCarrierMarketIds,
                    search.CarrierSelectionRevision
                );
            }
            if (selection is not null)
            {
                JsonObject selections = root[CarrierSelectionsKey] as JsonObject ?? [];
                selections[key] = JsonSerializer.SerializeToNode(selection);
                root[CarrierSelectionsKey] = selections;
            }
            root[VersionKey] = 1;
            root[key] = JsonSerializer.SerializeToNode(session);
        });
    }

    /// <summary>Falls back to legacy search snapshots and keeps whichever valid preference has the newest revision.</summary>
    private static ColonizationCarrierSelectionState? ReadCarrierSelection(JsonObject root, string commanderKey)
    {
        ColonizationCarrierSelectionState? selection = ReadSavedCarrierSelection(root, commanderKey);
        if (
            ReadShopping(root, commanderKey)?.Search is { } search
            && search.CarrierSelectionRevision > (selection?.Revision ?? -1)
        )
        {
            selection = new(
                search.SelectedCarrierMarketId,
                search.SelectedCarrierMarketIds,
                search.CarrierSelectionRevision
            );
        }
        return selection;
    }

    /// <summary>Discards malformed carrier preferences so a later valid save can repair the sidecar.</summary>
    private static ColonizationCarrierSelectionState? ReadSavedCarrierSelection(JsonObject root, string commanderKey)
    {
        try
        {
            ColonizationCarrierSelectionState? selection = (root[CarrierSelectionsKey] as JsonObject)?[
                commanderKey
            ]?.Deserialize<ColonizationCarrierSelectionState>();
            return selection?.IsValid == true ? selection : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
