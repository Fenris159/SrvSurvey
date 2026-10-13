using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class ExplorationPoiViewModel : WorkspaceObservable, IDisposable
{
    private readonly IExplorationPoiClient client;
    private readonly ExplorationPoiSettingsStore settings;
    private readonly BookmarkCatalog? bookmarks;
    private readonly IStarSystemResolver systemResolver;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? requestCancellation;
    private CancellationTokenSource? referenceCancellation;
    private bool initialized;
    private readonly AsyncCommand refreshCommand;
    private bool autoShow;
    private bool externalEnabled;
    private bool loading;
    private bool jumping;
    private bool includeLegacy;
    private bool rareOnly;
    private bool currentOnly;
    private string systemQuery = string.Empty;
    private string attractionQuery = string.Empty;
    private string referenceSystem = string.Empty;
    private bool followsCurrentSystem = true;
    private GalacticCoordinate? referencePosition;
    private long? referenceAddress;
    private string referenceStatus = "Waiting for the current system.";
    private string category = "All";
    private double maximumDistanceLy = 500;
    private double minimumRating;
    private string currentSystem = string.Empty;
    private long? address;
    private GalacticCoordinate? position;
    private EliteStatus? status;
    private string? music;
    private string dataStatus = "POI catalogue has not been loaded.";
    private ExplorationPoiItemViewModel? selected;
    private Func<string, Task>? clipboard;
    private Func<Uri, Task<bool>>? launcher;

    /// <summary>Creates local catalogue browsing, reference-system lookup, bookmark actions and selected-description presentation.</summary>
    public ExplorationPoiViewModel(
        IExplorationPoiClient client,
        ExplorationPoiSettingsStore settings,
        BookmarkCatalog? bookmarks = null,
        IStarSystemResolver? systemResolver = null,
        CodexImageCache? imageCache = null
    )
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.bookmarks = bookmarks;
        this.systemResolver = systemResolver ?? new SpanshStarSystemResolver();
        DescriptionPreview = new ExplorationPoiDescriptionViewModel(imageCache);
        autoShow = settings.LoadAutoShow();
        refreshCommand = new AsyncCommand(() => LoadAsync(true), () => externalEnabled && !loading);
        RefreshCommand = refreshCommand;
        NewPoiCommand = new AsyncCommand(OpenNewPoiAsync, () => launcher is not null);
        CopySystemCommand = new AsyncCommand(CopySystemAsync, () => selected is not null && clipboard is not null);
        OpenSourceCommand = new AsyncCommand(
            OpenSourceAsync,
            () => selected?.Poi.SourceUri is not null && launcher is not null
        );
        SavePoiCommand = new WorkspaceCommand(SaveSelected, () => selected is not null && bookmarks is not null);
        UseCurrentSystemCommand = new WorkspaceCommand(() =>
        {
            followsCurrentSystem = true;
            Set(ref referenceSystem, currentSystem, nameof(ReferenceSystem));
            RefreshReference();
        });
    }

    public ExplorationPoiCatalog Catalog { get; private set; } = ExplorationPoiCatalog.Empty;
    public ExplorationPoiDescriptionViewModel DescriptionPreview { get; }
    public bool HasSelection => Selected is not null;
    public event EventHandler? CatalogChanged;
    public Task PendingLoad { get; private set; } = Task.CompletedTask;
    public Task PendingReferenceLookup { get; private set; } = Task.CompletedTask;
    public string CurrentSystem => currentSystem;
    public string DataStatus
    {
        get => dataStatus;
        private set => Set(ref dataStatus, value);
    }
    public IReadOnlyList<ExplorationPoiItemViewModel> Items { get; private set; } = [];
    public IReadOnlyList<ExplorationPoiItemViewModel> CurrentPois { get; private set; } = [];
    public IReadOnlyList<ExplorationPoiItemViewModel> OverlayPois { get; private set; } = [];
    public string MorePoisText =>
        CurrentPois.Count > 2 ? $"{CurrentPois.Count - 2} additional POIs · see Exploration" : string.Empty;
    public bool HasMorePois => CurrentPois.Count > 2;
    public bool HasDistinctOverlaySystem =>
        !OverlayPois.Any(p => string.Equals(p.Name, currentSystem, StringComparison.OrdinalIgnoreCase));
    public string ResultCount => $"{Items.Count:N0} attractions";
    public IReadOnlyList<string> Categories { get; private set; } = ["All"];
    public bool ShouldShow =>
        AutoShow
        && !jumping
        && CurrentPois.Count > 0
        && OverlayGameModeResolver.Resolve(status, musicTrack: music)
            is not (
                OverlayGameMode.Offline
                or OverlayGameMode.FsdJumping
                or OverlayGameMode.GalaxyMap
                or OverlayGameMode.Fss
            )
        && !string.IsNullOrWhiteSpace(currentSystem);

    public bool AutoShow
    {
        get => autoShow;
        set
        {
            if (Set(ref autoShow, value))
            {
                settings.SaveAutoShow(value);
                Changed(nameof(ShouldShow));
            }
        }
    }
    public bool CurrentOnly
    {
        get => currentOnly;
        set
        {
            if (Set(ref currentOnly, value))
            {
                RefreshRows();
            }
        }
    }
    public bool IncludeLegacy
    {
        get => includeLegacy;
        set
        {
            if (Set(ref includeLegacy, value))
            {
                RefreshRows();
            }
        }
    }
    public bool RareOnly
    {
        get => rareOnly;
        set
        {
            if (Set(ref rareOnly, value))
            {
                RefreshRows();
            }
        }
    }
    public bool IsSystemSearchEnabled => string.IsNullOrWhiteSpace(AttractionQuery);
    public bool IsAttractionSearchEnabled => string.IsNullOrWhiteSpace(SystemQuery);

    public string SystemQuery
    {
        get => systemQuery;
        set
        {
            if (
                (IsSystemSearchEnabled || string.IsNullOrWhiteSpace(value))
                && Set(ref systemQuery, value ?? string.Empty)
            )
            {
                Changed(nameof(IsAttractionSearchEnabled));
                RefreshRows();
            }
        }
    }

    public string AttractionQuery
    {
        get => attractionQuery;
        set
        {
            if (
                (IsAttractionSearchEnabled || string.IsNullOrWhiteSpace(value))
                && Set(ref attractionQuery, value ?? string.Empty)
            )
            {
                Changed(nameof(IsSystemSearchEnabled));
                RefreshRows();
            }
        }
    }

    public string ReferenceSystem
    {
        get => referenceSystem;
        set
        {
            if (Set(ref referenceSystem, value ?? string.Empty))
            {
                followsCurrentSystem = SameSystem(referenceSystem, currentSystem);
                RefreshReference(true);
            }
        }
    }

    public string ReferenceStatus
    {
        get => referenceStatus;
        private set => Set(ref referenceStatus, value);
    }
    public string Category
    {
        get => category;
        set
        {
            if (Set(ref category, value))
            {
                RefreshRows();
            }
        }
    }
    public double MaximumDistanceLy
    {
        get => maximumDistanceLy;
        set
        {
            if (double.IsFinite(value) && value >= 0 && Set(ref maximumDistanceLy, value))
            {
                RefreshRows();
            }
        }
    }
    public double MinimumRating
    {
        get => minimumRating;
        set
        {
            if (double.IsFinite(value) && value is >= 0 and <= 10 && Set(ref minimumRating, value))
            {
                RefreshRows();
            }
        }
    }
    public ExplorationPoiItemViewModel? Selected
    {
        get => selected;
        set
        {
            if (Set(ref selected, value))
            {
                Changed(nameof(HasSelection));
                DescriptionPreview.Update(value?.Poi, externalEnabled);
                RefreshCommands();
            }
        }
    }

    /// <summary>Opens EDAstro's form for submitting a new curated attraction.</summary>
    public ICommand NewPoiCommand { get; }

    public ICommand RefreshCommand { get; }
    public ICommand CopySystemCommand { get; }
    public ICommand OpenSourceCommand { get; }
    public ICommand SavePoiCommand { get; }
    public ICommand UseCurrentSystemCommand { get; }

    /// <summary>Installs the desktop clipboard and browser adapters and refreshes command availability.</summary>
    public void UseActions(Func<string, Task> writeClipboard, Func<Uri, Task<bool>> launchUri)
    {
        clipboard = writeClipboard;
        launcher = launchUri;
        RefreshCommands();
    }

    /// <summary>Applies external-data consent and cancels catalogue, reference and image work when it is withdrawn.</summary>
    public void SetExternalEnabled(bool enabled)
    {
        if (externalEnabled == enabled)
        {
            return;
        }

        externalEnabled = enabled;
        DescriptionPreview.Update(selected?.Poi, enabled);
        if (!enabled)
        {
            requestCancellation?.Cancel();
            referenceCancellation?.Cancel();
        }
        if (referencePosition is null)
        {
            RefreshReference();
        }

        refreshCommand.RaiseCanExecuteChanged();
        if (enabled && !loading && currentSystem.Length > 0)
        {
            PendingLoad = LoadAsync(false);
        }
    }

    /// <summary>Updates current-system POIs and passive overlay visibility from shared journal and game-interface evidence.</summary>
    public void UpdateContext(
        string? system,
        long? systemAddress,
        GalacticCoordinate? starPosition,
        EliteStatus? nextStatus,
        IReadOnlyList<JournalEventEnvelope> events,
        string? musicTrack = null
    )
    {
        currentSystem = system ?? string.Empty;
        address = systemAddress;
        position = starPosition;
        status = nextStatus ?? status;
        music = musicTrack;
        if (followsCurrentSystem)
        {
            Set(ref referenceSystem, currentSystem, nameof(ReferenceSystem));
            RefreshReference();
        }
        foreach (JournalEventEnvelope entry in events)
        {
            if (
                entry.EventName == "StartJump"
                && entry.Payload.TryGetProperty("JumpType", out JsonElement type)
                && type.GetString() == "Hyperspace"
            )
            {
                jumping = true;
            }
            else if (entry.EventName is "FSDJump" or "CarrierJump" or "Location")
            {
                jumping = false;
            }
        }
        RefreshRows();
        Changed(nameof(CurrentSystem));
        if (externalEnabled && !initialized && currentSystem.Length > 0 && !loading)
        {
            PendingLoad = LoadAsync();
        }
    }

    /// <summary>Refreshes the catalogue asynchronously and ignores cancelled or superseded responses.</summary>
    public async Task LoadAsync(bool forceRefresh = false)
    {
        if (!externalEnabled || loading)
        {
            return;
        }

        loading = true;
        initialized = true;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        requestCancellation = request;
        refreshCommand.RaiseCanExecuteChanged();
        try
        {
            ExplorationPoiLoadResult result = await client.GetAsync(forceRefresh, request.Token);
            if (lifetime.IsCancellationRequested || !externalEnabled)
            {
                return;
            }

            Catalog = result.Catalog;
            DataStatus =
                result.Warning
                ?? (
                    result.FetchedAt is { } fetched
                        ? $"GEC · {Catalog.Items.Count:N0} entries · updated {fetched.ToLocalTime():g}"
                        : "No POI catalogue is available."
                );
            RefreshRows();
            RefreshReference();
            Changed(nameof(Categories));
            CatalogChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        { /* Keep the existing catalogue when a request is revoked. */
        }
        finally
        {
            loading = false;
            requestCancellation = null;
            refreshCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Filters and sorts local attractions using the active search and resolved reference coordinates.</summary>
    private void RefreshRows()
    {
        CurrentPois = Catalog.ForSystem(currentSystem, address).Select(p => ToRow(p, position)).ToArray();
        OverlayPois = CurrentPois.Take(2).ToArray();
        Categories = Catalog
            .Items.SelectMany(p => new[] { p.Category, p.SecondaryCategory })
            .Where(s => s.Length > 0)
            .Distinct()
            .Order()
            .Prepend("All")
            .ToArray();
        IEnumerable<ExplorationPoi> source = CurrentOnly
            ? Catalog.ForSystem(ReferenceSystem, referenceAddress, IncludeLegacy)
            : Catalog.Items;
        Items = source
            .Where(p =>
                (IncludeLegacy || !p.IsLegacy)
                && (!RareOnly || p.IsRare)
                && (MinimumRating == 0 || p.Rating >= MinimumRating)
                && (Category == "All" || p.Category == Category || p.SecondaryCategory == Category)
                && (
                    string.IsNullOrWhiteSpace(SystemQuery)
                    || p.SystemName.Contains(SystemQuery.Trim(), StringComparison.OrdinalIgnoreCase)
                )
                && (
                    string.IsNullOrWhiteSpace(AttractionQuery)
                    || p.Name.Contains(AttractionQuery.Trim(), StringComparison.OrdinalIgnoreCase)
                )
            )
            .Select(p => ToRow(p, referencePosition))
            .Where(p => CurrentOnly || p.DistanceLy <= MaximumDistanceLy)
            .OrderBy(p => p.DistanceLy ?? double.MaxValue)
            .ThenByDescending(p => p.Poi.Rating)
            .Take(200)
            .ToArray();
        Selected =
            Items.FirstOrDefault(p => p.Poi.Identity == selected?.Poi.Identity) ?? (Items.Count == 0 ? null : Items[0]);
        Changed(nameof(Items));
        Changed(nameof(CurrentPois));
        Changed(nameof(OverlayPois));
        Changed(nameof(MorePoisText));
        Changed(nameof(HasMorePois));
        Changed(nameof(HasDistinctOverlaySystem));
        Changed(nameof(ResultCount));
        Changed(nameof(ShouldShow));
    }

    /// <summary>Creates a presentation row with distance measured from the resolved reference system.</summary>
    private static ExplorationPoiItemViewModel ToRow(ExplorationPoi poi, GalacticCoordinate? origin) =>
        new(poi, origin is { } start && poi.Position is { } target ? start.DistanceTo(target) : null);

    /// <summary>Resolves the active distance origin without retaining a previous system's coordinates.</summary>
    private void RefreshReference(bool replaceResolved = false)
    {
        if (!replaceResolved && !followsCurrentSystem && referencePosition is not null)
        {
            return;
        }
        referenceCancellation?.Cancel();
        referenceCancellation = null;
        referencePosition = null;
        referenceAddress = null;
        string name = ReferenceSystem.Trim();
        if (SameSystem(name, currentSystem))
        {
            referencePosition = position;
            referenceAddress = address;
        }
        ExplorationPoi? local = Catalog
            .ForSystem(name, referenceAddress, true)
            .FirstOrDefault(p => p.Position is not null);
        referencePosition ??= local?.Position;
        referenceAddress ??= local?.SystemAddress;
        if (referencePosition is not null)
        {
            ReferenceStatus = $"Distances from {name}.";
        }
        else if (name.Length == 0)
        {
            ReferenceStatus = "Enter a reference system or use the current system.";
        }
        else if (!externalEnabled)
        {
            ReferenceStatus = "Enable external data in Exploration Overlay Settings to resolve this system.";
        }
        else
        {
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            referenceCancellation = cancellation;
            ReferenceStatus = $"Resolving {name}…";
            PendingReferenceLookup = ResolveReferenceAsync(name, cancellation);
        }
        RefreshRows();
    }

    /// <summary>Looks up an edited reference system and applies only the latest lookup result.</summary>
    private async Task ResolveReferenceAsync(string name, CancellationTokenSource cancellation)
    {
        CancellationToken token = cancellation.Token;
        try
        {
            await Task.Delay(450, token);
            IReadOnlyList<StarSystemReference> matches = await systemResolver.SearchAsync(name, token);
            if (!ReferenceEquals(referenceCancellation, cancellation) || cancellation.IsCancellationRequested)
            {
                return;
            }
            StarSystemReference? match = matches.FirstOrDefault(p => SameSystem(p.Name, name));
            referencePosition = match?.Position;
            referenceAddress = match?.SystemAddress;
            ReferenceStatus = match is null ? $"No coordinates found for {name}." : $"Distances from {match.Name}.";
            RefreshRows();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { /* A newer origin or the external-data preference superseded this lookup. */
        }
        catch (Exception exception)
            when (exception is HttpRequestException or IOException or InvalidDataException or JsonException)
        {
            if (ReferenceEquals(referenceCancellation, cancellation))
            {
                ReferenceStatus = "The reference system could not be resolved: " + exception.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(referenceCancellation, cancellation))
            {
                referenceCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    /// <summary>Compares system names using the catalogue's case-insensitive system identity.</summary>
    private static bool SameSystem(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Copies the selected attraction's system using the installed clipboard adapter.</summary>
    private async Task CopySystemAsync()
    {
        if (selected is { } item && clipboard is { } write)
        {
            try
            {
                await write(item.Poi.SystemName);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                DataStatus = "The system could not be copied: " + exception.Message;
            }
        }
    }

    /// <summary>Opens the provider's attraction submission form and reports browser-launch failure.</summary>
    private async Task OpenNewPoiAsync()
    {
        if (launcher is not { } launch)
        {
            return;
        }
        try
        {
            if (!await launch(WellKnownUris.EdastroNewPoi))
            {
                DataStatus = "Unable to open EDAstro's New POI page.";
            }
        }
        catch (Exception exception)
        {
            DataStatus = "Unable to open EDAstro's New POI page: " + exception.Message;
        }
    }

    /// <summary>Opens the selected attraction's original provider page with contributor context.</summary>
    private async Task OpenSourceAsync()
    {
        if (selected is { } item && launcher is { } launch && item.Poi.SourceUri is { } sourceUri)
        {
            try
            {
                if (!await launch(sourceUri))
                {
                    DataStatus = "The GEC entry could not be opened.";
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                DataStatus = "The GEC entry could not be opened: " + exception.Message;
            }
        }
    }

    /// <summary>Bookmarks the selected provider attraction while preserving existing personal notes.</summary>
    private void SaveSelected()
    {
        if (selected is not { } item || bookmarks is null)
        {
            return;
        }

        ExplorationPoi poi = item.Poi;
        GalacticBookmark? existing = bookmarks.Items.FirstOrDefault(b => b.ExternalPoiIdentity == poi.Identity);
        if (existing is null)
        {
            bookmarks.Save(
                new GalacticBookmark
                {
                    System = poi.SystemName,
                    Position = poi.Position,
                    Category = BookmarkCategoryCatalog.Poi,
                    ExternalPoiIdentity = poi.Identity,
                    Notes = $"{poi.Name}\n{poi.Summary}\n{poi.SourceUri}",
                }
            );
        }
        DataStatus = existing is null
            ? "POI bookmarked."
            : "This POI is already bookmarked; your notes were preserved.";
    }

    /// <summary>Updates command availability after selection or desktop action adapters change.</summary>
    private void RefreshCommands()
    {
        ((AsyncCommand)CopySystemCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)OpenSourceCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)NewPoiCommand).RaiseCanExecuteChanged();
        ((WorkspaceCommand)SavePoiCommand).Refresh();
    }

    /// <summary>Cancels catalogue and reference work and releases selected-description resources.</summary>
    public void Dispose()
    {
        lifetime.Cancel();
        requestCancellation?.Dispose();
        requestCancellation = null;
        referenceCancellation?.Dispose();
        referenceCancellation = null;
        DescriptionPreview.Dispose();
        lifetime.Dispose();
    }

    /// <summary>Installs a supplied local catalogue for a deterministic overlay position preview.</summary>
    internal void InstallEditorPreview()
    {
        Catalog = OverlayEditorPreviewFactories.CreateExplorationPoiCatalog();
        ExplorationPoi poi = Catalog.Items[0];
        UpdateContext(poi.SystemName, poi.SystemAddress, poi.Position, null, []);
    }

    private sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
    {
        private bool executing;
        public event EventHandler? CanExecuteChanged;

        /// <summary>Checks whether the asynchronous desktop action is available and idle.</summary>
        public bool CanExecute(object? parameter) => !executing && canExecute();

        /// <summary>Runs one asynchronous action while updating command availability through its completion.</summary>
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

        /// <summary>Notifies command consumers after the action's availability changes.</summary>
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed record ExplorationPoiItemViewModel(ExplorationPoi Poi, double? DistanceLy)
{
    public bool ShowOverlayTitle { get; init; } = true;
    public bool HasSummary => Poi.Summary.Length > 0;
    public string Name => Poi.Name;
    public string SystemName => Poi.SystemName;
    public string Categories => Poi.CategoryText;
    public string Summary => Poi.Summary.Length > 0 ? Poi.Summary : Description;
    public string Description =>
        Poi.DescriptionBlocks.Count > 0 ? Poi.Description : ExplorationPoiDescription.Parse(Poi.Description).Text;
    public string DistanceText =>
        DistanceLy is { } distance ? distance.ToString("N1", CultureInfo.CurrentCulture) + " ly" : "—";
    public string RatingText => Poi.Rating is { } rating ? $"{rating:N1} / 10" : "Unrated";
    public string VotesText =>
        Poi.AverageStars is { } stars ? $"{stars:N1} / 5 stars · {Poi.Votes:N0} votes" : string.Empty;
    public string RareText => Poi.IsRare ? "RARE" : string.Empty;
}
