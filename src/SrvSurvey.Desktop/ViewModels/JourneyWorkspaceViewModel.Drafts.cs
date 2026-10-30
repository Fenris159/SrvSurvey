namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class JourneyWorkspaceViewModel
{
    private readonly Dictionary<string, JourneyEditDraft> editDrafts = new(StringComparer.OrdinalIgnoreCase);

    private void RememberJourneyDraft()
    {
        if (initializedProfileKey is not null && selectedDocument is not null)
        {
            if (!IsDirty)
            {
                editDrafts.Remove(initializedProfileKey);
                return;
            }
            editDrafts[initializedProfileKey] = new(
                selectedDocument.FileName,
                JourneyName != selectedDocument.Name ? JourneyName : null,
                JourneyDescription != selectedDocument.Description ? JourneyDescription : null,
                SelectedSystemNotes != loadedSystemNotes ? SelectedSystemNotes : null,
                SelectedSystem is { } system ? (system.Visit.StarSystem.SystemAddress, system.Visit.Arrived) : null,
                ShowArchived
            );
        }
    }

    private async Task RestoreJourneyDraftAsync(string key, int version)
    {
        if (!editDrafts.TryGetValue(key, out JourneyEditDraft? draft))
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
        if (version != contextVersion)
        {
            return;
        }
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
        }
    }

    private string? PreferredJourneyFile(string? profileKey, string? fallback)
    {
        if (profileKey is null || !editDrafts.TryGetValue(profileKey, out JourneyEditDraft? draft))
        {
            return fallback;
        }
        showArchived = draft.Archived;
        OnPropertyChanged(nameof(ShowArchived));
        return draft.FileName;
    }

    private void ClearJourneyDraft()
    {
        if (initializedProfileKey is not null)
        {
            editDrafts.Remove(initializedProfileKey);
        }
    }

    private sealed record JourneyEditDraft(
        string FileName,
        string? Name,
        string? Description,
        string? Notes,
        (long Address, DateTimeOffset Arrived)? System,
        bool Archived
    );
}
