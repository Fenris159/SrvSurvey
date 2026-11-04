using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Storage;

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
            systemNotes.CountUpdate,
            systemNotes.Context,
            systemNotes.BaselineKnown ? systemNotes.Loaded : null,
            systemNotes.NoteWrite
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
        if (draft.System is not null || draft.NotesContext is not null)
        {
            SelectedSystem =
                VisitedSystems.FirstOrDefault(item =>
                    item.Visit.StarSystem.SystemAddress == draft.System?.Address
                    && item.Visit.Arrived == draft.System?.Arrived
                )
                ?? VisitedSystems.FirstOrDefault(item =>
                    item.Visit.StarSystem.SystemAddress == draft.NotesContext?.SystemAddress
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
        RestoreSystemNoteDraft(draft);
        retainedDraft = null;
    }

    private void RestoreSystemNoteDraft(JourneyEditDraft draft)
    {
        bool loadedContextMatches = HasLoadedDraftContext(draft);
        if (draft.Notes is null || (loadedContextMatches && WasDraftSaved(draft)))
        {
            return;
        }
        string loaded = loadedContextMatches ? systemNotes.Loaded : string.Empty;
        string? written = draft.NoteWrite is { WrittenCount: not null } receipt ? receipt.Text : null;
        systemNotes = new SystemNoteDraft(
            written ?? draft.NotesBaseline ?? loaded,
            written is null && draft.NotesBaseline is null ? MergeNoteDraft(loaded, draft.Notes) : draft.Notes,
            draft.NoteWrite?.WrittenCount ?? draft.CountUpdate,
            draft.NotesContext ?? systemNotes.Context,
            BaselineKnown: written is not null || draft.NotesBaseline is not null || loadedContextMatches
        );
        OnPropertyChanged(nameof(SelectedSystemNotes));
        OnPropertyChanged(nameof(CanEditSystemNotes));
        RaiseDirtyState();
        RaiseCommands();
    }

    private bool HasLoadedDraftContext(JourneyEditDraft draft) =>
        systemNotes.BaselineKnown
        && systemNotes.Context is { } loaded
        && draft.NotesContext is { } expected
        && string.Equals(loaded.FrontierId, expected.FrontierId, StringComparison.OrdinalIgnoreCase)
        && loaded.SystemAddress == expected.SystemAddress;

    private static bool WasDraftSaved(JourneyEditDraft draft) =>
        draft.NoteWrite is { WrittenCount.IsPending: false } write
        && string.Equals(draft.Notes, write.Text, StringComparison.Ordinal);

    private void ClearJourneyDraft() => retainedDraft = null;
}

internal sealed record JourneyEditDraft(
    string FileName,
    string? Name,
    string? Description,
    string? Notes,
    (long Address, DateTimeOffset Arrived)? System,
    bool Archived,
    JourneyNoteCountUpdate? CountUpdate = null,
    SystemNoteContext? NotesContext = null,
    string? NotesBaseline = null,
    JourneyNoteWriteReceipt? NoteWrite = null
);

internal sealed class JourneyNoteWriteReceipt
{
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal string Text { get; private set; } = string.Empty;
    internal JourneyNoteCountUpdate? WrittenCount { get; private set; }
    internal Task Completion => completion.Task;

    internal void MarkWritten(string text, JourneyNoteCountUpdate count)
    {
        Text = text;
        WrittenCount = count;
    }

    internal void Complete() => completion.TrySetResult();
}

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
