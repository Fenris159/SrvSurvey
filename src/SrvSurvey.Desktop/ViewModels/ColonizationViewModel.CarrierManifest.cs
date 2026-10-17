using System.Windows.Input;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationViewModel
{
    private Action? carrierManifestOpener;
    private ColonizationCarrierRefreshCooldown? carrierManifestRefreshCooldown;
    private readonly WorkspaceCommand carrierManifestCommand;
    public ICommand EditCarrierManifestCommand => carrierManifestCommand;

    internal void SetCarrierManifestOpener(Action? opener) => carrierManifestOpener = opener;

    internal ColonizationCarrierManifestViewModel CreateCarrierManifest()
    {
        if (
            client is not IRavenColonialCarrierManifestClient manifestClient
            || CommanderName is null
            || recovery.ApiKey is null
        )
        {
            throw new InvalidOperationException(
                "Load a commander and save a Raven API key before editing carrier manifests."
            );
        }
        string commander = CommanderName;
        string key = recovery.ApiKey;
        int profileVersion = recovery.ProfileVersion;
        return new ColonizationCarrierManifestViewModel(
            manifestClient,
            commander,
            key,
            () =>
                !disposed
                && IsEnabled
                && CommanderName == commander
                && recovery.ApiKey == key
                && recovery.ProfileVersion == profileVersion,
            updated => recovery.InstallFleetCarrierManifest(updated),
            localCargo: recovery.LocalCarrierCargo,
            refreshCooldown: carrierManifestRefreshCooldown ??= new ColonizationCarrierRefreshCooldown(
                settingsStore.LoadCarrierRefreshAttempt,
                settingsStore.SaveCarrierRefreshAttempt
            )
        )
        {
            PreferredCarrierMarketId = planningCarrierMarketId,
        };
    }
}
