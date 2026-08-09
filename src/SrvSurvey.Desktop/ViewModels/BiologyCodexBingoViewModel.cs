using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class BiologyCodexBingoViewModel : INotifyPropertyChanged, IDisposable
{
    private const string Unavailable = "—";

    private static readonly IReadOnlyDictionary<string, string> EdAstroLinks =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Anomaly"] =
                "https://edastro.b-cdn.net/mapcharts/codex/codex-anomalies-regions.jpg",
            ["Mollusc"] =
                "https://edastro.b-cdn.net/mapcharts/codex/codex-molluscs-regions.jpg",
            ["Lagrange"] =
                "https://edastro.b-cdn.net/mapcharts/codex/codex-lagrangeclouds-regions.jpg",
            ["Storm"] =
                "https://edastro.b-cdn.net/mapcharts/codex/codex-lagrangeclouds-regions.jpg",
            ["Crystals"] =
                "https://edastro.b-cdn.net/mapcharts/codex/codex-crystals-regions.jpg",
            ["Guardian"] =
                "https://edastro.b-cdn.net/mapcharts/codex/codex-aliens-regions.jpg",
            ["Thargoid"] =
                "https://edastro.b-cdn.net/mapcharts/codex/codex-aliens-regions.jpg",
        };

    private readonly CommanderCodexStore store;
    private readonly CanonnCodexChallengeImporter canonnImporter;
    private readonly CommanderCodexJournalImporter journalImporter;
    private readonly ICodexDiscoveryLocationClient locationClient;
    private readonly CodexBingoNode rootDefinition;
    private readonly CodexBingoTreeNodeViewModel rootNode;
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "An in-flight refresh may release this gate after disposal cancellation.")]
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private readonly AsyncCommand openWindowCommand;
    private readonly AsyncCommand refreshCommand;
    private readonly AsyncCommand importCanonnCommand;
    private readonly AsyncCommand importJournalsCommand;
    private readonly AsyncCommand requestManualCommand;
    private readonly AsyncCommand confirmManualCommand;
    private readonly DelegateCommand cancelManualCommand;
    private readonly AsyncCommand copyNameCommand;
    private readonly AsyncCommand copyEntryIdCommand;
    private readonly AsyncCommand openCanonnResearchCommand;
    private readonly AsyncCommand openBioforgeCommand;
    private readonly AsyncCommand openEdAstroCommand;
    private readonly AsyncCommand openLocationCommand;
    private readonly AsyncCommand openCanonnChallengeCommand;
    private readonly AsyncCommand openUndiscoveredCommand;
    private readonly AsyncCommand findNearestCommand;
    private readonly AsyncCommand findMissingVariantsCommand;
    private CommanderCodexOptionViewModel? selectedCommander;
    private CodexBingoRegionOptionViewModel selectedRegion;
    private CodexBingoTreeNodeViewModel? selectedNode;
    private CommanderCodexData? selectedLedger;
    private IReadOnlyList<CommanderCodexOptionViewModel> commanders = [];
    private IReadOnlyList<CodexBingoRegionOptionViewModel> regions;
    private Func<Task<bool>>? windowOpener;
    private Func<string, Task>? clipboardWriter;
    private Func<Uri, Task<bool>>? uriLauncher;
    private Func<CodexBingoNearestRequest, Task>? nearestSearchHandler;
    private CancellationTokenSource? locationCancellation;
    private string? activeFrontierId;
    private string? activeCommanderName;
    private string? currentSystemName;
    private int? currentRegionId;
    private string statusMessage =
        "Waiting for a commander before calculating Codex completion.";
    private string discoveryBody = Unavailable;
    private string discoveryRegion = "Select a Codex entry";
    private string discoveryDate = Unavailable;
    private Uri? selectedLocationUri;
    private bool isBusy;
    private bool isManualConfirmationPending;
    private bool initialized;
    private bool disposed;

    public BiologyCodexBingoViewModel(
        CommanderCodexStore store,
        ExobiologyReferenceCatalog catalog,
        CanonnCodexChallengeImporter canonnImporter,
        CommanderCodexJournalImporter journalImporter,
        ICodexDiscoveryLocationClient locationClient)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentNullException.ThrowIfNull(catalog);
        this.canonnImporter = canonnImporter
            ?? throw new ArgumentNullException(nameof(canonnImporter));
        this.journalImporter = journalImporter
            ?? throw new ArgumentNullException(nameof(journalImporter));
        this.locationClient = locationClient
            ?? throw new ArgumentNullException(nameof(locationClient));
        rootDefinition = CodexBingoCatalog.Build(catalog.Entries);
        rootNode = new CodexBingoTreeNodeViewModel(rootDefinition, true);
        RootNodes = [rootNode];
        regions = CreateRegions(null);
        selectedRegion = regions[0];

        openWindowCommand = new AsyncCommand(
            OpenWindowAsync,
            () => windowOpener is not null);
        refreshCommand = new AsyncCommand(RefreshAsync, CanRunBusyAction);
        importCanonnCommand = new AsyncCommand(
            ImportCanonnAsync,
            CanImport);
        importJournalsCommand = new AsyncCommand(
            ImportJournalsAsync,
            CanImport);
        requestManualCommand = new AsyncCommand(
            RequestManualOverrideAsync,
            CanRequestManualOverride);
        confirmManualCommand = new AsyncCommand(
            ConfirmManualOverrideAsync,
            () => IsManualConfirmationPending && !IsBusy);
        cancelManualCommand = new DelegateCommand(
            CancelManualOverride,
            () => IsManualConfirmationPending && !IsBusy);
        copyNameCommand = new AsyncCommand(CopyNameAsync, HasSelection);
        copyEntryIdCommand = new AsyncCommand(
            CopyEntryIdAsync,
            CanUseSelectedEntry);
        openCanonnResearchCommand = new AsyncCommand(
            OpenCanonnResearchAsync,
            CanUseSelectedEntry);
        openBioforgeCommand = new AsyncCommand(
            OpenBioforgeAsync,
            CanOpenBioforge);
        openEdAstroCommand = new AsyncCommand(
            OpenEdAstroAsync,
            CanOpenEdAstro);
        openLocationCommand = new AsyncCommand(
            OpenLocationAsync,
            () => selectedLocationUri is not null && !IsBusy);
        openCanonnChallengeCommand = new AsyncCommand(
            () => LaunchUriAsync(WellKnownUris.CanonnChallenge, "Canonn Challenge"),
            () => uriLauncher is not null && !IsBusy);
        openUndiscoveredCommand = new AsyncCommand(
            OpenUndiscoveredAsync,
            () => SelectedCommander is not null && uriLauncher is not null && !IsBusy);
        findNearestCommand = new AsyncCommand(
            FindNearestAsync,
            () => SelectedNode?.Definition.Entry is not null
                && nearestSearchHandler is not null
                && !IsBusy);
        findMissingVariantsCommand = new AsyncCommand(
            FindMissingVariantsAsync,
            CanFindMissingVariants);
        rootNode.ApplyProgress(CodexBingoCatalog.CalculateProgress(
            rootDefinition,
            new HashSet<long>()));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<CodexBingoTreeNodeViewModel> RootNodes { get; }

    public IReadOnlyList<CommanderCodexOptionViewModel> Commanders
    {
        get => commanders;
        private set
        {
            if (SetField(ref commanders, value))
            {
                OnPropertyChanged(nameof(HasCommanders));
            }
        }
    }

    public bool HasCommanders => Commanders.Count > 0;

    public CommanderCodexOptionViewModel? SelectedCommander
    {
        get => selectedCommander;
        set
        {
            if (!SetField(ref selectedCommander, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CommanderSummary));
            RaiseCommands();
            _ = ReloadLedgerAsync();
        }
    }

    public IReadOnlyList<CodexBingoRegionOptionViewModel> Regions
    {
        get => regions;
        private set => SetField(ref regions, value);
    }

    public CodexBingoRegionOptionViewModel SelectedRegion
    {
        get => selectedRegion;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetField(ref selectedRegion, value))
            {
                return;
            }

            OnPropertyChanged(nameof(RegionSummary));
            _ = ReloadLedgerAsync();
        }
    }

    public CodexBingoTreeNodeViewModel? SelectedNode
    {
        get => selectedNode;
        set
        {
            if (!SetField(ref selectedNode, value))
            {
                return;
            }

            IsManualConfirmationPending = false;
            UpdateSelectionDetails();
            RaiseSelectionProperties();
            RaiseCommands();
            _ = ResolveSelectedLocationAsync();
        }
    }

    public string CommanderSummary => SelectedCommander is null
        ? "No commander selected"
        : $"{SelectedCommander.CommanderName} · {SelectedCommander.FrontierId}";

    public string RegionSummary => SelectedRegion.RegionId == 0
        ? "Completion across all galactic regions"
        : $"Regional firsts in {SelectedRegion.Name}";

    public string WindowTitle => $"Codex Bingo · {CompletionText}";

    public int TotalCount => rootNode.TotalCount;

    public int DiscoveredCount => rootNode.DiscoveredCount;

    public int RemainingCount => Math.Max(0, TotalCount - DiscoveredCount);

    public double CompletionPercent => rootNode.Completion * 100;

    public string CompletionText => rootNode.Completion.ToString("P2");

    public string RemainingText => TotalCount == 0
        ? "No Codex entries loaded"
        : $"{RemainingCount:N0} scans to go · each entry is "
            + (1d / TotalCount).ToString("P2");

    public string SelectedTitle => SelectedNode?.Definition.Name
        ?? "Select a Codex category or entry";

    public string SelectedKind => SelectedNode?.Definition.Kind switch
    {
        CodexBingoNodeKind.HudCategory => "HUD category",
        CodexBingoNodeKind.SubClass => "Subclass",
        CodexBingoNodeKind.Group => "Group",
        CodexBingoNodeKind.Species => "Species",
        CodexBingoNodeKind.Entry => "Codex entry",
        _ => "The Codex",
    };

    public string SelectedProgress => SelectedNode is null
        ? string.Empty
        : $"{SelectedNode.DiscoveredCount:N0} of {SelectedNode.TotalCount:N0} · "
            + SelectedNode.Completion.ToString("P1");

    public double SelectedCompletionPercent =>
        SelectedNode?.CompletionPercent ?? 0;

    public bool HasSelectedEntry => SelectedNode?.Definition.Entry is not null;

    public string SelectedEntryId => SelectedNode?.Definition.Entry is { } entry
        ? entry.EntryId.ToString(CultureInfo.InvariantCulture)
        : Unavailable;

    public string SelectedReward => SelectedNode?.Definition.Reward is > 0 and var reward
        ? reward.ToString("N0", CultureInfo.CurrentCulture) + " CR"
        : Unavailable;

    public bool SelectedIsDiscovered => SelectedNode?.IsComplete == true
        && HasSelectedEntry;

    public bool SelectedIsJournalVerified => GetSelectedFirst()?.SystemAddress > 0;

    public bool SelectedIsManual => GetSelectedFirst()?.SystemAddress == -1;

    public string SelectedState => !HasSelectedEntry
        ? "Aggregate completion"
        : (SelectedIsJournalVerified) switch
        {
            true => "Journal verified",
            false => (SelectedIsManual) switch
            {
                true => "Manual / Canonn import",
                false => "Undiscovered"
            }
        };

    public string ManualActionText => SelectedIsManual
        ? "Remove manual scan"
        : (SelectedIsJournalVerified) switch
        {
            true => "Journal verified",
            false => "I have scanned this"
        };

    public string ManualConfirmationText => SelectedNode?.Definition.Entry is { } entry
        ? ((SelectedIsManual) switch
        {
            true => "Remove the locationless manual/imported discovery for ",
            false => "Confirm that you previously scanned "
        })
            + $"{SelectedTitle} (#{entry.EntryId})?"
        : string.Empty;

    public string DiscoveryBody
    {
        get => discoveryBody;
        private set => SetField(ref discoveryBody, value);
    }

    public string DiscoveryRegion
    {
        get => discoveryRegion;
        private set => SetField(ref discoveryRegion, value);
    }

    public string DiscoveryDate
    {
        get => discoveryDate;
        private set => SetField(ref discoveryDate, value);
    }

    public bool HasLocationLink => selectedLocationUri is not null;

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetField(ref isBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ImportButtonText));
            RaiseCommands();
        }
    }

    public string ImportButtonText => IsBusy ? "Working…" : "Import discoveries";

    public bool IsManualConfirmationPending
    {
        get => isManualConfirmationPending;
        private set
        {
            if (!SetField(ref isManualConfirmationPending, value))
            {
                return;
            }

            confirmManualCommand.RaiseCanExecuteChanged();
            cancelManualCommand.RaiseCanExecuteChanged();
        }
    }

    public ICommand OpenWindowCommand => openWindowCommand;

    public ICommand RefreshCommand => refreshCommand;

    public ICommand ImportCanonnCommand => importCanonnCommand;

    public ICommand ImportJournalsCommand => importJournalsCommand;

    public ICommand RequestManualCommand => requestManualCommand;

    public ICommand ConfirmManualCommand => confirmManualCommand;

    public ICommand CancelManualCommand => cancelManualCommand;

    public ICommand CopyNameCommand => copyNameCommand;

    public ICommand CopyEntryIdCommand => copyEntryIdCommand;

    public ICommand OpenCanonnResearchCommand => openCanonnResearchCommand;

    public ICommand OpenBioforgeCommand => openBioforgeCommand;

    public ICommand OpenEdAstroCommand => openEdAstroCommand;

    public ICommand OpenLocationCommand => openLocationCommand;

    public ICommand OpenCanonnChallengeCommand => openCanonnChallengeCommand;

    public ICommand OpenUndiscoveredCommand => openUndiscoveredCommand;

    public ICommand FindNearestCommand => findNearestCommand;

    public ICommand FindMissingVariantsCommand => findMissingVariantsCommand;

    public void SetWindowOpener(Func<Task<bool>>? opener)
    {
        windowOpener = opener;
        openWindowCommand.RaiseCanExecuteChanged();
    }

    public void SetPlatformServices(
        Func<string, Task>? writer,
        Func<Uri, Task<bool>>? launcher)
    {
        clipboardWriter = writer;
        uriLauncher = launcher;
        RaiseCommands();
    }

    public void SetNearestSearchHandler(
        Func<CodexBingoNearestRequest, Task>? searchHandler)
    {
        nearestSearchHandler = searchHandler;
        RaiseCommands();
    }

    public async Task UpdateContextAsync(
        string? frontierId,
        string? commanderName,
        string? systemName,
        GalacticCoordinate? position,
        bool forceRefresh = false)
    {
        var newRegionId = position is null
            ? null
            : GalacticRegionMap.Find(position.Value)?.Id;
        var changed = !string.Equals(
                activeFrontierId,
                frontierId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(activeCommanderName, commanderName, StringComparison.Ordinal)
            || currentRegionId != newRegionId;
        activeFrontierId = string.IsNullOrWhiteSpace(frontierId)
            ? null
            : frontierId;
        activeCommanderName = string.IsNullOrWhiteSpace(commanderName)
            ? null
            : commanderName;
        currentSystemName = string.IsNullOrWhiteSpace(systemName)
            ? null
            : systemName;
        currentRegionId = newRegionId;
        if (changed || forceRefresh || !initialized)
        {
            await RefreshAsync();
        }
    }

    public async Task EnsureInitializedAsync()
    {
        if (!initialized)
        {
            await RefreshAsync();
        }
    }

    public async Task SelectCommanderAsync(CommanderCodexOptionViewModel option)
    {
        ArgumentNullException.ThrowIfNull(option);
        selectedCommander = option;
        OnPropertyChanged(nameof(SelectedCommander));
        OnPropertyChanged(nameof(CommanderSummary));
        await ReloadLedgerAsync();
    }

    public async Task SelectRegionAsync(CodexBingoRegionOptionViewModel option)
    {
        ArgumentNullException.ThrowIfNull(option);
        selectedRegion = option;
        OnPropertyChanged(nameof(SelectedRegion));
        OnPropertyChanged(nameof(RegionSummary));
        await ReloadLedgerAsync();
    }

    public async Task RefreshAsync()
    {
        if (disposed)
        {
            return;
        }

        await refreshLock.WaitAsync(CancellationToken.None);
        try
        {
            IsBusy = true;
            StatusMessage = "Loading Commander Codex ledgers…";
            var previousFrontierId = SelectedCommander?.FrontierId;
            var catalog = await store.DiscoverCommandersAsync(
                CancellationToken.None);
            var options = catalog.Commanders
                .Select(data => new CommanderCodexOptionViewModel(
                    data.FrontierId,
                    data.CommanderName ?? data.FrontierId,
                    string.Equals(
                        data.FrontierId,
                        activeFrontierId,
                        StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (activeFrontierId is not null
                && options.All(option => !string.Equals(
                    option.FrontierId,
                    activeFrontierId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                options.Add(new CommanderCodexOptionViewModel(
                    activeFrontierId,
                    activeCommanderName ?? activeFrontierId,
                    true));
            }

            Commanders = options
                .OrderByDescending(option => option.IsActive)
                .ThenBy(option => option.CommanderName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            selectedCommander = Commanders.FirstOrDefault(option =>
                    string.Equals(
                        option.FrontierId,
                        previousFrontierId,
                        StringComparison.OrdinalIgnoreCase))
                ?? Commanders.FirstOrDefault(option => option.IsActive)
                ?? (Commanders.Count > 0 ? Commanders[0] : null);
            OnPropertyChanged(nameof(SelectedCommander));
            OnPropertyChanged(nameof(CommanderSummary));

            var previousRegionId = SelectedRegion.RegionId;
            Regions = CreateRegions(currentRegionId);
            selectedRegion = Regions.FirstOrDefault(option =>
                    option.RegionId == previousRegionId)
                ?? Regions[0];
            OnPropertyChanged(nameof(SelectedRegion));
            OnPropertyChanged(nameof(RegionSummary));
            initialized = true;
            await LoadLedgerCoreAsync();
            if (catalog.Warnings.Count > 0)
            {
                StatusMessage += " " + string.Join(" ", catalog.Warnings);
            }
        }
        finally
        {
            IsBusy = false;
            refreshLock.Release();
        }
    }

    public async Task ReloadLedgerAsync()
    {
        if (disposed)
        {
            return;
        }

        await refreshLock.WaitAsync(CancellationToken.None);
        try
        {
            IsBusy = true;
            await LoadLedgerCoreAsync();
        }
        finally
        {
            IsBusy = false;
            refreshLock.Release();
        }
    }

    public async Task ImportCanonnAsync()
    {
        if (SelectedCommander is not { } commander || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = $"Importing Canonn Challenge data for {commander.CommanderName}…";
            var result = await canonnImporter.ImportAsync(
                commander.FrontierId,
                commander.CommanderName,
                CancellationToken.None);
            StatusMessage = result.IsSuccess
                ? $"Canonn matched {result.MatchedEntryCount:N0} entries and added "
                    + $"{result.AddedEntryCount:N0}; {result.UnmatchedEntryCount:N0} "
                    + "names were not in this Codex reference."
                : "Canonn import failed: " + result.Error;
            if (result.IsSuccess)
            {
                await LoadLedgerCoreAsync(preserveStatus: true);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ImportJournalsAsync()
    {
        if (SelectedCommander is not { } commander || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var progress = new CallbackProgress<CommanderCodexJournalImportProgress>(value =>
            {
                StatusMessage = $"Scanning journal {value.ProcessedFileCount:N0} of "
                    + $"{value.TotalFileCount:N0}: {value.CurrentFile}";
            });
            var result = await journalImporter.ImportAsync(
                commander.FrontierId,
                progress,
                CancellationToken.None);
            StatusMessage = $"Scanned {result.JournalFileCount:N0} journals and "
                + $"{result.DiscoveryEventCount:N0} Codex events; added "
                + $"{result.ChangedEntryCount:N0} global/regional firsts."
                + (result.MalformedLineCount > 0
                    ? $" Ignored {result.MalformedLineCount:N0} malformed lines."
                    : string.Empty)
                + (result.Warnings.Count > 0
                    ? " " + string.Join(" ", result.Warnings)
                    : string.Empty);
            await LoadLedgerCoreAsync(preserveStatus: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task RequestManualOverrideAsync()
    {
        IsManualConfirmationPending = CanRequestManualOverride();
        return Task.CompletedTask;
    }

    public async Task ConfirmManualOverrideAsync()
    {
        if (SelectedCommander is not { } commander
            || SelectedNode?.Definition.Entry is not { } entry
            || !IsManualConfirmationPending)
        {
            return;
        }

        var shouldDiscover = !SelectedIsDiscovered;
        IsBusy = true;
        try
        {
            var result = await store.SetManualDiscoveryAsync(
                commander.FrontierId,
                commander.CommanderName,
                entry.EntryId,
                shouldDiscover,
                cancellationToken: CancellationToken.None);
            StatusMessage = !result.IsSuccess
                ? "Manual discovery update failed: " + result.Error
                : (result.Changed) switch
                {
                    true => (shouldDiscover) switch
                    {
                        true => $"Marked {SelectedTitle} as previously scanned.",
                        false => $"Removed the manual discovery for {SelectedTitle}."
                    },
                    false => "The journal-backed discovery was left unchanged."
                };
            IsManualConfirmationPending = false;
            await LoadLedgerCoreAsync(preserveStatus: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        locationCancellation?.Cancel();
        locationCancellation?.Dispose();
        SetWindowOpener(null);
        SetPlatformServices(null, null);
        SetNearestSearchHandler(null);
    }

    private async Task LoadLedgerCoreAsync(bool preserveStatus = false)
    {
        var commander = SelectedCommander;
        var region = SelectedRegion;
        if (commander is null)
        {
            selectedLedger = null;
            ApplyProgress(new HashSet<long>());
            StatusMessage = "No Commander Codex ledgers were found.";
            return;
        }

        var result = await store.LoadAsync(
            commander.FrontierId,
            commander.CommanderName,
            region.RegionId,
            CancellationToken.None);
        if (!string.Equals(
                SelectedCommander?.FrontierId,
                commander.FrontierId,
                StringComparison.OrdinalIgnoreCase)
            || SelectedRegion.RegionId != region.RegionId)
        {
            return;
        }
        if (result.Data is null)
        {
            selectedLedger = null;
            ApplyProgress(new HashSet<long>());
            StatusMessage = "Commander Codex could not be loaded: " + result.Error;
            return;
        }

        selectedLedger = result.Data;
        ApplyProgress(result.Data.Firsts.Keys.ToHashSet());
        if (!preserveStatus)
        {
            StatusMessage = result.Exists
                ? $"Loaded {DiscoveredCount:N0} of {TotalCount:N0} entries for "
                    + $"{commander.CommanderName}."
                : $"No {region.Name} discoveries are recorded for "
                    + $"{commander.CommanderName}.";
            if (result.Warnings.Count > 0)
            {
                StatusMessage += " " + string.Join(" ", result.Warnings);
            }
        }
    }

    private void ApplyProgress(IReadOnlySet<long> discoveredEntryIds)
    {
        var selectedKey = SelectedNode?.Definition.Key;
        rootNode.ApplyProgress(CodexBingoCatalog.CalculateProgress(
            rootDefinition,
            discoveredEntryIds));
        if (selectedKey is not null)
        {
            selectedNode = rootNode.Find(selectedKey);
            OnPropertyChanged(nameof(SelectedNode));
        }

        UpdateSelectionDetails();
        RaiseCompletionProperties();
        RaiseSelectionProperties();
        RaiseCommands();
        _ = ResolveSelectedLocationAsync();
    }

    private void UpdateSelectionDetails()
    {
        selectedLocationUri = null;
        OnPropertyChanged(nameof(HasLocationLink));
        if (GetSelectedFirst() is not { } first)
        {
            DiscoveryBody = Unavailable;
            DiscoveryRegion = HasSelectedEntry
                ? "Not discovered in this region scope"
                : "Select a discovered Codex entry";
            DiscoveryDate = Unavailable;
            return;
        }

        DiscoveryDate = first.Timestamp.ToLocalTime().ToString("g");
        if (first.SystemAddress == -1)
        {
            DiscoveryBody = "Unknown location";
            DiscoveryRegion = "Manual or Canonn Challenge import";
            return;
        }

        DiscoveryBody = $"{first.SystemAddress} / body {first.BodyId}";
        DiscoveryRegion = "Resolving galactic region…";
    }

    private async Task ResolveSelectedLocationAsync()
    {
        var previousCancellation = locationCancellation;
        if (previousCancellation is not null)
        {
            await previousCancellation.CancelAsync();
            previousCancellation.Dispose();
        }

        locationCancellation = new CancellationTokenSource();
        var cancellationToken = locationCancellation.Token;
        if (SelectedNode?.Definition.Entry is not { } entry
            || GetSelectedFirst() is not { SystemAddress: > 0 } first)
        {
            return;
        }

        CodexDiscoveryLocationLoadResult result;
        try
        {
            result = await locationClient.GetAsync(
                first.SystemAddress,
                first.BodyId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        if (cancellationToken.IsCancellationRequested
            || SelectedNode?.Definition.Entry?.EntryId != entry.EntryId)
        {
            return;
        }

        if (result.Location is not { } location)
        {
            DiscoveryRegion = "Location unavailable: " + result.Error;
            return;
        }

        DiscoveryBody = location.BodyName;
        DiscoveryRegion = location.Region?.Name ?? "Unknown galactic region";
        selectedLocationUri = location.SpanshUri;
        OnPropertyChanged(nameof(HasLocationLink));
        openLocationCommand.RaiseCanExecuteChanged();
    }

    private CommanderCodexFirst? GetSelectedFirst()
    {
        return SelectedNode?.Definition.Entry is { } entry
            && selectedLedger?.Firsts.TryGetValue(entry.EntryId, out var first) == true
                ? first
                : null;
    }

    private async Task<bool> OpenWindowAsync()
    {
        await EnsureInitializedAsync();
        return windowOpener is not null && await windowOpener();
    }

    public Task CopyNameAsync()
    {
        var text = SelectedNode?.Definition.Entry?.DisplayName
            ?? SelectedNode?.Definition.Species
            ?? SelectedNode?.Definition.Name;
        return CopyAsync(text, "Codex name");
    }

    public Task CopyEntryIdAsync()
    {
        return CopyAsync(
            SelectedNode?.Definition.Entry?.EntryId.ToString(
                CultureInfo.InvariantCulture),
            "entry ID");
    }

    public Task<bool> OpenCanonnResearchAsync()
    {
        return SelectedNode?.Definition.Entry is { } entry
            ? LaunchUriAsync(
                new Uri(
                    WellKnownUris.CanonnCodexRegionsEntryPrefix
                        + entry.EntryId.ToString(CultureInfo.InvariantCulture)
                        + "&hud_category="
                        + Uri.EscapeDataString(entry.HudCategory ?? string.Empty)),
                "Canonn Research")
            : Task.FromResult(false);
    }

    public Task<bool> OpenBioforgeAsync()
    {
        var text = SelectedNode?.Definition.Entry?.DisplayName
            ?? SelectedNode?.Definition.Species;
        return string.IsNullOrWhiteSpace(text)
            ? Task.FromResult(false)
            : LaunchUriAsync(
                new Uri(
                    WellKnownUris.CanonnBioforgeEntryPrefix
                        + Uri.EscapeDataString(text)),
                "Canonn Bioforge");
    }

    public Task<bool> OpenEdAstroAsync()
    {
        var uri = CreateEdAstroUri(SelectedNode?.Definition);
        return uri is null
            ? Task.FromResult(false)
            : LaunchUriAsync(uri, "EDAstro Codex map");
    }

    public Task<bool> OpenLocationAsync()
    {
        return selectedLocationUri is null
            ? Task.FromResult(false)
            : LaunchUriAsync(selectedLocationUri, "Spansh discovery location");
    }

    public Task<bool> OpenUndiscoveredAsync()
    {
        if (SelectedCommander is not { } commander)
        {
            return Task.FromResult(false);
        }

        var uri = WellKnownUris.CanonnUndiscoveredCodexCommanderPrefix
            + Uri.EscapeDataString(commander.CommanderName);
        if (!string.IsNullOrWhiteSpace(currentSystemName)
            && commander.IsActive)
        {
            uri += "&System=" + Uri.EscapeDataString(currentSystemName);
        }

        return LaunchUriAsync(new Uri(uri), "undiscovered Codex map");
    }

    public async Task FindNearestAsync()
    {
        if (SelectedNode?.Definition.Entry is not { } entry
            || nearestSearchHandler is null)
        {
            return;
        }

        await nearestSearchHandler(new CodexBingoNearestRequest(
            CodexBingoNearestMode.Signal,
            entry.DisplayName ?? SelectedNode.Definition.Name,
            null,
            null,
            []));
    }

    public async Task FindMissingVariantsAsync()
    {
        if (SelectedNode is not { } node || nearestSearchHandler is null)
        {
            return;
        }

        var missing = node.Children
            .Where(child => child.Definition.Entry is not null && !child.IsComplete)
            .Select(child => child.Definition.Name)
            .ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        await nearestSearchHandler(new CodexBingoNearestRequest(
            CodexBingoNearestMode.MissingVariants,
            null,
            node.Definition.Genus,
            node.Definition.Species,
            missing));
    }

    private async Task CopyAsync(string? text, string description)
    {
        if (clipboardWriter is null || string.IsNullOrWhiteSpace(text))
        {
            StatusMessage = $"No {description} is available to copy.";
            return;
        }

        await clipboardWriter(text);
        StatusMessage = $"Copied {description}.";
    }

    private async Task<bool> LaunchUriAsync(Uri uri, string description)
    {
        if (uriLauncher is null)
        {
            StatusMessage = $"No launcher is available for {description}.";
            return false;
        }

        var launched = await uriLauncher(uri);
        StatusMessage = launched
            ? $"Opened {description}."
            : $"Could not open {description}.";
        return launched;
    }

    private static Uri? CreateEdAstroUri(CodexBingoNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node.Kind == CodexBingoNodeKind.Root)
        {
            return WellKnownUris.EdastroCodexMap;
        }

        var match = EdAstroLinks.FirstOrDefault(pair =>
            node.Name.Contains(
                pair.Key,
                StringComparison.OrdinalIgnoreCase));
        if (match.Value is not null)
        {
            return new Uri(match.Value);
        }

        if (string.IsNullOrWhiteSpace(node.Genus))
        {
            return null;
        }

        var genus = node.Genus.Replace(' ', '-').ToLowerInvariant();
        if (genus.EndsWith("anemone", StringComparison.Ordinal))
        {
            genus = "anemone";
        }

        return new Uri(
            WellKnownUris.EdastroOrganicMapPrefix
                + Uri.EscapeDataString(genus)
                + "-regions.jpg");
    }

    private static CodexBingoRegionOptionViewModel[] CreateRegions(
        int? currentRegionId)
    {
        return new[]
            {
                new CodexBingoRegionOptionViewModel(
                    0,
                    "All regions",
                    false),
            }
            .Concat(GalacticRegionMap.Regions.Select(region =>
                new CodexBingoRegionOptionViewModel(
                    region.Id,
                    region.Name,
                    region.Id == currentRegionId)))
            .ToArray();
    }

    private bool CanRunBusyAction() => !IsBusy;

    private bool CanImport() => SelectedCommander is not null && !IsBusy;

    private bool HasSelection() => SelectedNode is not null
        && clipboardWriter is not null
        && !IsBusy;

    private bool CanUseSelectedEntry() => HasSelectedEntry
        && !IsBusy;

    private bool CanRequestManualOverride() => HasSelectedEntry
        && !SelectedIsJournalVerified
        && !IsBusy;

    private bool CanOpenBioforge() => SelectedNode is not null
        && (SelectedNode.Definition.Entry is not null
            || !string.IsNullOrWhiteSpace(SelectedNode.Definition.Species))
        && uriLauncher is not null
        && !IsBusy;

    private bool CanOpenEdAstro() => CreateEdAstroUri(SelectedNode?.Definition) is not null
        && uriLauncher is not null
        && !IsBusy;

    private bool CanFindMissingVariants() => SelectedNode is { } node
        && !string.IsNullOrWhiteSpace(node.Definition.Species)
        && node.Children.Any(child =>
            child.Definition.Entry is not null && !child.IsComplete)
        && nearestSearchHandler is not null
        && !IsBusy;

    private void CancelManualOverride()
    {
        IsManualConfirmationPending = false;
    }

    private void RaiseCompletionProperties()
    {
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(DiscoveredCount));
        OnPropertyChanged(nameof(RemainingCount));
        OnPropertyChanged(nameof(CompletionPercent));
        OnPropertyChanged(nameof(CompletionText));
        OnPropertyChanged(nameof(RemainingText));
    }

    private void RaiseSelectionProperties()
    {
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedKind));
        OnPropertyChanged(nameof(SelectedProgress));
        OnPropertyChanged(nameof(SelectedCompletionPercent));
        OnPropertyChanged(nameof(HasSelectedEntry));
        OnPropertyChanged(nameof(SelectedEntryId));
        OnPropertyChanged(nameof(SelectedReward));
        OnPropertyChanged(nameof(SelectedIsDiscovered));
        OnPropertyChanged(nameof(SelectedIsJournalVerified));
        OnPropertyChanged(nameof(SelectedIsManual));
        OnPropertyChanged(nameof(SelectedState));
        OnPropertyChanged(nameof(ManualActionText));
        OnPropertyChanged(nameof(ManualConfirmationText));
    }

    private void RaiseCommands()
    {
        refreshCommand.RaiseCanExecuteChanged();
        importCanonnCommand.RaiseCanExecuteChanged();
        importJournalsCommand.RaiseCanExecuteChanged();
        requestManualCommand.RaiseCanExecuteChanged();
        confirmManualCommand.RaiseCanExecuteChanged();
        cancelManualCommand.RaiseCanExecuteChanged();
        copyNameCommand.RaiseCanExecuteChanged();
        copyEntryIdCommand.RaiseCanExecuteChanged();
        openCanonnResearchCommand.RaiseCanExecuteChanged();
        openBioforgeCommand.RaiseCanExecuteChanged();
        openEdAstroCommand.RaiseCanExecuteChanged();
        openLocationCommand.RaiseCanExecuteChanged();
        openCanonnChallengeCommand.RaiseCanExecuteChanged();
        openUndiscoveredCommand.RaiseCanExecuteChanged();
        findNearestCommand.RaiseCanExecuteChanged();
        findMissingVariantsCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

    private sealed class AsyncCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
    {
        private bool executing;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => !executing && canExecute();

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            executing = true;
            RaiseCanExecuteChanged();
            try
            {
                await execute();
            }
            finally
            {
                executing = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class DelegateCommand(
        Action execute,
        Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute();

        public void Execute(object? parameter)
        {
            if (CanExecute(parameter))
            {
                execute();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class CallbackProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value)
        {
            report(value);
        }
    }
}

public sealed class CodexBingoTreeNodeViewModel : INotifyPropertyChanged
{
    private int discoveredCount;
    private int totalCount;
    private bool isExpanded;

    public CodexBingoTreeNodeViewModel(
        CodexBingoNode definition,
        bool expand = false)
    {
        Definition = definition
            ?? throw new ArgumentNullException(nameof(definition));
        isExpanded = expand;
        Children = definition.Children
            .Select(child => new CodexBingoTreeNodeViewModel(child))
            .ToArray();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CodexBingoNode Definition { get; }

    public IReadOnlyList<CodexBingoTreeNodeViewModel> Children { get; }

    public string Name => Definition.Name;

    public string EntryId => Definition.Entry?.EntryId.ToString(
        CultureInfo.InvariantCulture) ?? string.Empty;

    public bool IsEntry => Definition.Entry is not null;

    public int DiscoveredCount
    {
        get => discoveredCount;
        private set => SetField(ref discoveredCount, value);
    }

    public int TotalCount
    {
        get => totalCount;
        private set => SetField(ref totalCount, value);
    }

    public double Completion => TotalCount == 0
        ? 0
        : (double)DiscoveredCount / TotalCount;

    public double CompletionPercent => Completion * 100;

    public bool IsComplete => TotalCount > 0 && DiscoveredCount == TotalCount;

    public bool IsIncomplete => TotalCount > 0 && !IsComplete;

    public string CompletionText => IsEntry
        ? (IsComplete) switch
        {
            true => "Discovered",
            false => "Missing"
        }
        : $"{DiscoveredCount:N0}/{TotalCount:N0} · {Completion:P1}";

    public bool IsExpanded
    {
        get => isExpanded;
        set => SetField(ref isExpanded, value);
    }

    public void ApplyProgress(CodexBingoProgress progress)
    {
        DiscoveredCount = progress.DiscoveredCount;
        TotalCount = progress.TotalCount;
        OnPropertyChanged(nameof(Completion));
        OnPropertyChanged(nameof(CompletionPercent));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(IsIncomplete));
        OnPropertyChanged(nameof(CompletionText));
        for (var index = 0; index < Children.Count; index++)
        {
            Children[index].ApplyProgress(progress.Children[index]);
        }
    }

    public CodexBingoTreeNodeViewModel? Find(string key)
    {
        if (string.Equals(Definition.Key, key, StringComparison.Ordinal))
        {
            return this;
        }

        foreach (var child in Children)
        {
            if (child.Find(key) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
}

public sealed record CommanderCodexOptionViewModel(
    string FrontierId,
    string CommanderName,
    bool IsActive)
{
    public string DisplayName => IsActive
        ? CommanderName + " · active"
        : CommanderName;
}

public sealed record CodexBingoRegionOptionViewModel(
    int RegionId,
    string Name,
    bool IsCurrent)
{
    public string DisplayName => RegionId == 0
        ? Name
        : $"#{RegionId} {Name}" + ((IsCurrent) switch
        {
            true => " · current",
            false => string.Empty
        });
}

public enum CodexBingoNearestMode
{
    Signal,
    MissingVariants,
}

public sealed record CodexBingoNearestRequest(
    CodexBingoNearestMode Mode,
    string? Signal,
    string? Genus,
    string? Species,
    IReadOnlyList<string> Variants);
