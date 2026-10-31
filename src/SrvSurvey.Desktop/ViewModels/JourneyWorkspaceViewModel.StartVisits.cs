using System.Globalization;
using SrvSurvey.Core.Journeys;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class JourneyWorkspaceViewModel
{
    private IReadOnlyList<JourneyStartVisitViewModel> startVisits = [];
    private JourneyStartVisitViewModel? selectedStartVisit;
    private long? selectedStartSystemAddress;
    private int startVisitVersion;
    private bool useCurrentStart = true;
    private DateTime? visitsOnOrAfter;

    // The async search owns and disposes its cancellation source.
    private Action? cancelStartVisitSearch;

    public bool UseCurrentStart
    {
        get => useCurrentStart;
        set
        {
            if (SetField(ref useCurrentStart, value))
            {
                ClearStartVisits();
                selectedStartSystemAddress = null;
                OnPropertyChanged(nameof(UsePreviousStart));
                StartStatus = value
                    ? "Your current visit will be used automatically."
                    : "Search for a system to see your previous visits.";
                RaiseCommands();
            }
        }
    }

    public bool UsePreviousStart
    {
        get => !UseCurrentStart;
        set
        {
            if (value)
            {
                UseCurrentStart = false;
            }
        }
    }

    public DateTime? VisitsOnOrAfter
    {
        get => visitsOnOrAfter;
        set
        {
            if (SetField(ref visitsOnOrAfter, value?.Date))
            {
                ClearStartVisits();
                OnPropertyChanged(nameof(HasVisitDate));
                StartStatus = "Search for visits on or after the selected date, or clear it for all dates.";
                RaiseCommands();
            }
        }
    }

    public bool HasVisitDate => VisitsOnOrAfter is not null;

    public IReadOnlyList<JourneyStartVisitViewModel> StartVisits
    {
        get => startVisits;
        private set
        {
            if (SetField(ref startVisits, value))
            {
                OnPropertyChanged(nameof(HasStartVisits));
                OnPropertyChanged(nameof(StartVisitsHeading));
            }
        }
    }

    public bool HasStartVisits => StartVisits.Count > 0;
    public string StartVisitsHeading => $"Your previous visits ({StartVisits.Count:N0})";

    public JourneyStartVisitViewModel? SelectedStartVisit
    {
        get => selectedStartVisit;
        set
        {
            if (value is not null && !StartVisits.Contains(value))
            {
                return;
            }
            if (SetField(ref selectedStartVisit, value))
            {
                if (value is not null)
                {
                    StartStatus = $"Start at {value.Entry.System.Name} on {value.Arrived}.";
                }
                else if (HasStartVisits)
                {
                    StartStatus = "Choose the visit where your journey began.";
                }
                beginJourneyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private void ClearStartVisits()
    {
        startVisitVersion++;
        Action? cancel = cancelStartVisitSearch;
        cancelStartVisitSearch = null;
        SelectedStartVisit = null;
        StartVisits = [];
        if (cancel is not null)
        {
            cancel();
            IsBusy = false;
        }
    }

    private void InvalidateStartVisitContext(string? profileKey)
    {
        if (!string.Equals(initializedProfileKey, profileKey, StringComparison.OrdinalIgnoreCase))
        {
            ClearStartVisits();
            selectedStartSystemAddress = null;
        }
    }

    private bool CanBeginJourney() =>
        IsCreating
        && !HasActiveJourney
        && !IsDirty
        && !string.IsNullOrWhiteSpace(NewJourneyName)
        && HasProfileAndNotBusy()
        && (UseCurrentStart ? HasCurrentStartSystem : SelectedStartVisit is not null);

    private bool CanCancelNewJourney() => IsCreating && (!IsBusy || cancelStartVisitSearch is not null);

    public async Task SelectStartingSystemAsync(string? systemName, long? systemAddress)
    {
        if (!IsCreating || UseCurrentStart || IsBusy || string.IsNullOrWhiteSpace(systemName))
        {
            return;
        }
        StartSystemQuery = systemName;
        selectedStartSystemAddress = systemAddress is > 0 ? systemAddress : null;
        await FindStartAsync();
    }

    public async Task FindStartAsync()
    {
        if (frontierId is not { } profile || !CanFindStart())
        {
            return;
        }
        string name = StartSystemQuery.Trim();
        long? address = selectedStartSystemAddress;
        DateOnly? minimum = VisitsOnOrAfter is { } date ? DateOnly.FromDateTime(date) : null;
        ClearStartVisits();
        int version = startVisitVersion;
        using var cancellation = new CancellationTokenSource();
        cancelStartVisitSearch = cancellation.Cancel;
        try
        {
            IsBusy = true;
            StartStatus = $"Finding your visits to {name}...";
            JourneyJournalSystemVisitsResult result = address is > 0
                ? await journeyService.FindStartVisitsAsync(
                    profile,
                    isOdyssey,
                    address.Value,
                    minimum,
                    cancellation.Token
                )
                : await journeyService.FindStartVisitsAsync(profile, isOdyssey, name, minimum, cancellation.Token);
            if (version != startVisitVersion || cancellation.IsCancellationRequested)
            {
                return;
            }
            ApplyStartVisits(result, name);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A changed query, commander, or closed form invalidated this search.
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            if (version == startVisitVersion)
            {
                StartStatus = "Your previous visits could not be loaded: " + exception.Message;
            }
        }
        finally
        {
            if (version == startVisitVersion)
            {
                cancelStartVisitSearch = null;
                IsBusy = false;
                beginJourneyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private async Task<JourneyBeginRequest?> PrepareBeginRequestAsync(JourneyStartContext context)
    {
        JourneyJournalSystemEntry? entry = context.Entry;
        JourneyStartDraft draft = context.Draft;
        IReadOnlyList<string> errors = [];
        if (context.UseCurrent && context.SystemAddress is { } address)
        {
            JourneyJournalSystemSearchResult current = await journeyService.FindCurrentStartAsync(
                draft.FrontierId,
                draft.IsOdyssey,
                address
            );
            entry = current.Entry;
            errors = current.Errors;
        }
        if (context.Version != contextVersion)
        {
            return null;
        }
        if (context.UseCurrent && (context.SystemAddress != currentSystemAddress || !UseCurrentStart))
        {
            StartStatus = "Your current system changed. Begin the journey again to use the new location.";
            return null;
        }
        if (entry is null)
        {
            StartStatus =
                "The current location could not be found. Load the current journal or choose a previous visit."
                + FormatJournalWarnings(errors);
            return null;
        }
        return new(draft.FrontierId, draft.CommanderName, draft.IsOdyssey, draft.Name, draft.Description, entry);
    }

    private sealed record JourneyStartDraft(
        string FrontierId,
        string CommanderName,
        bool IsOdyssey,
        string Name,
        string Description
    );

    private sealed record JourneyStartContext(
        JourneyStartDraft Draft,
        bool UseCurrent,
        long? SystemAddress,
        JourneyJournalSystemEntry? Entry,
        int Version
    );

    private void ApplyStartVisits(JourneyJournalSystemVisitsResult result, string? name)
    {
        StartVisits = result.Entries.Select(entry => new JourneyStartVisitViewModel(entry)).ToArray();
        StartStatus =
            StartVisits.Count == 0
                ? $"No previous visits match {name}. Try another system name."
                : $"Found {StartVisits.Count:N0} visits. Choose the visit where your journey began.";
        if (StartVisits.Count == 1)
        {
            SelectedStartVisit = StartVisits[0];
        }
        if (result.Errors.Count > 0)
        {
            StartStatus += FormatJournalWarnings(result.Errors);
        }
    }

    private static string FormatJournalWarnings(IReadOnlyList<string> errors) =>
        errors.Count == 0 ? string.Empty : $" Journal read warning(s): {errors.Count:N0}. {errors[0]}";
}

public sealed record JourneyStartVisitViewModel(JourneyJournalSystemEntry Entry)
{
    public string SystemName => Entry.System.Name;
    public string Arrived =>
        Entry.Event.Timestamp?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
        ?? "Unknown arrival time";
    public string ArrivalType => Entry.Event.EventName == "CarrierJump" ? "Fleet carrier arrival" : "Ship arrival";
}
