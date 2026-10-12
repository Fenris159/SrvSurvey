using System.ComponentModel;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>Shares a refreshed attraction snapshot with the existing navigation and overlay workspaces.</summary>
    private void OnPoiCatalogChanged(object? sender, EventArgs eventArgs)
    {
        JumpInfo.ApplyPoiCatalog(ExplorationPois.Catalog);
        GalaxyMap.ApplyPoiCatalog(ExplorationPois.Catalog);
        Route.ApplyPoiCatalog(ExplorationPois.Catalog);
        FleetCarrierRoute.ApplyPoiCatalog(ExplorationPois.Catalog);
    }

    /// <summary>Propagates external-data consent to catalogue browsing and selected-image loading.</summary>
    private void OnPoiExternalDataChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(SystemSurvey.UseExternalData))
        {
            ExplorationPois.SetExternalEnabled(SystemSurvey.UseExternalData && !IsDiagnosticReplay);
        }
    }
}
