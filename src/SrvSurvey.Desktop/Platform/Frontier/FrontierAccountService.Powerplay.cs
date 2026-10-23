using System.Globalization;
using SrvSurvey.Core.Frontier;

namespace SrvSurvey.Desktop.Platform.Frontier;

public sealed partial class FrontierAccountService
{
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
        // Once found, keep the small status snapshot; never cache the remote travel journal.
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

            FrontierPowerplaySnapshot? powerplay = response.Content is { } content
                ? FrontierPowerplayJournal.Parse(content, commander.FrontierId, snapshot.Powerplay)
                : null;
            if (powerplay is not null)
            {
                if (snapshot.Powerplay is null || powerplay.UpdatedAt >= snapshot.Powerplay.UpdatedAt)
                {
                    snapshot = snapshot with { Powerplay = powerplay };
                }

                break;
            }
        }

        return (snapshot with { PowerplayFetchedAt = now, PowerplayError = string.Empty }, credential);
    }
}
