using System.ComponentModel;
using System.Globalization;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Owns one read-only build preview, refreshing only while its popout is open and retaining the last snapshot after a failed refresh.</summary>
public sealed class ColonizationProjectPreviewViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IRavenColonialProjectReader reader;
    private readonly Func<IReadOnlyList<string>> buildIds;
    private readonly bool isCombined;
    private readonly Func<bool> isEnabled;
    private readonly Func<int> currentShipCapacity;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken lifetimeToken;
    private bool disposed;
    private bool isRunning;
    private bool isExporting;
    private int shipCapacity;

    /// <summary>Captures the clicked project identity and obtains live ship and Raven consent state at each refresh.</summary>
    public ColonizationProjectPreviewViewModel(
        IRavenColonialProjectReader reader,
        string buildId,
        Func<bool>? isEnabled = null,
        Func<int>? currentShipCapacity = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null
    )
        : this(reader, () => [buildId], false, isEnabled, currentShipCapacity, delayAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
    }

    /// <summary>Reports every current workspace build independently of Show selection, refreshing membership along with live data.</summary>
    public ColonizationProjectPreviewViewModel(
        IRavenColonialProjectReader reader,
        Func<IReadOnlyList<string>> buildIds,
        Func<bool>? isEnabled = null,
        Func<int>? currentShipCapacity = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null
    )
        : this(reader, buildIds, true, isEnabled, currentShipCapacity, delayAsync) { }

    /// <summary>Shares consent, timing, and cancellation between individual and combined reports.</summary>
    private ColonizationProjectPreviewViewModel(
        IRavenColonialProjectReader reader,
        Func<IReadOnlyList<string>> buildIds,
        bool isCombined,
        Func<bool>? isEnabled,
        Func<int>? currentShipCapacity,
        Func<TimeSpan, CancellationToken, Task>? delayAsync
    )
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.buildIds = buildIds ?? throw new ArgumentNullException(nameof(buildIds));
        this.isCombined = isCombined;
        this.isEnabled = isEnabled ?? (() => true);
        this.currentShipCapacity = currentShipCapacity ?? (() => 0);
        this.delayAsync = delayAsync ?? Task.Delay;
        lifetimeToken = lifetime.Token;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ColonizationProjectPreview? Snapshot { get; private set; }
    public bool IsBusy { get; private set; }
    public bool CanExport => Snapshot is not null && !isExporting && !disposed;
    public string Title
    {
        get
        {
            if (isCombined)
            {
                return "Combined Build Report";
            }
            return Snapshot?.Project.BuildName is { } name && !string.IsNullOrWhiteSpace(name) ? name : "Build preview";
        }
    }
    public string WindowTitle => $"{Title} - Raven build preview";
    public bool IsCombinedReport => isCombined;
    public string DetailsTitle => isCombined ? "Build projects in this report" : "Project details";
    public string EffectsDescription =>
        isCombined
            ? "Combined reference effects, grouped by system. Actual system totals depend on other sites and links."
            : "Reference effects for this build type. Actual system totals depend on other sites and links.";
    public string Subtitle
    {
        get
        {
            if (Snapshot is not { } snapshot)
            {
                return "Loading live Raven build data…";
            }
            return isCombined
                ? CombinedSubtitle(snapshot)
                : $"{snapshot.Project.SystemName} · {snapshot.Effects?.Name ?? snapshot.Project.BuildType} ({snapshot.Project.BuildType})";
        }
    }

    /// <summary>States aggregate membership independently of overlay selection, with readable singular counts.</summary>
    private static string CombinedSubtitle(ColonizationProjectPreview snapshot)
    {
        string projectsText =
            snapshot.Projects.Count == 1 ? "1 build project" : $"{snapshot.Projects.Count:N0} build projects";
        int systems = snapshot
            .Projects.Select(project => project.SystemName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        string systemsText = systems == 1 ? "1 system" : $"{systems:N0} systems";
        return $"{projectsText} · {systemsText} · includes all projects regardless of Show selection";
    }

    public string Status { get; private set; } = "Loading live Raven build data…";
    public string ExportStatus { get; private set; } = string.Empty;
    public string RefreshedAt =>
        Snapshot is { } snapshot
            ? $"Updated {snapshot.FetchedAt.ToLocalTime():G} · refreshes every 30 seconds"
            : string.Empty;
    public string RemainingText => Quantity(Snapshot?.Remaining);
    public string DeficitText => Quantity(Snapshot?.CarrierDeficit);
    public string ReadyText => Quantity(Snapshot?.ReadyOnCarriers);
    public string DeliveredText => Quantity(Snapshot?.Delivered);
    public string RemainingTrips => TripsText(Snapshot?.Remaining);
    public string DeficitTrips => TripsText(Snapshot?.CarrierDeficit);
    public string CurrentShipTrips =>
        $"Current ship ({shipCapacity:N0} tonnes): {Quantity(ColonizationProjectPreview.Trips(Snapshot?.Remaining, shipCapacity))} trips";
    public bool HasCurrentShip => shipCapacity > 0;
    public double Progress => Snapshot?.Progress ?? 0;
    public string ProgressText =>
        Snapshot?.Progress is { } progress ? $"{progress:0}% delivered" : "Progress unavailable";
    public double ReadyProgress =>
        Snapshot?.MaximumRequired > 0 && Snapshot.ReadyOnCarriers is { } ready
            ? 100d * ready / Snapshot.MaximumRequired
            : 0;
    public IReadOnlyList<PreviewField> Details { get; private set; } = [];
    public IReadOnlyList<PreviewField> Effects { get; private set; } = [];
    public IReadOnlyList<PreviewEffectGroup> EffectGroups { get; private set; } = [];
    public IReadOnlyList<PreviewField> Commanders { get; private set; } = [];
    public IReadOnlyList<PreviewField> CarrierDetails { get; private set; } = [];
    public IReadOnlyList<PreviewField> DeliveryTotals { get; private set; } = [];
    public IReadOnlyList<PreviewDelivery> DeliveryHistory { get; private set; } = [];
    public IReadOnlyList<PreviewCommodityRow> Rows { get; private set; } = [];
    public IReadOnlyList<string> CarrierHeaders { get; private set; } = [];
    public string HistoryStatus =>
        Snapshot?.Statistics switch
        {
            null => "Delivery history unavailable.",
            { TotalDeliveries: 0 } => "No tracked deliveries yet.",
            var statistics => $"{statistics.TotalDeliveries:N0} tracked deliveries",
        };

    /// <summary>Runs a single refresh loop until the owning window closes; manual refreshes share its nonoverlapping request guard.</summary>
    public async Task RunAsync()
    {
        if (isRunning || disposed)
        {
            return;
        }
        isRunning = true;
        try
        {
            while (!lifetimeToken.IsCancellationRequested)
            {
                await RefreshAsync();
                await delayAsync(TimeSpan.FromSeconds(30), lifetimeToken);
            }
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
            // Closing the window is the normal end of its live refresh loop.
        }
        finally
        {
            isRunning = false;
        }
    }

    /// <summary>Loads fresh data without publishing anything, distinguishing a missing build from a failed request or unavailable optional section.</summary>
    public async Task RefreshAsync()
    {
        if (IsBusy || disposed)
        {
            return;
        }
        if (!isEnabled())
        {
            Status = "Raven Colonial access is off.";
            Notify();
            return;
        }
        IsBusy = true;
        Notify();
        try
        {
            ColonizationProjectPreview? snapshot = await ReadSnapshotAsync();
            if (disposed)
            {
                return;
            }
            if (snapshot is null)
            {
                Snapshot = null;
                ClearRows();
                Status = isCombined
                    ? "No build projects are available in the workspace."
                    : "This build is no longer available on Raven Colonial.";
                return;
            }
            Snapshot = snapshot;
            ApplySnapshot(Snapshot);
            Status = (Snapshot.CarrierDeficit, Snapshot.Statistics) switch
            {
                (null, _) => "Carrier cargo unavailable. Shortages and trips cannot be confirmed.",
                (_, null) => "Cargo updated. Delivery history unavailable.",
                _ => "Live Raven data · read-only preview",
            };
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
            // A closed preview must not report its cancellation as a service failure.
        }
        catch (Exception exception)
            when (exception is HttpRequestException or IOException or InvalidDataException or OperationCanceledException
            )
        {
            Status = Snapshot is null
                ? "Could not load this report. Choose Refresh to try again."
                : "Refresh failed. Showing the last successful snapshot.";
        }
        finally
        {
            IsBusy = false;
            Notify();
        }
    }

    /// <summary>Reads distinct builds sequentially to bound request bursts and commits only a complete report snapshot.</summary>
    private async Task<ColonizationProjectPreview?> ReadSnapshotAsync()
    {
        var data = new List<ColonizationProjectPreviewData>();
        foreach (string id in buildIds().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            lifetimeToken.ThrowIfCancellationRequested();
            ColonizationProjectPreviewData? build = await reader.ReadProjectPreviewAsync(id, lifetimeToken);
            if (build is null)
            {
                if (isCombined)
                {
                    throw new InvalidDataException(
                        "A build is no longer available. Refresh the workspace project list."
                    );
                }
                return null;
            }
            data.Add(build);
        }
        if (data.Count == 0)
        {
            return null;
        }
        return isCombined
            ? ColonizationProjectPreview.CreateCombined(data, DateTimeOffset.UtcNow)
            : new ColonizationProjectPreview(data[0], DateTimeOffset.UtcNow);
    }

    /// <summary>Exports the captured displayed snapshot, allowing a refresh during the save picker without changing the file's content.</summary>
    public async Task ExportAsync(Func<string, string, Task<bool>> saveAsync)
    {
        if (!CanExport || Snapshot is not { } snapshot)
        {
            return;
        }
        isExporting = true;
        Notify();
        try
        {
            if (
                await saveAsync(
                    ColonizationProjectCsvExporter.SuggestedFileName(snapshot),
                    ColonizationProjectCsvExporter.Write(snapshot, shipCapacity)
                )
            )
            {
                ExportStatus = "CSV exported.";
            }
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidOperationException
                        or NotSupportedException
            )
        {
            ExportStatus = "Could not export CSV. Check the destination and try again.";
        }
        finally
        {
            isExporting = false;
            Notify();
        }
    }

    /// <summary>Cancels pending network work and refresh timing when the window or application closes.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        lifetime.Cancel();
        lifetime.Dispose();
        Notify();
    }

    /// <summary>Projects public data into native table and progress rows without borrowing aggregate overlay state.</summary>
    private void ApplySnapshot(ColonizationProjectPreview snapshot)
    {
        shipCapacity = Math.Max(0, currentShipCapacity());
        CarrierHeaders = snapshot.Carriers.Select(carrier => carrier.Name).ToArray();
        Details = snapshot.IsCombined
            ? ColonizationProjectCsvExporter
                .BuildDetails(snapshot)
                .Select(pair => new PreviewField(pair.Key, pair.Value))
                .ToArray()
            : ColonizationProjectCsvExporter
                .Details(snapshot)
                .Take(8)
                .Concat([KeyValuePair.Create("Notes", snapshot.Project.Notes ?? string.Empty)])
                .Select(pair => new PreviewField(pair.Key, pair.Value))
                .ToArray();
        if (snapshot.IsCombined)
        {
            EffectGroups = ColonizationProjectCsvExporter
                .SystemEffectGroups(snapshot)
                .Select(system => new PreviewEffectGroup(
                    system.Name,
                    system.Fields.Select(pair => new PreviewField(pair.Key, pair.Value)).ToArray()
                ))
                .ToArray();
            Effects = ColonizationProjectCsvExporter
                .CombinedEffectDetails(snapshot)
                .Select(pair => new PreviewField(pair.Key, pair.Value))
                .ToArray();
        }
        else
        {
            Effects = snapshot.Effects is { } effects
                ? ColonizationProjectCsvExporter
                    .EffectDetails(effects)
                    .Select(pair => new PreviewField(pair.Key, pair.Value))
                    .ToArray()
                : [new PreviewField("System effects", "Not available for this build type.")];
        }
        Commanders = snapshot
            .Commanders.Select(pair => new PreviewField(
                pair.Key,
                pair.Value.Count == 0 ? "No commodity assignments" : string.Join(", ", pair.Value)
            ))
            .ToArray();
        CarrierDetails = snapshot
            .Carriers.Select(carrier => new PreviewField(
                carrier.Label,
                $"{Quantity(carrier.TotalCargo)} tonnes · total carrier cargo"
            ))
            .ToArray();
        Rows = snapshot
            .Rows.Select(row => new PreviewCommodityRow(
                row.Category,
                row.Name,
                Quantity(row.Need),
                row.CarrierDifference?.ToString("+0;-0;0", CultureInfo.CurrentCulture) ?? "Unknown",
                row.CarrierQuantities.Select(quantity => Quantity(quantity)).ToArray()
            ))
            .ToArray();
        DeliveryTotals =
            snapshot
                .Statistics?.Cmdrs.Select(pair => new PreviewField(pair.Key, $"{pair.Value:N0} tonnes delivered"))
                .ToArray()
            ?? [];
        long maximum = Math.Max(
            1,
            snapshot.Statistics?.Stats.Select(bucket => bucket.Total).DefaultIfEmpty(0).Max() ?? 0
        );
        DeliveryHistory =
            snapshot
                .Statistics?.Stats.OrderBy(bucket => bucket.Time)
                .Select(bucket => new PreviewDelivery(
                    bucket.Time.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    Quantity(bucket.Total),
                    string.Join(" · ", bucket.Cmdrs.Select(pair => $"{pair.Key}: {pair.Value:N0}")),
                    100d * bucket.Total / maximum
                ))
                .ToArray()
            ?? [];
    }

    /// <summary>Clears every project-specific row when Raven reports that the build no longer exists.</summary>
    private void ClearRows()
    {
        shipCapacity = 0;
        Details = Effects = Commanders = CarrierDetails = DeliveryTotals = [];
        EffectGroups = [];
        Rows = [];
        CarrierHeaders = [];
        DeliveryHistory = [];
    }

    /// <summary>Publishes dependent property updates as one snapshot change.</summary>
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    /// <summary>Formats unknown cargo explicitly so missing service data cannot look like an empty carrier.</summary>
    private static string Quantity(long? quantity) => quantity?.ToString("N0", CultureInfo.CurrentCulture) ?? "Unknown";

    /// <summary>Matches Raven's default large and medium ship trip estimates while stating their capacities.</summary>
    private static string TripsText(long? quantity) =>
        $"Large ({ColonizationProjectPreview.LargeShipCapacity:N0}t): {Quantity(ColonizationProjectPreview.Trips(quantity, ColonizationProjectPreview.LargeShipCapacity))} trips · Medium ({ColonizationProjectPreview.MediumShipCapacity:N0}t): {Quantity(ColonizationProjectPreview.Trips(quantity, ColonizationProjectPreview.MediumShipCapacity))} trips";
}

/// <summary>A label and public value rendered in read-only project cards.</summary>
public sealed record PreviewField(string Label, string Value);

/// <summary>A system heading with short effect rows so combined reports remain readable across long system names.</summary>
public sealed record PreviewEffectGroup(string Name, IReadOnlyList<PreviewField> Fields);

/// <summary>A commodity row whose carrier cells stay aligned with the linked carrier headers.</summary>
public sealed record PreviewCommodityRow(
    string Category,
    string Commodity,
    string Need,
    string Difference,
    IReadOnlyList<string> Quantities
);

/// <summary>An hourly delivery bar with its commander breakdown available alongside it.</summary>
public sealed record PreviewDelivery(string Time, string Cargo, string Commanders, double Percentage);
