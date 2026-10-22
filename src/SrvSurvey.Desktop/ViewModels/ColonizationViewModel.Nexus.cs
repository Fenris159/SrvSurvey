namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationViewModel
{
    internal ColonizationProjectPreviewViewModel CreateNexusReport(string id, long systemAddress) =>
        Nexus.CreateReport(id, systemAddress, () => recovery.ShipCargoCapacity);

    private void UpdateNexusContext(bool activate = false)
    {
        Nexus.SetContext(IsEnabled, CommanderName, recovery.ApiKey);
        if (activate && SelectedWorkspaceTabIndex == 3)
        {
            Nexus.Activate();
        }
    }
}
