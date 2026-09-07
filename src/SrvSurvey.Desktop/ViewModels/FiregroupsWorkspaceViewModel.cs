using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using SrvSurvey.Core.Firegroups;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class FiregroupsWorkspaceViewModel : WorkspaceObservable
{
    private readonly FiregroupStore store;
    private readonly MiningStore legacyStore;
    private FiregroupDocument document = new();
    private string? commander;
    private bool storageAvailable;
    private string status = "Waiting for a commander and Loadout journal event.";
    private FiregroupShip? liveShip, editorShip;
    private EliteStatus? latestStatus;
    private string boarded = "unknown";
    private string? profileId;
    private string configurationName = "";
    private int groupNumber;
    private bool loading;
    private readonly SortedDictionary<int, FiregroupAssignment> groups = new();
    private readonly Dictionary<string, EditorDraft> drafts = new();

    public FiregroupsWorkspaceViewModel(string directory)
    {
        store = new(directory);
        legacyStore = new(directory);
        AddPrimaryCommand = new WorkspaceCommand(() => AddRow(Primary));
        AddSecondaryCommand = new WorkspaceCommand(() => AddRow(Secondary));
        PreviousGroupCommand = new WorkspaceCommand(() => ChangeGroup((groupNumber + 7) % 8));
        NextGroupCommand = new WorkspaceCommand(() => ChangeGroup((groupNumber + 1) % 8));
        AddGroupCommand = new WorkspaceCommand(AddGroup);
        RemoveGroupCommand = new WorkspaceCommand(RemoveGroup);
        SaveCommand = new WorkspaceCommand(Save);
        RemoveCommand = new WorkspaceCommand(Remove);
        NewCommand = new WorkspaceCommand(() => OpenEditor(liveShip, null));
        ResetRows(null);
    }

    public ObservableCollection<FiregroupSelectionRow> Primary { get; } = [];
    public ObservableCollection<FiregroupSelectionRow> Secondary { get; } = [];
    public ObservableCollection<FiregroupTreeNode> GroupPreview { get; } = [];
    public ObservableCollection<FiregroupSavedRow> SavedProfiles { get; } = [];
    public FiregroupSavedRow? SelectedSavedProfile => SavedProfiles.FirstOrDefault(row => row.Profile.Id == profileId);
    public IReadOnlyList<string> GroupLetters { get; } = ["A", "B", "C", "D", "E", "F", "G", "H"];
    public string GroupLetter { get => GroupLetters[groupNumber]; set { var number = GroupLetters.ToList().IndexOf(value); if (number >= 0 && number != groupNumber) ChangeGroup(number); } }
    public string ConfigurationName { get => configurationName; set => Set(ref configurationName, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public string ShipSummary => editorShip?.Display ?? "Waiting for a Loadout event. Board your ship to identify its equipped modules.";
    public bool CanEdit => storageAvailable && editorShip is not null;
    public bool HasEquippedModules => editorShip?.Modules.Any(module => !FiregroupLoadout.IsExcluded(module)) == true;
    public string ModuleStatus => HasEquippedModules ? "Equipped hardpoints, utilities, Surface Scanner and internal limpet controllers are listed with the ship's built-in scanners. Shield cell banks, shield boosters, point defence, power distributors, module reinforcement and cargo racks are excluded. Slots distinguish duplicate modules."
        : "No matching equipped modules are available yet. A full Loadout event is needed; saved assignments remain editable.";
    public string LiveSummary => ActiveProfile is { } profile ? $"Active: {profile.Name} · {liveShip?.Display}" : "No saved configuration selected for the current ship.";
    public FiregroupProfile? ActiveProfile => liveShip is null ? null : document.Profiles.FirstOrDefault(p => p.Ship.Key == liveShip.Key
        && document.ActiveProfiles.GetValueOrDefault(liveShip.Key) == p.Id);
    public int ActiveGroupNumber => latestStatus?.FireGroup ?? 0;
    public FiregroupAssignment? ActiveGroup => ActiveProfile?.Groups.FirstOrDefault(g => g.Number == ActiveGroupNumber);
    public bool ShouldShow => storageAvailable && liveShip is not null && latestStatus is { OnFoot: false }
        && boarded == liveShip.Type && ActiveProfile is not null;
    public ICommand AddPrimaryCommand { get; }
    public ICommand AddSecondaryCommand { get; }
    public ICommand PreviousGroupCommand { get; }
    public ICommand NextGroupCommand { get; }
    public ICommand AddGroupCommand { get; }
    public ICommand RemoveGroupCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand NewCommand { get; }

    public void Apply(JournalMonitorUpdate update, JournalSessionState journal, EliteStatus? currentStatus)
    {
        var previousCommander = commander;
        var nextCommander = journal.FrontierId;
        if (update.IsAwaitingCommanderIdentity || journal.IsShutdown) nextCommander = null;
        if (commander != nextCommander) ChangeCommander(nextCommander);
        latestStatus = currentStatus;
        boarded = OverlayVehicleCatalog.Resolve(journal, currentStatus);
        if (commander is null || !storageAvailable) { NotifyLive(); return; }

        var loadouts = ReadLoadouts(update.JournalEvents, previousCommander);
        var changed = UpdateLoadouts(loadouts);
        var nextShip = document.Ships.LastOrDefault(s => s.Type.Equals(journal.ShipType, StringComparison.OrdinalIgnoreCase)
            && (journal.ShipId is null || s.Id == journal.ShipId));
        if (liveShip?.Key != nextShip?.Key)
        {
            liveShip = nextShip;
            ImportLegacy();
            OpenEditor(nextShip, ActiveProfile);
        }
        else if (nextShip is not null && liveShip != nextShip)
        {
            liveShip = nextShip;
            if (editorShip?.Key == nextShip.Key) { editorShip = nextShip; RefreshOptions(); NotifyEditor(); }
        }
        if (changed) Persist(document, "Equipped loadout updated.");
        NotifyLive();
    }

    private bool UpdateLoadouts(List<FiregroupShip> loadouts)
    {
        var changed = false;
        foreach (var ship in loadouts)
        {
            var old = document.Ships.FirstOrDefault(s => s.Key == ship.Key);
            if (old is not null && old.Name == ship.Name && old.Modules.SequenceEqual(ship.Modules)) continue;
            document.Ships.RemoveAll(s => s.Key == ship.Key); document.Ships.Add(ship); changed = true;
        }
        return changed;
    }

    private void ChangeCommander(string? nextCommander)
    {
        StashDraft();
        commander = nextCommander;
        document = new(); storageAvailable = false; liveShip = null; editorShip = null; profileId = null;
        if (commander is not null)
        {
            try { document = store.Load(commander); storageAvailable = true; }
            catch (Exception ex) when (IsStorageError(ex)) { Status = "Firegroups could not be loaded: " + ex.Message; }
        }
        RefreshSaved();
        ClearEditor();
    }
    private List<FiregroupShip> ReadLoadouts(IReadOnlyList<JournalEventEnvelope> entries, string? previousCommander)
    {
        var loadouts = new List<FiregroupShip>();
        var eventCommander = previousCommander ?? commander;
        foreach (var entry in entries)
        {
            if (entry.EventName is "LoadGame" or "Commander"
                && entry.Payload.TryGetProperty("FID", out var fid) && fid.ValueKind == JsonValueKind.String)
                eventCommander = fid.GetString();
            if (entry.EventName == "Loadout" && eventCommander == commander && FiregroupLoadout.Parse(entry.Payload) is { } ship)
                loadouts.Add(ship);
        }
        return loadouts;
    }

    public string Backup(string expectedCommander)
    {
        if (commander != expectedCommander || !storageAvailable)
            throw new IOException("Connect the same commander before backing up Firegroups.");
        return FiregroupStore.Export(document);
    }

    public bool Restore(string expectedCommander, string json)
    {
        if (commander != expectedCommander || !storageAvailable)
        { Status = "Commander changed; Firegroups backup was not applied."; return false; }
        try
        {
            document = store.Restore(expectedCommander, json);
            drafts.Clear();
            editorShip = null; profileId = null;
            OpenEditor(liveShip, ActiveProfile);
            RefreshSaved(); NotifyLive();
            Status = "Firegroups restored. Previous configurations retained in the before-restore file.";
            return true;
        }
        catch (Exception ex) when (IsStorageError(ex))
        { Status = "Firegroups restore failed: " + ex.Message; return false; }
    }

    private void ImportLegacy()
    {
        if (document.LegacyImported || liveShip is null || commander is null) return;
        try
        {
            var legacy = legacyStore.Load(commander).Settings.Firegroups;
            if (legacy.Count > 0)
            {
                var profile = new FiregroupProfile(Guid.NewGuid().ToString("N"), "Imported firegroups", liveShip,
                    legacy.Select(g => new FiregroupAssignment(g.Group, LegacyModules(g.Primary), LegacyModules(g.Secondary))).ToArray());
                document.Profiles.Add(profile); document.ActiveProfiles[liveShip.Key] = profile.Id;
            }
            document.LegacyImported = true;
            Persist(document, legacy.Count > 0 ? "Previous firegroups imported. Select equipped modules to update their assignments." : "Ship loadout ready.");
            RefreshSaved();
        }
        catch (Exception ex) when (IsStorageError(ex)) { Status = "Previous firegroups could not be imported: " + ex.Message; }
    }
    private static FiregroupModule[] LegacyModules(string value) => string.IsNullOrWhiteSpace(value) ? [] : [new("", "", value)];

    private void OpenEditor(FiregroupShip? ship, FiregroupProfile? profile)
    {
        StashDraft();
        editorShip = ship; profileId = profile?.Id;
        ClearEditor();
        ConfigurationName = profile?.Name ?? "";
        if (profile is not null) foreach (var group in profile.Groups) groups[group.Number] = group;
        if (drafts.TryGetValue(DraftKey, out var draft))
        {
            ConfigurationName = draft.Name;
            groups.Clear(); foreach (var group in draft.Groups) groups[group.Number] = group;
            groupNumber = draft.Number;
            ResetRows(draft.Current);
        }
        else ResetRows(groups.GetValueOrDefault(groupNumber));
        RefreshPreview(); NotifyEditor();
    }
    private string DraftKey => $"{commander}|{profileId ?? "new:" + editorShip?.Key}";
    private void StashDraft()
    {
        if (editorShip is null) return;
        drafts[DraftKey] = new(ConfigurationName, groupNumber, groups.Values.ToArray(), Capture());
    }
    private void ClearEditor()
    {
        groups.Clear(); groupNumber = 0; ConfigurationName = ""; ResetRows(null); RefreshPreview(); NotifyEditor();
    }
    private void ChangeGroup(int number)
    {
        if (!CommitCurrent(requireModules: false)) return;
        groupNumber = number; ResetRows(groups.GetValueOrDefault(number)); Changed(nameof(GroupLetter)); RefreshPreview();
    }
    private void AddGroup()
    {
        if (!CanEdit) { Status = "Wait for a commander and ship Loadout before adding groups."; return; }
        if (!CommitCurrent(requireModules: true)) return;
        Status = $"Group {GroupLetter} added to the draft. Save the named configuration when ready.";
        var next = Enumerable.Range(1, 8).Select(offset => (groupNumber + offset) % 8).FirstOrDefault(n => !groups.ContainsKey(n), groupNumber);
        groupNumber = next; ResetRows(groups.GetValueOrDefault(next)); Changed(nameof(GroupLetter)); RefreshPreview();
    }
    private void RemoveGroup()
    {
        groups.Remove(groupNumber); ResetRows(null); RefreshPreview(); Status = $"Group {GroupLetter} removed from the draft; save to keep the change.";
    }
    private FiregroupAssignment Capture() => new(groupNumber, Primary.Select(r => r.SelectedModule).OfType<FiregroupModule>().ToArray(), Secondary.Select(r => r.SelectedModule).OfType<FiregroupModule>().ToArray());
    private bool CommitCurrent(bool requireModules)
    {
        var current = Capture();
        if (current.Primary.Count + current.Secondary.Count == 0)
        {
            if (requireModules) { Status = "Choose at least one primary or secondary module."; return false; }
            groups.Remove(groupNumber);
            return true;
        }
        if (new[] { current.Primary, current.Secondary }.Any(side => side.DistinctBy(m => (m.Slot, m.Symbol)).Count() != side.Count))
        { Status = "Each equipped module can appear only once per trigger. Use its slot to distinguish duplicate modules."; return false; }
        groups[groupNumber] = current; RefreshPreview(); return true;
    }
    private void ResetRows(FiregroupAssignment? group)
    {
        loading = true;
        try
        {
            Primary.Clear(); Secondary.Clear();
            foreach (var module in group?.Primary ?? []) AddRow(Primary, module);
            foreach (var module in group?.Secondary ?? []) AddRow(Secondary, module);
            if (Primary.Count == 0) AddRow(Primary);
            if (Secondary.Count == 0) AddRow(Secondary);
        }
        finally { loading = false; }
    }
    private void AddRow(ObservableCollection<FiregroupSelectionRow> target, FiregroupModule? module = null)
    {
        var row = new FiregroupSelectionRow(editorShip?.Modules ?? [], module, () => { if (!loading) RefreshPreview(); });
        row.RemoveCommand = new WorkspaceCommand(() => { target.Remove(row); RefreshPreview(); });
        target.Add(row);
    }
    private void RefreshOptions() { foreach (var row in Primary.Concat(Secondary)) row.UpdateOptions(editorShip?.Modules ?? []); }
    private void RefreshPreview()
    {
        var preview = new SortedDictionary<int, FiregroupAssignment>(groups);
        var current = Capture();
        if (current.Primary.Count + current.Secondary.Count > 0) preview[groupNumber] = current;
        else preview.Remove(groupNumber);
        GroupPreview.Clear(); foreach (var group in preview.Values) GroupPreview.Add(FiregroupTreeNode.From(group));
    }

    private void Save()
    {
        if (!CanEdit || commander is null || editorShip is null) { Status = "Connect a commander and read a ship Loadout before saving."; return; }
        if (string.IsNullOrWhiteSpace(ConfigurationName)) { Status = "Enter a firegroup configuration name before saving."; return; }
        if (!CommitCurrent(requireModules: false)) return;
        if (groups.Count == 0) { Status = "Add at least one configured group before saving."; return; }
        var name = ConfigurationName.Trim();
        if (document.Profiles.Any(p => p.Ship.Key == editorShip.Key && p.Id != profileId && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        { Status = "That name is already saved for this ship. Select it below to edit, or choose a different name."; return; }
        var profile = new FiregroupProfile(profileId ?? Guid.NewGuid().ToString("N"), name, editorShip, groups.Values.ToArray());
        var candidate = document with { Profiles = document.Profiles.Where(p => p.Id != profile.Id).Append(profile).ToList(), ActiveProfiles = new(document.ActiveProfiles) { [editorShip.Key] = profile.Id } };
        if (!Persist(candidate, $"Saved {name} for {editorShip.Display}.")) return;
        drafts.Remove(DraftKey); document = candidate; profileId = profile.Id;
        drafts.Remove(DraftKey); RefreshSaved(); NotifyLive();
    }
    private void Remove()
    {
        if (profileId is null) { Status = "Select a saved configuration to remove."; return; }
        if (SelectedSavedProfile is { } row) Remove(row.Profile);
    }
    private void Remove(FiregroupProfile target)
    {
        // Confirmation can span a commander switch or a restore. Never act on a stale row.
        if (!document.Profiles.Any(profile => ReferenceEquals(profile, target))) return;
        var candidate = document with { Profiles = document.Profiles.Where(p => p.Id != target.Id).ToList(), ActiveProfiles = new(document.ActiveProfiles) };
        foreach (var key in candidate.ActiveProfiles.Where(p => p.Value == target.Id).Select(p => p.Key).ToArray()) candidate.ActiveProfiles.Remove(key);
        if (!Persist(candidate, $"Removed {target.Name}.")) return;
        drafts.Remove($"{commander}|{target.Id}");
        document = candidate;
        if (profileId == target.Id) { profileId = null; ClearEditor(); }
        RefreshSaved(); NotifyLive();
    }
    private void Select(FiregroupProfile profile)
    {
        var ship = document.Ships.FirstOrDefault(s => s.Key == profile.Ship.Key) ?? profile.Ship;
        var candidate = document with { ActiveProfiles = new(document.ActiveProfiles) { [ship.Key] = profile.Id } };
        if (Persist(candidate, $"Editing {profile.Name}. Changes are saved only when you select Save.")) document = candidate;
        OpenEditor(ship, profile); NotifyLive();
    }
    private bool Persist(FiregroupDocument candidate, string message)
    {
        if (commander is null || !storageAvailable) return false;
        try { store.Save(commander, candidate); Status = message; return true; }
        catch (Exception ex) when (IsStorageError(ex)) { Status = "Firegroups could not be saved: " + ex.Message; return false; }
    }
    private static bool IsStorageError(Exception ex) => ex is IOException or UnauthorizedAccessException or JsonException;
    private void RefreshSaved()
    {
        SavedProfiles.Clear();
        foreach (var profile in document.Profiles.OrderBy(p => p.Ship.Display).ThenBy(p => p.Name)) SavedProfiles.Add(new(profile, new WorkspaceCommand(() => Select(profile)), new WorkspaceCommand(() => Remove(profile))));
    }
    private void NotifyEditor() { Changed(nameof(ShipSummary)); Changed(nameof(CanEdit)); Changed(nameof(HasEquippedModules)); Changed(nameof(ModuleStatus)); Changed(nameof(GroupLetter)); }
    private void NotifyLive() { Changed(nameof(ActiveProfile)); Changed(nameof(ActiveGroup)); Changed(nameof(ActiveGroupNumber)); Changed(nameof(ShouldShow)); Changed(nameof(LiveSummary)); }
    private sealed record EditorDraft(string Name, int Number, IReadOnlyList<FiregroupAssignment> Groups, FiregroupAssignment Current);
}

public sealed class FiregroupSelectionRow : WorkspaceObservable
{
    private FiregroupModule? selected;
    private readonly Action changed;
    public FiregroupSelectionRow(IReadOnlyList<FiregroupModule> modules, FiregroupModule? selection, Action changed)
    { this.changed = changed; selected = selection; UpdateOptions(modules); }
    public IReadOnlyList<FiregroupModule> Options { get; private set; } = [];
    public FiregroupModule? SelectedModule { get => selected; set { if (Set(ref selected, value)) { Changed(nameof(Warning)); Changed(nameof(HasWarning)); changed(); } } }
    public bool HasWarning => Warning.Length > 0;
    public string Warning
    {
        get
        {
            if (selected is null) return "";
            if (FiregroupLoadout.IsExcluded(selected)) return $"{selected.Name} is excluded from Firegroups; choose a replacement or remove this row.";
            return equipped.Any(m => m.Slot == selected.Slot && m.Symbol == selected.Symbol)
                ? "" : "Not equipped in the latest loadout; choose a replacement or remove this row.";
        }
    }
    private IReadOnlyList<FiregroupModule> equipped = [];
    public ICommand? RemoveCommand { get; set; }
    public void UpdateOptions(IReadOnlyList<FiregroupModule> modules)
    {
        equipped = modules.Where(module => !FiregroupLoadout.IsExcluded(module)).ToArray();
        var selection = selected;
        Options = selection is not null && !FiregroupLoadout.IsExcluded(selection) && !equipped.Contains(selection)
            ? equipped.Append(selection).ToArray() : equipped;
        Changed(nameof(Options));
        selected = selection; Changed(nameof(SelectedModule)); Changed(nameof(Warning)); Changed(nameof(HasWarning));
    }
}

public sealed record FiregroupTreeNode(string Label, IReadOnlyList<FiregroupTreeNode> Children)
{
    public static FiregroupTreeNode From(FiregroupAssignment group) => new(group.Label,
        [new("Primary", group.Primary.Select(m => new FiregroupTreeNode(m.Display, [])).ToArray()),
         new("Secondary", group.Secondary.Select(m => new FiregroupTreeNode(m.Display, [])).ToArray())]);
}
public sealed record FiregroupSavedRow(FiregroupProfile Profile, ICommand EditCommand, ICommand DeleteCommand)
{
    public string Name => Profile.Name;
    public string ShipName => Profile.Ship.Display;
    public string DeleteLabel => $"Delete {Name}";
    public IReadOnlyList<FiregroupTreeNode> Preview { get; } = Profile.Groups.Select(FiregroupTreeNode.From).ToArray();
}
