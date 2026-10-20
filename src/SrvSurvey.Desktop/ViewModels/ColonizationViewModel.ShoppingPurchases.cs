using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationViewModel
{
    private const int MaximumRememberedShoppingPurchases = 512;

    /// <summary>Applies live purchases once, independently of the purchase market and of open windows.</summary>
    private void ApplyShoppingPurchases(IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        if (!IsEnabled || CommanderName is null || shoppingSession?.Search is not { } saved)
        {
            return;
        }
        ColonizationShoppingState state = shoppingWorkspace?.CaptureState() ?? saved;
        var applied = state.AppliedPurchaseIds.ToHashSet(StringComparer.Ordinal);
        var identities = state.AppliedPurchaseIds.ToList();
        IReadOnlyList<ColonizationWishListItem> allocations = state.WishList;
        foreach (JournalEventEnvelope journalEvent in journalEvents)
        {
            JsonElement root = journalEvent.Payload;
            if (
                journalEvent.EventName != "MarketBuy"
                || GetJournalString(root, "Type") is not { } commodity
                || !root.TryGetProperty("Count", out JsonElement count)
                || count.ValueKind != JsonValueKind.Number
                || !count.TryGetInt64(out long quantity)
                || quantity <= 0
            )
            {
                continue;
            }
            string identity = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(journalEvent.SourceRecordIdentity ?? journalEvent.RawJson))
            );
            if (!applied.Add(identity))
            {
                continue;
            }
            identities.Add(identity);
            allocations = ColonizationShoppingPlan.ConsumePurchase(allocations, commodity, quantity);
        }
        if (identities.Count == state.AppliedPurchaseIds.Count)
        {
            return;
        }
        state = state with
        {
            WishList = allocations,
            AppliedPurchaseIds = identities.TakeLast(MaximumRememberedShoppingPurchases).ToArray(),
        };
        shoppingSession = shoppingSession with { Search = state };
        if (shoppingWorkspace is not null)
        {
            shoppingWorkspace.ApplyPurchasedWishList(state);
        }
        else
        {
            ReconcileWishList();
            SaveShoppingSession("The ShoppingList purchases could not be saved to disk.");
        }
    }
}
