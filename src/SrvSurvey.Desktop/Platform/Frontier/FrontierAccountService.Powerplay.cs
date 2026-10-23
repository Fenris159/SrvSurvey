using System.Globalization;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.Platform.Frontier;

public sealed partial class FrontierAccountService
{
    /// <summary>Recovers bounded daily Powerplay history while preserving cached status and renewed credentials on failure.</summary>
    private async Task<(FrontierAccountSnapshot Snapshot, FrontierAccountCredential Credential)> RefreshPowerplayAsync(
        FrontierAccountSnapshot snapshot,
        FrontierAccountSnapshot? previous,
        FrontierCommanderIdentity commander,
        FrontierAccountCredential credential,
        bool isLegacy,
        CancellationToken cancellationToken
    )
    {
        previous = IsSameCommander(snapshot, previous) ? previous : null;
        snapshot = snapshot with
        {
            Powerplay = previous?.Powerplay,
            PowerplayFetchedAt = previous?.PowerplayFetchedAt,
            PowerplayError = previous?.PowerplayError ?? string.Empty,
        };
        DateTimeOffset now = utcNow();
        if (snapshot.PowerplayFetchedAt is { } checkedAt && now - checkedAt < TimeSpan.FromMinutes(15))
        {
            return (snapshot, credential);
        }

        // A new connection can recover a recent session even when no local journal is available.
        // Recover a complete baseline before replaying daily deltas in chronological order.
        // Keep only the small status snapshot; never persist the remote travel journal.
        var journals = new List<IReadOnlyList<JournalEventEnvelope>>();
        for (int daysAgo = 0; daysAgo < 7; daysAgo++)
        {
            string date = now.AddDays(-daysAgo).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
            OptionalCapiResponse response = await TryRequestOptionalCapiAsync(
                    "https://companion.orerve.net/journal/" + date,
                    commander,
                    credential,
                    isLegacy,
                    cancellationToken
                )
                .ConfigureAwait(false);
            credential = response.Credential;
            if (!response.Succeeded)
            {
                return (snapshot with { PowerplayError = response.Error }, credential);
            }

            if (response.Content is { } content)
            {
                journals.Insert(0, FrontierPowerplayJournal.ReadPowerplayEvents(content, commander.FrontierId));
                if (FrontierPowerplayJournal.HasRecoveryBaseline(journals.SelectMany(day => day)))
                {
                    break;
                }
            }

            if (
                snapshot.Powerplay is { } cached
                && now.AddDays(-daysAgo).UtcDateTime.Date <= cached.UpdatedAt.UtcDateTime.Date
            )
            {
                break;
            }
        }

        FrontierPowerplaySnapshot? powerplay = snapshot.Powerplay;
        foreach (JournalEventEnvelope entry in journals.SelectMany(day => day))
        {
            powerplay = FrontierPowerplayJournal.Apply(powerplay, entry);
        }
        snapshot = snapshot with { Powerplay = powerplay };

        return (snapshot with { PowerplayFetchedAt = now, PowerplayError = string.Empty }, credential);
    }
}
