using System.Windows.Input;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class JourneyWorkspaceViewModel
{
    private AsyncCommand? archiveCommand;
    private AsyncCommand? requestDeleteCommand;
    private AsyncCommand? confirmDeleteCommand;
    private AsyncCommand? cancelDeleteCommand;
    private bool showArchived;
    private bool isDeletePending;
    private IReadOnlyList<JourneyListItemViewModel> visibleJourneys = [];
    private IReadOnlyList<JourneyStatisticViewModel> primaryStatistics = [];

    public IReadOnlyList<JourneyListItemViewModel> VisibleJourneys => visibleJourneys;
    public bool HasVisibleJourneys => VisibleJourneys.Count > 0;
    public string JourneysLabel => $"Journeys ({Journeys.Count(item => !item.Document.IsArchived):N0})";
    public string ArchiveLabel => $"Archive ({Journeys.Count(item => item.Document.IsArchived):N0})";
    public string LibraryEmptyMessage =>
        ShowArchived
            ? "No archived journeys. Archive a completed journey to keep it here."
            : "Begin a journey to record your next expedition.";
    public bool ShowArchived
    {
        get => showArchived;
        set
        {
            if (showArchived == value)
            {
                return;
            }
            if (IsDirty)
            {
                StatusMessage = "Save or discard changes before switching journey lists.";
                OnPropertyChanged();
                return;
            }
            showArchived = value;
            OnPropertyChanged();
            RaiseLibraryState();
            SelectVisibleJourney(selectedDocument?.FileName);
        }
    }

    public bool IsSelectedActiveJourney => selectedDocument is { IsActive: true };
    public string EmptyWorkspaceMessage =>
        HasProfile
            ? "Choose a journey from the list, or begin a new expedition."
            : "Load the game or select a commander in Overview to read journey history.";

    public bool IsCompletedJourney => selectedDocument is { IsActive: false };
    public bool CanExportReport => IsCompletedJourney && !IsDirty && !IsBusy;
    public string SelectedJourneyState => selectedDocument?.IsActive == true ? "Active" : "Complete";
    public string ArchiveButtonText => selectedDocument?.IsArchived == true ? "Restore journey" : "Archive journey";
    public bool HasVisitedSystems => VisitedSystems.Count > 0;
    public string RouteOrigin =>
        selectedDocument?.VisitedSystems is { Count: > 0 } visits ? visits[0].StarSystem.Name : "No systems recorded";
    public string RouteDestination =>
        selectedDocument?.VisitedSystems is { Count: > 0 } visits ? visits[^1].StarSystem.Name : "Awaiting a visit";
    public IReadOnlyList<JourneyStatisticViewModel> PrimaryStatistics => primaryStatistics;
    public bool IsDeletePending
    {
        get => isDeletePending;
        private set
        {
            if (SetField(ref isDeletePending, value))
            {
                RaiseHistoryCommands();
            }
        }
    }

    public ICommand ArchiveCommand => archiveCommand ??= new AsyncCommand(ArchiveSelectedAsync, CanManageHistory);
    public ICommand RequestDeleteCommand =>
        requestDeleteCommand ??= new AsyncCommand(
            () =>
            {
                IsDeletePending = true;
                return Task.CompletedTask;
            },
            CanManageHistory
        );
    public ICommand ConfirmDeleteCommand =>
        confirmDeleteCommand ??= new AsyncCommand(DeleteSelectedAsync, () => IsDeletePending && CanManageHistory());
    public ICommand CancelDeleteCommand =>
        cancelDeleteCommand ??= new AsyncCommand(
            () =>
            {
                IsDeletePending = false;
                return Task.CompletedTask;
            },
            () => IsDeletePending && !IsBusy
        );

    private bool CanManageHistory() => IsCompletedJourney && !IsDirty && !IsBusy;

    public async Task ArchiveSelectedAsync()
    {
        if (!CanManageHistory() || selectedDocument is not { } document)
        {
            return;
        }
        int version = contextVersion;
        try
        {
            IsBusy = true;
            JourneyDocument updated = await journeyService.SetArchivedAsync(
                document.FrontierId,
                document.FileName,
                !document.IsArchived
            );
            if (version != contextVersion)
            {
                return;
            }
            UpdateCatalogDocument(updated);
            SelectVisibleJourney(document.FileName);
            StatusMessage = updated.IsArchived
                ? $"Archived {updated.Name}. Find it in Archive."
                : $"Restored {updated.Name} to Journeys.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            if (version == contextVersion)
            {
                StatusMessage = "The journey could not be moved: " + exception.Message;
            }
        }
        finally
        {
            if (version == contextVersion)
            {
                IsBusy = false;
            }
        }
    }

    public async Task DeleteSelectedAsync()
    {
        if (!IsDeletePending || !CanManageHistory() || selectedDocument is not { } document)
        {
            return;
        }
        int version = contextVersion;
        try
        {
            IsBusy = true;
            await journeyService.DeleteAsync(document.FrontierId, document.FileName);
            if (version != contextVersion)
            {
                return;
            }
            Journeys = Journeys.Where(item => item.Document.FileName != document.FileName).ToArray();
            SelectVisibleJourney(null);
            IsDeletePending = false;
            StatusMessage = $"Deleted {document.Name}. Journals, notes, and screenshots were kept.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            if (version == contextVersion)
            {
                StatusMessage = "The journey could not be deleted: " + exception.Message;
            }
        }
        finally
        {
            if (version == contextVersion)
            {
                IsBusy = false;
            }
        }
    }

    /// <summary>Captures the completed journey and its saved system notes for a portable report.</summary>
    public async Task<JourneyHtmlExport?> CreateHtmlReportAsync()
    {
        if (!CanExportReport || selectedDocument is not { } document)
        {
            StatusMessage = "Select a completed journey and save any changes before exporting.";
            return null;
        }
        int version = contextVersion;
        try
        {
            IsBusy = true;
            StatusMessage = "Preparing the journey report...";
            Dictionary<long, string?> imageFolders = CaptureReportImageFolders(document);
            JourneyReportImages images = await Task.Run(() => CaptureReportImages(imageFolders));
            if (version != contextVersion)
            {
                return null;
            }
            var notes = new Dictionary<long, string>();
            foreach (
                JourneySystemReference system in document
                    .VisitedSystems.Select(visit => visit.StarSystem)
                    .DistinctBy(system => system.SystemAddress)
            )
            {
                SystemNoteLoadResult result = await noteStore.LoadAsync(
                    document.FrontierId,
                    system.Name,
                    system.SystemAddress
                );
                if (version != contextVersion)
                {
                    return null;
                }
                if (!result.IsSuccess)
                {
                    throw new IOException(result.Error ?? "System notes could not be read.");
                }
                notes[system.SystemAddress] = result.Notes ?? string.Empty;
            }
            string html = await Task.Run(() => JourneyReport.Html(document, notes, images.BySystem));
            if (version != contextVersion)
            {
                return null;
            }
            return new JourneyHtmlExport($"journey-{document.FileName}.html", html, images.Assets);
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            if (version == contextVersion)
            {
                StatusMessage = "The journey report could not be created: " + exception.Message;
            }
            return null;
        }
        finally
        {
            if (version == contextVersion)
            {
                IsBusy = false;
            }
        }
    }

    public void SetExportStatus(string message) => StatusMessage = message;

    private void SelectVisibleJourney(string? preferredFileName)
    {
        IReadOnlyList<JourneyListItemViewModel> visible = VisibleJourneys;
        selectedJourney =
            visible.FirstOrDefault(item => item.Document.FileName == preferredFileName)
            ?? (visible.Count > 0 ? visible[0] : null);
        OnPropertyChanged(nameof(SelectedJourney));
        ApplyDocument(selectedJourney?.Document);
    }

    private void RaiseLibraryState()
    {
        isUpdatingLibrary = true;
        try
        {
            visibleJourneys = Journeys.Where(item => item.Document.IsArchived == ShowArchived).ToArray();
            OnPropertyChanged(nameof(EmptyWorkspaceMessage));
            OnPropertyChanged(nameof(HasJourneys));
            OnPropertyChanged(nameof(VisibleJourneys));
            OnPropertyChanged(nameof(HasVisibleJourneys));
            OnPropertyChanged(nameof(JourneysLabel));
            OnPropertyChanged(nameof(ArchiveLabel));
            OnPropertyChanged(nameof(LibraryEmptyMessage));
        }
        finally
        {
            isUpdatingLibrary = false;
        }
    }

    private void RaiseHistoryCommands()
    {
        archiveCommand?.RaiseCanExecuteChanged();
        requestDeleteCommand?.RaiseCanExecuteChanged();
        confirmDeleteCommand?.RaiseCanExecuteChanged();
        cancelDeleteCommand?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanExportReport));
    }
}
