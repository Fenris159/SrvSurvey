using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Platform.Frontier;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class CommanderProfileViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly TimeSpan AutomaticRefreshAge = TimeSpan.FromMinutes(15);

    private readonly IFrontierAccountService accountService;
    private readonly ICommunityGoalJournalHistoryReader? communityGoalHistoryReader;
    private readonly Func<DateTimeOffset> now;
    private readonly AsyncCommand connectCommand;
    private readonly AsyncCommand cancelConnectionCommand;
    private readonly AsyncCommand refreshCommand;
    private readonly AsyncCommand unlinkCommand;
    private CancellationTokenSource? connectionCancellation;
    private FrontierAccountSnapshot? snapshot;
    private CargoSnapshot? detectedShipCargo;
    private ShipLockerSnapshot? detectedShipLocker;
    private CargoSnapshot? localShipCargo;
    private ShipLockerSnapshot? localShipLocker;
    private IReadOnlyList<FrontierLocalInventoryRowViewModel>
        currentShipCargoRows = [];
    private IReadOnlyList<FrontierLocalInventoryRowViewModel>
        currentShipLockerRows = [];
    private IReadOnlyList<FrontierShipModuleGroupViewModel> currentShipModuleGroups = [];
    private IReadOnlyList<FrontierLockerCategoryViewModel> currentShipLockerGroups = [];
    private IReadOnlyList<FrontierReputationSnapshot> journalReputation = [];
    private string? journalReputationCommanderName;
    private DateTimeOffset? journalReputationUpdatedAt;
    private IReadOnlyList<FrontierCommunityGoalSnapshot> journalCommunityGoals = [];
    private IReadOnlyList<FrontierCommunityGoalSnapshot>
        journalCommunityGoalHistory = [];
    private string? journalCommunityGoalCommanderName;
    private DateTimeOffset? journalCommunityGoalsUpdatedAt;
    private string journalCommunityGoalHistoryError = string.Empty;
    private string? detectedFrontierId;
    private string? detectedCommanderName;
    private string? activeFrontierId;
    private string? activeCommanderName;
    private string? manuallySelectedFrontierId;
    private string? manuallySelectedCommanderName;
    private IReadOnlyList<FrontierCommanderSelectionOption> commanderSelectionOptions =
        [FrontierCommanderSelectionOption.Automatic(null, null)];
    private FrontierCommanderSelectionOption? selectedCommanderOption;
    private bool isUpdatingCommanderSelection;
    private long commanderContextVersion;
    private bool isCompanionInventorySuppressed;
    private bool isManualInventorySuppressed;
    private bool isLocalInventorySuppressed;
    private bool isLinked;
    private bool isBusy;
    private bool isConnecting;
    private bool initialized;
    private string statusMessage = string.Empty;
    private bool disposed;

    public CommanderProfileViewModel(
        IFrontierAccountService accountService,
        Func<DateTimeOffset>? now = null,
        ICommunityGoalJournalHistoryReader? communityGoalHistoryReader = null)
    {
        this.accountService = accountService
            ?? throw new ArgumentNullException(nameof(accountService));
        this.communityGoalHistoryReader = communityGoalHistoryReader;
        this.accountService.AuthorizationCallbackReceived +=
            HandleAuthorizationCallbackReceived;
        this.now = now ?? (() => DateTimeOffset.Now);
        connectCommand = new AsyncCommand(
            ConnectAsync,
            () => !IsBusy && !IsLinked && activeFrontierId is not null);
        cancelConnectionCommand = new AsyncCommand(
            CancelConnectionAsync,
            () => IsConnecting);
        refreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy && IsLinked);
        unlinkCommand = new AsyncCommand(UnlinkAsync, () => !IsBusy && IsLinked);
        ConnectCommand = connectCommand;
        CancelConnectionCommand = cancelConnectionCommand;
        RefreshCommand = refreshCommand;
        UnlinkCommand = unlinkCommand;
        selectedCommanderOption = commanderSelectionOptions[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? AuthorizationCallbackReceived;

    public ICommand ConnectCommand { get; }

    public ICommand CancelConnectionCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand UnlinkCommand { get; }

    public FrontierConsolePaneStates PaneStates { get; } = new();

    public bool IsLinked
    {
        get => isLinked;
        private set
        {
            if (SetField(ref isLinked, value))
            {
                OnPropertyChanged(nameof(IsUnlinked));
                RaiseCommandStates();
            }
        }
    }

    public bool IsUnlinked => !IsLinked;

    public IReadOnlyList<FrontierCommanderSelectionOption> CommanderSelectionOptions
    {
        get => commanderSelectionOptions;
        private set => SetField(ref commanderSelectionOptions, value);
    }

    public FrontierCommanderSelectionOption? SelectedCommanderOption
    {
        get => selectedCommanderOption;
        set
        {
            if (!SetField(ref selectedCommanderOption, value)
                || isUpdatingCommanderSelection
                || value is null)
            {
                return;
            }

            _ = SelectCommanderAsync(value);
        }
    }

    public bool IsAutomaticCommanderSelection => manuallySelectedFrontierId is null;

    public string DetectedCommanderDescription => detectedFrontierId is null
        ? "Waiting for active journal commander"
        : string.IsNullOrWhiteSpace(detectedCommanderName)
            ? detectedFrontierId
            : $"{detectedCommanderName} ({detectedFrontierId})";

    public string ActiveCommanderDescription => activeFrontierId is null
        ? "No Frontier commander selected"
        : string.IsNullOrWhiteSpace(activeCommanderName)
            ? activeFrontierId
            : $"{activeCommanderName} ({activeFrontierId})";

    public string CommanderSelectionDescription => IsAutomaticCommanderSelection
        ? $"Automatic · Journal: {DetectedCommanderDescription}"
        : string.Equals(
            activeFrontierId,
            detectedFrontierId,
            StringComparison.OrdinalIgnoreCase)
                ? "Manual selection · Matches the active journal commander"
                : $"Manual selection · Journal remains {DetectedCommanderDescription}";

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsConnecting
    {
        get => isConnecting;
        private set
        {
            if (SetField(ref isConnecting, value))
            {
                OnPropertyChanged(nameof(ConnectButtonText));
                RaiseCommandStates();
            }
        }
    }

    public string ConnectButtonText => IsConnecting
        ? "Waiting for Frontier..."
        : "Connect to Frontier";

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public FrontierAccountSnapshot? Snapshot
    {
        get => snapshot;
        private set
        {
            if (EqualityComparer<FrontierAccountSnapshot?>.Default.Equals(snapshot, value))
            {
                return;
            }

            snapshot = value;
            RebuildCurrentShipModuleGroups();
            OnPropertyChanged();
            RaiseSnapshotProperties();
        }
    }

    public bool HasSnapshot => Snapshot is not null;

    public string CommanderName => Snapshot?.CommanderName ?? "Commander";

    public string Balance => FormatCredits(Snapshot?.Credits ?? 0);

    public string Debt => FormatCredits(Snapshot?.Debt ?? 0);

    public string NetWorth => FormatCredits(Snapshot?.NetWorth ?? 0);

    public string FleetValue => FormatCredits(Snapshot?.FleetValue ?? 0);

    public string FleetCount => $"{Snapshot?.Ships.Count ?? 0:N0} ships";

    public string CurrentLocation
    {
        get
        {
            if (Snapshot is null)
            {
                return "—";
            }

            return JoinLocation(Snapshot.LastSystem, Snapshot.LastStation);
        }
    }

    public string CurrentShipDescription
    {
        get
        {
            var ship = Snapshot?.CurrentShip;
            if (ship is null)
            {
                return "—";
            }

            return string.Equals(ship.Name, ship.Type, StringComparison.OrdinalIgnoreCase)
                ? ship.Type
                : $"{ship.Name} · {ship.Type}";
        }
    }

    public bool HasCurrentShip => Snapshot?.CurrentShip is not null;

    public string CurrentShipHealth
    {
        get
        {
            var ship = Snapshot?.CurrentShip;
            if (ship is null)
            {
                return "Health unavailable";
            }

            var parts = new List<string>();
            if (ship.HullHealth is { } hull)
            {
                parts.Add($"Hull {NormalizePercentForDisplay(hull):N0}%");
            }

            if (ship.ShieldHealth is { } shield)
            {
                parts.Add($"Shield {NormalizePercentForDisplay(shield):N0}%");
            }

            return parts.Count == 0 ? "Health unavailable" : string.Join(" · ", parts);
        }
    }

    public string LastUpdated => Snapshot is null
        ? "Not refreshed"
        : $"Frontier updated {Snapshot.FetchedAt.ToLocalTime():g}";

    public string CommanderId => Snapshot?.CommanderId is { } id
        ? id.ToString(CultureInfo.InvariantCulture)
        : "Unavailable";

    public string CommanderState => Snapshot is null
        ? "Unavailable"
        : $"{(Snapshot.IsDocked ? "Docked" : "In flight")} · "
            + (Snapshot.IsAlive ? "Active" : "Destroyed");

    public string LocationAllegiance => FirstNonEmpty(
        Snapshot?.LastSystemDetails?.Allegiance,
        "Independent / unknown");

    public string FactionIconPath => FactionIcon(
        Snapshot?.LastSystemDetails?.Allegiance);

    public bool HasFactionIcon => !string.IsNullOrWhiteSpace(FactionIconPath);

    public IReadOnlyList<string> StationServices =>
        Snapshot?.LastStationDetails?.Services ?? [];

    public IReadOnlyList<FrontierRankCardViewModel> Ranks => Snapshot?.Ranks
        .Select(rank => new FrontierRankCardViewModel(
            rank.Category,
            rank.Name,
            rank.Level,
            RankIcon(rank.Key, rank.Level)))
        .ToArray() ?? [];

    public IReadOnlyList<FrontierDataPointSnapshot> ProfileData =>
        Snapshot?.ProfileData ?? [];

    public IReadOnlyList<FrontierDetailRowViewModel> CurrentShipValueRows
    {
        get
        {
            var ship = Snapshot?.CurrentShip;
            return ship is null
                ? []
                :
                [
                    new("Hull", FormatCredits(ship.HullValue)),
                    new("Modules", FormatCredits(ship.ModulesValue)),
                    new("Cargo", FormatCredits(ship.CargoValue)),
                    new("Total", FormatCredits(ship.Value)),
                    new("Unloaned", FormatCredits(ship.UnloanedValue)),
                ];
        }
    }

    public IReadOnlyList<FrontierDetailRowViewModel> CurrentShipConditionRows
    {
        get
        {
            var ship = Snapshot?.CurrentShip;
            return ship is null
                ? []
                :
                [
                    new("Hull", FormatPercent(ship.HullHealth)),
                    new("Shield", FormatPercent(ship.ShieldHealth),
                        ship.ShieldUp ? "Up" : "Down"),
                    new("Integrity", FormatPercent(ship.Integrity)),
                    new("Cockpit", ship.CockpitBreached ? "Breached" : "Secure"),
                    new("Oxygen", ship.OxygenRemaining is { } oxygen
                        ? $"{oxygen:N0} seconds" : "Unavailable"),
                ];
        }
    }

    public IReadOnlyList<FrontierShipModuleRowViewModel> CurrentShipModules =>
        Snapshot?.CurrentShip?.Modules?
            .Where(item => !IsLiverySlot(item.Slot))
            .Select(CreateModuleRow)
            .OrderBy(item => ModuleGroupOrder(item.Group))
            .ThenBy(item => ModuleSlotOrder(item.Slot))
            .ThenBy(item => item.Slot, StringComparer.CurrentCultureIgnoreCase)
            .ToArray() ?? [];

    public IReadOnlyList<FrontierShipModuleGroupViewModel> CurrentShipModuleGroups =>
        currentShipModuleGroups;

    public IReadOnlyList<FrontierLiveryRowViewModel> CurrentShipLivery =>
        Snapshot?.CurrentShip?.Modules?
            .Where(item => IsLiverySlot(item.Slot))
            .Select(item => new FrontierLiveryRowViewModel(
                FriendlyLiverySlot(item.Slot),
                FriendlyLiveryName(item, ResolveInternalName(item)),
                FriendlyDescription(item.Description)))
            .OrderBy(item => LiveryOrder(item.Category))
            .ThenBy(item => item.Category, StringComparer.CurrentCultureIgnoreCase)
            .ToArray() ?? [];

    public string CurrentShipPaintwork => FormatPercent(
        Snapshot?.CurrentShip?.Paintwork);

    public bool HasCurrentShipLivery => CurrentShipLivery.Count > 0
        || Snapshot?.CurrentShip?.Paintwork is not null;

    public IReadOnlyList<FrontierLaunchBayRowViewModel> CurrentShipLaunchBays =>
        Snapshot?.CurrentShip?.LaunchBays?
            .Select(item => new FrontierLaunchBayRowViewModel(
                item.Slot,
                item.Vehicle,
                item.Loadout,
                $"{item.Rebuilds:N0} rebuilds"))
            .ToArray() ?? [];

    public bool HasCurrentShipLaunchBays => CurrentShipLaunchBays.Count > 0;

    public IReadOnlyList<FrontierLocalInventoryRowViewModel> CurrentShipCargo =>
        currentShipCargoRows;

    public IReadOnlyList<FrontierLocalInventoryRowViewModel> CurrentShipLocker =>
        currentShipLockerRows;

    public IReadOnlyList<FrontierLockerCategoryViewModel> CurrentShipLockerGroups =>
        currentShipLockerGroups;

    public bool HasCurrentShipCargo => CurrentShipCargo.Count > 0;

    public bool HasCurrentShipLocker => CurrentShipLocker.Count > 0;

    public string LocalInventoryStatus
    {
        get
        {
            if (isLocalInventorySuppressed)
            {
                if (isManualInventorySuppressed)
                {
                    return "Local cargo and ship-locker inventory belongs to the active journal commander and is hidden while viewing a different Frontier account.";
                }

                return "Local cargo and ship-locker inventory is hidden while multiple Elite windows are running because the shared companion files cannot be attributed safely.";
            }

            var latest = new DateTimeOffset?[]
                {
                    localShipCargo?.Timestamp,
                    localShipLocker?.Timestamp,
                }
                .Where(value => value is not null)
                .Select(value => value!.Value)
                .DefaultIfEmpty()
                .Max();
            return latest == default
                ? "Waiting for Elite to write Cargo.json or ShipLocker.json."
                : $"Local companion inventory updated {latest.ToLocalTime():g}.";
        }
    }

    public void UpdateLocalInventory(
        CargoSnapshot? cargo,
        ShipLockerSnapshot? shipLocker,
        bool isSuppressed)
    {
        detectedShipCargo = cargo;
        detectedShipLocker = shipLocker;
        isCompanionInventorySuppressed = isSuppressed;
        ApplyLocalInventorySelection();
    }

    private void ApplyLocalInventorySelection()
    {
        var manualSuppression = manuallySelectedFrontierId is not null
            && !string.Equals(
                manuallySelectedFrontierId,
                detectedFrontierId,
                StringComparison.OrdinalIgnoreCase);
        var suppression = isCompanionInventorySuppressed || manualSuppression;
        var nextCargo = !suppression
            && string.Equals(
                detectedShipCargo?.Vessel,
                "Ship",
                StringComparison.OrdinalIgnoreCase)
                ? detectedShipCargo
                : null;
        var nextShipLocker = suppression ? null : detectedShipLocker;
        var cargoChanged = !ReferenceEquals(localShipCargo, nextCargo);
        var shipLockerChanged = !ReferenceEquals(localShipLocker, nextShipLocker);
        var suppressionChanged = isLocalInventorySuppressed != suppression
            || isManualInventorySuppressed != manualSuppression;

        localShipCargo = nextCargo;
        localShipLocker = nextShipLocker;
        isLocalInventorySuppressed = suppression;
        isManualInventorySuppressed = manualSuppression;

        if (shipLockerChanged || suppressionChanged)
        {
            currentShipLockerRows = localShipLocker?.Items
                .Select(item => new FrontierLocalInventoryRowViewModel(
                    item.Category,
                    FirstNonEmpty(
                        item.LocalizedName,
                        HumanizeIdentifier(item.Name)),
                    item.Count.ToString("N0", CultureInfo.CurrentCulture),
                    string.Empty))
                .ToArray() ?? [];
            RebuildCurrentShipLockerGroups();
            OnPropertyChanged(nameof(CurrentShipLocker));
            OnPropertyChanged(nameof(CurrentShipLockerGroups));
            OnPropertyChanged(nameof(HasCurrentShipLocker));
        }

        if (cargoChanged || suppressionChanged)
        {
            currentShipCargoRows = localShipCargo?.Inventory
                .Select(item => new FrontierLocalInventoryRowViewModel(
                    "Cargo",
                    FirstNonEmpty(
                        item.LocalizedName,
                        HumanizeIdentifier(item.Name)),
                    item.Count.ToString("N0", CultureInfo.CurrentCulture),
                    item.Stolen > 0
                        ? $"{item.Stolen:N0} stolen"
                        : string.Empty))
                .ToArray() ?? [];
            OnPropertyChanged(nameof(CurrentShipCargo));
            OnPropertyChanged(nameof(HasCurrentShipCargo));
        }

        if (cargoChanged || shipLockerChanged || suppressionChanged)
        {
            OnPropertyChanged(nameof(LocalInventoryStatus));
        }
    }

    public void UpdateJournalReputation(
        string? commanderName,
        IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        var normalizedCommander = commanderName?.Trim();
        var commanderChanged = !string.Equals(
            journalReputationCommanderName,
            normalizedCommander,
            StringComparison.OrdinalIgnoreCase);
        if (commanderChanged)
        {
            journalReputationCommanderName = normalizedCommander;
            journalReputation = [];
            journalReputationUpdatedAt = null;
        }

        var latest = journalEvents.LastOrDefault(journalEvent =>
            string.Equals(
                journalEvent.EventName,
                "Reputation",
                StringComparison.OrdinalIgnoreCase));
        if (latest is null)
        {
            if (commanderChanged)
            {
                OnPropertyChanged(nameof(CommanderReputation));
            }

            return;
        }

        journalReputation = latest.Payload.EnumerateObject()
            .Where(property => property.Name is not "timestamp" and not "event")
            .Select(property => new FrontierReputationSnapshot(
                HumanizeIdentifier(property.Name),
                ReadJournalNumber(property.Value) ?? double.NaN))
            .Where(item => !double.IsNaN(item.Score))
            .OrderBy(item => item.Faction, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        journalReputationUpdatedAt = latest.Timestamp;
        OnPropertyChanged(nameof(CommanderReputation));
    }

    public void UpdateJournalCommunityGoals(
        string? commanderName,
        IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        var normalizedCommander = commanderName?.Trim();
        var commanderChanged = !string.Equals(
            journalCommunityGoalCommanderName,
            normalizedCommander,
            StringComparison.OrdinalIgnoreCase);
        if (commanderChanged)
        {
            journalCommunityGoalCommanderName = normalizedCommander;
            journalCommunityGoals = [];
            journalCommunityGoalHistory = [];
            journalCommunityGoalsUpdatedAt = null;
            journalCommunityGoalHistoryError = string.Empty;
        }

        var goalEvents = journalEvents
            .Where(journalEvent => string.Equals(
                journalEvent.EventName,
                "CommunityGoal",
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(journalEvent => journalEvent.Timestamp ?? DateTimeOffset.MinValue)
            .ToArray();
        if (goalEvents.Length == 0)
        {
            if (commanderChanged)
            {
                RaiseCommunityGoalProperties();
            }

            return;
        }

        var updated = false;
        foreach (var goalEvent in goalEvents)
        {
            try
            {
                var parsed = FrontierCapiSnapshotParser
                    .ParseCommunityGoals(goalEvent.RawJson);
                journalCommunityGoals = StampJournalCommunityGoals(
                    parsed,
                    goalEvent.Timestamp);
                journalCommunityGoalsUpdatedAt = goalEvent.Timestamp;
                journalCommunityGoalHistory = MergeJournalCommunityGoalHistory(
                    journalCommunityGoalHistory,
                    journalCommunityGoals);
                updated = true;
            }
            catch (Exception exception) when (
                exception is JsonException or InvalidDataException)
            {
                // The journal monitor reports malformed journal input. Preserve
                // the last valid Community Goal state in this projection.
            }
        }

        if (updated)
        {
            RaiseCommunityGoalProperties();
        }
    }

    public IReadOnlyList<FrontierShipRowViewModel> Ships => Snapshot?.Ships
        .Select(ship => new FrontierShipRowViewModel(
            ship.Name,
            ship.Type,
            JoinLocation(ship.System, ship.Station),
            FormatCredits(ship.Value),
            ship.IsCurrent ? "CURRENT" : string.Empty))
        .ToArray() ?? [];

    public IReadOnlyList<string> Capabilities => Snapshot?.Capabilities ?? [];

    public bool HasCapabilities => Capabilities.Count > 0;

    public FrontierCarrierSnapshot? Carrier => Snapshot?.Carrier;

    public bool HasCarrier => Carrier is not null;

    public string CarrierTitle => Carrier is null
        ? "No Fleet Carrier is associated with this account."
        : string.Equals(Carrier.Name, Carrier.Callsign, StringComparison.OrdinalIgnoreCase)
            ? Carrier.Callsign
            : $"{Carrier.Name} · {Carrier.Callsign}";

    public string CarrierLocation => Carrier is null
        ? "—"
        : string.IsNullOrWhiteSpace(Carrier.System) ? "Unknown system" : Carrier.System;

    public string CarrierBalance => FormatCredits(Carrier?.BankBalance ?? 0);

    public string CarrierCapacity => Carrier is null
        ? "—"
        : $"{Carrier.CapacityFree:N0} t remaining · "
            + $"{Carrier.CapacityUsed + Carrier.CapacityFree:N0} t total";

    public string CarrierCapacityHeader => Carrier is null
        ? "Capacity"
        : $"Capacity ({Carrier.CapacityFree:N0} / "
            + $"{Carrier.CapacityUsed + Carrier.CapacityFree:N0} t)";

    public string CarrierMarketSummary => Carrier is null
        ? "—"
        : $"{Carrier.SellOrders.Count:N0} sell · {Carrier.BuyOrders.Count:N0} buy · {FormatCredits(Carrier.MarketCargoValue)} cargo value";

    public IReadOnlyList<FrontierCapacityRowViewModel> CarrierCapacityRows =>
        Carrier?.Capacity
            .OrderBy(item => item.Category.Equals(
                "Cargo Not For Sale",
                StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(item => item.Used)
            .Select(item => new FrontierCapacityRowViewModel(
                item.Category.Equals(
                    "Cargo Not For Sale",
                    StringComparison.OrdinalIgnoreCase)
                        ? "Not For Sale"
                        : item.Category,
                $"{item.Used:N0} t"))
            .ToArray() ?? [];

    public IReadOnlyList<FrontierInventoryRowViewModel> CarrierCargo =>
        Carrier?.Cargo
            .Select(item => new FrontierInventoryRowViewModel(
                item.Category,
                item.Name,
                $"{item.Quantity:N0}",
                item.Value > 0 ? FormatCredits(item.Value) : string.Empty))
            .ToArray() ?? [];

    public IReadOnlyList<FrontierInventoryRowViewModel> CarrierLocker =>
        Carrier?.Locker
            .Select(item => new FrontierInventoryRowViewModel(
                item.Category,
                item.Name,
                $"{item.Quantity:N0}",
                string.Empty))
            .ToArray() ?? [];

    public IReadOnlyList<FrontierOrderRowViewModel> CarrierSellOrders =>
        Carrier?.SellOrders
            .Select(item => new FrontierOrderRowViewModel(
                item.Category,
                item.Name,
                $"{item.Quantity:N0} available",
                FormatCredits(item.Price)))
            .ToArray() ?? [];

    public IReadOnlyList<FrontierOrderRowViewModel> CarrierBuyOrders =>
        Carrier?.BuyOrders
            .Select(item => new FrontierOrderRowViewModel(
                item.Category,
                item.Name,
                item.Remaining is { } remaining
                    ? $"{remaining:N0} remaining of {item.Quantity:N0}"
                    : $"{item.Quantity:N0} requested",
                FormatCredits(item.Price)))
            .ToArray() ?? [];

    public IReadOnlyList<string> CarrierServices => Carrier?.Services ?? [];

    public string CarrierError => Snapshot?.CarrierError ?? string.Empty;

    public bool HasCarrierError => !string.IsNullOrWhiteSpace(CarrierError);

    public IReadOnlyList<FrontierDetailRowViewModel> CarrierOperations => Carrier is null
        ? []
        :
        [
            new("State", Carrier.State),
            new("Theme", FirstNonEmpty(Carrier.Theme, "Standard")),
            new("Docking access", Carrier.DockingAccess),
            new("Notorious access", Carrier.NotoriousAccess ? "Allowed" : "Denied"),
            new("Tritium reserve", $"{Carrier.Tritium:N0} t"),
            new("Distance jumped", $"{Carrier.TotalDistanceJumped:N1} ly"),
            new("Current jump", FirstNonEmpty(Carrier.CurrentJump, "None plotted")),
        ];

    public IReadOnlyList<FrontierDetailRowViewModel> CarrierFinances => Carrier is null
        ? []
        :
        [
            new("Bank balance", FormatCredits(Carrier.BankBalance)),
            new("Reserved balance", FormatCredits(Carrier.ReservedBalance)),
            new("Weekly maintenance", FormatCredits(Carrier.WeeklyMaintenance)),
            new("Maintenance paid", FormatCredits(Carrier.MaintenanceToDate)),
            new("Core cost", FormatCredits(Carrier.CoreCost)),
            new("Services cost", FormatCredits(Carrier.ServicesCost)),
            new("Services paid", FormatCredits(Carrier.ServicesCostToDate)),
            new("Jump cost", FormatCredits(Carrier.JumpsCost), $"{Carrier.WeeklyJumps:N0} jumps"),
            new("Debt threshold", FormatCredits(Carrier.DebtThreshold)),
            new("Base taxation", $"{Carrier.Taxation:N0}%"),
            new("Market cargo", FormatCredits(Carrier.MarketCargoValue)),
            new("Market profit", FormatCredits(Carrier.MarketProfit)),
            new("Purchase allocation", FormatCredits(Carrier.PurchaseOrderAllocation)),
        ];

    public IReadOnlyList<FrontierDetailRowViewModel> CarrierServiceTaxation =>
        Carrier?.ServiceTaxation?
            .Select(item => new FrontierDetailRowViewModel(
                item.Name,
                item.Value + "%"))
            .ToArray() ?? [];

    public IReadOnlyList<FrontierCarrierCrewRowViewModel> CarrierCrew =>
        Carrier?.ServiceCrew?
            .Select(item => new FrontierCarrierCrewRowViewModel(
                item.Service,
                FirstNonEmpty(item.Name, "Unassigned"),
                FirstNonEmpty(item.Faction, "Unknown faction"),
                FormatCredits(item.Salary),
                item.Enabled ? "ACTIVE" : "SUSPENDED",
                item.LastChanged?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                    ?? string.Empty))
            .ToArray() ?? [];

    public IReadOnlyList<FrontierCarrierJumpRowViewModel> CarrierItinerary =>
        Carrier?.Itinerary?
            .Select(item => new FrontierCarrierJumpRowViewModel(
                item.System,
                item.State,
                item.ArrivedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                    ?? "Unknown arrival",
                item.DepartedAt is { } departure
                    ? departure.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                    : "Current location",
                TimeSpan.FromSeconds(Math.Max(0, item.VisitDurationSeconds))
                    .ToString("d'.'hh':'mm", CultureInfo.InvariantCulture)))
            .ToArray() ?? [];

    public IReadOnlyList<FrontierReputationRowViewModel> CarrierReputation =>
        Carrier?.Reputation?
            .Select(item => new FrontierReputationRowViewModel(
                item.Faction,
                $"{item.Score:N0}%",
                FactionIcon(item.Faction)))
            .ToArray() ?? [];

    public IReadOnlyList<FrontierReputationRowViewModel> CommanderReputation =>
        EffectiveCommanderReputation()
            .Select(item => new FrontierReputationRowViewModel(
                item.Faction,
                $"{item.Score:N0}%",
                FactionIcon(item.Faction)))
            .ToArray();

    public IReadOnlyList<FrontierDataPointSnapshot> CarrierData =>
        Snapshot?.CarrierEndpointData
            ?? Carrier?.DataPoints
            ?? [];

    public FrontierMarketSnapshot? Market => Snapshot?.Market;

    public bool HasMarket => Market is not null;

    public string MarketTitle => Market is null
        ? "No market data is available from the last docked location."
        : FirstNonEmpty(Market.Name, "Last docked market");

    public string MarketSubtitle => Market is null
        ? "Dock at a market and refresh Frontier data."
        : $"{FirstNonEmpty(Market.OutpostType, "Station")} · "
            + $"{Market.Commodities.Count:N0} commodities · "
            + $"updated {Market.FetchedAt.ToLocalTime():g}";

    public string MarketError => Snapshot?.MarketError ?? string.Empty;

    public bool HasMarketError => !string.IsNullOrWhiteSpace(MarketError);

    public IReadOnlyList<FrontierCommodityRowViewModel> MarketCommodities =>
        Market?.Commodities
            .Select(item => new FrontierCommodityRowViewModel(
                item.Category,
                item.Name,
                FormatCredits(item.BuyPrice),
                FormatCredits(item.SellPrice),
                item.Stock.ToString("N0", CultureInfo.CurrentCulture),
                item.Demand.ToString("N0", CultureInfo.CurrentCulture),
                FirstNonEmpty(item.Legality, "Legal"),
                $"Stock {BracketName(item.StockBracket)} · Demand {BracketName(item.DemandBracket)}"))
            .ToArray() ?? [];

    public IReadOnlyList<FrontierNamedValueSnapshot> MarketServices =>
        Market?.Services ?? [];

    public IReadOnlyList<FrontierEconomyRowViewModel> MarketEconomies =>
        Market?.Economies
            .Select(item => new FrontierEconomyRowViewModel(
                item.Name,
                $"{item.Proportion:P0}"))
            .ToArray() ?? [];

    public IReadOnlyList<string> MarketImported => Market?.Imported ?? [];

    public IReadOnlyList<string> MarketExported => Market?.Exported ?? [];

    public IReadOnlyList<string> MarketProhibited => Market?.Prohibited ?? [];

    public IReadOnlyList<FrontierDataPointSnapshot> MarketData =>
        Market?.DataPoints ?? [];

    public FrontierShipyardSnapshot? Shipyard => Snapshot?.Shipyard;

    public bool HasShipyard => Shipyard is not null;

    public string ShipyardTitle => Shipyard is null
        ? "No shipyard or outfitting data is available."
        : FirstNonEmpty(Shipyard.Name, "Last docked shipyard");

    public string ShipyardSubtitle => Shipyard is null
        ? "Dock at a supported station and refresh Frontier data."
        : $"{FirstNonEmpty(Shipyard.OutpostType, "Station")} · "
            + $"{Shipyard.Ships.Count:N0} ships · {Shipyard.Modules.Count:N0} modules · "
            + $"updated {Shipyard.FetchedAt.ToLocalTime():g}";

    public string ShipyardError => Snapshot?.ShipyardError ?? string.Empty;

    public bool HasShipyardError => !string.IsNullOrWhiteSpace(ShipyardError);

    public IReadOnlyList<FrontierShipForSaleRowViewModel> ShipyardShips =>
        Shipyard?.Ships
            .Select(item => new FrontierShipForSaleRowViewModel(
                item.Name,
                FormatCredits(item.BaseValue),
                item.Stock < 0 ? "Unlimited" : item.Stock.ToString("N0", CultureInfo.CurrentCulture),
                item.Sku))
            .ToArray() ?? [];

    public IReadOnlyList<FrontierOutfittingModuleRowViewModel> ShipyardModules =>
        Shipyard?.Modules
            .Select(item => new FrontierOutfittingModuleRowViewModel(
                item.Category,
                item.Name,
                FormatCredits(item.Cost),
                item.Stock < 0 ? "Unlimited" : item.Stock.ToString("N0", CultureInfo.CurrentCulture),
                item.Sku))
            .ToArray() ?? [];

    public IReadOnlyList<FrontierNamedValueSnapshot> ShipyardServices =>
        Shipyard?.Services ?? [];

    public IReadOnlyList<FrontierDataPointSnapshot> ShipyardData =>
        Shipyard?.DataPoints ?? [];

    public IReadOnlyList<FrontierCommunityGoalCardViewModel> CommunityGoals =>
        EffectiveCommunityGoals()
            .Select(CreateCommunityGoalCard)
            .ToArray();

    public bool HasCommunityGoals => CommunityGoals.Count > 0;

    public string CommunityGoalsMessage
    {
        get
        {
            var goals = EffectiveCommunityGoals();
            if (goals.Count == 0)
            {
                return "No community goals were returned for this commander.";
            }

            var currentTime = now().ToUniversalTime();
            var active = goals
                .Where(goal => !goal.IsComplete
                    && (goal.ExpiresAt is null || goal.ExpiresAt > currentTime))
                .ToArray();
            var frontierActive = active.Count(goal => !IsInaraOnlyGoal(goal));
            var inaraActive = active.Length - frontierActive;
            var ended = goals.Count - active.Length;
            return $"{active.Length:N0} active ({frontierActive:N0} Frontier, "
                + $"{inaraActive:N0} Inara) · {ended:N0} recently completed or ended";
        }
    }

    public string CommunityGoalsError => string.Join(
        Environment.NewLine,
        new[]
        {
            Snapshot?.CommunityGoalsError,
            Snapshot?.InaraCommunityGoalsError,
            journalCommunityGoalHistoryError,
        }.Where(message => !string.IsNullOrWhiteSpace(message)));

    public bool HasCommunityGoalsError =>
        !string.IsNullOrWhiteSpace(CommunityGoalsError);

    public IReadOnlyList<FrontierDataPointSnapshot> CommunityGoalsData =>
        Snapshot?.CommunityGoalsData ?? [];

    private FrontierCommunityGoalCardViewModel CreateCommunityGoalCard(
        FrontierCommunityGoalSnapshot item)
    {
        var dataPoints = item.DataPoints ?? [];
        var system = FirstNonEmpty(
            item.System,
            CommunityGoalDataValue(dataPoints, "starsystem_name", "starsystemName"));
        var market = FirstNonEmpty(
            item.Market,
            CommunityGoalDataValue(dataPoints, "market_name", "marketName"));
        var objective = FirstNonEmpty(
            item.Objective,
            CommunityGoalDataValue(dataPoints, "objective"));
        var reward = FirstNonEmpty(
            item.Reward,
            CommunityGoalDataValue(dataPoints, "reward", "rewardText"));
        var briefing = CleanCommunityGoalBriefing(
            FirstNonEmpty(
                item.Description,
                CommunityGoalDataValue(dataPoints, "bulletin"),
                CommunityGoalDataValue(dataPoints, "news")),
            item.Title);
        var activityType = FirstNonEmpty(
            item.ActivityType,
            CommunityGoalDataValue(dataPoints, "activityType", "activity_type"));
        var currentTotal = item.CurrentTotal != 0
            ? item.CurrentTotal
            : CommunityGoalDataLong(dataPoints, "qty", "currentTotal") ?? 0;
        var cachedTarget = CommunityGoalDataLong(
            dataPoints,
            "target_qty",
            "targetTotal");
        var target = item.TargetTotal is > 0
            ? item.TargetTotal
            : cachedTarget is > 0
                ? cachedTarget
                : null;
        var hasPlayerContributionData = item.HasPlayerContributionData
            || HasCommunityGoalData(
                dataPoints,
                "playerContribution",
                "commander.contribution");
        var playerContribution = item.HasPlayerContributionData
            ? item.PlayerContribution
            : CommunityGoalDataLong(
                dataPoints,
                "playerContribution",
                "commander.contribution") ?? 0;
        var hasContributorData = item.HasContributorData
            || HasCommunityGoalData(
                dataPoints,
                "numContributors",
                "contributorsNum",
                "contributors");
        var contributors = item.HasContributorData
            ? item.Contributors
            : (int)Math.Clamp(
                CommunityGoalDataLong(
                    dataPoints,
                    "numContributors",
                    "contributorsNum",
                    "contributors") ?? 0,
                int.MinValue,
                int.MaxValue);
        var tier = FirstNonEmpty(
            item.TierReached,
            CommunityGoalDataValue(dataPoints, "tierReached", "tier"));
        var inaraLastUpdated = CommunityGoalDataDateTimeOffset(
            dataPoints,
            "inara.lastUpdate");
        var inaraFetchedAt = CommunityGoalDataDateTimeOffset(
            dataPoints,
            "inara.fetchedAt");
        var journalRecordedAt = CommunityGoalDataDateTimeOffset(
            dataPoints,
            "journal.communityGoalTimestamp");
        var hasInaraData = inaraLastUpdated is not null || inaraFetchedAt is not null;
        var isInaraOnly = IsInaraOnlyGoal(item);
        var sourceStatusParts = new List<string>();
        if (hasInaraData)
        {
            sourceStatusParts.Add(
                (isInaraOnly
                    ? "Global goal supplied by Inara"
                    : "Frontier goal supplemented by Inara")
                + (inaraLastUpdated is { } updated
                    ? $" · updated {updated.ToLocalTime():g}"
                    : string.Empty));
        }

        if (journalRecordedAt is { } recordedAt)
        {
            sourceStatusParts.Add(
                $"Personal progress restored from local journals · recorded {recordedAt.ToLocalTime():g}");
        }

        var sourceStatus = string.Join(" · ", sourceStatusParts);
        var currentTime = now().ToUniversalTime();
        var progress = target is { } targetTotal
            ? Math.Clamp((double)currentTotal / targetTotal * 100, 0, 100)
            : 0;
        var status = item.IsComplete
            ? "COMPLETED"
            : item.ExpiresAt is { } expiry && expiry <= currentTime
                ? "ENDED"
                : "ACTIVE";
        var standing = item.PlayerPercentile is { } percentile
            ? item.Bonus > 0
                ? $"Top {percentile:N0}% · {FormatCredits(item.Bonus)} reward"
                : $"Top {percentile:N0}%"
            : item.PlayerInTopRank
                ? item.TopRankSize is { } topRankSize
                    ? $"Top {topRankSize:N0} commander"
                    : "Top contributor"
                : item.Bonus > 0
                    ? $"Reward: {FormatCredits(item.Bonus)}"
                    : string.Empty;
        return new FrontierCommunityGoalCardViewModel(
            item.Title,
            briefing,
            objective,
            reward,
            system,
            market,
            JoinLocation(system, market),
            FriendlyCommunityGoalActivity(activityType),
            item.Id is { } id ? $"GOAL #{id:N0}" : "COMMUNITY GOAL",
            status,
            item.ExpiresAt is { } deadline
                ? deadline.ToLocalTime().ToString("f", CultureInfo.CurrentCulture)
                : "No deadline supplied",
            FormatCommunityGoalTimeRemaining(
                item.ExpiresAt,
                item.IsComplete,
                currentTime),
            progress,
            target is not null ? $"{progress:0.00}% complete" : "Target not supplied",
            target is { } total
                ? $"{currentTotal:N0} / {total:N0}"
                : $"{currentTotal:N0} contributed",
            target is { } maximum
                ? $"{Math.Max(0, maximum - currentTotal):N0} remaining"
                : string.Empty,
            hasPlayerContributionData
                ? playerContribution > 0
                    ? $"{playerContribution:N0} contributed"
                    : "Signed up · no contribution recorded"
                : "Personal progress not supplied by Frontier or local journals",
            standing,
            hasContributorData
                ? $"{contributors:N0} commanders"
                : "Contributor count not supplied",
            string.IsNullOrWhiteSpace(tier)
                ? "Tier not supplied"
                : tier,
            !string.IsNullOrWhiteSpace(briefing),
            !string.IsNullOrWhiteSpace(objective),
            !string.IsNullOrWhiteSpace(reward),
            !string.IsNullOrWhiteSpace(system)
                || !string.IsNullOrWhiteSpace(market),
            target is not null,
            sourceStatus,
            hasInaraData,
            dataPoints,
            PaneStates.GetCommunityGoalData(item.Id, item.Title));
    }

    public async Task SetCommanderContextAsync(
        string? frontierId,
        string? commanderName,
        bool refreshIfOpen,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var normalizedId = NormalizeFrontierId(frontierId);
        var normalizedName = string.IsNullOrWhiteSpace(commanderName)
            ? null
            : commanderName.Trim();
        var detectedIdentityChanged = !string.Equals(
            detectedFrontierId,
            normalizedId,
            StringComparison.OrdinalIgnoreCase);

        detectedFrontierId = normalizedId;
        detectedCommanderName = normalizedName;
        OnPropertyChanged(nameof(DetectedCommanderDescription));
        OnPropertyChanged(nameof(CommanderSelectionDescription));
        if (detectedIdentityChanged)
        {
            journalReputationCommanderName = normalizedName;
            journalReputation = [];
            journalReputationUpdatedAt = null;
            journalCommunityGoalCommanderName = normalizedName;
            journalCommunityGoals = [];
            journalCommunityGoalHistory = [];
            journalCommunityGoalsUpdatedAt = null;
            journalCommunityGoalHistoryError = string.Empty;
            UpdateLocalInventory(null, null, isSuppressed: false);
            await LoadJournalCommunityGoalHistoryAsync(
                normalizedId,
                normalizedName,
                cancellationToken);
        }

        await TryRefreshCommanderSelectionOptionsAsync(cancellationToken);
        await ActivateFrontierCommanderAsync(
            manuallySelectedFrontierId ?? detectedFrontierId,
            manuallySelectedFrontierId is null
                ? detectedCommanderName
                : manuallySelectedCommanderName,
            refreshIfOpen,
            cancellationToken);
    }

    private async Task LoadJournalCommunityGoalHistoryAsync(
        string? frontierId,
        string? commanderName,
        CancellationToken cancellationToken)
    {
        if (communityGoalHistoryReader is null || frontierId is null)
        {
            return;
        }

        try
        {
            var result = await communityGoalHistoryReader
                .ReadAsync(frontierId, cancellationToken);
            if (!string.Equals(
                    detectedFrontierId,
                    frontierId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            journalCommunityGoalCommanderName = commanderName;
            journalCommunityGoalHistory = result.Goals;
            journalCommunityGoalHistoryError = result.Warning;
            RaiseCommunityGoalProperties();
            OnPropertyChanged(nameof(CommunityGoalsError));
            OnPropertyChanged(nameof(HasCommunityGoalsError));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            journalCommunityGoalHistoryError =
                "Local Community Goal history could not be read: "
                + exception.Message;
            OnPropertyChanged(nameof(CommunityGoalsError));
            OnPropertyChanged(nameof(HasCommunityGoalsError));
        }
    }

    private async Task ActivateFrontierCommanderAsync(
        string? frontierId,
        string? commanderName,
        bool refreshIfOpen,
        CancellationToken cancellationToken = default)
    {
        var normalizedId = NormalizeFrontierId(frontierId);
        var normalizedName = string.IsNullOrWhiteSpace(commanderName)
            ? null
            : commanderName.Trim();
        var identityChanged = !string.Equals(
            activeFrontierId,
            normalizedId,
            StringComparison.OrdinalIgnoreCase);

        activeFrontierId = normalizedId;
        activeCommanderName = normalizedName;
        accountService.SetActiveCommander(normalizedId, normalizedName);
        OnPropertyChanged(nameof(ActiveCommanderDescription));
        OnPropertyChanged(nameof(CommanderSelectionDescription));
        ApplyLocalInventorySelection();
        OnPropertyChanged(nameof(CommanderReputation));
        if (!identityChanged)
        {
            RaiseCommandStates();
            return;
        }

        Interlocked.Increment(ref commanderContextVersion);
        var previousConnection = connectionCancellation;
        connectionCancellation = null;
        previousConnection?.Cancel();
        previousConnection?.Dispose();
        IsBusy = false;
        IsConnecting = false;
        Snapshot = null;
        IsLinked = false;
        initialized = false;
        StatusMessage = string.Empty;
        RaiseCommandStates();

        if (refreshIfOpen && normalizedId is not null)
        {
            await OpenAsync(cancellationToken);
        }
    }

    public async Task SelectCommanderAsync(
        FrontierCommanderSelectionOption selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ThrowIfDisposed();
        if (!Equals(SelectedCommanderOption, selection))
        {
            isUpdatingCommanderSelection = true;
            try
            {
                SelectedCommanderOption = selection;
            }
            finally
            {
                isUpdatingCommanderSelection = false;
            }
        }

        manuallySelectedFrontierId = selection.IsAutomatic
            ? null
            : selection.FrontierId;
        manuallySelectedCommanderName = selection.IsAutomatic
            ? null
            : selection.CommanderName;
        OnPropertyChanged(nameof(IsAutomaticCommanderSelection));
        OnPropertyChanged(nameof(CommanderSelectionDescription));
        ApplyLocalInventorySelection();
        try
        {
            await ActivateFrontierCommanderAsync(
                selection.IsAutomatic ? detectedFrontierId : selection.FrontierId,
                selection.IsAutomatic ? detectedCommanderName : selection.CommanderName,
                refreshIfOpen: true,
                cancellationToken);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusMessage = exception.Message;
        }
    }

    private async Task TryRefreshCommanderSelectionOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var linkedCommanders = await accountService
                .GetLinkedCommandersAsync(cancellationToken);
            var automatic = FrontierCommanderSelectionOption.Automatic(
                detectedFrontierId,
                detectedCommanderName);
            var allLinkedOptions = linkedCommanders
                .Select(FrontierCommanderSelectionOption.Linked)
                .ToArray();
            var selected = manuallySelectedFrontierId is null
                ? automatic
                : allLinkedOptions.FirstOrDefault(option => string.Equals(
                    option.FrontierId,
                    manuallySelectedFrontierId,
                    StringComparison.OrdinalIgnoreCase));
            if (selected is null)
            {
                manuallySelectedFrontierId = null;
                manuallySelectedCommanderName = null;
                selected = automatic;
                OnPropertyChanged(nameof(IsAutomaticCommanderSelection));
            }
            else if (!selected.IsAutomatic)
            {
                manuallySelectedCommanderName = selected.CommanderName;
            }

            var linkedOptions = allLinkedOptions
                .Where(option => !string.Equals(
                        option.FrontierId,
                        detectedFrontierId,
                        StringComparison.OrdinalIgnoreCase)
                    || !selected.IsAutomatic
                    && string.Equals(
                        option.FrontierId,
                        selected.FrontierId,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
            CommanderSelectionOptions = [automatic, .. linkedOptions];
            isUpdatingCommanderSelection = true;
            try
            {
                SelectedCommanderOption = selected;
            }
            finally
            {
                isUpdatingCommanderSelection = false;
            }

            OnPropertyChanged(nameof(CommanderSelectionDescription));
            ApplyLocalInventorySelection();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusMessage = exception.Message;
        }
    }

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (IsBusy)
        {
            return;
        }

        var contextVersion = Interlocked.Read(ref commanderContextVersion);
        try
        {
            IsBusy = true;
            var state = await accountService.GetStateAsync(cancellationToken);
            if (contextVersion != Interlocked.Read(ref commanderContextVersion))
            {
                return;
            }

            IsLinked = state.IsLinked;
            if (Snapshot is null
                || state.Snapshot is { } cached
                && cached.FetchedAt >= Snapshot.FetchedAt)
            {
                Snapshot = state.Snapshot;
            }
            if (!state.IsLinked)
            {
                StatusMessage = string.Empty;
                initialized = true;
                return;
            }

            var shouldRefresh = state.Snapshot is null
                || now().ToUniversalTime() - state.Snapshot.FetchedAt
                    >= AutomaticRefreshAge;
            var lastRequest = state.LastCapiAttemptAt is { } attempt
                && (state.LastCapiRefreshAt is null
                    || attempt > state.LastCapiRefreshAt)
                    ? attempt
                    : state.LastCapiRefreshAt;
            var cooldownActive = lastRequest is { } priorRequest
                && now().ToUniversalTime() - priorRequest < TimeSpan.FromMinutes(1);
            if (!initialized && shouldRefresh && !cooldownActive)
            {
                StatusMessage = "Refreshing commander data from Frontier...";
                var refreshed = await accountService.RefreshAsync(cancellationToken);
                if (contextVersion != Interlocked.Read(ref commanderContextVersion))
                {
                    return;
                }

                Snapshot = refreshed;
            }

            StatusMessage = string.Empty;
            initialized = true;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (contextVersion == Interlocked.Read(ref commanderContextVersion))
            {
                StatusMessage = exception.Message;
                initialized = true;
            }
        }
        finally
        {
            if (contextVersion == Interlocked.Read(ref commanderContextVersion))
            {
                IsBusy = false;
            }
        }
    }

    private async Task ConnectAsync()
    {
        var contextVersion = Interlocked.Read(ref commanderContextVersion);
        connectionCancellation?.Dispose();
        connectionCancellation = new CancellationTokenSource();
        try
        {
            IsBusy = true;
            IsConnecting = true;
            StatusMessage =
                "Complete authorization in your browser. SrvSurvey will update this page when Frontier returns.";
            var connected = await accountService.ConnectAsync(connectionCancellation.Token);
            if (contextVersion != Interlocked.Read(ref commanderContextVersion))
            {
                return;
            }

            Snapshot = connected;
            IsLinked = true;
            initialized = true;
            StatusMessage = "Frontier account connected.";
            await TryRefreshCommanderSelectionOptionsAsync();
        }
        catch (OperationCanceledException)
        {
            if (contextVersion == Interlocked.Read(ref commanderContextVersion))
            {
                StatusMessage = "Frontier authorization was cancelled.";
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (contextVersion == Interlocked.Read(ref commanderContextVersion))
            {
                StatusMessage = exception.Message;
                await TryReloadStateAsync();
            }
        }
        finally
        {
            if (contextVersion == Interlocked.Read(ref commanderContextVersion))
            {
                IsConnecting = false;
                IsBusy = false;
                connectionCancellation?.Dispose();
                connectionCancellation = null;
            }
        }
    }

    private async Task CancelConnectionAsync()
    {
        connectionCancellation?.Cancel();
        await accountService.CancelConnectionAsync();
        StatusMessage = "Frontier authorization was cancelled.";
    }

    private async Task RefreshAsync()
    {
        var contextVersion = Interlocked.Read(ref commanderContextVersion);
        try
        {
            IsBusy = true;
            StatusMessage = "Refreshing commander data from Frontier...";
            var refreshed = await accountService.RefreshAsync();
            if (contextVersion != Interlocked.Read(ref commanderContextVersion))
            {
                return;
            }

            Snapshot = refreshed;
            StatusMessage = "Commander data refreshed.";
            await TryRefreshCommanderSelectionOptionsAsync();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (contextVersion == Interlocked.Read(ref commanderContextVersion))
            {
                StatusMessage = exception.Message;
                await TryReloadStateAsync();
            }
        }
        finally
        {
            if (contextVersion == Interlocked.Read(ref commanderContextVersion))
            {
                IsBusy = false;
            }
        }
    }

    private async Task UnlinkAsync()
    {
        var contextVersion = Interlocked.Read(ref commanderContextVersion);
        var wasManuallySelected = manuallySelectedFrontierId is not null;
        try
        {
            IsBusy = true;
            await accountService.UnlinkAsync();
            if (contextVersion != Interlocked.Read(ref commanderContextVersion))
            {
                return;
            }

            Snapshot = null;
            IsLinked = false;
            initialized = true;
            StatusMessage = string.Empty;
            await TryRefreshCommanderSelectionOptionsAsync();
            if (wasManuallySelected && manuallySelectedFrontierId is null)
            {
                IsBusy = false;
                await ActivateFrontierCommanderAsync(
                    detectedFrontierId,
                    detectedCommanderName,
                    refreshIfOpen: true);
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (contextVersion == Interlocked.Read(ref commanderContextVersion))
            {
                StatusMessage = exception.Message;
            }
        }
        finally
        {
            if (contextVersion == Interlocked.Read(ref commanderContextVersion))
            {
                IsBusy = false;
            }
        }
    }

    private FrontierShipModuleRowViewModel CreateModuleRow(
        FrontierShipModuleSnapshot item)
    {
        var internalName = ResolveInternalName(item);
        var group = ModuleGroup(item.Slot);
        var blueprint = FriendlyGameIdentifier(item.Blueprint);
        return new FrontierShipModuleRowViewModel(
            FriendlyModuleSlot(item.Slot),
            FriendlyModuleName(item.Name, internalName),
            FriendlyDescription(item.Description),
            FormatCredits(item.Value),
            FormatPercent(item.Health),
            item.IsPowered
                ? item.Priority is { } priority
                    ? $"Powered · priority {priority}"
                    : "Powered"
                : "Powered off",
            blueprint,
            item.BlueprintLevel is { } level ? $"Grade {level}" : string.Empty,
            string.Join(", ", item.ExperimentalEffects.Select(FriendlyGameIdentifier)),
            item.Engineer,
            group,
            ModuleGlyph(item.Name, group),
            ModuleClassRating(internalName, item.Slot, group));
    }

    private string ResolveInternalName(FrontierShipModuleSnapshot item)
    {
        if (!string.IsNullOrWhiteSpace(item.InternalName))
        {
            return item.InternalName;
        }

        const string prefix = "ship.modules.";
        const string suffix = ".module.name";
        var slotKey = NormalizeLookupKey(item.Slot);
        return Snapshot?.CurrentShip?.DataPoints?
            .FirstOrDefault(point =>
                point.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && point.Path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && NormalizeLookupKey(point.Path[prefix.Length..^suffix.Length]) == slotKey)
            ?.Value ?? string.Empty;
    }

    private static bool IsLiverySlot(string slot)
    {
        var key = NormalizeLookupKey(slot);
        return key.StartsWith("decal", StringComparison.Ordinal)
            || key is "enginecolour" or "paintjob" or "vesselvoice" or "weaponcolour"
            || key.StartsWith("shipkit", StringComparison.Ordinal)
            || key.StartsWith("shipname", StringComparison.Ordinal);
    }

    private static string FriendlyLiverySlot(string slot)
    {
        var key = NormalizeLookupKey(slot);
        if (key.StartsWith("decal", StringComparison.Ordinal))
        {
            return "Decal";
        }

        if (key.StartsWith("shipname", StringComparison.Ordinal))
        {
            return "Nameplate";
        }

        if (key.StartsWith("shipkit", StringComparison.Ordinal))
        {
            return slot.Replace("Ship Kit ", string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return key switch
        {
            "enginecolour" => "Engine colour",
            "paintjob" => "Paint job",
            "vesselvoice" => "COVAS",
            "weaponcolour" => "Weapon colour",
            _ => HumanizeIdentifier(slot),
        };
    }

    private static string FriendlyLiveryName(
        FrontierShipModuleSnapshot item,
        string internalName)
    {
        if (!IsLocalizationToken(item.Name))
        {
            return item.Name.Trim();
        }

        var key = NormalizeLookupKey(item.Slot);
        var value = internalName;
        if (key.StartsWith("decal", StringComparison.Ordinal)
            && value.Contains("SquadronLogo", StringComparison.OrdinalIgnoreCase))
        {
            return "Squadron Logo";
        }

        value = key switch
        {
            "enginecolour" => RemovePrefix(value, "EngineCustomisation_"),
            "weaponcolour" => RemovePrefix(value, "WeaponCustomisation_"),
            "paintjob" => RemovePrefix(value, "PaintJob_"),
            "vesselvoice" => RemovePrefix(value, "VoicePack_"),
            _ when key.StartsWith("decal", StringComparison.Ordinal) =>
                RemovePrefix(value, "Decal_"),
            _ when key.StartsWith("shipname", StringComparison.Ordinal) =>
                RemovePrefix(value, "Nameplate_"),
            _ => value,
        };

        return FirstNonEmpty(FriendlyGameIdentifier(value), FriendlyLiverySlot(item.Slot));
    }

    private static string FriendlyModuleName(string localizedName, string internalName)
    {
        if (!IsLocalizationToken(localizedName))
        {
            return localizedName.Trim();
        }

        var key = internalName.ToLowerInvariant();
        if (key.Contains("int_engine_", StringComparison.Ordinal))
        {
            return "Thrusters";
        }

        if (key.Contains("int_hyperdrive", StringComparison.Ordinal))
        {
            return "Frame Shift Drive";
        }

        if (key.Contains("int_powerplant", StringComparison.Ordinal))
        {
            return "Power Plant";
        }

        if (key.Contains("int_powerdistributor", StringComparison.Ordinal))
        {
            return "Power Distributor";
        }

        if (key.Contains("int_lifesupport", StringComparison.Ordinal))
        {
            return "Life Support";
        }

        if (key.Contains("int_cargorack", StringComparison.Ordinal))
        {
            return "Cargo Rack";
        }

        if (key.Contains("int_shieldgenerator", StringComparison.Ordinal))
        {
            return "Shield Generator";
        }

        var cleaned = Regex.Replace(
            internalName,
            @"^(int|hpt)_|_size\d+|_class\d+|_(fixed|gimbal|turret)$",
            string.Empty,
            RegexOptions.IgnoreCase);
        return FirstNonEmpty(
            FriendlyGameIdentifier(cleaned),
            FriendlyGameIdentifier(localizedName),
            "Unknown module");
    }

    private static string FriendlyDescription(string description)
    {
        return IsLocalizationToken(description) ? string.Empty : description.Trim();
    }

    private static bool IsLocalizationToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim('$', ';', ' ');
        return trimmed.EndsWith("_Name", StringComparison.OrdinalIgnoreCase)
            || trimmed.EndsWith("_Info", StringComparison.OrdinalIgnoreCase)
            || trimmed.EndsWith("_Description", StringComparison.OrdinalIgnoreCase);
    }

    private static string FriendlyGameIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value.Trim('$', ';', ' ');
        cleaned = Regex.Replace(
            cleaned,
            @"_(Name|Info|Description)$",
            string.Empty,
            RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"([a-z])([A-Z])", "$1 $2");
        cleaned = Regex.Replace(cleaned, @"([A-Za-z])([0-9])", "$1 $2");
        cleaned = Regex.Replace(cleaned, @"([0-9])([A-Za-z])", "$1 $2");
        cleaned = HumanizeIdentifier(cleaned);
        return cleaned
            .Replace("Fsd", "FSD", StringComparison.Ordinal)
            .Replace("Covas", "COVAS", StringComparison.Ordinal)
            .Replace(" Mk Ii", " Mk II", StringComparison.Ordinal)
            .Replace("Sco", "SCO", StringComparison.Ordinal);
    }

    private static string RemovePrefix(string value, string prefix)
    {
        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? value[prefix.Length..]
            : value;
    }

    private static string NormalizeLookupKey(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static string ModuleGroup(string slot)
    {
        var key = NormalizeLookupKey(slot);
        if (key.Contains("tinyhardpoint", StringComparison.Ordinal))
        {
            return "Utility Mounts";
        }

        if (key.Contains("hardpoint", StringComparison.Ordinal))
        {
            return "Hardpoints";
        }

        if (key.StartsWith("military", StringComparison.Ordinal))
        {
            return "Military Internal";
        }

        if (key is "armour" or "powerplant" or "mainengines" or "frameshiftdrive"
            or "lifesupport" or "powerdistributor" or "radar" or "fueltank")
        {
            return "Core Internal";
        }

        if (key.StartsWith("slot", StringComparison.Ordinal)
            || key.StartsWith("cargo", StringComparison.Ordinal)
            || key.Contains("planetaryapproachsuite", StringComparison.Ordinal))
        {
            return "Optional Internal";
        }

        return "Ship Systems";
    }

    private static int ModuleGroupOrder(string group) => group switch
    {
        "Hardpoints" => 0,
        "Utility Mounts" => 1,
        "Core Internal" => 2,
        "Military Internal" => 3,
        "Optional Internal" => 4,
        _ => 5,
    };

    private static int ModuleSlotOrder(string slot)
    {
        var key = NormalizeLookupKey(slot);
        return key switch
        {
            "armour" => 0,
            "powerplant" => 1,
            "mainengines" => 2,
            "frameshiftdrive" => 3,
            "lifesupport" => 4,
            "powerdistributor" => 5,
            "radar" => 6,
            "fueltank" => 7,
            _ => 20,
        };
    }

    private static string ModuleGroupGlyph(string group) => group switch
    {
        "Hardpoints" => "◎",
        "Utility Mounts" => "◇",
        "Core Internal" => "⬢",
        "Military Internal" => "◆",
        "Optional Internal" => "▦",
        _ => "◈",
    };

    private static string ModuleGlyph(string name, string group)
    {
        var value = name.ToLowerInvariant();
        if (value.Contains("laser", StringComparison.Ordinal)) return "✦";
        if (value.Contains("missile", StringComparison.Ordinal)
            || value.Contains("torpedo", StringComparison.Ordinal)) return "➤";
        if (value.Contains("cannon", StringComparison.Ordinal)
            || value.Contains("gun", StringComparison.Ordinal)) return "◎";
        if (value.Contains("shield", StringComparison.Ordinal)) return "⬡";
        if (value.Contains("thruster", StringComparison.Ordinal)
            || value.Contains("engine", StringComparison.Ordinal)) return "»";
        if (value.Contains("frame shift", StringComparison.Ordinal)) return "✦";
        if (value.Contains("power", StringComparison.Ordinal)) return "⚡";
        if (value.Contains("sensor", StringComparison.Ordinal)
            || value.Contains("scanner", StringComparison.Ordinal)
            || value.Contains("radar", StringComparison.Ordinal)) return "⌁";
        if (value.Contains("cargo", StringComparison.Ordinal)) return "▦";
        if (value.Contains("fuel", StringComparison.Ordinal)) return "◒";
        if (value.Contains("armour", StringComparison.Ordinal)
            || value.Contains("hull", StringComparison.Ordinal)
            || value.Contains("reinforcement", StringComparison.Ordinal)) return "◆";
        return ModuleGroupGlyph(group);
    }

    private static string ModuleClassRating(
        string internalName,
        string slot,
        string group)
    {
        var match = Regex.Match(
            internalName,
            @"size(?<size>\d+)_class(?<rating>\d+)",
            RegexOptions.IgnoreCase);
        if (match.Success
            && int.TryParse(match.Groups["rating"].Value, out var rating))
        {
            var letter = rating switch
            {
                1 => "E",
                2 => "D",
                3 => "C",
                4 => "B",
                5 => "A",
                _ => "",
            };
            return match.Groups["size"].Value + letter;
        }

        var key = (internalName + " " + slot).ToLowerInvariant();
        if (key.Contains("huge", StringComparison.Ordinal)) return "H";
        if (key.Contains("large", StringComparison.Ordinal)) return "L";
        if (key.Contains("medium", StringComparison.Ordinal)) return "M";
        if (key.Contains("small", StringComparison.Ordinal)) return "S";
        return group == "Utility Mounts" ? "U" : string.Empty;
    }

    private static string FriendlyModuleSlot(string slot)
    {
        var optional = Regex.Match(
            slot,
            @"^Slot(?<number>\d+)\s+Size(?<size>\d+)$",
            RegexOptions.IgnoreCase);
        if (optional.Success)
        {
            return $"Optional {optional.Groups["number"].Value} · Class {optional.Groups["size"].Value}";
        }

        var utility = Regex.Match(
            slot,
            @"^Tiny\s*Hardpoint(?<number>\d+)$",
            RegexOptions.IgnoreCase);
        if (utility.Success)
        {
            return $"Utility mount {utility.Groups["number"].Value}";
        }

        var cargo = Regex.Match(slot, @"^Cargo(?<number>\d+)$", RegexOptions.IgnoreCase);
        if (cargo.Success)
        {
            return $"Cargo slot {cargo.Groups["number"].Value}";
        }

        return HumanizeIdentifier(slot);
    }

    private static int LiveryOrder(string category) => category switch
    {
        "Paint job" => 0,
        "Ship kit" => 1,
        "Bumper" => 2,
        "Spoiler" => 3,
        "Wings" => 4,
        "Tail" => 5,
        "Nameplate" => 6,
        "Decal" => 7,
        "Engine colour" => 8,
        "Weapon colour" => 9,
        "COVAS" => 10,
        _ => 20,
    };

    private static int LockerCategoryOrder(string category) =>
        category.ToLowerInvariant() switch
        {
            "items" => 0,
            "components" => 1,
            "consumables" => 2,
            "data" => 3,
            _ => 10,
        };

    private void RebuildCurrentShipModuleGroups()
    {
        var expansionState = currentShipModuleGroups.ToDictionary(
            group => group.Name,
            group => group.IsExpanded,
            StringComparer.CurrentCultureIgnoreCase);
        var groups = CurrentShipModules
            .GroupBy(item => item.Group, StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(group => ModuleGroupOrder(group.Key))
            .Select(group => new FrontierShipModuleGroupViewModel(
                group.Key,
                ModuleGroupGlyph(group.Key),
                group.ToArray()))
            .ToArray();

        foreach (var group in groups)
        {
            if (expansionState.TryGetValue(group.Name, out var isExpanded))
            {
                group.IsExpanded = isExpanded;
            }
        }

        currentShipModuleGroups = groups;
    }

    private void RebuildCurrentShipLockerGroups()
    {
        var expansionState = currentShipLockerGroups.ToDictionary(
            group => group.Category,
            group => group.IsExpanded,
            StringComparer.CurrentCultureIgnoreCase);
        var groups = CurrentShipLocker
            .GroupBy(item => item.Category, StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(group => LockerCategoryOrder(group.Key))
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new FrontierLockerCategoryViewModel(
                group.Key,
                group.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray()))
            .ToArray();

        foreach (var group in groups)
        {
            if (expansionState.TryGetValue(group.Category, out var isExpanded))
            {
                group.IsExpanded = isExpanded;
            }
        }

        currentShipLockerGroups = groups;
    }

    private void RaiseSnapshotProperties()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    private void RaiseCommunityGoalProperties()
    {
        OnPropertyChanged(nameof(CommunityGoals));
        OnPropertyChanged(nameof(HasCommunityGoals));
        OnPropertyChanged(nameof(CommunityGoalsMessage));
    }

    private IReadOnlyList<FrontierCommunityGoalSnapshot> EffectiveCommunityGoals()
    {
        var accountGoals = Snapshot?.CommunityGoals ?? [];
        var journalCandidates = journalCommunityGoalHistory.Count > 0
            ? journalCommunityGoalHistory
            : journalCommunityGoals;
        if (journalCandidates.Count == 0
            || Snapshot is null
            || manuallySelectedFrontierId is not null
                && !string.Equals(
                    manuallySelectedFrontierId,
                    detectedFrontierId,
                    StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                Snapshot.CommanderName,
                journalCommunityGoalCommanderName,
                StringComparison.OrdinalIgnoreCase))
        {
            return FrontierCommunityGoalOrdering.Order(accountGoals);
        }

        var matchedJournalGoals = new HashSet<int>();
        var result = new List<FrontierCommunityGoalSnapshot>(
            accountGoals.Count + journalCommunityGoals.Count);
        foreach (var accountGoal in accountGoals)
        {
            var matchIndex = FindCommunityGoalMatch(
                accountGoal,
                journalCandidates,
                matchedJournalGoals);
            if (matchIndex is null)
            {
                result.Add(accountGoal);
                continue;
            }

            matchedJournalGoals.Add(matchIndex.Value);
            var journalGoal = journalCandidates[matchIndex.Value];
            var journalUpdatedAt = JournalCommunityGoalTimestamp(journalGoal)
                ?? journalCommunityGoalsUpdatedAt;
            var journalIsCurrent = Snapshot.CommunityGoalsFetchedAt is null
                || journalUpdatedAt is { } timestamp
                    && timestamp >= Snapshot.CommunityGoalsFetchedAt;
            result.Add(MergeJournalCommunityGoal(
                accountGoal,
                journalGoal,
                journalIsCurrent,
                journalUpdatedAt));
        }

        foreach (var currentGoal in journalCommunityGoals)
        {
            if (FindCommunityGoalMatch(
                    currentGoal,
                    result,
                    new HashSet<int>()) is null)
            {
                result.Add(currentGoal);
            }
        }

        return FrontierCommunityGoalOrdering.Order(result);
    }

    private static IReadOnlyList<FrontierCommunityGoalSnapshot>
        StampJournalCommunityGoals(
            IReadOnlyList<FrontierCommunityGoalSnapshot> goals,
            DateTimeOffset? timestamp)
    {
        if (timestamp is null)
        {
            return goals;
        }

        return goals.Select(goal =>
        {
            var data = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var point in goal.DataPoints ?? [])
            {
                data[point.Path] = point.Value;
            }

            data["journal.communityGoalTimestamp"] = timestamp.Value.ToString(
                "O",
                CultureInfo.InvariantCulture);
            return goal with
            {
                DataPoints = data
                    .Select(pair => new FrontierDataPointSnapshot(
                        pair.Key,
                        pair.Value))
                    .ToArray(),
            };
        }).ToArray();
    }

    private static IReadOnlyList<FrontierCommunityGoalSnapshot>
        MergeJournalCommunityGoalHistory(
            IReadOnlyList<FrontierCommunityGoalSnapshot> existing,
            IReadOnlyList<FrontierCommunityGoalSnapshot> incoming)
    {
        var history = new Dictionary<string, FrontierCommunityGoalSnapshot>(
            StringComparer.Ordinal);
        foreach (var goal in existing.Concat(incoming))
        {
            var key = CommunityGoalHistoryKey(goal);
            if (!history.TryGetValue(key, out var prior)
                || (JournalCommunityGoalTimestamp(goal)
                        ?? DateTimeOffset.MinValue)
                    >= (JournalCommunityGoalTimestamp(prior)
                        ?? DateTimeOffset.MinValue))
            {
                history[key] = goal;
            }
        }

        return history.Values
            .OrderByDescending(
                goal => JournalCommunityGoalTimestamp(goal)
                    ?? DateTimeOffset.MinValue)
            .Take(250)
            .ToArray();
    }

    private static DateTimeOffset? JournalCommunityGoalTimestamp(
        FrontierCommunityGoalSnapshot goal) =>
        CommunityGoalDataDateTimeOffset(
            goal.DataPoints ?? [],
            "journal.communityGoalTimestamp");

    private static string CommunityGoalHistoryKey(
        FrontierCommunityGoalSnapshot goal) =>
        goal.Id is { } id
            ? $"id:{id.ToString(CultureInfo.InvariantCulture)}"
            : "goal:"
                + NormalizeLookupKey(goal.Title)
                + ":"
                + (goal.ExpiresAt?.ToUniversalTime().ToString(
                    "O",
                    CultureInfo.InvariantCulture) ?? string.Empty);

    private static bool IsInaraOnlyGoal(FrontierCommunityGoalSnapshot goal) =>
        goal.DataPoints?.Any(point => string.Equals(
                point.Path,
                "inara.sourceOnly",
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                point.Value,
                "true",
                StringComparison.OrdinalIgnoreCase)) == true;

    private static int? FindCommunityGoalMatch(
        FrontierCommunityGoalSnapshot goal,
        IReadOnlyList<FrontierCommunityGoalSnapshot> candidates,
        IReadOnlySet<int> alreadyMatched)
    {
        if (goal.Id is { } id)
        {
            var idMatch = candidates
                .Select((candidate, index) => (candidate, index))
                .FirstOrDefault(pair => !alreadyMatched.Contains(pair.index)
                    && pair.candidate.Id == id);
            if (idMatch.candidate is not null)
            {
                return idMatch.index;
            }
        }

        var matches = candidates
            .Select((candidate, index) => (candidate, index))
            .Where(pair => !alreadyMatched.Contains(pair.index)
                && CommunityGoalTextMatches(goal.Title, pair.candidate.Title)
                && CommunityGoalOptionalTextMatches(goal.System, pair.candidate.System)
                && CommunityGoalOptionalTextMatches(goal.Market, pair.candidate.Market)
                && CommunityGoalExpiryMatches(goal.ExpiresAt, pair.candidate.ExpiresAt))
            .ToArray();
        return matches.Length == 1 ? matches[0].index : null;
    }

    private static bool CommunityGoalTextMatches(string first, string second) =>
        NormalizeLookupKey(first) == NormalizeLookupKey(second);

    private static bool CommunityGoalOptionalTextMatches(
        string first,
        string second) =>
        string.IsNullOrWhiteSpace(first)
        || string.IsNullOrWhiteSpace(second)
        || CommunityGoalTextMatches(first, second);

    private static bool CommunityGoalExpiryMatches(
        DateTimeOffset? first,
        DateTimeOffset? second) =>
        first is null
        || second is null
        || (first.Value - second.Value).Duration() <= TimeSpan.FromMinutes(10);

    private static FrontierCommunityGoalSnapshot MergeJournalCommunityGoal(
        FrontierCommunityGoalSnapshot account,
        FrontierCommunityGoalSnapshot journal,
        bool journalIsCurrent,
        DateTimeOffset? journalUpdatedAt)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in account.DataPoints ?? [])
        {
            data[point.Path] = point.Value;
        }

        foreach (var point in journal.DataPoints ?? [])
        {
            if (journalIsCurrent || !data.ContainsKey(point.Path))
            {
                data[point.Path] = point.Value;
            }
        }

        if (journalUpdatedAt is { } timestamp)
        {
            data["journal.communityGoalTimestamp"] = timestamp.ToString(
                "O",
                CultureInfo.InvariantCulture);
        }

        return account with
        {
            Id = account.Id ?? journal.Id,
            Description = FirstNonEmpty(account.Description, journal.Description),
            Objective = FirstNonEmpty(account.Objective, journal.Objective),
            Reward = FirstNonEmpty(account.Reward, journal.Reward),
            System = FirstNonEmpty(account.System, journal.System),
            Market = FirstNonEmpty(account.Market, journal.Market),
            ExpiresAt = account.ExpiresAt ?? journal.ExpiresAt,
            IsComplete = account.IsComplete || journalIsCurrent && journal.IsComplete,
            CurrentTotal = journalIsCurrent || account.CurrentTotal == 0
                ? journal.CurrentTotal
                : account.CurrentTotal,
            TargetTotal = journalIsCurrent || account.TargetTotal is null
                ? journal.TargetTotal ?? account.TargetTotal
                : account.TargetTotal,
            PlayerContribution = journal.HasPlayerContributionData
                && (journalIsCurrent || !account.HasPlayerContributionData)
                    ? journal.PlayerContribution
                    : account.PlayerContribution,
            Contributors = journal.HasContributorData
                && (journalIsCurrent || !account.HasContributorData)
                    ? journal.Contributors
                    : account.Contributors,
            TierReached = journalIsCurrent && !string.IsNullOrWhiteSpace(journal.TierReached)
                ? journal.TierReached
                : FirstNonEmpty(account.TierReached, journal.TierReached),
            PlayerPercentile = journal.HasPlayerContributionData
                && (journalIsCurrent || !account.HasPlayerContributionData)
                    ? journal.PlayerPercentile
                    : account.PlayerPercentile,
            Bonus = journal.HasPlayerContributionData
                && (journalIsCurrent || !account.HasPlayerContributionData)
                    ? journal.Bonus
                    : account.Bonus,
            TopRankSize = journal.HasPlayerContributionData
                && (journalIsCurrent || !account.HasPlayerContributionData)
                    ? journal.TopRankSize
                    : account.TopRankSize,
            PlayerInTopRank = journal.HasPlayerContributionData
                && (journalIsCurrent || !account.HasPlayerContributionData)
                    ? journal.PlayerInTopRank
                    : account.PlayerInTopRank,
            ActivityType = FirstNonEmpty(account.ActivityType, journal.ActivityType),
            HasPlayerContributionData = account.HasPlayerContributionData
                || journal.HasPlayerContributionData,
            HasContributorData = account.HasContributorData
                || journal.HasContributorData,
            DataPoints = data
                .Select(pair => new FrontierDataPointSnapshot(pair.Key, pair.Value))
                .ToArray(),
        };
    }

    private IReadOnlyList<FrontierReputationSnapshot> EffectiveCommanderReputation()
    {
        var capiReputation = Snapshot?.CommanderReputation is { Count: > 0 } account
            ? account
            : Carrier?.Reputation ?? [];
        if (journalReputation.Count == 0
            || Snapshot is null
            || manuallySelectedFrontierId is not null
                && !string.Equals(
                    manuallySelectedFrontierId,
                    detectedFrontierId,
                    StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                Snapshot.CommanderName,
                journalReputationCommanderName,
                StringComparison.OrdinalIgnoreCase))
        {
            return capiReputation;
        }

        var capiUpdatedAt = Snapshot.CommanderReputationFetchedAt
            ?? Snapshot.CarrierFetchedAt;
        var journalIsCurrent = capiReputation.Count == 0
            || journalReputationUpdatedAt is { } journalUpdatedAt
                && (capiUpdatedAt is null || journalUpdatedAt >= capiUpdatedAt);
        if (!journalIsCurrent)
        {
            return capiReputation;
        }

        return capiReputation
            .Concat(journalReputation)
            .GroupBy(item => item.Faction, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(item => item.Faction, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static double? ReadJournalNumber(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && double.TryParse(
                value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number)
            ? number
            : null;
    }

    private static string FormatCredits(long value)
    {
        return value.ToString("N0", CultureInfo.CurrentCulture) + " CR";
    }

    private static string FormatPercent(double? value)
    {
        return value is { } number
            ? $"{NormalizePercentForDisplay(number):N0}%"
            : "Unavailable";
    }

    private static double NormalizePercentForDisplay(double value) =>
        value > 100 ? value / 10_000 : value;

    private static string BracketName(int value)
    {
        return value switch
        {
            <= 0 => "none",
            1 => "low",
            2 => "medium",
            _ => "high",
        };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?.Trim() ?? string.Empty;
    }

    private static string HumanizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var result = new List<char>(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (index > 0
                && char.IsUpper(current)
                && !char.IsWhiteSpace(value[index - 1])
                && !char.IsUpper(value[index - 1]))
            {
                result.Add(' ');
            }

            result.Add(current is '_' or '-' ? ' ' : current);
        }

        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
            new string(result.ToArray())
                .Trim()
                .ToLower(CultureInfo.CurrentCulture));
    }

    private static string RankIcon(string key, int level)
    {
        var rank = Math.Clamp(level + 1, 1, 9);
        return key.ToLowerInvariant() switch
        {
            "combat" => RankAsset("combat", rank),
            "trade" => RankAsset("trade", rank),
            "explore" => RankAsset("exploration", rank),
            "cqc" => RankAsset("cqc", rank),
            "power" => RankAsset("powerplay", Math.Clamp(level + 1, 1, 5)),
            "federation" => FactionAsset("federation"),
            "empire" => FactionAsset("empire"),
            _ => RankAsset("pilots-federation", rank),
        };
    }

    private static string FactionIcon(string? faction)
    {
        if (string.IsNullOrWhiteSpace(faction))
        {
            return string.Empty;
        }

        var value = faction.ToLowerInvariant();
        if (value.Contains("federation", StringComparison.Ordinal))
        {
            return FactionAsset("federation");
        }

        if (value.Contains("empire", StringComparison.Ordinal)
            || value.Contains("imperial", StringComparison.Ordinal))
        {
            return FactionAsset("empire");
        }

        if (value.Contains("alliance", StringComparison.Ordinal))
        {
            return FactionAsset("alliance");
        }

        if (value.Contains("independent", StringComparison.Ordinal))
        {
            return FactionAsset("independent");
        }

        return string.Empty;
    }

    private static string RankAsset(string category, int level) =>
        $"avares://SrvSurvey.Desktop/Assets/Frontier/Ranks/{category}/rank-{level}.png";

    private static string FactionAsset(string faction) =>
        $"avares://SrvSurvey.Desktop/Assets/Frontier/Factions/{faction}.png";

    private static string FriendlyCommunityGoalActivity(string activityType)
    {
        var normalized = NormalizeLookupKey(activityType);
        return normalized switch
        {
            "tradelist" or "trade" => "Trade delivery",
            "bounty" or "bountyhunt" or "bountyhunting" => "Bounty hunting",
            "combat" or "combatbond" or "combatbonds" => "Combat bonds",
            "exploration" or "explorationdata" => "Exploration data",
            "salvage" => "Salvage operation",
            "rescue" or "searchandrescue" => "Search and rescue",
            "mining" => "Mining delivery",
            _ => string.IsNullOrWhiteSpace(activityType)
                ? "Community initiative"
                : HumanizeIdentifier(activityType),
        };
    }

    private static string CommunityGoalDataValue(
        IReadOnlyList<FrontierDataPointSnapshot> dataPoints,
        params string[] names)
    {
        return dataPoints.FirstOrDefault(point => names.Any(name =>
            CommunityGoalPathMatches(point.Path, name)))?.Value ?? string.Empty;
    }

    private static long? CommunityGoalDataLong(
        IReadOnlyList<FrontierDataPointSnapshot> dataPoints,
        params string[] names)
    {
        var value = CommunityGoalDataValue(dataPoints, names);
        return long.TryParse(
            value,
            NumberStyles.Integer | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out var parsed)
                ? parsed
                : long.TryParse(
                    value,
                    NumberStyles.Integer | NumberStyles.AllowThousands,
                    CultureInfo.CurrentCulture,
                    out parsed)
                        ? parsed
                        : null;
    }

    private static DateTimeOffset? CommunityGoalDataDateTimeOffset(
        IReadOnlyList<FrontierDataPointSnapshot> dataPoints,
        params string[] names)
    {
        var value = CommunityGoalDataValue(dataPoints, names);
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
                ? parsed
                : null;
    }

    private static bool HasCommunityGoalData(
        IReadOnlyList<FrontierDataPointSnapshot> dataPoints,
        params string[] names)
    {
        return dataPoints.Any(point => names.Any(name =>
            CommunityGoalPathMatches(point.Path, name)));
    }

    private static bool CommunityGoalPathMatches(string path, string name)
    {
        return string.Equals(path, name, StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("." + name, StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanCommunityGoalBriefing(string value, string title)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("{{top5}}", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
        if (!string.IsNullOrWhiteSpace(title)
            && normalized.StartsWith(title, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[title.Length..].TrimStart('\n', ' ');
        }

        while (normalized.Contains("\n\n\n", StringComparison.Ordinal))
        {
            normalized = normalized.Replace(
                "\n\n\n",
                "\n\n",
                StringComparison.Ordinal);
        }

        return normalized.Trim();
    }

    private static string FormatCommunityGoalTimeRemaining(
        DateTimeOffset? expiry,
        bool isComplete,
        DateTimeOffset currentTime)
    {
        if (isComplete)
        {
            return "Goal completed";
        }

        if (expiry is not { } deadline)
        {
            return "Deadline not supplied";
        }

        var remaining = deadline - currentTime;
        if (remaining <= TimeSpan.Zero)
        {
            return "Deadline reached";
        }

        if (remaining.TotalDays >= 1)
        {
            return $"{(int)remaining.TotalDays:N0}d {remaining.Hours:N0}h remaining";
        }

        if (remaining.TotalHours >= 1)
        {
            return $"{(int)remaining.TotalHours:N0}h {remaining.Minutes:N0}m remaining";
        }

        return $"{Math.Max(1, remaining.Minutes):N0}m remaining";
    }

    private static string JoinLocation(string system, string station)
    {
        if (string.IsNullOrWhiteSpace(system))
        {
            return string.IsNullOrWhiteSpace(station) ? "—" : station;
        }

        return string.IsNullOrWhiteSpace(station)
            ? system
            : $"{system} · {station}";
    }

    private static string? NormalizeFrontierId(string? frontierId)
    {
        var normalized = frontierId?.Trim().ToUpperInvariant();
        return normalized is not null
            && normalized.Length > 1
            && normalized[0] == 'F'
            && normalized[1..].All(char.IsAsciiDigit)
                ? normalized
                : null;
    }

    private static bool IsExpected(Exception exception)
    {
        return exception is IOException
            or InvalidDataException
            or InvalidOperationException
            or NotSupportedException
            or HttpRequestException
            or TimeoutException
            or UnauthorizedAccessException;
    }

    private async Task TryReloadStateAsync()
    {
        try
        {
            var state = await accountService.GetStateAsync();
            IsLinked = state.IsLinked;
            Snapshot = state.Snapshot;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            // Preserve the original actionable error when secure storage itself
            // is unavailable; a second failure must not escape an async command.
        }
    }

    private void RaiseCommandStates()
    {
        connectCommand.RaiseCanExecuteChanged();
        cancelConnectionCommand.RaiseCanExecuteChanged();
        refreshCommand.RaiseCanExecuteChanged();
        unlinkCommand.RaiseCanExecuteChanged();
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private void HandleAuthorizationCallbackReceived(
        object? sender,
        EventArgs eventArgs)
    {
        AuthorizationCallbackReceived?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        connectionCancellation?.Cancel();
        connectionCancellation?.Dispose();
        connectionCancellation = null;
        accountService.AuthorizationCallbackReceived -=
            HandleAuthorizationCallbackReceived;
        accountService.Dispose();
    }

    private sealed class AsyncCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute();

        public async void Execute(object? parameter)
        {
            if (CanExecute(parameter))
            {
                await execute();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed record FrontierCommanderSelectionOption(
    string FrontierId,
    string CommanderName,
    string DisplayName,
    bool IsAutomatic)
{
    public static FrontierCommanderSelectionOption Automatic(
        string? frontierId,
        string? commanderName)
    {
        var commander = string.IsNullOrWhiteSpace(commanderName)
            ? frontierId
            : commanderName.Trim();
        var detail = string.IsNullOrWhiteSpace(commander)
            ? "waiting for journal"
            : string.IsNullOrWhiteSpace(frontierId)
                ? commander
                : $"{commander} ({frontierId})";
        return new FrontierCommanderSelectionOption(
            frontierId ?? string.Empty,
            commander ?? string.Empty,
            $"Automatic · {detail}",
            true);
    }

    public static FrontierCommanderSelectionOption Linked(
        FrontierLinkedCommander commander) => new(
        commander.FrontierId,
        commander.CommanderName,
        string.Equals(
            commander.CommanderName,
            commander.FrontierId,
            StringComparison.OrdinalIgnoreCase)
                ? commander.FrontierId
                : $"{commander.CommanderName} ({commander.FrontierId})",
        false);
}

public sealed record FrontierShipRowViewModel(
    string Name,
    string Type,
    string Location,
    string Value,
    string Status);

public sealed record FrontierCapacityRowViewModel(string Category, string Used);

public sealed record FrontierLocalInventoryRowViewModel(
    string Category,
    string Name,
    string Quantity,
    string Detail);

public sealed record FrontierInventoryRowViewModel(
    string Category,
    string Name,
    string Quantity,
    string Value);

public sealed record FrontierOrderRowViewModel(
    string Category,
    string Name,
    string Quantity,
    string Price);

public sealed record FrontierRankCardViewModel(
    string Category,
    string Name,
    int Level,
    string IconPath);

public sealed record FrontierDetailRowViewModel(
    string Label,
    string Value,
    string Detail = "");

public sealed record FrontierShipModuleRowViewModel(
    string Slot,
    string Name,
    string Description,
    string Value,
    string Health,
    string Power,
    string Engineering,
    string EngineeringGrade,
    string ExperimentalEffects,
    string Engineer,
    string Group,
    string Glyph,
    string ClassRating)
{
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public bool HasEngineering => !string.IsNullOrWhiteSpace(Engineering)
        || !string.IsNullOrWhiteSpace(Engineer)
        || !string.IsNullOrWhiteSpace(ExperimentalEffects);

    public bool HasClassRating => !string.IsNullOrWhiteSpace(ClassRating);
}

public sealed class FrontierShipModuleGroupViewModel : INotifyPropertyChanged
{
    private bool isExpanded = true;

    public FrontierShipModuleGroupViewModel(
        string name,
        string glyph,
        IReadOnlyList<FrontierShipModuleRowViewModel> modules)
    {
        Name = name;
        Glyph = glyph;
        Modules = modules;
        ToggleCommand = new PanelToggleCommand(() => IsExpanded = !IsExpanded);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public string Glyph { get; }

    public IReadOnlyList<FrontierShipModuleRowViewModel> Modules { get; }

    public ICommand ToggleCommand { get; }

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (isExpanded == value)
            {
                return;
            }

            isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }
}

public sealed record FrontierLiveryRowViewModel(
    string Category,
    string Name,
    string Description)
{
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
}

public sealed class FrontierLockerCategoryViewModel : INotifyPropertyChanged
{
    private bool isExpanded;

    public FrontierLockerCategoryViewModel(
        string category,
        IReadOnlyList<FrontierLocalInventoryRowViewModel> items)
    {
        Category = category;
        Items = items;
        ToggleCommand = new PanelToggleCommand(() => IsExpanded = !IsExpanded);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Category { get; }

    public IReadOnlyList<FrontierLocalInventoryRowViewModel> Items { get; }

    public ICommand ToggleCommand { get; }

    public string Title => $"{Category} ({Items.Count:N0})";

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (isExpanded == value)
            {
                return;
            }

            isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }
}

public sealed record FrontierLaunchBayRowViewModel(
    string Slot,
    string Vehicle,
    string Loadout,
    string Rebuilds);

public sealed record FrontierCarrierCrewRowViewModel(
    string Service,
    string Name,
    string Faction,
    string Salary,
    string Status,
    string LastChanged);

public sealed record FrontierCarrierJumpRowViewModel(
    string System,
    string State,
    string Arrived,
    string Departed,
    string Duration);

public sealed record FrontierReputationRowViewModel(
    string Faction,
    string Score,
    string IconPath);

public sealed record FrontierCommodityRowViewModel(
    string Category,
    string Name,
    string BuyPrice,
    string SellPrice,
    string Stock,
    string Demand,
    string Legality,
    string Availability);

public sealed record FrontierEconomyRowViewModel(
    string Name,
    string Proportion);

public sealed record FrontierShipForSaleRowViewModel(
    string Name,
    string Price,
    string Stock,
    string Sku);

public sealed record FrontierOutfittingModuleRowViewModel(
    string Category,
    string Name,
    string Price,
    string Stock,
    string Sku);

public sealed record FrontierCommunityGoalCardViewModel(
    string Title,
    string Briefing,
    string Objective,
    string Reward,
    string System,
    string Market,
    string Location,
    string Activity,
    string GoalReference,
    string Status,
    string Expiry,
    string TimeRemaining,
    double Progress,
    string ProgressPercent,
    string ProgressText,
    string RemainingText,
    string PlayerContribution,
    string PlayerStanding,
    string Contributors,
    string Tier,
    bool HasBriefing,
    bool HasObjective,
    bool HasReward,
    bool HasLocation,
    bool HasTarget,
    string SourceStatus,
    bool HasSourceStatus,
    IReadOnlyList<FrontierDataPointSnapshot> DataPoints,
    FrontierPaneStateViewModel PaneState);

public sealed class FrontierConsolePaneStates
{
    private readonly Dictionary<string, FrontierPaneStateViewModel> communityGoalData =
        new(StringComparer.OrdinalIgnoreCase);

    public FrontierPaneStateViewModel CommanderFactionReputation { get; } = new(true);
    public FrontierPaneStateViewModel CommanderOwnedFleet { get; } = new(true);
    public FrontierPaneStateViewModel CommanderProfileData { get; } = new(false);

    public FrontierPaneStateViewModel CurrentShipCargo { get; } = new(true);
    public FrontierPaneStateViewModel CurrentShipLaunchBays { get; } = new(false);
    public FrontierPaneStateViewModel CurrentShipLivery { get; } = new(true);
    public FrontierPaneStateViewModel CurrentShipLocker { get; } = new(true);
    public FrontierPaneStateViewModel CurrentShipModules { get; } = new(false);

    public FrontierPaneStateViewModel CarrierStoredCargo { get; } = new(true);
    public FrontierPaneStateViewModel CarrierLocker { get; } = new(false);
    public FrontierPaneStateViewModel CarrierTravelHistory { get; } = new(false);
    public FrontierPaneStateViewModel CarrierCapacity { get; } = new(true);
    public FrontierPaneStateViewModel CarrierSellOrders { get; } = new(false);
    public FrontierPaneStateViewModel CarrierBuyOrders { get; } = new(false);
    public FrontierPaneStateViewModel CarrierServices { get; } = new(false);
    public FrontierPaneStateViewModel CarrierCrew { get; } = new(false);
    public FrontierPaneStateViewModel CarrierData { get; } = new(false);

    public FrontierPaneStateViewModel MarketCommodities { get; } = new(true);
    public FrontierPaneStateViewModel MarketImported { get; } = new(false);
    public FrontierPaneStateViewModel MarketExported { get; } = new(false);
    public FrontierPaneStateViewModel MarketProhibited { get; } = new(false);
    public FrontierPaneStateViewModel MarketData { get; } = new(false);

    public FrontierPaneStateViewModel ShipyardShips { get; } = new(true);
    public FrontierPaneStateViewModel ShipyardModules { get; } = new(false);
    public FrontierPaneStateViewModel ShipyardServices { get; } = new(false);
    public FrontierPaneStateViewModel ShipyardData { get; } = new(false);

    public FrontierPaneStateViewModel CommunityResponseData { get; } = new(false);

    public FrontierPaneStateViewModel GetCommunityGoalData(long? id, string title)
    {
        var key = id is { } goalId
            ? $"id:{goalId}"
            : $"title:{title}";
        if (!communityGoalData.TryGetValue(key, out var state))
        {
            state = new FrontierPaneStateViewModel(false);
            communityGoalData.Add(key, state);
        }

        return state;
    }
}

public sealed class FrontierPaneStateViewModel : INotifyPropertyChanged
{
    private bool isExpanded;

    public FrontierPaneStateViewModel(bool isExpanded)
    {
        this.isExpanded = isExpanded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (isExpanded == value)
            {
                return;
            }

            isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }
}

file sealed class PanelToggleCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}
