using System.Text.Json;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class MiningWorkspaceViewModel
{
    private readonly MiningOverlaySettingsStore overlaySettings;
    private MiningOverlayPreferences lastOverlayPreferences = new();
    private readonly Dictionary<string, PendingOverlayEdit> pendingOverlayEdits = new(StringComparer.OrdinalIgnoreCase);
    private string? pendingDraftCommander;
    private string? overlayCommander;
    private string overlaySettingsStatus = "";

    public string OverlaySettingsStatus
    {
        get => overlaySettingsStatus;
        private set
        {
            if (Set(ref overlaySettingsStatus, value))
            {
                Changed(nameof(HasOverlaySettingsStatus));
            }
        }
    }

    public bool HasOverlaySettingsStatus => OverlaySettingsStatus.Length > 0;

    private void LoadOverlayPreferences(bool readCommanderDefaults = false)
    {
        try
        {
            MiningOverlayPreferences saved = ReadOverlayPreferences(
                () =>
                    readCommanderDefaults && overlayCommander is not null
                        ? MiningOverlayPreferences.From(store.Load(overlayCommander).Settings)
                        : MiningOverlayPreferences.From(Settings),
                out bool assignDraft
            );
            string key = overlayCommander ?? string.Empty;
            MiningOverlayPreferences current = pendingOverlayEdits.TryGetValue(key, out PendingOverlayEdit? pending)
                ? saved.WithChangesFrom(pending.Before, pending.After)
                : saved;
            if (overlayCommander is not null && pendingOverlayEdits.Remove(string.Empty, out PendingOverlayEdit? draft))
            {
                current = current.WithChangesFrom(draft.Before, draft.After);
                pendingDraftCommander = overlayCommander;
                assignDraft = true;
            }
            if (pending is not null || assignDraft || current != saved)
            {
                MiningOverlayPreferences baseline = pending is null
                    ? saved
                    : current.WithChangesFrom(pending.After, pending.Before);
                pendingOverlayEdits[key] = new(
                    baseline,
                    current,
                    assignDraft || pending?.AssignDraft == true,
                    pending?.ReplaceSnapshot == true
                );
            }
            current.ApplyTo(Settings);
            lastOverlayPreferences = saved;
            SaveOverlayPreferences();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            OverlaySettingsStatus = "Mining overlay settings could not be loaded: " + exception.Message;
        }
    }

    private bool SaveOverlayPreferences(bool replaceSnapshot = false)
    {
        string key = overlayCommander ?? string.Empty;
        var current = MiningOverlayPreferences.From(Settings);
        if (replaceSnapshot || current != lastOverlayPreferences || pendingOverlayEdits.ContainsKey(key))
        {
            pendingOverlayEdits[key] = pendingOverlayEdits.TryGetValue(key, out PendingOverlayEdit? prior)
                ? prior with
                {
                    After = current,
                    ReplaceSnapshot = replaceSnapshot || prior.ReplaceSnapshot,
                }
                : new(lastOverlayPreferences, current, false, replaceSnapshot);
        }
        string? failure = null;
        foreach ((string commanderKey, PendingOverlayEdit edit) in pendingOverlayEdits.ToArray())
        {
            try
            {
                if (commanderKey.Length == 0)
                {
                    overlaySettings.Save(null, edit.Before, edit.After);
                }
                else
                {
                    MiningOverlayPreferences persisted = overlaySettings.SaveCommander(
                        commanderKey,
                        edit.Before,
                        edit.After,
                        edit.AssignDraft,
                        edit.ReplaceSnapshot
                    );
                    if (commanderKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                    {
                        persisted.ApplyTo(Settings);
                        current = MiningOverlayPreferences.From(Settings);
                        Changed(nameof(Settings));
                    }
                }
                pendingOverlayEdits.Remove(commanderKey);
                if (commanderKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    lastOverlayPreferences = current;
                }
                if (edit.AssignDraft)
                {
                    pendingDraftCommander = null;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                failure ??= "Mining overlay settings could not be saved: " + exception.Message;
            }
        }
        OverlaySettingsStatus = failure ?? string.Empty;
        return failure is null;
    }

    private MiningOverlayPreferences ReadOverlayPreferences(
        Func<MiningOverlayPreferences> fallback,
        out bool assignDraft
    )
    {
        assignDraft = false;
        if (commander is null)
        {
            return overlaySettings.ReadPreferences(overlayCommander, fallback);
        }
        if (
            pendingDraftCommander is not null
            && !pendingDraftCommander.Equals(overlayCommander, StringComparison.OrdinalIgnoreCase)
        )
        {
            return overlaySettings.ReadPreferences(overlayCommander, fallback, includeDraft: false);
        }
        try
        {
            return overlaySettings.Load(overlayCommander, fallback);
        }
        catch (Exception exception)
            when (overlayCommander is not null && exception is IOException or UnauthorizedAccessException)
        {
            MiningOverlayPreferences result = overlaySettings.ReadPreferences(overlayCommander, fallback);
            assignDraft = true;
            pendingDraftCommander = overlayCommander;
            return result;
        }
    }

    private sealed record PendingOverlayEdit(
        MiningOverlayPreferences Before,
        MiningOverlayPreferences After,
        bool AssignDraft,
        bool ReplaceSnapshot = false
    );
}
