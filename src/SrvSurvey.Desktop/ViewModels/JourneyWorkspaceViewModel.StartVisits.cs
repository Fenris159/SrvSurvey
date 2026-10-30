using System.Globalization;
using SrvSurvey.Core.Journeys;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class JourneyWorkspaceViewModel
{
    private IReadOnlyList<JourneyStartVisitViewModel> startVisits = [];
    private JourneyStartVisitViewModel? selectedStartVisit;
    private int startVisitVersion;

    // The async search owns and disposes its cancellation source.
    private Action? cancelStartVisitSearch;

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
    public string StartVisitsHeading => $"Recorded arrivals ({StartVisits.Count:N0})";

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
                    StartStatus = "Choose a recorded arrival as the starting point.";
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

    private void InvalidateStartVisitContext(string? profileKey, long? address)
    {
        if (
            UseCurrentStart && currentSystemAddress != address
            || !string.Equals(initializedProfileKey, profileKey, StringComparison.OrdinalIgnoreCase)
        )
        {
            ClearStartVisits();
        }
    }

    private bool CanBeginJourney() =>
        IsCreating
        && !HasActiveJourney
        && !string.IsNullOrWhiteSpace(NewJourneyName)
        && CanFindStart()
        && SelectedStartVisit is not null;

    private bool CanCancelNewJourney() => IsCreating && (!IsBusy || cancelStartVisitSearch is not null);

    public async Task FindStartAsync()
    {
        long? address = UseCurrentStart ? currentSystemAddress : SelectedStartSystem?.SystemAddress;
        string? name = UseCurrentStart ? currentSystemName : SelectedStartSystem?.Name;
        if (frontierId is null || address is not > 0 || !IsCreating || IsBusy)
        {
            return;
        }
        ClearStartVisits();
        int version = startVisitVersion;
        using var cancellation = new CancellationTokenSource();
        cancelStartVisitSearch = cancellation.Cancel;
        try
        {
            IsBusy = true;
            StartStatus = $"Finding recorded arrivals in {name}...";
            JourneyJournalSystemVisitsResult result = await journeyService.FindStartVisitsAsync(
                frontierId,
                isOdyssey,
                address.Value,
                cancellation.Token
            );
            if (version != startVisitVersion || cancellation.IsCancellationRequested)
            {
                return;
            }
            ApplyStartVisits(result, name);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A changed system, commander, or closed form invalidated this search.
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            if (version == startVisitVersion)
            {
                StartStatus = "The starting visits could not be found: " + exception.Message;
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

    private void ApplyStartVisits(JourneyJournalSystemVisitsResult result, string? name)
    {
        StartVisits = result.Entries.Select(entry => new JourneyStartVisitViewModel(entry)).ToArray();
        StartStatus =
            StartVisits.Count == 0
                ? $"No recorded ship or carrier arrivals in {name} were found in this commander's journals."
                : $"Found {StartVisits.Count:N0} arrivals. Choose the visit where this journey began.";
        if (StartVisits.Count == 1)
        {
            SelectedStartVisit = StartVisits[0];
        }
        if (result.Errors.Count > 0)
        {
            StartStatus += $" Ignored {result.Errors.Count:N0} malformed journal line(s).";
        }
    }
}

public sealed record JourneyStartVisitViewModel(JourneyJournalSystemEntry Entry)
{
    public string Arrived =>
        Entry.Event.Timestamp?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
        ?? "Unknown arrival time";
    public string ArrivalType => Entry.Event.EventName == "CarrierJump" ? "Fleet carrier arrival" : "Ship arrival";
}
