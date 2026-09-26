using System.ComponentModel;
using System.Windows.Input;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class FleetCarrierWorkspaceViewModel : WorkspaceObservable, IDisposable
{
    private readonly CommanderProfileViewModel profile;
    private readonly ColonizationViewModel colonization;
    private readonly ICommand selectPersonalCommand;
    private readonly ICommand selectSquadronCommand;
    private readonly ICommand selectLinkedCommand;
    private readonly ICommand refreshLinkedCommand;
    private readonly Action raiseRefreshCanExecute;
    private FleetCarrierWorkspaceTab selectedTab = FleetCarrierWorkspaceTab.Personal;
    private long? selectedMarketId;
    private string? commander;
    private IReadOnlyList<ColonizationFleetCarrier>? observedCarriers;
    private IReadOnlyList<ColonizationFleetCarrier> candidates = [];
    private string? personalCallsign;
    private string? squadronCallsign;
    private long? detectedMarketId;
    private bool viewingJournalCommander;

    public FleetCarrierWorkspaceViewModel(CommanderProfileViewModel profile, ColonizationViewModel colonization)
    {
        this.profile = profile;
        this.colonization = colonization;
        selectPersonalCommand = new RelayCommand(() => SelectedTab = FleetCarrierWorkspaceTab.Personal);
        selectSquadronCommand = new RelayCommand(() => SelectedTab = FleetCarrierWorkspaceTab.Squadron);
        selectLinkedCommand = new RelayCommand(() => SelectedTab = FleetCarrierWorkspaceTab.Linked);
        var refresh = new AsyncCommand(
            RefreshLinkedCarriersAsync,
            () => colonization.IsEnabled && !colonization.IsBusy && colonization.CommanderName is not null
        );
        refreshLinkedCommand = refresh;
        raiseRefreshCanExecute = refresh.RaiseCanExecuteChanged;
        colonization.PropertyChanged += OnChanged;
        profile.PropertyChanged += OnProfileChanged;
        ApplyCarrierWorkspaceKind();
        Refresh();
    }

    public ICommand SelectPersonalTabCommand => selectPersonalCommand;
    public ICommand SelectSquadronTabCommand => selectSquadronCommand;
    public ICommand SelectLinkedTabCommand => selectLinkedCommand;
    public ICommand RefreshLinkedCarriersCommand => refreshLinkedCommand;

    public FleetCarrierWorkspaceTab SelectedTab
    {
        get => selectedTab;
        private set
        {
            if (selectedTab == value)
            {
                return;
            }

            selectedTab = value;
            ApplyCarrierWorkspaceKind();
            Changed(nameof(SelectedTab));
            Changed(nameof(IsPersonalTabSelected));
            Changed(nameof(IsSquadronTabSelected));
            Changed(nameof(IsLinkedTabSelected));
            Changed(nameof(IsPersonalTabActive));
            Changed(nameof(IsSquadronTabActive));
            Changed(nameof(IsLinkedTabActive));
        }
    }

    public bool IsPersonalTabSelected => selectedTab == FleetCarrierWorkspaceTab.Personal;
    public bool IsSquadronTabSelected => selectedTab == FleetCarrierWorkspaceTab.Squadron;
    public bool IsLinkedTabSelected => selectedTab == FleetCarrierWorkspaceTab.Linked;
    public bool IsPersonalTabActive => IsPersonalTabSelected;
    public bool IsSquadronTabActive => IsSquadronTabSelected;
    public bool IsLinkedTabActive => IsLinkedTabSelected;

    public IReadOnlyList<ColonizationFleetCarrier> SquadronCandidates => candidates;

    public ColonizationFleetCarrier? SelectedSquadronCarrier
    {
        get => SquadronCandidates.FirstOrDefault(c => c.MarketId == selectedMarketId);
        set
        {
            if (value is null || selectedMarketId == value.MarketId)
            {
                return;
            }

            selectedMarketId = value.MarketId;
            RaiseSquadron();
        }
    }

    public bool HasSquadronCarrier => SelectedSquadronCarrier is not null;

    public string SquadronCargoSummary =>
        SelectedSquadronCarrier is { } c
            ? $"{c.Cargo.Values.Sum():N0} t of linked cargo · capacity unavailable from RavenColonial"
            : "Choose a linked squadron carrier, or dock there to identify it automatically.";

    public IReadOnlyList<FrontierInventoryRowViewModel> SquadronCargo { get; private set; } = [];

    private void ApplyCarrierWorkspaceKind()
    {
        profile.SetCarrierWorkspaceKind(
            selectedTab == FleetCarrierWorkspaceTab.Squadron
                ? CarrierWorkspaceKind.Squadron
                : CarrierWorkspaceKind.Personal
        );
    }

    private async Task RefreshLinkedCarriersAsync()
    {
        await colonization.RefreshAsync().ConfigureAwait(true);
        raiseRefreshCanExecute();
        Refresh(force: true);
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (
            e.PropertyName
            is nameof(ColonizationViewModel.LinkedFleetCarriers)
                or nameof(ColonizationViewModel.DetectedSquadronCarrierMarketId)
                or nameof(ColonizationViewModel.CommanderName)
                or nameof(ColonizationViewModel.IsBusy)
                or nameof(ColonizationViewModel.IsEnabled)
                or nameof(ColonizationViewModel.FleetCarrierCargoSyncEnabled)
        )
        {
            raiseRefreshCanExecute();
            Refresh();
            if (e.PropertyName is nameof(ColonizationViewModel.LinkedFleetCarriers))
            {
                _ = SeedCapiCargoAsync();
            }
        }
    }

    private void OnProfileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (
            e.PropertyName
            is nameof(CommanderProfileViewModel.Snapshot)
                or nameof(CommanderProfileViewModel.IsViewingJournalCommander)
                or nameof(CommanderProfileViewModel.PersonalCarrier)
                or nameof(CommanderProfileViewModel.SquadronCarrier)
                or nameof(CommanderProfileViewModel.Carrier)
        )
        {
            Refresh();
            _ = SeedCapiCargoAsync();
        }
    }

    private async Task SeedCapiCargoAsync()
    {
        if (profile.IsViewingJournalCommander)
        {
            await colonization.SeedLinkedCarrierCargoFromCapiAsync(profile.Snapshot).ConfigureAwait(true);
        }
    }

    private void Refresh(bool force = false)
    {
        bool isViewingJournalCommander = profile.IsViewingJournalCommander;
        string? nextPersonalCallsign = isViewingJournalCommander ? profile.PersonalCarrier?.Callsign : null;
        string? nextSquadronCallsign = isViewingJournalCommander ? profile.SquadronCarrier?.Callsign : null;
        long? nextDetectedMarketId = isViewingJournalCommander ? colonization.DetectedSquadronCarrierMarketId : null;
        if (
            !force
            && ReferenceEquals(observedCarriers, colonization.LinkedFleetCarriers)
            && commander == colonization.CommanderName
            && personalCallsign == nextPersonalCallsign
            && squadronCallsign == nextSquadronCallsign
            && detectedMarketId == nextDetectedMarketId
            && viewingJournalCommander == isViewingJournalCommander
        )
        {
            return;
        }

        observedCarriers = colonization.LinkedFleetCarriers;
        personalCallsign = nextPersonalCallsign;
        squadronCallsign = nextSquadronCallsign;
        detectedMarketId = nextDetectedMarketId;
        viewingJournalCommander = isViewingJournalCommander;
        candidates = isViewingJournalCommander
            ? observedCarriers.Where(c => !IsOwnedCarrierCallsign(c.Name)).ToArray()
            : [];
        if (commander != colonization.CommanderName)
        {
            commander = colonization.CommanderName;
            selectedMarketId = null;
        }

        if (selectedMarketId is not null && candidates.All(c => c.MarketId != selectedMarketId))
        {
            selectedMarketId = null;
        }

        if (selectedMarketId is null && detectedMarketId is { } id && SquadronCandidates.Any(c => c.MarketId == id))
        {
            selectedMarketId = id;
        }

        profile.UpdateLinkedFleetCarriers(
            profile.IsViewingJournalCommander ? commander : null,
            profile.IsViewingJournalCommander ? colonization.LinkedFleetCarriers : []
        );
        Changed(nameof(SquadronCandidates));
        RaiseSquadron();
    }

    private bool IsOwnedCarrierCallsign(string? name)
    {
        return string.Equals(name, personalCallsign, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, squadronCallsign, StringComparison.OrdinalIgnoreCase);
    }

    private void RaiseSquadron()
    {
        SquadronCargo =
            SelectedSquadronCarrier
                ?.Cargo.OrderBy(p => p.Key)
                .Select(p => new FrontierInventoryRowViewModel("Commodity", p.Key, $"{p.Value:N0}", ""))
                .ToArray()
            ?? [];
        Changed(nameof(SelectedSquadronCarrier));
        Changed(nameof(SquadronCargo));
        Changed(nameof(HasSquadronCarrier));
        Changed(nameof(SquadronCargoSummary));
    }

    public void Dispose()
    {
        colonization.PropertyChanged -= OnChanged;
        profile.PropertyChanged -= OnProfileChanged;
    }
}

public enum FleetCarrierWorkspaceTab
{
    Personal,
    Squadron,
    Linked,
}

file sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add
        {
            // Always executable.
        }
        remove
        {
            // Always executable.
        }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}

file sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    private bool isRunning;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !isRunning && canExecute();

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        isRunning = true;
        RaiseCanExecuteChanged();
        try
        {
            await execute().ConfigureAwait(true);
        }
        finally
        {
            isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
