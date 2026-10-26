using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationViewModel
{
    private string? localFocusProjectId;

    private string? FocusProjectId => localFocusProjectId ?? primaryProjectId;

    private void RetireMissingPlannedFocus(IReadOnlyList<ColonizationProject> plans)
    {
        if (
            localFocusProjectId is not { Length: > 0 } focusId
            || !authoritativePlanningPrefixes.Any(prefix => focusId.StartsWith(prefix, StringComparison.Ordinal))
            || plans.Any(plan => string.Equals(plan.BuildId, focusId, StringComparison.OrdinalIgnoreCase))
        )
        {
            return;
        }
        localFocusProjectId = null;
        if (CommanderName is { } commander && ClearSavedLocalFocus(commander) is { } warning)
        {
            plannedReadWarning = "Project focus could not be saved: " + warning;
        }
    }

    /// <summary>Focuses catalog plans locally and keeps live-build focus synchronized with Raven.</summary>
    public async Task ToggleFocusProjectAsync(ColonizationProjectRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!IsEnabled || IsBusy || CommanderName is not { } commander)
        {
            return;
        }

        string? nextId = row.IsFocused ? null : row.Project.BuildId;
        int version = recovery.ProfileVersion;
        IsBusy = true;
        StatusMessage = nextId is null ? "Clearing project focus..." : $"Focusing {row.BuildName}...";
        try
        {
            string? persistenceWarning = null;
            if (row.Project.IsPlanned || projectScope.IsLocal(row.Project))
            {
                SaveLocalFocus(commander, nextId, row.Project);
            }
            else
            {
                await client.SetPrimaryProjectAsync(commander, nextId, CancellationToken.None);
                if (version != recovery.ProfileVersion)
                {
                    return;
                }
                primaryProjectId = nextId;
                localFocusProjectId = null;
                persistenceWarning = ClearSavedLocalFocus(commander);
            }
            projectScope.ReleaseUnselected(FocusProjectId);
            SaveLocalProjects();
            RebuildProjectRows();
            UpdateProjectSummary();
            StatusMessage = nextId is null ? "Project focus was cleared." : $"{row.BuildName} is now the focus.";
            if (persistenceWarning is not null)
            {
                StatusMessage += " Local focus preferences could not be saved: " + persistenceWarning;
            }
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or InvalidDataException
                        or TaskCanceledException
                        or ArgumentException
                        or IOException
                        or UnauthorizedAccessException
            )
        {
            if (version == recovery.ProfileVersion)
            {
                StatusMessage = "Project focus could not be saved: " + exception.Message;
            }
        }
        finally
        {
            IsBusy = false;
            await RefreshPendingAsync(CancellationToken.None);
        }
    }

    private string? ClearSavedLocalFocus(string commander)
    {
        try
        {
            settingsStore.SaveLocalProjectFocus(commander, null);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return exception.Message;
        }
    }

    private void SaveLocalFocus(string commander, string? nextId, ColonizationProject project)
    {
        string localId = nextId ?? string.Empty;
        settingsStore.SaveLocalProjectFocus(commander, localId);
        localFocusProjectId = localId;
        if (nextId is not null && projectScope.IsLocal(project))
        {
            projectScope.RetainFocus(project);
        }
    }
}
