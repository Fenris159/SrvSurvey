using System.Globalization;
using SrvSurvey.Core.Journeys;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class JourneyWorkspaceViewModel
{
    private IReadOnlyList<JourneyStartVisitViewModel> startVisits = [];
    private JourneyStartVisitViewModel? selectedStartVisit;
    private long? selectedStartSystemAddress;
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
        && !string.IsNullOrWhiteSpace(NewJourneyName)
        && CanFindStart()
        && SelectedStartVisit is not null;

    private bool CanCancelNewJourney() => IsCreating && (!IsBusy || cancelStartVisitSearch is not null);

    public async Task SelectStartingSystemAsync(string? systemName, long? systemAddress)
    {
        if (!IsCreating || IsBusy || string.IsNullOrWhiteSpace(systemName))
        {
            return;
        }
        StartSystemQuery = systemName;
        selectedStartSystemAddress = systemAddress is > 0 ? systemAddress : null;
        await FindStartAsync();
    }

    private Task UseCurrentSystemAsync() => SelectStartingSystemAsync(currentSystemName, currentSystemAddress);

    public async Task FindStartAsync()
    {
        if (frontierId is not { } profile || !CanFindStart())
        {
            return;
        }
        string name = StartSystemQuery.Trim();
        long? address = selectedStartSystemAddress;
        ClearStartVisits();
        int version = startVisitVersion;
        using var cancellation = new CancellationTokenSource();
        cancelStartVisitSearch = cancellation.Cancel;
        try
        {
            IsBusy = true;
            StartStatus = $"Finding your visits to {name}...";
            JourneyJournalSystemVisitsResult result = address is > 0
                ? await journeyService.FindStartVisitsAsync(profile, isOdyssey, address.Value, cancellation.Token)
                : await journeyService.FindStartVisitsAsync(profile, isOdyssey, name, cancellation.Token);
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
            StartStatus += $" Ignored {result.Errors.Count:N0} malformed journal line(s).";
        }
    }
}

public sealed record JourneyStartVisitViewModel(JourneyJournalSystemEntry Entry)
{
    public string SystemName => Entry.System.Name;
    public string Arrived =>
        Entry.Event.Timestamp?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
        ?? "Unknown arrival time";
    public string ArrivalType => Entry.Event.EventName == "CarrierJump" ? "Fleet carrier arrival" : "Ship arrival";
}
