using System.ComponentModel;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class FleetCarrierWorkspaceViewModel : WorkspaceObservable, IDisposable
{
    private readonly CommanderProfileViewModel profile;
    private readonly ColonizationViewModel colonization;
    private long? selectedMarketId;
    private string? commander;
    private IReadOnlyList<ColonizationFleetCarrier>? observedCarriers;
    private IReadOnlyList<ColonizationFleetCarrier> candidates = [];
    private string? callsign;
    private long? detectedMarketId;
    public FleetCarrierWorkspaceViewModel(CommanderProfileViewModel profile, ColonizationViewModel colonization)
    {
        this.profile = profile; this.colonization = colonization;
        colonization.PropertyChanged += OnChanged;
        profile.PropertyChanged += OnProfileChanged;
        Refresh();
    }
    public IReadOnlyList<ColonizationFleetCarrier> SquadronCandidates => candidates;
    public ColonizationFleetCarrier? SelectedSquadronCarrier
    {
        get => SquadronCandidates.FirstOrDefault(c => c.MarketId == selectedMarketId);
        set { if (value is null || selectedMarketId == value.MarketId) return; selectedMarketId = value.MarketId; RaiseSquadron(); }
    }
    public bool HasSquadronCarrier => SelectedSquadronCarrier is not null;
    public string SquadronCargoSummary => SelectedSquadronCarrier is { } c ? $"{c.Cargo.Values.Sum():N0} t of linked cargo · capacity unavailable from RavenColonial" : "Choose a linked squadron carrier, or dock there to identify it automatically.";
    public IReadOnlyList<FrontierInventoryRowViewModel> SquadronCargo => SelectedSquadronCarrier?.Cargo.OrderBy(p => p.Key).Select(p => new FrontierInventoryRowViewModel("Commodity", p.Key, $"{p.Value:N0}", "")).ToArray() ?? [];
    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ColonizationViewModel.LinkedFleetCarriers) or nameof(ColonizationViewModel.DetectedSquadronCarrierMarketId) or nameof(ColonizationViewModel.CommanderName)) Refresh();
    }
    private void OnProfileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommanderProfileViewModel.Snapshot)) Refresh();
    }
    private void Refresh()
    {
        if (ReferenceEquals(observedCarriers, colonization.LinkedFleetCarriers) && commander == colonization.CommanderName && callsign == profile.Carrier?.Callsign && detectedMarketId == colonization.DetectedSquadronCarrierMarketId) return;
        observedCarriers = colonization.LinkedFleetCarriers;
        callsign = profile.Carrier?.Callsign;
        detectedMarketId = colonization.DetectedSquadronCarrierMarketId;
        candidates = observedCarriers.Where(c => !string.Equals(c.Name, callsign, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (commander != colonization.CommanderName) { commander = colonization.CommanderName; selectedMarketId = null; }
        if (selectedMarketId is null && colonization.DetectedSquadronCarrierMarketId is { } id && SquadronCandidates.Any(c => c.MarketId == id)) selectedMarketId = id;
        profile.UpdateLinkedFleetCarriers(commander, colonization.LinkedFleetCarriers);
        Changed(nameof(SquadronCandidates)); RaiseSquadron();
    }
    private void RaiseSquadron() { Changed(nameof(SelectedSquadronCarrier)); Changed(nameof(SquadronCargo)); Changed(nameof(HasSquadronCarrier)); Changed(nameof(SquadronCargoSummary)); }
    public void Dispose() { colonization.PropertyChanged -= OnChanged; profile.PropertyChanged -= OnProfileChanged; }
}
