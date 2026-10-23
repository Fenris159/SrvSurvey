using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationCarrierManifestViewModel
{
    private readonly ColonizationCarrierRefreshCooldown refreshCooldown;

    public bool CanRefresh =>
        CanEdit && isCurrentContext() && carrier is { } current && refreshCooldown.Remaining(current) == TimeSpan.Zero;

    internal void UpdateRefreshAvailability() => Changed(nameof(CanRefresh));

    public Task RefreshAsync() =>
        CanRefresh
            ? RunAsync(async token =>
            {
                ColonizationFleetCarrier original = carrier!;
                if (!refreshCooldown.TryBegin(original))
                {
                    UpdateRefreshAvailability();
                    return;
                }
                UpdateRefreshAvailability();
                ColonizationFleetCarrier updated = await client.RefreshFleetCarrierAsync(
                    original.MarketId,
                    apiKey,
                    token
                );
                if (IsActive)
                {
                    Apply(updated);
                    saved(updated);
                }
            })
            : Task.CompletedTask;
}
