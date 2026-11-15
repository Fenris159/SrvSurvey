using System.Globalization;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.ViewModels;

public sealed record RavenBodyRemovalWarning(string SystemName, long SystemAddress, IReadOnlyList<string> BodyNames);

public sealed partial class ColonizationSystemEditorViewModel
{
    private readonly AsyncCommand rediscoverBodiesCommand;
    private readonly AsyncCommand updateScannedBodiesCommand;
    private ColonizationBodyJournalCatalog? bodyJournalCatalog;
    private CancellationTokenSource? bodyCatalogCancellation;
    private bool bodyCatalogCheckPending;
    private readonly Queue<JournalEventEnvelope> bufferedBodyJournalEvents = [];
    private bool isLoadingSystemForBodies;
    private readonly HashSet<long> bodyCatalogLoadTriggers = [];
    private bool bodyCatalogCommanderConfirmed = true;

    public IColonizationBodyRecoveryClient? BodyRecoveryClient { get; set; }
    public bool BodyCatalogUpdatesAllowed { get; set; } = true;
    public Action<string>? BodyCatalogLog { get; set; }
    public Func<long, bool>? HasLocalFssCompletion { get; set; }
    public Func<
        long,
        CancellationToken,
        Task<IReadOnlyList<JournalEventEnvelope>>
    >? BodyJournalHistoryReader { get; set; }
    public event Func<RavenBodyRemovalWarning, Task<bool>>? BodyRemovalConfirmationRequested;

    public System.Windows.Input.ICommand RediscoverBodiesCommand => rediscoverBodiesCommand;
    public System.Windows.Input.ICommand UpdateScannedBodiesCommand => updateScannedBodiesCommand;
    public Task BodyCatalogUpdateTask { get; private set; } = Task.CompletedTask;
    public bool CanRediscoverBodies => CanUpdateBodyCatalog && BodyRecoveryClient is not null;
    private bool CanUpdateBodyCatalog =>
        !catalogDisposed
        && BodyCatalogUpdatesAllowed
        && bodyCatalogCommanderConfirmed
        && system is not null
        && CanEdit
        && context.IsExternalDataEnabled
        && !IsBusy
        && client is IRavenColonialBodyClient;

    private void ApplyBodyCatalogEvents(IReadOnlyList<JournalEventEnvelope> entries, bool allowPublishing)
    {
        if ((isLoadingSystemForBodies || !bodyCatalogCommanderConfirmed && system is not null) && allowPublishing)
        {
            foreach (JournalEventEnvelope entry in entries)
            {
                if (BodyCatalogTriggerAddress(entry) is { } address)
                {
                    bodyCatalogLoadTriggers.Add(address);
                }
            }
        }
        bool triggered =
            bodyJournalCatalog?.Apply(
                entries,
                system is { } loaded && HasLocalFssCompletion?.Invoke(loaded.SystemAddress) == true
            ) == true;
        if (triggered && allowPublishing)
        {
            bodyCatalogCheckPending = true;
            StartPendingBodyCatalogCheck();
        }
    }

    private static long? BodyCatalogTriggerAddress(JournalEventEnvelope entry) =>
        entry.EventName is "FSSDiscoveryScan" or "FSSAllBodiesFound" or "NavBeaconScan"
        && entry.Payload.TryGetProperty("SystemAddress", out System.Text.Json.JsonElement address)
        && address.ValueKind == System.Text.Json.JsonValueKind.Number
        && address.TryGetInt64(out long value)
        && value > 0
            ? value
            : null;

    private void BeginBodyCatalogLoad(bool preserveLiveTriggers = false)
    {
        isLoadingSystemForBodies = true;
        if (!preserveLiveTriggers)
        {
            bodyCatalogLoadTriggers.Clear();
        }
        bodyCatalogCheckPending = false;
        bodyJournalCatalog = null;
    }

    internal void SuspendBodyCatalogForCommanderChange(bool discardSiteHistory = true)
    {
        contextVersion++;
        sessionVersion++;
        bodyCatalogCommanderConfirmed = false;
        if (discardSiteHistory)
        {
            bufferedJournalEvents.Clear();
        }
        bufferedBodyJournalEvents.Clear();
        ResetBodyCatalogWork();
        RaiseCommandStates();
    }

    internal async Task ConfirmBodyCatalogCommanderAsync(string? commander)
    {
        if (
            bodyCatalogCommanderConfirmed
            || !string.Equals(commander, context.CommanderName, StringComparison.OrdinalIgnoreCase)
        )
        {
            return;
        }
        int version = contextVersion;
        if (system is { } loaded)
        {
            BeginBodyCatalogLoad(preserveLiveTriggers: true);
            try
            {
                await LoadBodyJournalHistoryAsync(loaded, version);
            }
            finally
            {
                isLoadingSystemForBodies = false;
            }
        }
        if (version != contextVersion || catalogDisposed)
        {
            return;
        }
        bodyCatalogCommanderConfirmed = true;
        RaiseCommandStates();
        StartPendingBodyCatalogCheck();
    }

    private void StartPendingBodyCatalogCheck()
    {
        if (!bodyCatalogCheckPending || !CanUpdateBodyCatalog)
        {
            return;
        }

        bodyCatalogCheckPending = false;
        BodyCatalogUpdateTask = UpdateScannedBodiesAsync();
    }

    private async Task<(ColonizationSystemRecord System, string? Warning)> ImportMissingBodiesForLoadAsync(
        ColonizationSystemRecord loaded
    )
    {
        if (
            loaded.Bodies is not null
            || client is not IRavenColonialBodyClient
            || !bodyCatalogCommanderConfirmed
            || !BodyCatalogUpdatesAllowed
        )
        {
            return (loaded, null);
        }
        try
        {
            ColonizationSystemRecord imported = await client.ImportSystemBodiesAsync(
                loaded.SystemAddress.ToString(CultureInfo.InvariantCulture),
                CancellationToken.None
            );
            if (
                imported.SystemAddress != loaded.SystemAddress
                || !string.Equals(imported.Name, loaded.Name, StringComparison.OrdinalIgnoreCase)
            )
            {
                throw new InvalidDataException("Raven's body import returned a different system.");
            }
            return (imported, null);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return (
                loaded,
                "The system loaded, but Raven's body import failed. You can use Rediscover Absent Bodies or complete local scans to recover it. "
                    + exception.Message
            );
        }
    }

    private async Task LoadBodyJournalHistoryAsync(ColonizationSystemRecord loaded, int version)
    {
        var catalog = new ColonizationBodyJournalCatalog(loaded.SystemAddress, loaded.Name);
        IReadOnlyList<JournalEventEnvelope> history = [];
        if (BodyJournalHistoryReader is { } read)
        {
            try
            {
                history = await read(loaded.SystemAddress, CancellationToken.None);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                BodyCatalogLog?.Invoke("Raven body journal backfill could not be read: " + exception.Message);
            }
        }
        if (version != contextVersion)
        {
            return;
        }

        catalog.Apply(
            ColonizationBodyJournalHistoryReader.MergeWithBufferedEvents(history, bufferedBodyJournalEvents),
            HasLocalFssCompletion?.Invoke(loaded.SystemAddress) == true
        );
        bodyJournalCatalog = catalog;
        bodyCatalogCheckPending |= bodyCatalogLoadTriggers.Contains(loaded.SystemAddress);
        bodyCatalogLoadTriggers.Clear();
    }

    /// <summary>Mirrors upstream's explicit Next/reload check without adding a removal confirmation.</summary>
    public Task UpdateScannedBodiesAsync() => UpdateScannedBodiesCoreAsync(force: false);

    public Task UploadScannedBodiesAsync() => UpdateScannedBodiesCoreAsync(force: true);

    private async Task UpdateScannedBodiesCoreAsync(bool force)
    {
        if (!CanUpdateBodyCatalog || system is not { } loaded || bodyJournalCatalog is not { } catalog)
        {
            return;
        }

        if (!catalog.CanUpload)
        {
            StatusMessage =
                $"Complete the system FSS and collect all body scans before updating Raven ({catalog.ScannedBodyCount:N0}/{catalog.ExpectedBodyCount:N0}). Scan the nav beacon if body records are missing.";
            return;
        }
        await RunBodyCatalogUpdateAsync(
            loaded,
            async token =>
            {
                ColonizationSystemRecord latest = await ReadBodyTargetAsync(loaded, token);
                if (!catalog.CanUpload)
                {
                    StatusMessage =
                        "The local body catalog changed during the Raven check. Complete the FSS and collect all body scans before retrying.";
                    return null;
                }
                if (!force && (latest.Bodies?.Count ?? 0) >= catalog.ExpectedBodyCount)
                {
                    return null;
                }

                return catalog.CreateBodies();
            },
            "Local scans"
        );
    }

    /// <summary>Uses the loaded editor identity, independent of the live location or unsubmitted input text.</summary>
    public async Task RediscoverBodiesAsync()
    {
        if (!CanRediscoverBodies || system is not { } loaded || BodyRecoveryClient is not { } recoveryClient)
        {
            return;
        }

        int version = contextVersion;

        await RunBodyCatalogUpdateAsync(
            loaded,
            async token =>
            {
                StatusMessage = $"Recovering bodies for {loaded.Name} ({loaded.SystemAddress}) from Spansh and EDSM...";
                IReadOnlyList<ColonizationSystemBody> recovered = await recoveryClient.RecoverAsync(
                    loaded.SystemAddress,
                    loaded.Name,
                    loaded.Bodies ?? [],
                    token
                );
                ColonizationSystemRecord latest = await ReadBodyTargetAsync(loaded, token);
                token.ThrowIfCancellationRequested();
                if (!IsBodyTargetCurrent(loaded, version))
                {
                    return null;
                }
                IReadOnlyList<ColonizationSystemBody> proposed = ColonizationBodyCatalog.Merge(
                    recovered,
                    latest.Bodies
                );
                string[] removed = RemovedBodies(latest.Bodies, proposed);
                if (removed.Length > 0)
                {
                    var warning = new RavenBodyRemovalWarning(loaded.Name, loaded.SystemAddress, removed);
                    Task<bool>? confirmation = BodyRemovalConfirmationRequested?.Invoke(warning);
                    if (confirmation is null || !await confirmation)
                    {
                        StatusMessage = "Rediscovery cancelled. Raven's body catalog was not changed.";
                        return null;
                    }
                    token.ThrowIfCancellationRequested();
                    ColonizationSystemRecord checkedLatest = await ReadBodyTargetAsync(loaded, token);
                    if (!SameBodyIdentities(latest.Bodies, checkedLatest.Bodies))
                    {
                        throw new InvalidDataException(
                            "Raven's body catalog changed while the warning was open. Retry rediscovery to review the current removals."
                        );
                    }

                    proposed = ColonizationBodyCatalog.Merge(recovered, checkedLatest.Bodies);
                }
                return proposed;
            },
            "Spansh and EDSM"
        );
    }

    private async Task RunBodyCatalogUpdateAsync(
        ColonizationSystemRecord loaded,
        Func<CancellationToken, Task<IReadOnlyList<ColonizationSystemBody>?>> prepare,
        string source
    )
    {
        if (client is not IRavenColonialBodyClient publisher)
        {
            return;
        }

        int version = contextVersion;
        using var cancellation = new CancellationTokenSource();
        bodyCatalogCancellation = cancellation;
        IsBusy = true;
        try
        {
            IReadOnlyList<ColonizationSystemBody>? proposed = await prepare(cancellation.Token);
            if (proposed is null || !IsBodyTargetCurrent(loaded, version))
            {
                return;
            }

            cancellation.Token.ThrowIfCancellationRequested();
            IReadOnlyList<ColonizationSystemBody> updated = await publisher.UpdateSystemBodiesAsync(
                loaded.SystemAddress,
                proposed,
                cancellation.Token
            );
            if (!IsBodyTargetCurrent(loaded, version))
            {
                return;
            }

            ApplyUpdatedBodies(updated);
            StatusMessage =
                $"Updated {updated.Count:N0} Raven bodies for {loaded.Name} from {source}. Site edits were preserved.";
        }
        catch (OperationCanceledException)
        {
            if (IsBodyTargetCurrent(loaded, version))
            {
                StatusMessage = "The body catalog update was cancelled or timed out. Reload Raven before retrying.";
            }
        }
        catch (Exception exception)
            when (IsExpectedFailure(exception)
                || exception is System.Text.Json.JsonException or IOException or UnauthorizedAccessException
            )
        {
            if (IsBodyTargetCurrent(loaded, version))
            {
                StatusMessage = "The body catalog update could not be completed: " + exception.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(bodyCatalogCancellation, cancellation))
            {
                bodyCatalogCancellation = null;
            }

            IsBusy = false;
        }
    }

    private async Task<ColonizationSystemRecord> ReadBodyTargetAsync(
        ColonizationSystemRecord loaded,
        CancellationToken token
    )
    {
        ColonizationSystemRecord latest = await client.GetSystemAsync(
            loaded.SystemAddress.ToString(CultureInfo.InvariantCulture),
            token
        );
        if (
            latest.SystemAddress != loaded.SystemAddress
            || !string.Equals(latest.Name, loaded.Name, StringComparison.OrdinalIgnoreCase)
            || !CanCommanderEdit(latest, context.CommanderName)
        )
        {
            throw new InvalidDataException(
                "The loaded Raven system identity or architect changed. Reload the system before updating bodies."
            );
        }

        return latest;
    }

    private bool IsBodyTargetCurrent(ColonizationSystemRecord loaded, int version) =>
        !catalogDisposed
        && BodyCatalogUpdatesAllowed
        && contextVersion == version
        && system?.SystemAddress == loaded.SystemAddress
        && context.IsExternalDataEnabled;

    private void ApplyUpdatedBodies(IReadOnlyList<ColonizationSystemBody> updated)
    {
        IsBodyImportConfirmationPending = false;
        journalTracker?.UpdateKnownBodies(updated.Select(body => body.Number));
        system = system! with { Bodies = updated.Select(CloneBody).ToList() };
        bodies = updated.Select(body => new ColonizationSystemBodyOptionViewModel(body.Number, body.Name)).ToArray();
        ClearReview();
        OnPropertyChanged(nameof(Bodies));
        OnPropertyChanged(nameof(NeedsBodyImport));
        OnPropertyChanged(nameof(PlanningSnapshot));
        RaiseCommandStates();
    }

    private static string[] RemovedBodies(
        IReadOnlyList<ColonizationSystemBody>? previous,
        IReadOnlyList<ColonizationSystemBody> next
    ) =>
        (previous ?? [])
            .Where(body =>
                !next.Any(candidate =>
                    candidate.Number == body.Number
                    && string.Equals(candidate.Name, body.Name, StringComparison.OrdinalIgnoreCase)
                )
            )
            .Select(body => $"{body.Name} (BodyID {body.Number})")
            .ToArray();

    private static bool SameBodyIdentities(List<ColonizationSystemBody>? left, List<ColonizationSystemBody>? right) =>
        (left?.Count ?? 0) == (right?.Count ?? 0) && RemovedBodies(left, right ?? []).Length == 0;

    private void ResetBodyCatalogWork()
    {
        bodyCatalogCancellation?.Cancel();
        bodyCatalogCheckPending = false;
        bodyJournalCatalog = null;
        bodyCatalogLoadTriggers.Clear();
    }
}
