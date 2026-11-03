using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Displays one Journey activation; detached activations cannot publish into another commander.</summary>
public sealed class JourneyWorkspaceViewModel : INotifyPropertyChanged
{
    private readonly Func<JourneyWorkspaceSession> createSession;
    private readonly Dictionary<string, JourneyEditDraft> drafts = new(StringComparer.OrdinalIgnoreCase);
    private JourneyWorkspaceSession session;
    private readonly ConditionalWeakTable<JourneyHtmlExport, JourneyWorkspaceSession> reportOwners = [];
    private string? profileKey;
    private Func<Task<bool>>? workspaceOpener;
    private Func<DirectoryInfo, Task<bool>>? directoryLauncher;

    public JourneyWorkspaceViewModel(
        JourneyService journeyService,
        SystemNoteStore noteStore,
        SystemNotesSettingsStore settingsStore,
        Func<string, string?>? screenshotDirectoryProvider = null
    )
    {
        ArgumentNullException.ThrowIfNull(journeyService);
        ArgumentNullException.ThrowIfNull(noteStore);
        ArgumentNullException.ThrowIfNull(settingsStore);
        createSession = () =>
            new JourneyWorkspaceSession(journeyService, noteStore, settingsStore, screenshotDirectoryProvider);
        session = createSession();
        session.PropertyChanged += OnSessionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (ReferenceEquals(sender, session))
        {
            PropertyChanged?.Invoke(this, args);
        }
    }

    public async Task<bool> UpdateContextAsync(
        string? nextFrontierId,
        string? nextCommanderName,
        bool nextIsOdyssey,
        string? nextSystemName,
        long? nextSystemAddress
    )
    {
        string? profile = string.IsNullOrWhiteSpace(nextFrontierId) ? null : nextFrontierId;
        string? key = profile is null ? null : $"{profile}|{nextIsOdyssey}";
        if (string.Equals(profileKey, key, StringComparison.OrdinalIgnoreCase))
        {
            session.UpdateLocation(nextCommanderName, nextSystemName, nextSystemAddress);
            return false;
        }
        if (profileKey is not null)
        {
            if (session.CaptureDraft() is { } dirty)
            {
                drafts[profileKey] = dirty;
            }
            else
            {
                drafts.Remove(profileKey);
            }
        }
        session.PropertyChanged -= OnSessionChanged;
        session.Detach();
        JourneyWorkspaceSession next = createSession();
        session = next;
        profileKey = key;
        next.SetWorkspaceOpener(workspaceOpener);
        next.SetDirectoryLauncher(directoryLauncher);
        next.PropertyChanged += OnSessionChanged;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        JourneyEditDraft? draft =
            key is not null && drafts.TryGetValue(key, out JourneyEditDraft? saved) ? saved : null;
        await next.InitializeAsync(profile, nextCommanderName, nextIsOdyssey, nextSystemName, nextSystemAddress, draft);
        return true;
    }

    public void SetWorkspaceOpener(Func<Task<bool>>? opener)
    {
        workspaceOpener = opener;
        session.SetWorkspaceOpener(opener);
    }

    public void SetDirectoryLauncher(Func<DirectoryInfo, Task<bool>>? launcher)
    {
        directoryLauncher = launcher;
        session.SetDirectoryLauncher(launcher);
    }

    public async Task SetPreferencesAsync(bool nextAlwaysOnTop, bool nextUseGalacticTime)
    {
        JourneyWorkspaceSession owner = session;
        string message = await owner.SetPreferencesAsync(nextAlwaysOnTop, nextUseGalacticTime);
        session.ReloadPreferences(owner.AlwaysOnTop, owner.UseGalacticTime);
        session.SetExportStatus(message);
    }

    public IReadOnlyList<JourneyListItemViewModel> VisibleJourneys => session.VisibleJourneys;
    public bool HasVisibleJourneys => session.HasVisibleJourneys;
    public string JourneysLabel => session.JourneysLabel;
    public string ArchiveLabel => session.ArchiveLabel;
    public string LibraryEmptyMessage => session.LibraryEmptyMessage;
    public bool ShowArchived
    {
        get => session.ShowArchived;
        set => session.ShowArchived = value;
    }
    public bool IsSelectedActiveJourney => session.IsSelectedActiveJourney;
    public string EmptyWorkspaceMessage => session.EmptyWorkspaceMessage;
    public bool IsCompletedJourney => session.IsCompletedJourney;
    public bool CanExportReport => session.CanExportReport;
    public string SelectedJourneyState => session.SelectedJourneyState;
    public string ArchiveButtonText => session.ArchiveButtonText;
    public bool HasVisitedSystems => session.HasVisitedSystems;
    public string RouteOrigin => session.RouteOrigin;
    public string RouteDestination => session.RouteDestination;
    public IReadOnlyList<JourneyStatisticViewModel> PrimaryStatistics => session.PrimaryStatistics;
    public bool IsDeletePending => session.IsDeletePending;
    public ICommand ArchiveCommand => session.ArchiveCommand;
    public ICommand RequestDeleteCommand => session.RequestDeleteCommand;
    public ICommand ConfirmDeleteCommand => session.ConfirmDeleteCommand;
    public ICommand CancelDeleteCommand => session.CancelDeleteCommand;
    public bool HasUnavailableJourney => session.HasUnavailableJourney;
    public ICommand ClearUnavailableJourneyCommand => session.ClearUnavailableJourneyCommand;
    public bool CanResumeJourney => session.CanResumeJourney;
    public ICommand ResumeJourneyCommand => session.ResumeJourneyCommand;
    public bool UseCurrentStart
    {
        get => session.UseCurrentStart;
        set => session.UseCurrentStart = value;
    }
    public bool UsePreviousStart
    {
        get => session.UsePreviousStart;
        set => session.UsePreviousStart = value;
    }
    public DateTime? VisitsOnOrAfter
    {
        get => session.VisitsOnOrAfter;
        set => session.VisitsOnOrAfter = value;
    }
    public bool HasVisitDate => session.HasVisitDate;
    public IReadOnlyList<JourneyStartVisitViewModel> StartVisits => session.StartVisits;
    public bool HasStartVisits => session.HasStartVisits;
    public string StartVisitsHeading => session.StartVisitsHeading;
    public JourneyStartVisitViewModel? SelectedStartVisit
    {
        get => session.SelectedStartVisit;
        set => session.SelectedStartVisit = value;
    }
    public bool HasProfile => session.HasProfile;
    public bool HasActiveJourney => session.HasActiveJourney;
    public string ActiveJourneyName => session.ActiveJourneyName;
    public string ActiveJourneySummary => session.ActiveJourneySummary;
    public bool IsBusy => session.IsBusy;
    public string StatusMessage => session.StatusMessage;
    public IReadOnlyList<JourneyListItemViewModel> Journeys => session.Journeys;
    public bool HasJourneys => session.HasJourneys;
    public JourneyListItemViewModel? SelectedJourney
    {
        get => session.SelectedJourney;
        set => session.SelectedJourney = value;
    }
    public bool HasSelectedJourney => session.HasSelectedJourney;
    public string JourneyName
    {
        get => session.JourneyName;
        set => session.JourneyName = value;
    }
    public string JourneyDescription
    {
        get => session.JourneyDescription;
        set => session.JourneyDescription = value;
    }
    public string JourneyByline => session.JourneyByline;
    public IReadOnlyList<JourneyStatisticViewModel> QuickStatistics => session.QuickStatistics;
    public IReadOnlyList<JourneySystemItemViewModel> VisitedSystems => session.VisitedSystems;
    public JourneySystemItemViewModel? SelectedSystem
    {
        get => session.SelectedSystem;
        set => session.SelectedSystem = value;
    }
    public bool HasSelectedSystem => session.HasSelectedSystem;
    public string SelectedSystemName => session.SelectedSystemName;
    public string SelectedSystemAddress => session.SelectedSystemAddress;
    public string SelectedSystemAddressText => session.SelectedSystemAddressText;
    public string SelectedSystemNotes
    {
        get => session.SelectedSystemNotes;
        set => session.SelectedSystemNotes = value;
    }
    public bool CanEditSystemNotes => session.CanEditSystemNotes;
    public string SelectedSystemDetails => session.SelectedSystemDetails;
    public IReadOnlyList<string> ScreenshotFiles => session.ScreenshotFiles;
    public bool HasScreenshots => session.HasScreenshots;
    public bool HasImagesDirectory => session.HasImagesDirectory;
    public bool IsDirty => session.IsDirty;
    public bool IsCreating => session.IsCreating;
    public string NewJourneyName
    {
        get => session.NewJourneyName;
        set => session.NewJourneyName = value;
    }
    public string NewJourneyDescription
    {
        get => session.NewJourneyDescription;
        set => session.NewJourneyDescription = value;
    }
    public bool HasCurrentStartSystem => session.HasCurrentStartSystem;
    public string CurrentStartSystem => session.CurrentStartSystem;
    public string StartSystemQuery
    {
        get => session.StartSystemQuery;
        set => session.StartSystemQuery = value;
    }
    public string StartStatus => session.StartStatus;
    public bool IsConcludePending => session.IsConcludePending;
    public bool IsReprocessPending => session.IsReprocessPending;
    public bool AlwaysOnTop => session.AlwaysOnTop;
    public bool UseGalacticTime => session.UseGalacticTime;
    public ICommand OpenWorkspaceCommand => session.OpenWorkspaceCommand;
    public ICommand RefreshCommand => session.RefreshCommand;
    public ICommand NewJourneyCommand => session.NewJourneyCommand;
    public ICommand CancelNewJourneyCommand => session.CancelNewJourneyCommand;
    public ICommand FindStartCommand => session.FindStartCommand;
    public ICommand BeginJourneyCommand => session.BeginJourneyCommand;
    public ICommand SaveCommand => session.SaveCommand;
    public ICommand DiscardCommand => session.DiscardCommand;
    public ICommand RequestConcludeCommand => session.RequestConcludeCommand;
    public ICommand ConfirmConcludeCommand => session.ConfirmConcludeCommand;
    public ICommand CancelConcludeCommand => session.CancelConcludeCommand;
    public ICommand RequestReprocessCommand => session.RequestReprocessCommand;
    public ICommand ConfirmReprocessCommand => session.ConfirmReprocessCommand;
    public ICommand CancelReprocessCommand => session.CancelReprocessCommand;
    public ICommand OpenImagesCommand => session.OpenImagesCommand;

    public Task ArchiveSelectedAsync() => session.ArchiveSelectedAsync();

    public Task DeleteSelectedAsync() => session.DeleteSelectedAsync();

    public async Task<JourneyHtmlExport?> CreateHtmlReportAsync()
    {
        JourneyWorkspaceSession owner = session;
        JourneyHtmlExport? report = await owner.CreateHtmlReportAsync();
        if (report is not null && ReferenceEquals(owner, session))
        {
            reportOwners.GetValue(report, _ => owner);
            return report;
        }
        return null;
    }

    public void SetExportStatus(string message) => session.SetExportStatus(message);

    public void SetExportStatus(JourneyHtmlExport report, string message)
    {
        if (reportOwners.TryGetValue(report, out JourneyWorkspaceSession? owner) && ReferenceEquals(owner, session))
        {
            owner.SetExportStatus(message);
        }
    }

    public Task SaveHtmlReportAsync(JourneyHtmlExport report, string destination) =>
        reportOwners.TryGetValue(report, out JourneyWorkspaceSession? owner) && ReferenceEquals(owner, session)
            ? owner.SaveHtmlReportAsync(report, destination)
            : Task.CompletedTask;

    public Task ResumeJourneyAsync() => session.ResumeJourneyAsync();

    public Task ClearUnavailableJourneyAsync() => session.ClearUnavailableJourneyAsync();

    public Task SelectStartingSystemAsync(string? systemName, long? systemAddress) =>
        session.SelectStartingSystemAsync(systemName, systemAddress);

    public Task FindStartAsync() => session.FindStartAsync();

    public void RefreshScreenshotAvailability() => session.RefreshScreenshotAvailability();

    public Task ApplyJournalEventsAsync(IReadOnlyList<JournalEventEnvelope> journalEvents) =>
        session.ApplyJournalEventsAsync(journalEvents);

    public Task RefreshAsync() => session.RefreshAsync();

    public Task StartNewJourneyAsync() => session.StartNewJourneyAsync();

    public Task BeginJourneyAsync() => session.BeginJourneyAsync();

    public Task SaveAsync() => session.SaveAsync();

    public Task DiscardAsync() => session.DiscardAsync();

    public Task ConfirmConcludeAsync() => session.ConfirmConcludeAsync();

    public Task ConfirmReprocessAsync() => session.ConfirmReprocessAsync();
}

public sealed record JourneyListItemViewModel(JourneyDocument Document, string Started, string Updated)
{
    public string Name => Document.Name;

    public string Description =>
        string.IsNullOrWhiteSpace(Document.Description) ? "No description" : Document.Description;

    public string State => Document.IsActive ? "Active" : "Complete";

    public string VisitSummary => $"{Document.VisitedSystems.Count:N0} recorded visits";
}

public sealed record JourneyStatisticViewModel(string Label, string Value);

public sealed record JourneySystemItemViewModel(
    JourneySystemVisit Visit,
    string InterestFlags,
    string Arrived,
    string Departed
)
{
    public string Name => Visit.StarSystem.Name;

    public string ActivitySummary =>
        string.Join(
            ", ",
            new[]
            {
                Visit.Counts.BodyScans > 0 ? $"{Visit.Counts.BodyScans:N0} scanned" : "",
                Visit.Counts.DetailedSurfaceScans > 0 ? $"{Visit.Counts.DetailedSurfaceScans:N0} mapped" : "",
                Visit.Counts.Organisms > 0 ? $"{Visit.Counts.Organisms:N0} organisms" : "",
                Visit.Counts.NewCodexEntries > 0 ? $"{Visit.Counts.NewCodexEntries:N0} new Codex" : "",
                ScreenshotSummary,
                NotesSummary,
                LandingSummary,
            }.Where(value => value.Length > 0)
        );

    private string ScreenshotSummary =>
        Visit.Counts.Screenshots switch
        {
            > 1 => $"{Visit.Counts.Screenshots:N0} screenshots",
            1 => "1 screenshot",
            _ when InterestFlags.Contains('p') => "Screenshots on another visit",
            _ => "",
        };

    private string NotesSummary =>
        Visit.Counts.Notes switch
        {
            > 1 => $"{Visit.Counts.Notes:N0} notes",
            1 => "1 note",
            _ when InterestFlags.Contains('n') => "Notes on another visit",
            _ => "",
        };

    private string LandingSummary =>
        Visit.Counts.Touchdowns switch
        {
            > 1 => $"{Visit.Counts.Touchdowns:N0} landings",
            1 => "1 landing",
            _ => "",
        };

    public string Address => Visit.StarSystem.SystemAddress.ToString(CultureInfo.InvariantCulture);
}

public sealed record JourneyStartVisitViewModel(JourneyJournalSystemEntry Entry)
{
    public string SystemName => Entry.System.Name;
    public string Arrived =>
        Entry.Event.Timestamp?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
        ?? "Unknown arrival time";
    public string ArrivalType => Entry.Event.EventName == "CarrierJump" ? "Fleet carrier arrival" : "Ship arrival";
}
