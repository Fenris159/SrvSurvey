using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Journeys;

public sealed partial class JourneyService
{
    public bool IsActiveFor(string? frontierId, bool isOdyssey)
    {
        JourneyDocument? journey = ActiveJourney;
        return journey is not null
            && activeIsOdyssey == isOdyssey
            && string.Equals(journey.FrontierId, frontierId, StringComparison.OrdinalIgnoreCase);
    }

    public JourneyRecoveryReference? RecoveryReference { get; private set; }

    private async Task<JourneyServiceResult> ResolveActiveJourneyAsync(
        string frontierId,
        bool isOdyssey,
        CommanderProfileData profile,
        bool replay,
        CancellationToken cancellationToken
    )
    {
        RecoveryReference = null;
        LogState("resolve", frontierId, isOdyssey, profile.ActiveJourneyFileName);
        if (string.IsNullOrWhiteSpace(profile.ActiveJourneyFileName))
        {
            activeProcessor = null;
            return JourneyServiceResult.Empty;
        }
        string fileName;
        try
        {
            fileName = JourneyStore.NormalizeReference(profile.ActiveJourneyFileName);
        }
        catch (ArgumentException exception)
        {
            return Unavailable(frontierId, isOdyssey, profile.ActiveJourneyFileName, exception.Message);
        }
        JourneyLoadResult load = await journeyStore
            .UpgradeAsync(frontierId, fileName, cancellationToken)
            .ConfigureAwait(false);
        if (load.Journey is not { } journey)
        {
            if (!load.Exists)
            {
                activeProcessor = null;
                if (
                    !await profileStore
                        .TryReplaceActiveJourneyAsync(
                            frontierId,
                            isOdyssey,
                            profile.ActiveJourneyFileName,
                            null,
                            cancellationToken
                        )
                        .ConfigureAwait(false)
                )
                {
                    return SelectionChanged();
                }
                return new(
                    null,
                    [$"The active journey file was missing. Its active selection was cleared: {load.Path}"],
                    0
                );
            }
            return Unavailable(
                frontierId,
                isOdyssey,
                profile.ActiveJourneyFileName,
                load.Error ?? "The active journey could not be loaded."
            );
        }
        if (!journey.IsActive)
        {
            activeProcessor = null;
            if (
                !await profileStore
                    .TryReplaceActiveJourneyAsync(
                        frontierId,
                        isOdyssey,
                        profile.ActiveJourneyFileName,
                        null,
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            )
            {
                return SelectionChanged();
            }
            return new(
                journey,
                ["The commander profile pointed to a concluded journey. Its active reference was cleared."],
                0
            );
        }
        if (
            profile.ActiveJourneyFileName != journey.FileName
            && !await profileStore
                .TryReplaceActiveJourneyAsync(
                    frontierId,
                    isOdyssey,
                    profile.ActiveJourneyFileName,
                    journey.FileName,
                    cancellationToken
                )
                .ConfigureAwait(false)
        )
        {
            return SelectionChanged();
        }
        return await ResumeSavedJourneyAsync(journey, isOdyssey, replay, load.Error, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<JourneyServiceResult> ResumeSavedJourneyAsync(
        JourneyDocument journey,
        bool isOdyssey,
        bool replay,
        string? upgradeWarning,
        CancellationToken cancellationToken
    )
    {
        if (
            replay
            || activeProcessor?.Journey.FileName != journey.FileName
            || activeProcessor.Journey.FrontierId != journey.FrontierId
            || activeIsOdyssey != isOdyssey
        )
        {
            activeProcessor = new(journey, exobiologyCatalog, isOdyssey);
            activeIsOdyssey = isOdyssey;
        }
        IReadOnlyList<string> warnings = upgradeWarning is null ? [] : [upgradeWarning];
        if (!replay)
        {
            return new(activeProcessor.Journey, warnings, 0);
        }
        try
        {
            JourneyServiceResult caughtUp = await CatchUpAndActivateAsync(journey, isOdyssey, cancellationToken)
                .ConfigureAwait(false);
            return caughtUp with { Errors = [.. warnings, .. caughtUp.Errors] };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            string warning =
                $"Journey {journey.Name} was restored from its saved history. Journal catch-up is unavailable; you can continue or conclude it. {exception.Message}";
            log?.Invoke(warning);
            return new(activeProcessor.Journey, [.. warnings, warning], 0);
        }
    }

    private JourneyServiceResult Unavailable(string frontierId, bool isOdyssey, string fileName, string error)
    {
        activeProcessor = null;
        RecoveryReference = new(frontierId, isOdyssey, fileName);
        log?.Invoke($"Journeys: unavailable active selection for {frontierId}: {error}");
        return new(
            null,
            [
                error,
                "The saved journey file was kept. You can begin a new journey or clear its unavailable active selection.",
            ],
            0
        )
        {
            Recovery = RecoveryReference,
        };
    }

    public async Task<bool> ClearUnavailableJourneyAsync(
        JourneyRecoveryReference expected,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(expected);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CommanderProfileLoadResult profile = await profileStore
                .LoadAsync(expected.FrontierId, expected.IsOdyssey, cancellationToken)
                .ConfigureAwait(false);
            if (profile.Data is not { } data || data.ActiveJourneyFileName != expected.FileName)
            {
                return false;
            }
            JourneyLoadResult? load = null;
            try
            {
                load = await journeyStore
                    .LoadAsync(
                        expected.FrontierId,
                        JourneyStore.NormalizeReference(expected.FileName),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            catch (ArgumentException)
            {
                // An invalid reference can be detached without opening or changing any saved file.
            }
            if (load?.Journey is not null)
            {
                return false;
            }
            if (
                !await profileStore
                    .TryReplaceActiveJourneyAsync(
                        expected.FrontierId,
                        expected.IsOdyssey,
                        expected.FileName,
                        null,
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            )
            {
                return false;
            }
            if (activeProcessor?.Journey.FrontierId == expected.FrontierId && activeIsOdyssey == expected.IsOdyssey)
            {
                activeProcessor = null;
            }
            if (RecoveryReference == expected)
            {
                RecoveryReference = null;
            }
            return true;
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<JourneyServiceResult> ResumeAsync(
        string frontierId,
        bool isOdyssey,
        string fileName,
        CancellationToken cancellationToken = default
    )
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CommanderProfileLoadResult profile = await profileStore
                .LoadAsync(frontierId, isOdyssey, cancellationToken)
                .ConfigureAwait(false);
            CommanderProfileData data = profile.Data ?? throw new InvalidDataException(profile.Error);
            JourneyServiceResult current = await ResolveActiveJourneyAsync(
                    frontierId,
                    isOdyssey,
                    data,
                    replay: false,
                    cancellationToken
                )
                .ConfigureAwait(false);
            string selected = JourneyStore.NormalizeReference(fileName);
            if (current.Journey?.IsActive == true && current.Journey.FileName != selected)
            {
                throw new InvalidOperationException("Complete the current active journey before resuming another one.");
            }
            JourneyLoadResult load = await journeyStore
                .UpgradeAsync(frontierId, selected, cancellationToken)
                .ConfigureAwait(false);
            JourneyDocument journey =
                load.Journey ?? throw new InvalidDataException(load.Error ?? "The saved journey could not be opened.");
            if (!journey.IsActive)
            {
                throw new InvalidOperationException("A completed journey cannot be resumed.");
            }
            string? expectedReference =
                RecoveryReference is not null || string.IsNullOrWhiteSpace(data.ActiveJourneyFileName)
                    ? data.ActiveJourneyFileName
                    : null;
            if (current.Journey?.IsActive == true)
            {
                expectedReference = current.Journey.FileName;
            }
            if (
                !await profileStore
                    .TryReplaceActiveJourneyAsync(
                        frontierId,
                        isOdyssey,
                        expectedReference,
                        journey.FileName,
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            )
            {
                return SelectionChanged();
            }
            RecoveryReference = null;
            JourneyServiceResult resumed = await ResumeSavedJourneyAsync(
                    journey,
                    isOdyssey,
                    replay: true,
                    load.Error,
                    cancellationToken
                )
                .ConfigureAwait(false);
            return resumed with { Errors = [.. current.Errors, .. resumed.Errors] };
        }
        finally
        {
            operationLock.Release();
        }
    }

    private JourneyServiceResult SelectionChanged()
    {
        activeProcessor = null;
        return new(
            null,
            ["The active journey selection changed while loading. Refresh Journeys to load the latest selection."],
            0
        );
    }

    private void LogState(string action, string frontierId, bool isOdyssey, string? reference) =>
        log?.Invoke(
            $"Journeys: {action}; commander={frontierId}; mode={(isOdyssey ? "live" : "legacy")}; profile={profileStore.GetProfilePath(frontierId, isOdyssey)}; savedReferencePresent={!string.IsNullOrWhiteSpace(reference)}; memoryCommander={activeProcessor?.Journey.FrontierId ?? "none"}; memoryJourney={activeProcessor?.Journey.FileName ?? "none"}."
        );
}
