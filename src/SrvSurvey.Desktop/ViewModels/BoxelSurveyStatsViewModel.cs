using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Threading;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class BoxelSurveyStatsViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly (BoxelPlanetClass Class, string Code, string DisplayName)[] DisplayOrder =
    [
        (BoxelPlanetClass.Earthlike, "ELW", "Earth-like world"),
        (BoxelPlanetClass.WaterWorld, "WW", "Water world"),
        (BoxelPlanetClass.HighMetalContent, "HMC", "High metal content"),
        (BoxelPlanetClass.MetalRich, "MR", "Metal rich"),
        (BoxelPlanetClass.AmmoniaWorld, "AW", "Ammonia world"),
        (BoxelPlanetClass.Rocky, "RB", "Rocky body"),
        (BoxelPlanetClass.Icy, "IB", "Icy body"),
        (BoxelPlanetClass.RockyIce, "RIB", "Rocky ice body"),
        (BoxelPlanetClass.SudarskyI, "GG1", "Sudarsky class I"),
        (BoxelPlanetClass.SudarskyII, "GG2", "Sudarsky class II"),
        (BoxelPlanetClass.SudarskyIII, "GG3", "Sudarsky class III"),
        (BoxelPlanetClass.SudarskyIV, "GG4", "Sudarsky class IV"),
        (BoxelPlanetClass.SudarskyV, "GG5", "Sudarsky class V"),
        (BoxelPlanetClass.GasGiantWaterLife, "GGWL", "Gas giant with water life"),
        (BoxelPlanetClass.GasGiantAmmoniaLife, "GGAL", "Gas giant with ammonia life"),
        (BoxelPlanetClass.HeliumRichGasGiant, "HRGG", "Helium-rich gas giant"),
        (BoxelPlanetClass.WaterGiant, "WG", "Water giant"),
        (BoxelPlanetClass.WaterGiantWithLife, "WG Life", "Water giant with life"),
        (BoxelPlanetClass.HeliumGasGiant, "HGG", "Helium gas giant"),
    ];

    private readonly BoxelSurveyStatsCoordinator coordinator;
    private readonly BoxelSurveyStatsSettingsStore settingsStore;
    private readonly BoxelSearchViewModel? search;
    private readonly string? journalDirectory;
    private readonly Func<string?>? currentJournalPath;
    private readonly List<string> focusedPrefixes = [];
    private BoxelSurveyStatsPreferences preferences;
    private char selectedMassCode = 'c';
    private string? selectedPrefix;
    private bool isDetailVisible;
    private bool showSearchRollup;
    private bool isBusy;
    private string statusMessage = string.Empty;
    private string detailTitle = "Select a boxel";
    private string heliumText = BoxelSurveyAverageFormatter.Placeholder;
    private string visitedText = BoxelSurveyAverageFormatter.Placeholder;
    private string configuredSystemsText = string.Empty;
    private string highestRecordedSuffixText = BoxelSurveyAverageFormatter.Placeholder;
    private string completenessText = BoxelSurveyAverageFormatter.Placeholder;
    private string valueText = BoxelSurveyAverageFormatter.Placeholder;
    private string browserTitle = "BOXELS · MASS CODE C";
    private string browserDescription = "No statistics recorded at mass code C.";
    private string? browserParentPrefix;
    private IReadOnlyList<BoxelSurveyBrowserRowViewModel> browserRows = [];
    private IReadOnlyList<BoxelSurveyClassRowViewModel> classRows = [];
    private BoxelSurveyBoxelSnapshot? detail;
    private string? lastExportDirectory;
    private int detailRequestVersion;
    private CancellationTokenSource? detailRefreshCancellation;
    private bool disposed;

    public BoxelSurveyStatsViewModel(
        BoxelSurveyStatsCoordinator coordinator,
        BoxelSurveyStatsSettingsStore settingsStore,
        BoxelSearchViewModel? search = null,
        string? journalDirectory = null,
        Func<string?>? currentJournalPath = null)
    {
        this.coordinator = coordinator
            ?? throw new ArgumentNullException(nameof(coordinator));
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        this.search = search;
        this.journalDirectory = journalDirectory;
        this.currentJournalPath = currentJournalPath;
        preferences = settingsStore.Load();
        ApplyPreferences();
        MassCodes = CreateMassCodes();
        SelectMassCodeCommand = new RelayCommand(parameter =>
        {
            if (parameter is char massCode)
            {
                SelectedMassCode = massCode;
            }
            else if (parameter is string text
                && text.Length == 1)
            {
                SelectedMassCode = text[0];
            }
        });
        BackCommand = new RelayCommand(_ =>
        {
            IsDetailVisible = false;
            RefreshBrowser();
        });
        ExploreChildrenCommand = new RelayCommand(_ => ExploreChildren());
        ShowAllMassCodeCommand = new RelayCommand(_ =>
        {
            browserParentPrefix = null;
            RefreshBrowser();
        });
        RefreshCommand = new AsyncCommand(
            RefreshAsync,
            () => !IsBusy,
            ReportCommandFailure);
        RebuildCommand = new AsyncCommand(
            RebuildAsync,
            () => !IsBusy,
            ReportCommandFailure);
        ExportCommand = new AsyncCommand(
            () => ExportAsync(),
            () => !IsBusy,
            ReportCommandFailure);
        coordinator.Changed += OnCoordinatorChanged;
        coordinator.PersistenceFailed += OnPersistenceFailed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? RebuildRequested;

    public event EventHandler? ExportRequested;

    public ICommand SelectMassCodeCommand { get; }

    public ICommand BackCommand { get; }

    public ICommand ExploreChildrenCommand { get; }

    public ICommand ShowAllMassCodeCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand RebuildCommand { get; }

    public ICommand ExportCommand { get; }

    public IReadOnlyList<BoxelSurveyMassCodeOption> MassCodes { get; }

    public IReadOnlyList<BoxelSurveyBrowserRowViewModel> BrowserRows
    {
        get => browserRows;
        private set => SetField(ref browserRows, value);
    }

    public IReadOnlyList<BoxelSurveyClassRowViewModel> ClassRows
    {
        get => classRows;
        private set => SetField(ref classRows, value);
    }

    public IReadOnlyList<BoxelSurveyIndexEntry> RecentEntries
        => coordinator.RecentEntries();

    public bool HasRecentEntries => RecentEntries.Count > 0;

    public string BrowserTitle
    {
        get => browserTitle;
        private set => SetField(ref browserTitle, value);
    }

    public string BrowserDescription
    {
        get => browserDescription;
        private set => SetField(ref browserDescription, value);
    }

    public bool IsBrowsingChildren => !string.IsNullOrWhiteSpace(browserParentPrefix);

    public string ShowAllMassCodeText => string.Create(
        CultureInfo.CurrentCulture,
        $"Show all mass code {char.ToUpperInvariant(selectedMassCode)} boxels");

    public char SelectedMassCode
    {
        get => selectedMassCode;
        set
        {
            var massCode = char.ToLowerInvariant(value);
            if (!BoxelAddress.IsValidMassCode(massCode)
                || !SetField(ref selectedMassCode, massCode))
            {
                return;
            }

            ClearFocusedPrefixes();
            RefreshMassCodes();
            RefreshBrowser();
        }
    }

    public bool IsDetailVisible
    {
        get => isDetailVisible;
        private set
        {
            if (SetField(ref isDetailVisible, value))
            {
                OnPropertyChanged(nameof(IsBrowserVisible));
            }
        }
    }

    public bool IsBrowserVisible => !IsDetailVisible;

    public bool CanShowSearchRollup => SavedSearchBoxelCount > 1;

    private bool HasSavedSearchScope => focusedPrefixes.Count > 0
        || (search?.IsActive == true && search.SearchPrefixes.Count > 0);

    private int SavedSearchBoxelCount
    {
        get
        {
            if (focusedPrefixes.Count > 0)
            {
                return focusedPrefixes.Count;
            }

            return search?.IsActive == true ? search.SearchPrefixes.Count : 0;
        }
    }

    public bool ShowSearchRollup
    {
        get => showSearchRollup;
        set
        {
            if (value && !CanShowSearchRollup)
            {
                return;
            }

            if (SetField(ref showSearchRollup, value))
            {
                OnPropertyChanged(nameof(IsSelectedBoxelScope));
                OnPropertyChanged(nameof(IsEntireSavedSearchScope));
                OnPropertyChanged(nameof(EntireSavedSearchScopeText));
                OnPropertyChanged(nameof(StatisticsScopeDescription));
                QueueDetailRefresh();
            }
        }
    }

    public bool IsSelectedBoxelScope
    {
        get => !ShowSearchRollup;
        set
        {
            if (value)
            {
                ShowSearchRollup = false;
            }
        }
    }

    public bool IsEntireSavedSearchScope
    {
        get => ShowSearchRollup;
        set
        {
            if (value && CanShowSearchRollup)
            {
                ShowSearchRollup = true;
            }
        }
    }

    public string EntireSavedSearchScopeText
    {
        get
        {
            if (!HasSavedSearchScope)
            {
                return "Entire saved search (not available)";
            }

            var boxelSuffix = SavedSearchBoxelCount == 1 ? string.Empty : "s";
            return string.Create(
                CultureInfo.CurrentCulture,
                $"Entire saved search ({SavedSearchBoxelCount:N0} boxel{boxelSuffix})");
        }
    }

    public string StatisticsScopeDescription
    {
        get
        {
            if (ShowSearchRollup)
            {
                return "Combines recorded totals and averages from every boxel in this saved search. "
                    + "Configured system counts and highest suffixes are per-boxel and cannot be combined. "
                    + "If only one boxel has recorded data, the totals will match that boxel.";
            }

            if (!HasSavedSearchScope)
            {
                return "Showing the selected boxel. Open statistics from Saved boxel searches to view "
                    + "or combine every boxel in a saved search.";
            }

            if (SavedSearchBoxelCount == 1)
            {
                return "Showing the selected boxel. This saved search contains only one boxel, so "
                    + "an entire-search total would be identical.";
            }

            return "Shows statistics for the selected boxel only, including its configured system "
                + "count and highest recorded suffix.";
        }
    }

    public bool TreatNavBeaconAsFullyScanned
    {
        get => preferences.TreatNavBeaconAsFullyScanned;
        set
        {
            if (preferences.TreatNavBeaconAsFullyScanned == value)
            {
                return;
            }

            preferences = preferences with { TreatNavBeaconAsFullyScanned = value };
            SavePreferences();
            QueueDetailRefresh();
        }
    }

    public int MinSystemsForAverages
    {
        get => preferences.MinSystemsForAverages;
        set
        {
            var normalized = Math.Clamp(value, 1, 1000);
            if (preferences.MinSystemsForAverages == normalized)
            {
                OnPropertyChanged();
                return;
            }

            preferences = preferences with
            {
                MinSystemsForAverages = normalized,
            };
            OnPropertyChanged();
            SavePreferences();
            RefreshFormattedDetail();
            ReportStatus(string.Empty);
        }
    }

    public int MinSystemsForExport
    {
        get => preferences.MinSystemsForExport;
        set
        {
            var normalized = Math.Clamp(value, 1, 1000);
            if (preferences.MinSystemsForExport == normalized)
            {
                OnPropertyChanged();
                return;
            }

            preferences = preferences with
            {
                MinSystemsForExport = normalized,
            };
            OnPropertyChanged();
            SavePreferences();
            ReportStatus(string.Empty);
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetField(ref isBusy, value))
            {
                return;
            }

            RaiseAsyncCanExecuteChanged(RefreshCommand);
            RaiseAsyncCanExecuteChanged(RebuildCommand);
            RaiseAsyncCanExecuteChanged(ExportCommand);
        }
    }

    public string DetailTitle
    {
        get => detailTitle;
        private set => SetField(ref detailTitle, value);
    }

    public string HeliumText
    {
        get => heliumText;
        private set => SetField(ref heliumText, value);
    }

    public string VisitedText
    {
        get => visitedText;
        private set => SetField(ref visitedText, value);
    }

    public string ConfiguredSystemsText
    {
        get => configuredSystemsText;
        private set
        {
            if (SetField(ref configuredSystemsText, value))
            {
                OnPropertyChanged(nameof(HasConfiguredSystemsText));
            }
        }
    }

    public bool HasConfiguredSystemsText => !string.IsNullOrWhiteSpace(ConfiguredSystemsText);

    public string HighestRecordedSuffixText
    {
        get => highestRecordedSuffixText;
        private set => SetField(ref highestRecordedSuffixText, value);
    }

    public bool CanExploreChildren => detail is not null
        && !string.IsNullOrWhiteSpace(detail.Prefix)
        && detail.MassCode > BoxelAddress.MinimumMassCode;

    public string ExploreChildrenText => detail is not null && CanExploreChildren
        ? string.Create(
            CultureInfo.CurrentCulture,
            $"Explore child boxels (mass code {(char)(detail.MassCode - 1)})")
        : "Explore child boxels";

    public string CompletenessText
    {
        get => completenessText;
        private set => SetField(ref completenessText, value);
    }

    public string ValueText
    {
        get => valueText;
        private set => SetField(ref valueText, value);
    }

    public string? SelectedPrefix => selectedPrefix;

    public string? LastExportDirectory
    {
        get => lastExportDirectory;
        private set => SetField(ref lastExportDirectory, value);
    }

    public BoxelSurveyBoxelSnapshot? Detail => detail;

    public BoxelSurveyStatsPreferences Preferences => preferences;

    public BoxelSurveyStatsCoordinator Coordinator => coordinator;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ClearFocusedPrefixes();
        var current = coordinator.Current?.Prefix;
        if (!string.IsNullOrWhiteSpace(current))
        {
            await OpenPrefixAsync(current, cancellationToken).ConfigureAwait(false);
            return;
        }

        IsDetailVisible = false;
        RefreshBrowser();
    }

    public async Task FocusPrefixesAsync(
        IEnumerable<string> prefixes,
        char? massCode = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefixes);
        focusedPrefixes.Clear();
        browserParentPrefix = null;
        focusedPrefixes.AddRange(
            prefixes.Where(prefix => !string.IsNullOrWhiteSpace(prefix))
                .Distinct(StringComparer.Ordinal));
        coordinator.SetRetainPrefixes(focusedPrefixes);
        if (focusedPrefixes.Count <= 1 && showSearchRollup)
        {
            showSearchRollup = false;
            OnPropertyChanged(nameof(ShowSearchRollup));
            OnPropertyChanged(nameof(IsSelectedBoxelScope));
            OnPropertyChanged(nameof(IsEntireSavedSearchScope));
            OnPropertyChanged(nameof(StatisticsScopeDescription));
        }

        if (massCode is { } code && BoxelAddress.IsValidMassCode(code))
        {
            selectedMassCode = char.ToLowerInvariant(code);
            OnPropertyChanged(nameof(SelectedMassCode));
            RefreshMassCodes();
        }

        OnPropertyChanged(nameof(CanShowSearchRollup));
        OnPropertyChanged(nameof(EntireSavedSearchScopeText));
        OnPropertyChanged(nameof(StatisticsScopeDescription));
        var first = focusedPrefixes.FirstOrDefault();
        if (first is not null)
        {
            await OpenPrefixAsync(first, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            IsDetailVisible = false;
            RefreshBrowser();
        }
    }

    public async Task OpenPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        selectedPrefix = prefix;
        IsDetailVisible = true;
        await RefreshDetailAsync(cancellationToken).ConfigureAwait(false);
        RefreshBrowser();
    }

    public async Task RefreshAsync()
    {
        if (IsDetailVisible)
        {
            await RefreshDetailAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await RunOnUiThreadAsync(() =>
        {
            RefreshBrowser();
            OnPropertyChanged(nameof(RecentEntries));
        }).ConfigureAwait(false);
    }

    public async Task RebuildAsync()
    {
        RebuildRequested?.Invoke(this, EventArgs.Empty);
        if (string.IsNullOrWhiteSpace(journalDirectory))
        {
            ReportStatus("Rebuild needs a journal folder.");
            return;
        }

        IsBusy = true;
        try
        {
            var progress = new Progress<BoxelSurveyRebuildProgress>(update =>
            {
                ReportStatus(string.Create(
                    CultureInfo.CurrentCulture,
                    $"{update.Stage}: {update.Processed}/{update.Total} {update.CurrentFile}"));
            });
            var result = await coordinator.RebuildAsync(
                    journalDirectory,
                    currentJournalPath?.Invoke(),
                    progress,
                    CancellationToken.None)
                .ConfigureAwait(true);
            if (result is null)
            {
                ReportStatus("Rebuild needs an active commander.");
                return;
            }

            ReportStatus(string.Create(
                CultureInfo.CurrentCulture,
                $"Rebuilt {result.SystemFilesIngested} system files and {result.JournalFilesProcessed} journals."));
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or DirectoryNotFoundException
                or OperationCanceledException)
        {
            ReportStatus("Rebuild failed: " + exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExportAsync(string? destinationDirectory = null)
    {
        ExportRequested?.Invoke(this, EventArgs.Empty);
        IsBusy = true;
        try
        {
            var snapshots = await SelectExportSnapshotsAsync().ConfigureAwait(true);
            if (snapshots.Count == 0)
            {
                ReportStatus("Nothing met the export minimum.");
                return;
            }

            var directory = string.IsNullOrWhiteSpace(destinationDirectory)
                ? Path.Combine(
                    coordinator.StoreDataDirectory,
                    BoxelSurveyStatsStore.StoreDirectoryName,
                    coordinator.FrontierId ?? "unknown",
                    "exports")
                : Path.GetFullPath(destinationDirectory);
            Directory.CreateDirectory(directory);
            var stamp = DateTimeOffset.UtcNow.ToString(
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture);
            var stem = BoxelSurveyStatsStore.SanitizePrefix(
                snapshots.Count == 1 ? snapshots[0].Prefix : "search");
            var jsonPath = Path.Combine(directory, $"{stem}-{stamp}.json");
            var csvPath = Path.Combine(directory, $"{stem}-{stamp}.csv");
            var format = new BoxelSurveyAverageFormat(preferences.MinSystemsForAverages);
            var document = snapshots.Count == 1
                ? await coordinator.GetDocumentAsync(
                        snapshots[0].Prefix,
                        CancellationToken.None)
                    .ConfigureAwait(true)
                : null;
            if (document is not null)
            {
                await ExportSingleSnapshotAsync(
                        snapshots[0],
                        document,
                        jsonPath,
                        csvPath,
                        format)
                    .ConfigureAwait(true);
            }
            else
            {
                await ExportBundleAsync(snapshots, jsonPath, csvPath).ConfigureAwait(true);
            }

            LastExportDirectory = directory;
            ReportStatus($"Exported to {directory}.");
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or UnauthorizedAccessException
                or DirectoryNotFoundException)
        {
            ReportStatus("Export failed: " + exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ReportStatus(string message)
    {
        StatusMessage = message;
        OnPropertyChanged(nameof(HasStatusMessage));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Interlocked.Increment(ref detailRequestVersion);
        var cancellation = Interlocked.Exchange(ref detailRefreshCancellation, null);
        if (cancellation is not null)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        coordinator.Changed -= OnCoordinatorChanged;
        coordinator.PersistenceFailed -= OnPersistenceFailed;
    }

    private async Task RefreshDetailAsync(CancellationToken cancellationToken = default)
    {
        var request = await RunOnUiThreadAsync<DetailRefreshRequest?>(() =>
        {
            if (disposed || string.IsNullOrWhiteSpace(selectedPrefix))
            {
                return null;
            }

            var next = new DetailRefreshRequest(
                selectedPrefix,
                Interlocked.Increment(ref detailRequestVersion),
                showSearchRollup,
                showSearchRollup ? RollupPrefixes().ToArray() : []);
            IsBusy = true;
            return next;
        }).ConfigureAwait(false);
        if (request is null)
        {
            return;
        }

        try
        {
            var snapshot = request.UseRollup
                ? await coordinator.RollupAsync(request.RollupPrefixes, cancellationToken)
                    .ConfigureAwait(false)
                : await coordinator.GetAsync(request.Prefix, cancellationToken)
                    .ConfigureAwait(false)
                    ?? BoxelSurveyBoxelSnapshot.Empty;
            cancellationToken.ThrowIfCancellationRequested();
            await RunOnUiThreadAsync(() => ApplyDetailSnapshot(request, snapshot))
                .ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiThreadAsync(() =>
            {
                if (request.Version == detailRequestVersion)
                {
                    IsBusy = false;
                }
            }).ConfigureAwait(false);
        }
    }

    private void ApplyDetailSnapshot(
        DetailRefreshRequest request,
        BoxelSurveyBoxelSnapshot snapshot)
    {
        if (disposed
            || request.Version != detailRequestVersion
            || !string.Equals(request.Prefix, selectedPrefix, StringComparison.Ordinal)
            || request.UseRollup != showSearchRollup)
        {
            return;
        }

        detail = snapshot;
        var format = new BoxelSurveyAverageFormat(preferences.MinSystemsForAverages);
        DetailTitle = request.UseRollup
            ? request.Prefix + " · entire saved search"
            : snapshot.Prefix;
        HeliumText = FormatHelium(snapshot.MinHeliumPercent, snapshot.MaxHeliumPercent);
        VisitedText = string.Create(
            CultureInfo.CurrentCulture,
            $"Systems recorded: {snapshot.Visited:N0}");
        if (request.UseRollup)
        {
            ConfiguredSystemsText = "Configured search systems: — (per-boxel only)";
        }
        else if (search?.IsActive == true
            && search.CurrentBoxelPrefix is { } currentPrefix
            && string.Equals(currentPrefix, snapshot.Prefix, StringComparison.Ordinal))
        {
            ConfiguredSystemsText = string.Create(
                CultureInfo.CurrentCulture,
                $"Configured search systems: {search.CurrentExpectedSystemCount:N0}");
        }
        else
        {
            ConfiguredSystemsText = string.Empty;
        }

        if (request.UseRollup)
        {
            HighestRecordedSuffixText = "Highest recorded suffix: — (per-boxel only)";
        }
        else if (snapshot.HighestRecordedSuffix is { } suffix)
        {
            HighestRecordedSuffixText = string.Create(
                CultureInfo.CurrentCulture,
                $"Highest recorded suffix: {suffix:N0}");
        }
        else
        {
            HighestRecordedSuffixText = "Highest recorded suffix: —";
        }
        CompletenessText = string.Create(
            CultureInfo.CurrentCulture,
            $"FSS complete {snapshot.FssCompleteCount}    FSS bodies {snapshot.FssDiscoveryBodyCountSum} ({FormatAverage(snapshot.BodyAverage)})");
        ValueText = string.Create(
            CultureInfo.CurrentCulture,
            $"{FormatCredits(snapshot.CurrentValue)} as scanned   ·   {FormatCredits(snapshot.MappedPotentialValue)} if mapped");
        ClassRows = BuildClassRows(snapshot, format);
        OnPropertyChanged(nameof(CanExploreChildren));
        OnPropertyChanged(nameof(ExploreChildrenText));
    }

    private async Task<IReadOnlyList<BoxelSurveyBoxelSnapshot>> SelectExportSnapshotsAsync()
    {
        var min = preferences.MinSystemsForExport;
        if (showSearchRollup)
        {
            var exported = new List<BoxelSurveyBoxelSnapshot>();
            foreach (var prefix in RollupPrefixes())
            {
                var snapshot = await coordinator.GetAsync(prefix, CancellationToken.None)
                    .ConfigureAwait(true);
                if (snapshot is not null
                    && BoxelSurveyStatsExporter.MeetsExportMinimum(snapshot, min))
                {
                    exported.Add(snapshot);
                }
            }

            return exported;
        }

        if (detail is not null
            && BoxelSurveyStatsExporter.MeetsExportMinimum(detail, min))
        {
            return [detail];
        }

        return [];
    }

    private static async Task ExportSingleSnapshotAsync(
        BoxelSurveyBoxelSnapshot snapshot,
        BoxelSurveyBoxelDocument document,
        string jsonPath,
        string csvPath,
        BoxelSurveyAverageFormat format)
    {
        await File.WriteAllTextAsync(
                jsonPath,
                BoxelSurveyStatsExporter.ToJson(document),
                CancellationToken.None)
            .ConfigureAwait(true);
        await File.WriteAllTextAsync(
                csvPath,
                BoxelSurveyStatsExporter.ToDetailCsv(snapshot, format),
                CancellationToken.None)
            .ConfigureAwait(true);
    }

    private async Task ExportBundleAsync(
        IReadOnlyList<BoxelSurveyBoxelSnapshot> snapshots,
        string jsonPath,
        string csvPath)
    {
        var documents = new List<BoxelSurveyBoxelDocument>();
        foreach (var snapshot in snapshots)
        {
            var item = await coordinator.GetDocumentAsync(
                    snapshot.Prefix,
                    CancellationToken.None)
                .ConfigureAwait(true);
            if (item is not null)
            {
                documents.Add(item);
            }
        }

        var bundle = new System.Text.StringBuilder();
        bundle.Append('[');
        for (var index = 0; index < documents.Count; index++)
        {
            if (index > 0)
            {
                bundle.Append(',');
            }

            bundle.Append(BoxelSurveyStatsExporter.ToJson(documents[index]));
        }

        bundle.Append(']');
        await File.WriteAllTextAsync(
                jsonPath,
                bundle.ToString(),
                CancellationToken.None)
            .ConfigureAwait(true);
        await File.WriteAllTextAsync(
                csvPath,
                BoxelSurveyStatsExporter.ToIndexCsv(snapshots),
                CancellationToken.None)
            .ConfigureAwait(true);
    }

    private IReadOnlyList<string> RollupPrefixes()
    {
        if (focusedPrefixes.Count > 0)
        {
            return focusedPrefixes.ToArray();
        }

        return search?.SearchPrefixes ?? [selectedPrefix ?? string.Empty];
    }

    private void RefreshFormattedDetail()
    {
        if (detail is null)
        {
            return;
        }

        ClassRows = BuildClassRows(
            detail,
            new BoxelSurveyAverageFormat(preferences.MinSystemsForAverages));
    }

    private void QueueDetailRefresh()
    {
        if (disposed || string.IsNullOrWhiteSpace(selectedPrefix))
        {
            return;
        }

        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref detailRefreshCancellation, next);
        if (previous is not null)
        {
            previous.Cancel();
            previous.Dispose();
        }

        _ = RefreshDetailSafelyAsync(next);
    }

    private async Task RefreshDetailSafelyAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await RefreshDetailAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A newer scope or setting change superseded this refresh.
        }
        catch (Exception exception)
        {
            await RunOnUiThreadAsync(() => ReportStatus(
                "Could not refresh boxel statistics: " + exception.Message))
                .ConfigureAwait(false);
        }
        finally
        {
            if (ReferenceEquals(
                    Interlocked.CompareExchange(
                        ref detailRefreshCancellation,
                        null,
                        cancellation),
                    cancellation))
            {
                cancellation.Dispose();
            }
        }
    }

    private void ClearFocusedPrefixes()
    {
        focusedPrefixes.Clear();
        browserParentPrefix = null;
        coordinator.SetRetainPrefixes([]);
        if (showSearchRollup)
        {
            showSearchRollup = false;
            OnPropertyChanged(nameof(ShowSearchRollup));
            OnPropertyChanged(nameof(IsSelectedBoxelScope));
            OnPropertyChanged(nameof(IsEntireSavedSearchScope));
            OnPropertyChanged(nameof(StatisticsScopeDescription));
        }

        OnPropertyChanged(nameof(CanShowSearchRollup));
        OnPropertyChanged(nameof(EntireSavedSearchScopeText));
        OnPropertyChanged(nameof(StatisticsScopeDescription));
    }

    private void ExploreChildren()
    {
        if (!CanExploreChildren
            || selectedPrefix is null
            || !TryParsePrefix(selectedPrefix, out var parent))
        {
            return;
        }

        ClearFocusedPrefixes();
        browserParentPrefix = parent.Prefix;
        selectedMassCode = (char)(parent.MassCode - 1);
        OnPropertyChanged(nameof(SelectedMassCode));
        IsDetailVisible = false;
        RefreshMassCodes();
        RefreshBrowser();
    }

    private void RefreshBrowser()
    {
        var index = coordinator.Index.ToDictionary(
            entry => entry.Prefix,
            StringComparer.Ordinal);
        string[] roots;
        if (browserParentPrefix is not null
            && TryParsePrefix(browserParentPrefix, out var parent))
        {
            var childPrefixes = parent.Children
                .Select(child => child.Prefix)
                .ToHashSet(StringComparer.Ordinal);
            roots = index.Values
                .Where(entry => entry.MassCode == selectedMassCode)
                .Where(entry => childPrefixes.Contains(entry.Prefix))
                .Select(entry => entry.Prefix)
                .Order(StringComparer.Ordinal)
                .ToArray();
            BrowserTitle = string.Create(
                CultureInfo.CurrentCulture,
                $"CHILD BOXELS · MASS CODE {char.ToUpperInvariant(selectedMassCode)}");
            BrowserDescription = roots.Length == 0
                ? $"No child boxel statistics are recorded inside {parent.Prefix}."
                : string.Create(
                    CultureInfo.CurrentCulture,
                    $"{roots.Length:N0} recorded child boxels inside {parent.Prefix}.");
        }
        else if (focusedPrefixes.Count > 0)
        {
            roots = focusedPrefixes
                .Where(index.ContainsKey)
                .Order(StringComparer.Ordinal)
                .ToArray();
            BrowserTitle = "SAVED SEARCH BOXELS";
            BrowserDescription = string.Create(
                CultureInfo.CurrentCulture,
                $"{roots.Length:N0} boxels have recorded statistics in this saved search.");
        }
        else
        {
            roots = index.Values
                .Where(entry => entry.MassCode == selectedMassCode)
                .Select(entry => entry.Prefix)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var massCode = char.ToUpperInvariant(selectedMassCode);
            BrowserTitle = $"BOXELS · MASS CODE {massCode}";
            BrowserDescription = roots.Length == 0
                ? $"No statistics recorded at mass code {massCode}."
                : string.Create(
                    CultureInfo.CurrentCulture,
                    $"{roots.Length:N0} recorded boxels at mass code {massCode}.");
        }

        BrowserRows = roots
            .Select(prefix => CreateRow(prefix, index, indent: 0))
            .ToArray();
        OnPropertyChanged(nameof(RecentEntries));
        OnPropertyChanged(nameof(HasRecentEntries));
        OnPropertyChanged(nameof(IsBrowsingChildren));
        OnPropertyChanged(nameof(ShowAllMassCodeText));
    }

    private BoxelSurveyBrowserRowViewModel CreateRow(
        string prefix,
        Dictionary<string, BoxelSurveyIndexEntry> index,
        int indent)
    {
        index.TryGetValue(prefix, out var entry);
        var visited = entry?.VisitedSystemCount ?? 0;
        var helium = FormatHelium(entry?.MinHeliumPercent, entry?.MaxHeliumPercent);
        var status = visited == 0 ? "no recorded systems" : string.Empty;
        var highestSuffix = entry?.HighestRecordedSuffix is { } suffix
            ? suffix.ToString("N0", CultureInfo.CurrentCulture)
            : BoxelSurveyAverageFormatter.Placeholder;
        return new BoxelSurveyBrowserRowViewModel(
            prefix,
            string.Create(
                CultureInfo.CurrentCulture,
                $"{visited:N0} recorded · suffix {highestSuffix}"),
            helium,
            status,
            indent,
            string.Equals(prefix, selectedPrefix, StringComparison.Ordinal));
    }

    private static List<BoxelSurveyClassRowViewModel> BuildClassRows(
        BoxelSurveyBoxelSnapshot snapshot,
        BoxelSurveyAverageFormat format)
    {
        var rows = new List<BoxelSurveyClassRowViewModel>(DisplayOrder.Length + 1);
        foreach (var (classified, code, displayName) in DisplayOrder)
        {
            var counts = snapshot.CountsOf(classified);
            var showTf = BoxelPlanetClassifier.ShowsTerraformableColumn(classified);
            var showLand = BoxelPlanetClassifier.ShowsLandableColumns(classified);
            rows.Add(new BoxelSurveyClassRowViewModel(
                code,
                displayName,
                counts.Count,
                BoxelSurveyAverageFormatter.Format(counts.Count, snapshot.Visited, format),
                showTf
                    ? FormatSlice(counts.Terraformable)
                    : BoxelSurveyAverageFormatter.Placeholder,
                showLand
                    ? FormatSlice(counts.Landable)
                    : BoxelSurveyAverageFormatter.Placeholder,
                showLand
                    ? FormatSlice(counts.Atmospheric)
                    : BoxelSurveyAverageFormatter.Placeholder,
                showTf,
                showLand));
        }

        rows.Add(new BoxelSurveyClassRowViewModel(
            "TF+",
            "TF (other)",
            snapshot.OtherTerraformableCount,
            BoxelSurveyAverageFormatter.Format(
                snapshot.OtherTerraformableCount,
                snapshot.Visited,
                format),
            FormatSlice(snapshot.OtherTerraformableCount),
            BoxelSurveyAverageFormatter.Placeholder,
            BoxelSurveyAverageFormatter.Placeholder,
            true,
            false));
        return rows;
    }

    private void ApplyPreferences()
    {
        coordinator.TreatNavBeaconAsFullyScanned =
            preferences.TreatNavBeaconAsFullyScanned;
    }

    private void SavePreferences()
    {
        settingsStore.Save(preferences);
        ApplyPreferences();
    }

    private void RefreshMassCodes()
    {
        foreach (var option in MassCodes)
        {
            option.IsSelected = option.MassCode == selectedMassCode;
        }
    }

    private ObservableCollection<BoxelSurveyMassCodeOption> CreateMassCodes()
    {
        var options = new ObservableCollection<BoxelSurveyMassCodeOption>();
        for (var massCode = BoxelAddress.MinimumMassCode;
             massCode <= BoxelAddress.MaximumMassCode;
             massCode++)
        {
            options.Add(new BoxelSurveyMassCodeOption(
                massCode,
                massCode == selectedMassCode));
        }

        return options;
    }

    private async void OnCoordinatorChanged(object? sender, EventArgs eventArgs)
    {
        try
        {
            await RunOnUiThreadAsync(RefreshAsync).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await RunOnUiThreadAsync(() => ReportStatus(
                "Could not refresh boxel statistics: " + exception.Message))
                .ConfigureAwait(false);
        }
    }

    private void OnPersistenceFailed(object? sender, Exception exception)
    {
        Dispatcher.UIThread.Post(() => ReportStatus(
            "Could not save boxel survey statistics: " + exception.Message));
    }

    private static async Task RunOnUiThreadAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action);
    }

    private static async Task RunOnUiThreadAsync(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            await action().ConfigureAwait(true);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action);
    }

    private static async Task<T> RunOnUiThreadAsync<T>(Func<T> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return action();
        }

        return await Dispatcher.UIThread.InvokeAsync(action);
    }

    private static bool TryParsePrefix(string prefix, out BoxelAddress boxel)
    {
        boxel = null!;
        if (!BoxelAddress.TryParse(prefix + "0", out var parsed) || parsed is null)
        {
            return false;
        }

        boxel = parsed;
        return true;
    }

    private static string FormatHelium(double? min, double? max)
    {
        if (min is null && max is null)
        {
            return BoxelSurveyAverageFormatter.Placeholder;
        }

        var low = min ?? max!.Value;
        var high = max ?? min!.Value;
        return string.Create(CultureInfo.CurrentCulture, $"HE {low:0.#}–{high:0.#}%");
    }

    private static string FormatAverage(double? value)
        => value is null
            ? BoxelSurveyAverageFormatter.Placeholder
            : string.Create(CultureInfo.CurrentCulture, $"{value.Value:0.#} / sys");

    private static string FormatCredits(long value)
    {
        if (Math.Abs(value) >= 1_000_000)
        {
            return string.Create(
                CultureInfo.CurrentCulture,
                $"{value / 1_000_000d:0.0} M CR");
        }

        return string.Create(CultureInfo.CurrentCulture, $"{value:N0} CR");
    }

    private static string FormatSlice(int count)
        => count == 0
            ? BoxelSurveyAverageFormatter.Placeholder
            : count.ToString(CultureInfo.CurrentCulture);

    private void ReportCommandFailure(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return;
        }

        ReportStatus("Boxel statistics operation failed: " + exception.Message);
    }

    private static void RaiseAsyncCanExecuteChanged(ICommand? command)
    {
        if (command is AsyncCommand asyncCommand)
        {
            asyncCommand.RaiseCanExecuteChanged();
        }
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
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed record DetailRefreshRequest(
        string Prefix,
        int Version,
        bool UseRollup,
        IReadOnlyList<string> RollupPrefixes);

    private sealed class RelayCommand(Action<object?> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add => _ = value;
            remove => _ = value;
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute(parameter);
    }

    private sealed class AsyncCommand(
        Func<Task> execute,
        Func<bool> canExecute,
        Action<Exception> reportFailure) : ICommand
    {
        private bool isExecuting;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => !isExecuting && canExecute();

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            isExecuting = true;
            RaiseCanExecuteChanged();
            try
            {
                await execute().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                reportFailure(exception);
            }
            finally
            {
                isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged()
            => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class BoxelSurveyMassCodeOption : INotifyPropertyChanged
{
    private bool isSelected;

    public BoxelSurveyMassCodeOption(char massCode, bool isSelected)
    {
        MassCode = massCode;
        Label = char.ToString(massCode);
        this.isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public char MassCode { get; }

    public string Label { get; }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
            {
                return;
            }

            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}

public sealed record BoxelSurveyBrowserRowViewModel(
    string Prefix,
    string Glance,
    string Helium,
    string Status,
    int Indent,
    bool IsSelected);

public sealed record BoxelSurveyClassRowViewModel(
    string Code,
    string DisplayName,
    int Count,
    string Average,
    string Terraformable,
    string Landable,
    string Atmospheric,
    bool ShowsTerraformable,
    bool ShowsLandable);
