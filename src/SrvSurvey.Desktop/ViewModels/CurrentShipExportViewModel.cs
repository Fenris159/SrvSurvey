using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Ships;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Offers the journal commander's currently piloted ship as an EDSY import.</summary>
public sealed class CurrentShipExportViewModel : WorkspaceObservable
{
    private ShipLoadout? loadout;
    private bool canExport;
    private string slefJson = "";

    public string SlefJson => slefJson;
    public bool CanExport => canExport;
    public string ShipDescription
    {
        get
        {
            if (loadout is null)
            {
                return "Waiting for a complete ship loadout.";
            }
            string name = string.IsNullOrWhiteSpace(loadout.Name) ? "Unnamed ship" : loadout.Name;
            string type = ShipModuleCatalog.Default.FindShip(loadout.ShipType)?.Name ?? loadout.ShipType;
            return $"{name} · {type}";
        }
    }
    public string Status =>
        canExport
            ? "Paste this ship's latest recorded loadout into EDSY's Import dialog."
            : "Board your ship and read a complete Loadout after outfitting to enable export.";

    /// <summary>
    /// Refreshes export readiness from the identified commander's current ship and status.
    /// </summary>
    public void Update(JournalSessionState session, EliteStatus? status, bool awaitingCommander)
    {
        ShipLoadout? next = session.ShipLoadout.Current;
        bool enabled =
            next is not null
            && !awaitingCommander
            && !session.IsShutdown
            && !session.IsAtMainMenu
            && !string.IsNullOrWhiteSpace(session.FrontierId)
            && next.ShipType.Equals(session.ShipType, StringComparison.OrdinalIgnoreCase)
            && next.ShipId is not null
            && next.ShipId == session.ShipId
            && ShipModuleCatalog.Default.FindShip(next.ShipType) is not null
            && (status is null || status.InMainShip);
        if (ReferenceEquals(loadout, next) && enabled == canExport)
        {
            return;
        }

        loadout = next;
        canExport = enabled;
        slefJson = enabled
            ? next!.ExportSlef(typeof(CurrentShipExportViewModel).Assembly.GetName().Version!.ToString())
            : "";
        Changed(nameof(SlefJson));
        Changed(nameof(CanExport));
        Changed(nameof(ShipDescription));
        Changed(nameof(Status));
    }
}
