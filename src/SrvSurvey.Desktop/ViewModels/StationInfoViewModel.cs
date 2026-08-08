using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class StationInfoViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly string[] InterestingServices =
    [
        "Shipyard",
        "Outfitting",
        "Refuel",
        "Restock",
        "Repair",
        "Market",
        "Universal Cartographics",
        "Search and Rescue",
        "Interstellar Factors",
        "Material Trader",
        "Black Market",
        "Technology Broker",
    ];

    private readonly ISystemSummaryClient summaryClient;
    private readonly StationInfoSettingsStore? settingsStore;
    private CancellationTokenSource? loadCancellation;
    private SystemSummary? summary;
    private EliteStatus? status;
    private string? musicTrack;
    private string? systemName;
    private long systemAddress;
    private bool autoShow;
    private bool forceShow;
    private bool manuallyHidden;
    private bool isBusy;
    private HashSet<string> questTags = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase);
    private SystemStationSummary? projectedStation;
    private IReadOnlyList<StationInfoLineViewModel> economyLines = [];
    private IReadOnlyList<string> relevantServices = [];
    private string statusMessage = "Waiting for a current system.";
    private string settingsStatus = string.Empty;
    private string? editorStationName;
    private string? editorStationType;
    private string? editorLargestPadText;
    private string? editorPrimaryEconomyText;
    private string? editorFactionText;
    private string? editorUpdatedText;
    private bool editorIsQuestTagged;
    private IReadOnlyList<StationInfoLineViewModel>? editorEconomyLines;
    private IReadOnlyList<string>? editorRelevantServices;
    private IReadOnlyList<string>? editorProhibited;
    private bool disposed;

    public StationInfoViewModel(
        ISystemSummaryClient summaryClient,
        StationInfoSettingsStore? settingsStore = null)
    {
        this.summaryClient = summaryClient
            ?? throw new ArgumentNullException(nameof(summaryClient));
        this.settingsStore = settingsStore;
        autoShow = settingsStore?.Load().AutoShow
            ?? StationInfoPreferences.Default.AutoShow;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool AutoShow
    {
        get => autoShow;
        set
        {
            if (SetField(ref autoShow, value))
            {
                SavePreferences();
                NotifyStationState();
            }
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set => SetField(ref isBusy, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public string SettingsStatus
    {
        get => settingsStatus;
        private set
        {
            if (SetField(ref settingsStatus, value))
            {
                OnPropertyChanged(nameof(HasSettingsStatus));
            }
        }
    }

    public bool HasSettingsStatus => !string.IsNullOrWhiteSpace(SettingsStatus);

    public Task PendingLoad { get; private set; } = Task.CompletedTask;

    public SystemStationSummary? SelectedStation
    {
        get
        {
            var destination = status?.Destination;
            if (summary is null
                || destination is null
                || destination.System != systemAddress
                || ColonizationDockingSnapshot.IsConstructionSiteName(
                    destination.Name))
            {
                return null;
            }

            return summary.Stations.FirstOrDefault(station => string.Equals(
                station.Name,
                destination.Name,
                StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool HasSelectedStation =>
        editorStationName is not null || SelectedStation is not null;

    public bool IsForced => forceShow;

    public bool ShouldShow => editorStationName is not null
        || (AutoShow
            && HasSelectedStation
            && (forceShow
                || OverlayGameModeResolver.Resolve(
                        status,
                        musicTrack: musicTrack)
                        == OverlayGameMode.ExternalPanel
                    && !manuallyHidden));

    public string StationName => editorStationName
        ?? SelectedStation?.Name
        ?? "No station selected";

    public bool IsQuestTagged => editorIsQuestTagged
        || (SelectedStation is { } station
            && questTags.Contains(station.Name));

    public string StationType => editorStationType
        ?? SelectedStation?.Type
        ?? "Station information";

    public string LargestPadText => editorLargestPadText
        ?? (SelectedStation?.LandingPads?.Largest is { } pad
            ? $"Largest pad: {pad}"
            : "Landing-pad data unavailable");

    public string PrimaryEconomyText => editorPrimaryEconomyText
        ?? (SelectedStation?.PrimaryEconomy is { } economy
            ? $"Primary economy: {economy}"
            : "Primary economy unavailable");

    public string FactionText => editorFactionText
        ?? (SelectedStation is { } station
            && !string.IsNullOrWhiteSpace(station.ControllingFaction)
                ? (string.IsNullOrWhiteSpace(station.Government)) switch
                {
                    true => station.ControllingFaction,
                    false => $"{station.ControllingFaction} · {station.Government}"
                }
                : "Controlling faction unavailable");

    public IReadOnlyList<StationInfoLineViewModel> EconomyLines =>
        editorEconomyLines ?? economyLines;

    public IReadOnlyList<string> RelevantServices =>
        editorRelevantServices ?? relevantServices;

    public bool HasRelevantServices => RelevantServices.Count > 0;

    public IReadOnlyList<string> ProhibitedCommodities =>
        editorProhibited ?? SelectedStation?.ProhibitedCommodities ?? [];

    public bool HasProhibitedCommodities => ProhibitedCommodities.Count > 0;

    public string UpdatedText => editorUpdatedText
        ?? (SelectedStation?.UpdatedAt is { } updated
            ? $"Spansh data updated {updated.ToLocalTime():d}"
            : "Spansh update time unavailable");

    /// <summary>
    /// Installs representative station content for the position editor.
    /// </summary>
    internal void InstallEditorPreview(StationInfoEditorPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        editorStationName = preview.StationName;
        editorStationType = preview.StationType;
        editorLargestPadText = preview.LargestPad;
        editorPrimaryEconomyText = preview.PrimaryEconomy;
        editorFactionText = preview.Faction;
        editorUpdatedText = preview.Updated;
        editorIsQuestTagged = preview.IsQuestTagged;
        editorEconomyLines = preview.Economies;
        editorRelevantServices = preview.Services;
        editorProhibited = preview.Prohibited;
        OnPropertyChanged(nameof(StationName));
        OnPropertyChanged(nameof(StationType));
        OnPropertyChanged(nameof(LargestPadText));
        OnPropertyChanged(nameof(PrimaryEconomyText));
        OnPropertyChanged(nameof(FactionText));
        OnPropertyChanged(nameof(UpdatedText));
        OnPropertyChanged(nameof(IsQuestTagged));
        OnPropertyChanged(nameof(EconomyLines));
        OnPropertyChanged(nameof(RelevantServices));
        OnPropertyChanged(nameof(HasRelevantServices));
        OnPropertyChanged(nameof(ProhibitedCommodities));
        OnPropertyChanged(nameof(HasProhibitedCommodities));
        OnPropertyChanged(nameof(HasSelectedStation));
        OnPropertyChanged(nameof(ShouldShow));
    }

    public Task UpdateCurrentSystemAsync(
        string? currentSystemName,
        long currentSystemAddress)
    {
        if (disposed)
        {
            return Task.CompletedTask;
        }

        if (string.Equals(
                systemName,
                currentSystemName,
                StringComparison.OrdinalIgnoreCase)
            && systemAddress == currentSystemAddress)
        {
            return PendingLoad;
        }

        systemName = currentSystemName;
        systemAddress = currentSystemAddress;
        forceShow = false;
        manuallyHidden = false;
        summary = null;
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
        loadCancellation = null;
        NotifyStationState();
        if (string.IsNullOrWhiteSpace(currentSystemName)
            || currentSystemAddress <= 0)
        {
            StatusMessage = "Waiting for a current system.";
            PendingLoad = Task.CompletedTask;
            return PendingLoad;
        }

        loadCancellation = new CancellationTokenSource();
        PendingLoad = LoadAsync(
            currentSystemName,
            currentSystemAddress,
            loadCancellation);
        return PendingLoad;
    }

    public void UpdateQuestTags(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var next = tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (questTags.SetEquals(next))
        {
            return;
        }

        questTags = next;
        OnPropertyChanged(nameof(IsQuestTagged));
    }

    public void UpdateStatus(EliteStatus? currentStatus)
    {
        if (disposed)
        {
            return;
        }

        status = currentStatus;
        if (!forceShow
            && OverlayGameModeResolver.Resolve(
                currentStatus,
                musicTrack: musicTrack)
                != OverlayGameMode.ExternalPanel)
        {
            manuallyHidden = false;
        }

        NotifyStationState();
    }

    public void UpdateMusicTrack(string? currentMusicTrack)
    {
        if (disposed || string.Equals(
                musicTrack,
                currentMusicTrack,
                StringComparison.Ordinal))
        {
            return;
        }

        musicTrack = currentMusicTrack;
        NotifyStationState();
    }

    public bool ToggleForcedVisibility()
    {
        if (disposed || !AutoShow)
        {
            return false;
        }

        if (forceShow)
        {
            forceShow = false;
        }
        else if (AutoShow
            && OverlayGameModeResolver.Resolve(
                status,
                musicTrack: musicTrack)
                == OverlayGameMode.ExternalPanel
            && HasSelectedStation)
        {
            manuallyHidden = !manuallyHidden;
        }
        else
        {
            forceShow = true;
            manuallyHidden = false;
        }

        NotifyStationState();
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
        loadCancellation = null;
    }

    private async Task LoadAsync(
        string requestedSystemName,
        long requestedSystemAddress,
        CancellationTokenSource cancellation)
    {
        IsBusy = true;
        StatusMessage = $"Loading stations in {requestedSystemName}...";
        try
        {
            var result = await summaryClient.GetAsync(
                requestedSystemName,
                requestedSystemAddress,
                cancellation.Token);
            if (cancellation.IsCancellationRequested
                || !ReferenceEquals(loadCancellation, cancellation))
            {
                return;
            }

            summary = result.Summary;
            StatusMessage = result.Warnings.Count == 0
                ? $"Loaded {summary.Stations.Count:N0} station(s)."
                : string.Join(" ", result.Warnings);
            NotifyStationState();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer station request superseded this one.
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or IOException
                or InvalidDataException)
        {
            if (ReferenceEquals(loadCancellation, cancellation))
            {
                StatusMessage =
                    $"Station information is unavailable: {exception.Message}";
                NotifyStationState();
            }
        }
        finally
        {
            if (ReferenceEquals(loadCancellation, cancellation))
            {
                IsBusy = false;
            }
        }
    }

    private void SavePreferences()
    {
        if (settingsStore is null)
        {
            return;
        }

        try
        {
            settingsStore.Save(new StationInfoPreferences(AutoShow));
            SettingsStatus = string.Empty;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            SettingsStatus =
                $"Station information settings could not be saved: {exception.Message}";
        }
    }

    private void NotifyStationState()
    {
        RefreshStationCollections();
        OnPropertyChanged(nameof(SelectedStation));
        OnPropertyChanged(nameof(HasSelectedStation));
        OnPropertyChanged(nameof(IsForced));
        OnPropertyChanged(nameof(ShouldShow));
        OnPropertyChanged(nameof(StationName));
        OnPropertyChanged(nameof(IsQuestTagged));
        OnPropertyChanged(nameof(StationType));
        OnPropertyChanged(nameof(LargestPadText));
        OnPropertyChanged(nameof(PrimaryEconomyText));
        OnPropertyChanged(nameof(FactionText));
        OnPropertyChanged(nameof(EconomyLines));
        OnPropertyChanged(nameof(RelevantServices));
        OnPropertyChanged(nameof(HasRelevantServices));
        OnPropertyChanged(nameof(ProhibitedCommodities));
        OnPropertyChanged(nameof(HasProhibitedCommodities));
        OnPropertyChanged(nameof(UpdatedText));
    }

    private void RefreshStationCollections()
    {
        var station = SelectedStation;
        if (ReferenceEquals(projectedStation, station))
        {
            return;
        }

        projectedStation = station;
        economyLines = station?.Economies
            .OrderByDescending(economy => economy.Value)
            .ThenBy(economy => economy.Key)
            .Select(economy => new StationInfoLineViewModel(
                economy.Key,
                $"{economy.Value:F0}%"))
            .ToArray() ?? [];
        if (station is null)
        {
            relevantServices = [];
            return;
        }

        var services = InterestingServices
            .Where(service => station.Services.Contains(
                service,
                StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (string.Equals(
            station.Government,
            "Engineer",
            StringComparison.OrdinalIgnoreCase))
        {
            services.Add("Engineer");
        }

        relevantServices = services.ToArray();
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
}

internal sealed class StationInfoEditorPreview
{
    public required string StationName { get; init; }

    public required string StationType { get; init; }

    public required string LargestPad { get; init; }

    public required string PrimaryEconomy { get; init; }

    public required string Faction { get; init; }

    public required string Updated { get; init; }

    public required bool IsQuestTagged { get; init; }

    public required IReadOnlyList<StationInfoLineViewModel> Economies { get; init; }

    public required IReadOnlyList<string> Services { get; init; }

    public required IReadOnlyList<string> Prohibited { get; init; }
}

public sealed record StationInfoLineViewModel(string Label, string Value);
