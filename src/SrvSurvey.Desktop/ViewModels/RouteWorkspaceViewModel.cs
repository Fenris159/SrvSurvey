using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class RouteWorkspaceViewModel : INotifyPropertyChanged
{
    private const string Unavailable = "\u2014";

    private readonly FollowRouteService routeService;
    private readonly RouteNameImporter nameImporter;
    private readonly ISpanshRouteClient spanshClient;
    private readonly AsyncCommand openWindowCommand;
    private readonly AsyncCommand refreshCommand;
    private readonly AsyncCommand saveCommand;
    private readonly AsyncCommand discardCommand;
    private readonly AsyncCommand copyNextHopCommand;
    private string? frontierId;
    private string? initializedFrontierId;
    private string? currentSystemName;
    private long? currentSystemAddress;
    private GalacticCoordinate? currentPosition;
    private FollowRouteDocument? loadedRoute;
    private IReadOnlyList<FollowRouteHop> draftHops = [];
    private IReadOnlyList<RouteHopItemViewModel> hops = [];
    private int lastReachedIndex = -1;
    private bool isActive;
    private bool autoCopy = true;
    private bool isBusy;
    private GuiFocus lastGuiFocus;
    private bool destinationMatchesNextHop;
    private string? lastCopiedHopName;
    private StatusDestination? lastDestination;
    private string statusMessage = "Waiting for a commander profile.";
    private Func<Task<bool>>? windowOpener;
    private Func<string, Task>? clipboardWriter;

    public RouteWorkspaceViewModel(
        FollowRouteService routeService,
        RouteNameImporter nameImporter,
        ISpanshRouteClient spanshClient)
    {
        this.routeService = routeService
            ?? throw new ArgumentNullException(nameof(routeService));
        this.nameImporter = nameImporter
            ?? throw new ArgumentNullException(nameof(nameImporter));
        this.spanshClient = spanshClient
            ?? throw new ArgumentNullException(nameof(spanshClient));
        openWindowCommand = new AsyncCommand(OpenWindowAsync, CanOpenWindow);
        refreshCommand = new AsyncCommand(RefreshAsync, HasProfileAndNotBusy);
        saveCommand = new AsyncCommand(SaveAsync, () => IsDirty && !IsBusy);
        discardCommand = new AsyncCommand(
            DiscardAsync,
            () => IsDirty && !IsBusy);
        copyNextHopCommand = new AsyncCommand(
            CopyNextHopAsync,
            () => NextHop is not null && clipboardWriter is not null && !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool HasProfile => !string.IsNullOrWhiteSpace(frontierId);

    public bool HasRoute => draftHops.Count > 0;

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                RaiseCommands();
                OnPropertyChanged(nameof(ImportButtonText));
            }
        }
    }

    public string ImportButtonText => IsBusy ? "Importing\u2026" : "Import";

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public IReadOnlyList<RouteHopItemViewModel> Hops
    {
        get => hops;
        private set => SetField(ref hops, value);
    }

    public bool IsActive
    {
        get => isActive;
        set
        {
            var normalized = value && CanActivate;
            if (SetField(ref isActive, normalized))
            {
                RefreshPresentation();
            }
        }
    }

    public bool AutoCopy
    {
        get => autoCopy;
        set
        {
            if (SetField(ref autoCopy, value))
            {
                RefreshPresentation();
            }
        }
    }

    public bool CanActivate => HasRoute
        && lastReachedIndex < draftHops.Count - 1;

    public bool IsComplete => HasRoute
        && lastReachedIndex >= draftHops.Count - 1;

    public bool IsDirty => loadedRoute is not null
        && ((loadedRoute.IsActive
                && loadedRoute.Hops.Count > 0
                && !loadedRoute.IsComplete) != IsActive
            || loadedRoute.AutoCopy != AutoCopy
            || loadedRoute.LastReachedIndex != lastReachedIndex
            || !loadedRoute.Hops.SequenceEqual(draftHops));

    public int RouteCount => draftHops.Count;

    public int ReachedCount => Math.Clamp(
        lastReachedIndex + 1,
        0,
        draftHops.Count);

    public FollowRouteHop? NextHop
    {
        get
        {
            var nextIndex = lastReachedIndex + 1;
            return IsActive
                && nextIndex >= 0
                && nextIndex < draftHops.Count
                    ? draftHops[nextIndex]
                    : null;
        }
    }

    public string NextHopName => NextHop?.Name
        ?? (IsComplete
            ? "Route complete"
            : HasRoute
                ? "Route paused"
                : "No route loaded");

    public string ProgressSummary => !HasRoute
        ? "Import a route to begin."
        : IsComplete
            ? $"All {RouteCount:N0} systems reached."
            : lastReachedIndex < 0
                ? $"Not started \u2022 {RouteCount:N0} systems"
                : $"Reached {ReachedCount:N0} of {RouteCount:N0} systems";

    public string AutoCopySummary => AutoCopy
        ? "Next-hop clipboard guidance is enabled."
        : "Next-hop clipboard guidance is disabled.";

    public bool ShouldAutoCopyNextHop => IsActive
        && AutoCopy
        && NextHop is not null;

    public bool ShouldShowGalaxyMapOverlay => lastGuiFocus == GuiFocus.GalaxyMap
        && IsActive
        && NextHop is not null;

    public string NextHopDistance
    {
        get
        {
            var distance = currentPosition is { } start
                && NextHop?.Position is { } end
                    ? start.DistanceTo(end)
                    : (double?)null;
            return distance is null
                ? "Distance unavailable"
                : $"{distance:N2} ly from {CurrentSystem}";
        }
    }

    public string NextHopGuidance => NextHop is { } hop
        ? CreateNotes(hop)
        : Unavailable;

    public bool HasNextHopGuidance => NextHop is { } hop
        && (hop.Refuel || hop.Neutron || !string.IsNullOrWhiteSpace(hop.Notes));

    public string NextHopDestinationStatus => destinationMatchesNextHop
        ? "SELECTED IN GALAXY MAP"
        : "ROUTE TARGET";

    public string NextHopClipboardStatus => string.Equals(
        lastCopiedHopName,
        NextHop?.Name,
        StringComparison.Ordinal)
            ? "NEXT SYSTEM COPIED"
            : AutoCopy
                ? "AUTO-COPY READY"
                : "MANUAL COPY";

    public string CurrentSystem => string.IsNullOrWhiteSpace(currentSystemName)
        ? Unavailable
        : currentSystemName;

    public string RouteFileName => loadedRoute is null
        ? Unavailable
        : Path.GetFileName(loadedRoute.FilePath);

    public ICommand OpenWindowCommand => openWindowCommand;

    public ICommand RefreshCommand => refreshCommand;

    public ICommand SaveCommand => saveCommand;

    public ICommand DiscardCommand => discardCommand;

    public ICommand CopyNextHopCommand => copyNextHopCommand;

    public void SetWindowOpener(Func<Task<bool>>? opener)
    {
        windowOpener = opener;
        openWindowCommand.RaiseCanExecuteChanged();
    }

    public void SetClipboardWriter(Func<string, Task>? writer)
    {
        clipboardWriter = writer;
        copyNextHopCommand.RaiseCanExecuteChanged();
    }

    public async Task<bool> UpdateContextAsync(
        string? nextFrontierId,
        string? nextSystemName,
        long? nextSystemAddress,
        GalacticCoordinate? nextPosition)
    {
        frontierId = string.IsNullOrWhiteSpace(nextFrontierId)
            ? null
            : nextFrontierId;
        currentSystemName = nextSystemName;
        currentSystemAddress = nextSystemAddress;
        currentPosition = nextPosition;
        OnPropertyChanged(nameof(HasProfile));
        OnPropertyChanged(nameof(CurrentSystem));

        if (string.Equals(
            initializedFrontierId,
            frontierId,
            StringComparison.OrdinalIgnoreCase))
        {
            RefreshPresentation();
            return false;
        }

        initializedFrontierId = frontierId;
        if (frontierId is null)
        {
            loadedRoute = null;
            ApplyDraft([], -1, false, true);
            StatusMessage = "Waiting for a commander profile.";
            return true;
        }

        await LoadAsync();
        return true;
    }

    public async Task ApplyJournalEventsAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        if (loadedRoute is null || journalEvents.Count == 0)
        {
            return;
        }

        try
        {
            var hadUnsavedChanges = IsDirty;
            var changed = false;
            int? reachedIndex = null;
            foreach (var journalEvent in journalEvents)
            {
                if (!string.Equals(
                    journalEvent.EventName,
                    "FSDJump",
                    StringComparison.Ordinal))
                {
                    continue;
                }

                var name = GetString(journalEvent.Payload, "StarSystem");
                var address = GetInt64(journalEvent.Payload, "SystemAddress");
                if (string.IsNullOrWhiteSpace(name) && address is null)
                {
                    continue;
                }

                var result = await routeService.ApplyArrivalAsync(
                    loadedRoute,
                    name ?? string.Empty,
                    address);
                if (!result.Changed)
                {
                    continue;
                }

                loadedRoute = result.Route;
                changed = true;
                reachedIndex = result.ReachedIndex;
            }

            if (!changed)
            {
                return;
            }

            if (!hadUnsavedChanges)
            {
                ApplyDocument(loadedRoute);
            }
            else
            {
                RefreshPresentation();
            }

            var reachedName = reachedIndex is { } index
                && index >= 0
                && index < loadedRoute.Hops.Count
                    ? loadedRoute.Hops[index].Name
                    : currentSystemName ?? "the route";
            StatusMessage = loadedRoute.IsComplete
                ? $"Route complete after arriving at {reachedName}."
                : $"Arrived at hop #{reachedIndex + 1:N0}: {reachedName}."
                    + (hadUnsavedChanges
                        ? " Unsaved route edits were kept."
                        : string.Empty);
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "Route progress could not be saved: "
                + exception.Message;
        }
    }

    public async Task UpdateStatusAsync(EliteStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var enteredGalaxyMap = lastGuiFocus != GuiFocus.GalaxyMap
            && status.GuiFocus == GuiFocus.GalaxyMap;
        lastGuiFocus = status.GuiFocus;
        lastDestination = status.Destination;
        destinationMatchesNextHop = status.GuiFocus == GuiFocus.GalaxyMap
            && IsNextHop(lastDestination);
        if (status.GuiFocus != GuiFocus.GalaxyMap)
        {
            lastCopiedHopName = null;
        }

        RaiseOverlayProperties();
        if (enteredGalaxyMap && ShouldAutoCopyNextHop)
        {
            await CopyNextHopAsync();
        }
    }

    public Task RefreshAsync()
    {
        return LoadAsync();
    }

    public async Task ImportNamesAsync(IEnumerable<string> names)
    {
        if (!HasProfile || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Resolving route systems with Spansh\u2026";
            var result = await nameImporter.ImportAsync(names);
            ApplyImportedHops(result.Hops);
            StatusMessage = result.Hops.Count == 0
                ? "No system names were found to import."
                : $"Imported {result.Hops.Count:N0} systems; "
                    + $"{result.ResolvedCount:N0} resolved and "
                    + $"{result.UnresolvedCount:N0} kept by name.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "System names could not be imported: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task ImportNamesTextAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusMessage = "No system names were available to import.";
            return Task.CompletedTask;
        }

        return ImportNamesAsync(RouteNameImporter.ParseNames(text));
    }

    public void ReportImportError(string message)
    {
        StatusMessage = message;
    }

    public async Task ImportSpanshUrlAsync(string? text)
    {
        if (!HasProfile || IsBusy)
        {
            return;
        }

        if (!SpanshRouteUrlParser.TryParse(text, out var reference)
            || reference is null)
        {
            StatusMessage = "The clipboard does not contain a valid Spansh route URL or job ID.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"Importing Spansh {GetRouteKindLabel(reference.Kind)} route\u2026";
            var importedHops = await spanshClient.GetRouteAsync(reference);
            ApplyImportedHops(importedHops);
            StatusMessage = importedHops.Count == 0
                ? "Spansh returned a route with no systems."
                : $"Imported {importedHops.Count:N0} systems from Spansh. Review and save the route.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The Spansh route could not be imported: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SetProgressThrough(int index, bool reached)
    {
        if (index < 0 || index >= draftHops.Count)
        {
            return;
        }

        lastReachedIndex = reached ? index : index - 1;
        if (lastReachedIndex >= draftHops.Count - 1)
        {
            isActive = false;
        }

        RefreshPresentation();
    }

    public async Task SaveAsync()
    {
        if (loadedRoute is null || !IsDirty)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var saved = await routeService.ReplaceAsync(
                loadedRoute,
                draftHops,
                lastReachedIndex,
                IsActive,
                AutoCopy,
                currentSystemAddress);
            ApplyDocument(saved);
            StatusMessage = saved.IsComplete
                ? "Route saved as complete."
                : saved.IsActive
                    ? $"Route saved. Next system: {saved.NextHop?.Name ?? Unavailable}."
                    : "Route saved in a paused state.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The route could not be saved: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task DiscardAsync()
    {
        if (loadedRoute is not null)
        {
            ApplyDocument(loadedRoute);
            StatusMessage = "Unsaved route changes were discarded.";
        }

        return Task.CompletedTask;
    }

    public async Task CopyNextHopAsync()
    {
        if (NextHop is not { } nextHop || clipboardWriter is null)
        {
            StatusMessage = "There is no active next hop to copy.";
            return;
        }

        try
        {
            await clipboardWriter(nextHop.Name);
            lastCopiedHopName = nextHop.Name;
            StatusMessage = $"Copied {nextHop.Name} to the clipboard.";
            RaiseOverlayProperties();
        }
        catch (Exception exception)
        {
            StatusMessage = "The next hop could not be copied: "
                + exception.Message;
        }
    }

    private async Task LoadAsync()
    {
        if (frontierId is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var result = await routeService.LoadAsync(frontierId);
            if (result.Route is null)
            {
                loadedRoute = null;
                ApplyDraft([], -1, false, true);
                StatusMessage = result.Error ?? "The route could not be loaded.";
                return;
            }

            ApplyDocument(result.Route);
            StatusMessage = result.Exists
                ? $"Loaded {result.Route.Hops.Count:N0} route systems from "
                    + Path.GetFileName(result.Path)
                    + "."
                : "No followed route is saved for this commander.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The route could not be loaded: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool HasProfileAndNotBusy()
    {
        return HasProfile && !IsBusy;
    }

    private bool CanOpenWindow()
    {
        return HasProfile && windowOpener is not null && !IsBusy;
    }

    private async Task OpenWindowAsync()
    {
        if (windowOpener is null || !await windowOpener())
        {
            StatusMessage = "The route workspace requires a commander profile.";
        }
    }

    private void ApplyImportedHops(IReadOnlyList<FollowRouteHop> importedHops)
    {
        var nextHops = importedHops.ToArray();
        var nextLastIndex = nextHops.Length > 0
            && currentSystemAddress is { } address
            && nextHops[0].SystemAddress == address
                ? 0
                : -1;
        ApplyDraft(
            nextHops,
            nextLastIndex,
            nextHops.Length > 0 && nextLastIndex < nextHops.Length - 1,
            AutoCopy);
    }

    private void ApplyDocument(FollowRouteDocument route)
    {
        loadedRoute = route;
        ApplyDraft(
            route.Hops,
            route.LastReachedIndex,
            route.IsActive && !route.IsComplete,
            route.AutoCopy);
    }

    private void ApplyDraft(
        IReadOnlyList<FollowRouteHop> nextHops,
        int nextLastReachedIndex,
        bool nextIsActive,
        bool nextAutoCopy)
    {
        draftHops = nextHops.ToArray();
        lastReachedIndex = draftHops.Count == 0
            ? -1
            : Math.Clamp(nextLastReachedIndex, -1, draftHops.Count - 1);
        isActive = nextIsActive
            && draftHops.Count > 0
            && lastReachedIndex < draftHops.Count - 1;
        autoCopy = nextAutoCopy;
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(AutoCopy));
        RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        destinationMatchesNextHop = lastGuiFocus == GuiFocus.GalaxyMap
            && IsNextHop(lastDestination);
        if (!string.Equals(
            lastCopiedHopName,
            NextHop?.Name,
            StringComparison.Ordinal))
        {
            lastCopiedHopName = null;
        }

        var rows = new List<RouteHopItemViewModel>(draftHops.Count);
        for (var index = 0; index < draftHops.Count; index++)
        {
            var hop = draftHops[index];
            GalacticCoordinate? from = index == 0
                ? lastReachedIndex < 0
                    ? currentPosition
                    : hop.Position
                : draftHops[index - 1].Position;
            var distance = from is { } start && hop.Position is { } end
                ? start.DistanceTo(end)
                : (double?)null;
            var isCurrent = IsCurrentSystem(hop);
            var isNext = IsActive && index == lastReachedIndex + 1;
            rows.Add(new RouteHopItemViewModel(
                index,
                index + 1,
                hop,
                distance is null ? "?" : $"{distance:N2} ly",
                CreateNotes(hop),
                index <= lastReachedIndex,
                isCurrent,
                isNext));
        }

        Hops = rows;
        OnPropertyChanged(nameof(HasRoute));
        OnPropertyChanged(nameof(CanActivate));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(RouteCount));
        OnPropertyChanged(nameof(ReachedCount));
        OnPropertyChanged(nameof(NextHop));
        OnPropertyChanged(nameof(NextHopName));
        OnPropertyChanged(nameof(ProgressSummary));
        OnPropertyChanged(nameof(AutoCopySummary));
        OnPropertyChanged(nameof(ShouldAutoCopyNextHop));
        RaiseOverlayProperties();
        RaiseCommands();
    }

    private bool IsNextHop(StatusDestination? destination)
    {
        if (destination is null || NextHop is not { } nextHop)
        {
            return false;
        }

        return (destination.System > 0
                && nextHop.SystemAddress == destination.System)
            || (!string.IsNullOrWhiteSpace(destination.Name)
                && string.Equals(
                    nextHop.Name,
                    destination.Name,
                    StringComparison.OrdinalIgnoreCase));
    }

    private void RaiseOverlayProperties()
    {
        OnPropertyChanged(nameof(ShouldShowGalaxyMapOverlay));
        OnPropertyChanged(nameof(NextHopDistance));
        OnPropertyChanged(nameof(NextHopGuidance));
        OnPropertyChanged(nameof(HasNextHopGuidance));
        OnPropertyChanged(nameof(NextHopDestinationStatus));
        OnPropertyChanged(nameof(NextHopClipboardStatus));
    }

    private bool IsCurrentSystem(FollowRouteHop hop)
    {
        return (currentSystemAddress is not null
                && hop.SystemAddress == currentSystemAddress)
            || (!string.IsNullOrWhiteSpace(currentSystemName)
                && string.Equals(
                    hop.Name,
                    currentSystemName,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static string CreateNotes(FollowRouteHop hop)
    {
        var parts = new List<string>();
        if (hop.Refuel)
        {
            parts.Add("\u26fd Refuel");
        }

        if (hop.Neutron)
        {
            parts.Add("\u26a0 Neutron");
        }

        if (!string.IsNullOrWhiteSpace(hop.Notes))
        {
            parts.Add(hop.Notes);
        }

        return parts.Count == 0 ? Unavailable : string.Join(" \u2022 ", parts);
    }

    private static string GetRouteKindLabel(SpanshRouteKind kind)
    {
        return kind switch
        {
            SpanshRouteKind.Generic => "search",
            SpanshRouteKind.Tourist => "tourist",
            SpanshRouteKind.Neutron => "neutron",
            SpanshRouteKind.Galaxy => "galaxy",
            _ => "followed",
        };
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var result)
                ? result
                : null;
    }

    private static bool IsExpectedException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or HttpRequestException
            or TimeoutException;
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

    private void RaiseCommands()
    {
        openWindowCommand.RaiseCanExecuteChanged();
        refreshCommand.RaiseCanExecuteChanged();
        saveCommand.RaiseCanExecuteChanged();
        discardCommand.RaiseCanExecuteChanged();
        copyNextHopCommand.RaiseCanExecuteChanged();
    }

    private sealed class AsyncCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return canExecute();
        }

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

public sealed record RouteHopItemViewModel(
    int Index,
    int Number,
    FollowRouteHop Hop,
    string Distance,
    string Notes,
    bool IsReached,
    bool IsCurrent,
    bool IsNext)
{
    public string Name => Hop.Name;

    public string State => IsCurrent
        ? "CURRENT"
        : IsNext
            ? "NEXT"
            : IsReached
                ? "VISITED"
                : string.Empty;

    public bool HasState => State.Length > 0;
}
