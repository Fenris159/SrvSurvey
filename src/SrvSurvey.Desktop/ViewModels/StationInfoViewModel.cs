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
    private string? systemName;
    private long systemAddress;
    private bool autoShow;
    private bool forceShow;
    private bool manuallyHidden;
    private bool isBusy;
    private string statusMessage = "Waiting for a current system.";
    private string settingsStatus = string.Empty;
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

    public bool HasSelectedStation => SelectedStation is not null;

    public bool IsForced => forceShow;

    public bool ShouldShow => HasSelectedStation
        && (forceShow
            || AutoShow
                && status?.GuiFocus == GuiFocus.ExternalPanel
                && !manuallyHidden);

    public string StationName => SelectedStation?.Name ?? "No station selected";

    public string StationType => SelectedStation?.Type ?? "Station information";

    public string LargestPadText => SelectedStation?.LandingPads?.Largest is { } pad
        ? $"Largest pad: {pad}"
        : "Landing-pad data unavailable";

    public string PrimaryEconomyText => SelectedStation?.PrimaryEconomy is { } economy
        ? $"Primary economy: {economy}"
        : "Primary economy unavailable";

    public string FactionText => SelectedStation is { } station
        && !string.IsNullOrWhiteSpace(station.ControllingFaction)
            ? string.IsNullOrWhiteSpace(station.Government)
                ? station.ControllingFaction
                : $"{station.ControllingFaction} · {station.Government}"
            : "Controlling faction unavailable";

    public IReadOnlyList<StationInfoLineViewModel> EconomyLines =>
        SelectedStation?.Economies
            .OrderByDescending(economy => economy.Value)
            .ThenBy(economy => economy.Key)
            .Select(economy => new StationInfoLineViewModel(
                economy.Key,
                $"{economy.Value:F0}%"))
            .ToArray()
        ?? [];

    public IReadOnlyList<string> RelevantServices
    {
        get
        {
            if (SelectedStation is not { } station)
            {
                return [];
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

            return services;
        }
    }

    public bool HasRelevantServices => RelevantServices.Count > 0;

    public IReadOnlyList<string> ProhibitedCommodities =>
        SelectedStation?.ProhibitedCommodities ?? [];

    public bool HasProhibitedCommodities => ProhibitedCommodities.Count > 0;

    public string UpdatedText => SelectedStation?.UpdatedAt is { } updated
        ? $"Spansh data updated {updated.ToLocalTime():d}"
        : "Spansh update time unavailable";

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

    public void UpdateStatus(EliteStatus? currentStatus)
    {
        if (disposed)
        {
            return;
        }

        status = currentStatus;
        if (!forceShow && currentStatus?.GuiFocus != GuiFocus.ExternalPanel)
        {
            manuallyHidden = false;
        }

        NotifyStationState();
    }

    public bool ToggleForcedVisibility()
    {
        if (disposed)
        {
            return false;
        }

        if (forceShow)
        {
            forceShow = false;
        }
        else if (AutoShow
            && status?.GuiFocus == GuiFocus.ExternalPanel
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
        OnPropertyChanged(nameof(SelectedStation));
        OnPropertyChanged(nameof(HasSelectedStation));
        OnPropertyChanged(nameof(IsForced));
        OnPropertyChanged(nameof(ShouldShow));
        OnPropertyChanged(nameof(StationName));
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

public sealed record StationInfoLineViewModel(string Label, string Value);
