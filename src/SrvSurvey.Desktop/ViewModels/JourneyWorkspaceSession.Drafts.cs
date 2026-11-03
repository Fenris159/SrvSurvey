using SrvSurvey.Core.Journeys;

namespace SrvSurvey.Desktop.ViewModels;

internal sealed partial class JourneyWorkspaceSession
{
    private JourneyEditDraft? retainedDraft;

    internal JourneyEditDraft? CaptureDraft()
    {
        if (selectedDocument is null || !IsDirty)
        {
            return retainedDraft;
        }
        return new(
            selectedDocument.FileName,
            JourneyName != selectedDocument.Name ? JourneyName : null,
            JourneyDescription != selectedDocument.Description ? JourneyDescription : null,
            systemNotes.IsDirty ? SelectedSystemNotes : null,
            SelectedSystem is { } system ? (system.Visit.StarSystem.SystemAddress, system.Visit.Arrived) : null,
            ShowArchived,
            systemNotes.CountUpdate
        );
    }

    private async Task RestoreJourneyDraftAsync()
    {
        if (retainedDraft is not { } draft)
        {
            return;
        }
        if (selectedDocument?.FileName != draft.FileName)
        {
            JourneyListItemViewModel? item = Journeys.FirstOrDefault(candidate =>
                candidate.Document.FileName == draft.FileName
            );
            if (item is null)
            {
                return;
            }
            showArchived = item.Document.IsArchived;
            OnPropertyChanged(nameof(ShowArchived));
            RaiseLibraryState();
            SelectVisibleJourney(draft.FileName);
        }
        if (draft.System is { } identity)
        {
            SelectedSystem = VisitedSystems.FirstOrDefault(item =>
                item.Visit.StarSystem.SystemAddress == identity.Address && item.Visit.Arrived == identity.Arrived
            );
        }
        await LoadSelectedSystemAsync(SelectedSystem);
        if (draft.Name is not null)
        {
            JourneyName = draft.Name;
        }
        if (draft.Description is not null)
        {
            JourneyDescription = draft.Description;
        }
        if (SelectedSystem is not null && draft.Notes is not null)
        {
            SelectedSystemNotes = draft.Notes;
            systemNotes = systemNotes with { CountUpdate = draft.CountUpdate };
            RaiseDirtyState();
            RaiseCommands();
        }
        retainedDraft = null;
    }

    private void ClearJourneyDraft() => retainedDraft = null;
}

internal sealed record JourneyEditDraft(
    string FileName,
    string? Name,
    string? Description,
    string? Notes,
    (long Address, DateTimeOffset Arrived)? System,
    bool Archived,
    JourneyNoteCountUpdate? CountUpdate = null
);

internal sealed class JourneyNoteCountUpdate(JourneyDocument journey, long address, DateTimeOffset arrived)
{
    private readonly System.Threading.Lock gate = new();
    private Task<JourneyDocument>? update;

    internal bool IsPending
    {
        get
        {
            lock (gate)
            {
                return update?.IsCompletedSuccessfully != true;
            }
        }
    }

    internal Task<JourneyDocument> CompleteAsync(JourneyService service)
    {
        lock (gate)
        {
            if (update is null || update.IsFaulted || update.IsCanceled)
            {
                update = service.RecordNoteAsync(journey, address, arrived);
            }
            return update;
        }
    }
}
