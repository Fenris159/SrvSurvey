using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class MiningWorkspaceViewModel : WorkspaceObservable, IDisposable
{
    private readonly MiningStore store;
    private readonly TimeProvider clock;
    private DateTimeOffset lastRecoverySave;
    private readonly Platform.MiningCommunityListener community;
    private readonly string attachmentDirectory;
    private readonly Platform.MiningSpeechOutput speech = new();
    private IReadOnlyList<string> voices = [];
    private string presetName = "Laser mining";
    private readonly IStarSystemResolver resolver;
    private readonly BookmarksViewModel bookmarks;
    private MiningWorkspaceState state = new(new MiningCommanderData());
    private string? commander;
    private bool storageAvailable;
    private bool sessionAvailable;
    private string system = "", body = "", ship = "", status = "Waiting for commander journal data.";
    private GalacticCoordinate? position;
    private CargoSnapshot? cargo;
    private EliteStatus? eliteStatus;
    private int capacity, selectedTab;
    private string notes = "", targetMaterial = "platinum", filter = "", thresholdText = "20", destination = "", distanceResult = "", primary = "Mining laser", secondary = "Collector limpet";
    private int firegroup;
    private double refineryTons;
    private string refineryMineral = "platinum";
    private MiningSession? selectedSession;
    private MiningSession? removedSession;
    private string origin = "";
    private MiningRing? selectedRing;
    private MiningMission? selectedMission;
    private DateTimeOffset? fullSince;
    private bool fullNotified;
    private DateTimeOffset lastAutoSearch;

    public MiningWorkspaceViewModel(string directory, IStarSystemResolver resolver, BookmarksViewModel bookmarks, HttpClient? networkClient = null, TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
        store = new MiningStore(directory);
        community = new Platform.MiningCommunityListener(directory);
        attachmentDirectory = Path.Combine(directory, "mining", "attachments");
        this.resolver = resolver;
        this.bookmarks = bookmarks;
        Search = new MiningSearchViewModel(new MiningSearchClient(networkClient), bookmarks, CacheRing, () => state.Data.Rings, resolver, community.Cache);
        StartCommand = new WorkspaceCommand(Start, () => sessionAvailable && storageAvailable && state.Session.Current is null);
        PauseCommand = new WorkspaceCommand(TogglePause, () => storageAvailable && state.Session.Current is not null);
        StopCommand = new WorkspaceCommand(Stop, () => storageAvailable && state.Session.Current is not null);
        SaveSettingsCommand = new WorkspaceCommand(SaveSettings);
        SaveNotesCommand = new WorkspaceCommand(() => { if (SelectedSession is { } session) { session.Notes = Notes; Save(); Refresh(); } });
        BookmarkRingCommand = new WorkspaceCommand(BookmarkRing);
        MissionHotspotsCommand = new WorkspaceCommand(() => { if (SelectedMission is { } mission) { Filter = mission.Commodity; Search.Mineral = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(mission.Commodity); SelectedTab = 3; } });
        AddAsteroidCommand = new WorkspaceCommand(() => AdjustAsteroids(1));
        RemoveAsteroidCommand = new WorkspaceCommand(() => AdjustAsteroids(-1));
    }

    public string CommunityStatus => community.Status;
    public MiningSearchViewModel Search { get; }
    public IReadOnlyList<string> Voices { get => voices; private set => Set(ref voices, value); }
    public string PresetName { get => presetName; set => Set(ref presetName, value); }
    public IReadOnlyList<string> PresetNames => Settings.AnnouncementPresets.Keys.Order().ToArray();
    public ICommand StartCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand SaveNotesCommand { get; }
    public ICommand BookmarkRingCommand { get; }
    public ICommand MissionHotspotsCommand { get; }
    public ICommand AddAsteroidCommand { get; }
    public ICommand RemoveAsteroidCommand { get; }
    public MiningPreferences Settings => state.Data.Settings;
    public double RefineryTons { get => refineryTons; set => Set(ref refineryTons, value); }
    public string RefineryMineral { get => refineryMineral; set => Set(ref refineryMineral, value); }
    public string RefinerySummary => string.Join(" · ", (Current ?? SelectedSession)?.RefineryEstimates.Select(p => $"{p.Key}: {p.Value:0.##} t") ?? []);
    public double MaximumHistoryRate => Math.Max(1, History.Select(s => s.TonsPerHour).DefaultIfEmpty(0).Max());
    public MiningSession? Current => state.Session.Current;
    public string CurrentSystem => system;
    public string Context => string.Join(" · ", new[] { system, body, ship }.Where(s => s.Length > 0));
    public string SessionSummary => Current is not { } session ? "No active session" : $"{(session.PausedAt is null ? "Active" : "Paused")} · {session.ActiveDuration:hh\\:mm\\:ss} · {session.RefinedTons:N0} t · {session.TonsPerHour:0.0} t/h · {session.Asteroids} asteroids · {session.CoreHits} cores";
    public string CargoSummary => cargo is null ? "Cargo unavailable" : $"Cargo: {cargo.Count:N0} / {(capacity > 0 ? capacity.ToString("N0") : "unknown")} t · Limpets: {cargo.GetCount("drones")}";
    public IReadOnlyList<CargoItem> Cargo => cargo?.Inventory ?? [];
    public IReadOnlyList<MiningMaterialSummary> Materials => Current?.Summarize(Settings.Thresholds) ?? [];
    public IReadOnlyList<MiningCollection> EngineeringMaterials => Current?.Collections.Where(c => c.Engineering).ToArray() ?? [];
    public IReadOnlyList<MiningProspect> Prospects => Current?.Prospects.AsEnumerable().Reverse().ToArray() ?? [];
    public IReadOnlyList<MiningMission> Missions => state.Missions.Missions.Select(m => m with { }).ToArray();
    public IReadOnlyList<MiningNotice> Notices => state.Notices;
    public IReadOnlyList<string> ReportScreenshots => SelectedSession?.Screenshots.ToArray() ?? [];
    public IReadOnlyList<MiningSession> History => state.Data.History;
    public IReadOnlyList<MiningRing> Rings => state.Data.Rings.Where(r => $"{r.System} {r.Body} {r.RingType} {r.Minerals}".Contains(Filter, StringComparison.OrdinalIgnoreCase)).OrderBy(r => r.Position is { } p && position is { } current ? p.DistanceTo(current) : double.MaxValue).ToArray();
    public string HistorySummary => $"{History.Count} sessions · {History.Sum(s => s.RefinedTons):N0} t refined · {History.Sum(s => s.ActiveDuration.TotalHours):0.0} active hours";
    public string ThresholdSummary => Settings.Thresholds.Count == 0 ? "All minerals are announced." : string.Join(" · ", Settings.Thresholds.Select(p => $"{p.Key} ≥ {p.Value:0.0}%"));
    public string Status { get => status; set => Set(ref status, value); }
    public int SelectedTab { get => selectedTab; set => Set(ref selectedTab, value); }
    public string Notes { get => notes; set => Set(ref notes, value); }
    public string Filter { get => filter; set { if (Set(ref filter, value)) Changed(nameof(Rings)); } }
    public string TargetMaterial { get => targetMaterial; set => Set(ref targetMaterial, value); }
    public string ThresholdText { get => thresholdText; set => Set(ref thresholdText, value); }
    public string Origin { get => origin; set => Set(ref origin, value); }
    public string Destination { get => destination; set => Set(ref destination, value); }
    public string DistanceResult { get => distanceResult; private set => Set(ref distanceResult, value); }
    public string Primary { get => primary; set => Set(ref primary, value); }
    public string Secondary { get => secondary; set => Set(ref secondary, value); }
    public int Firegroup { get => firegroup; set => Set(ref firegroup, value); }
    public MiningSession? SelectedSession { get => selectedSession; set { if (Set(ref selectedSession, value)) { Notes = value?.Notes ?? ""; Changed(nameof(RefinerySummary)); Changed(nameof(ReportScreenshots)); } } }
    public MiningRing? SelectedRing { get => selectedRing; set => Set(ref selectedRing, value); }
    public MiningMission? SelectedMission { get => selectedMission; set => Set(ref selectedMission, value); }
    public bool ShouldShowNotifications => CanShowShipOverlays && VisibleNotices.Count > 0;
    public bool ShouldShowFiregroups => CanShowShipOverlays && Settings.Firegroups.Count > 0;
    private bool CanShowShipOverlays => sessionAvailable && storageAvailable && eliteStatus is { InMainShip: true, OnFoot: false, InSrv: false }
        && (!Settings.HideInSupercruise || !eliteStatus.Flags.HasFlag(StatusFlags.Supercruise)) && (!Settings.OverlaysOnlyDuringSession || Current is not null);
    public IReadOnlyList<MiningNotice> VisibleNotices => Notices.Where(n => clock.GetUtcNow() - n.Time < TimeSpan.FromSeconds(Math.Clamp(Settings.NotificationSeconds, 3, 120))).Take(5).ToArray();
    public string ActiveFiregroup => Settings.Firegroups.FirstOrDefault(g => g.Group == eliteStatus?.FireGroup) is { } group
        ? $"Group {(char)('A' + group.Group)} · Primary: {group.Primary} · Secondary: {group.Secondary}" : "No mining firegroup configured";

    public void Apply(JournalMonitorUpdate update, JournalSessionState context, CargoSnapshot? currentCargo, EliteStatus? currentStatus)
    {
        sessionAvailable = !update.IsAwaitingCommanderIdentity && !context.IsShutdown && !string.IsNullOrWhiteSpace(context.FrontierId);
        if (!sessionAvailable) { if (Current is { PausedAt: null }) { state.Session.Pause(clock.GetUtcNow()); Save(); } Refresh(); return; }
        if (commander != context.FrontierId) Load(context.FrontierId!);
        if (!storageAvailable) return;
        eliteStatus = currentStatus;
        var cargoChanged = !Equals(cargo, currentCargo);
        cargo = currentCargo;
        var dirty = false;
        var previousNotice = state.Notices.FirstOrDefault();
        // Read location transitions in journal order: the outer projection already represents the end of the batch.
        foreach (var entry in update.JournalEvents)
        {
            var json = entry.Payload;
            if (entry.EventName is "Location" or "FSDJump" or "CarrierJump")
            {
                system = Text(json, "StarSystem");
                body = Text(json, "Body");
                if (json.TryGetProperty("StarPos", out var starPos) && starPos.ValueKind == JsonValueKind.Array && starPos.GetArrayLength() == 3)
                    position = new GalacticCoordinate(starPos[0].GetDouble(), starPos[1].GetDouble(), starPos[2].GetDouble());
            }
            if (entry.EventName is "SupercruiseExit" or "ApproachBody") body = Text(json, "Body");
            if (entry.EventName == "Loadout")
            {
                ship = Text(json, "Ship");
                if (json.TryGetProperty("CargoCapacity", out var c) && c.TryGetInt32(out var count)) capacity = count;
            }
            if (entry.EventName is "FSDJump" or "Location" or "CarrierJump")
            {
                var broadcast = new Dictionary<string, object> { ["$schemaRef"] = "https://eddn.edcd.io/schemas/journal/1", ["message"] = entry.Payload };
                community.Cache.Apply(JsonSerializer.Serialize(broadcast), clock.GetUtcNow());
            }
            dirty |= state.Apply(entry, update.IsBootstrapRead, system, body, ship, position);
        }
        system = context.SystemName ?? system; body = context.BodyName ?? body; ship = context.ShipType ?? ship; position = context.StarPosition ?? position;
        if (Search.Reference.Length == 0) Search.Reference = system;
        state.Missions.UpdateCargo(Cargo);
        if (dirty) Save();
        if (Settings.SpeakAnnouncements && !update.IsBootstrapRead && Runtime.DesktopExternalEffectPolicy.IsAllowed)
            foreach (var notice in state.Notices.TakeWhile(n => !ReferenceEquals(n, previousNotice)).Reverse()) speech.Speak(notice.Text, Settings.Voice, Settings.SpeechVolume, Settings.SpeechRate);
        if (Settings.AutoSearch && !update.IsBootstrapRead && clock.GetUtcNow() - lastAutoSearch > TimeSpan.FromSeconds(10)
            && update.JournalEvents.Any(e => e.EventName is "FSDJump" or "SAASignalsFound"))
        {
            lastAutoSearch = clock.GetUtcNow(); Search.Reference = system; _ = Search.SearchRingsAsync();
        }
        if (Settings.AutoSwitchTabs && update.JournalEvents.Any(e => e.EventName == "LaunchDrone" && Text(e.Payload, "Type").Equals("Prospector", StringComparison.OrdinalIgnoreCase)) && !update.IsBootstrapRead) SelectedTab = 0;
        if (dirty || cargoChanged) Refresh();
        Tick();
    }

    public void Tick()
    {
        if (Current is { PausedAt: null } && capacity > 0 && cargo?.Count >= capacity && Settings.NotifyCargoFull)
        {
            fullSince ??= clock.GetUtcNow();
            var latestActivity = Current.Collections.LastOrDefault()?.Time ?? Current.Started;
            if (!fullNotified && clock.GetUtcNow() - fullSince >= TimeSpan.FromMinutes(1) && clock.GetUtcNow() - latestActivity >= TimeSpan.FromMinutes(1))
            {
                state.Notices.Insert(0, new MiningNotice(clock.GetUtcNow(), "Cargo", "Cargo is full. End the session when ready."));
                fullNotified = true;
                Changed(nameof(Notices));
            }
        }
        else { fullSince = null; fullNotified = false; }
        if (sessionAvailable && Current is { PausedAt: null } session && clock.GetUtcNow() - lastRecoverySave >= TimeSpan.FromMinutes(1))
        {
            session.LastObserved = clock.GetUtcNow(); lastRecoverySave = clock.GetUtcNow(); Save();
        }
        Refresh(false);
    }

    public async Task LoadVoicesAsync()
    {
        if (!speech.IsSupported) { Status = "Local mining speech currently uses Windows voices."; return; }
        try { Voices = await speech.GetVoicesAsync(); Status = Voices.Count > 0 ? "Local voices loaded." : "No local voices are available."; }
        catch (Exception ex) when (ex is TimeoutException or System.Runtime.InteropServices.COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { Status = "Speech unavailable: " + ex.Message; }
    }
    public void SaveAnnouncementPreset()
    {
        if (string.IsNullOrWhiteSpace(PresetName)) return;
        Settings.AnnouncementPresets[PresetName.Trim()] = new MiningAnnouncementPreset(new Dictionary<string, double>(Settings.Thresholds), Settings.AnnounceCores, Settings.AnnounceNonCores);
        Save(); Changed(nameof(PresetNames));
    }
    public void LoadAnnouncementPreset()
    {
        if (!Settings.AnnouncementPresets.TryGetValue(PresetName, out var preset)) return;
        Settings.Thresholds = new Dictionary<string, double>(preset.Thresholds);
        Settings.AnnounceCores = preset.Cores; Settings.AnnounceNonCores = preset.NonCores;
        Changed(nameof(Settings)); Save(); Refresh();
    }
    public void Dispose() { Search.Dispose(); speech.Dispose(); community.Dispose(); }
    public void SetThreshold(bool remove)
    {
        if (string.IsNullOrWhiteSpace(TargetMaterial)) { Status = "Enter a mineral name."; return; }
        var name = TargetMaterial.Trim().ToLowerInvariant();
        if (remove) Settings.Thresholds.Remove(name);
        else
        {
            if (!double.TryParse(ThresholdText, NumberStyles.Number, CultureInfo.CurrentCulture, out var threshold) || !double.IsFinite(threshold) || threshold is < 0 or > 100) { Status = "Enter a percentage from 0 to 100."; return; }
            Settings.Thresholds[name] = threshold;
        }
        SaveSettings();
    }
    public void AdjustQuality(int delta)
    {
        if ((Current ?? SelectedSession) is not { } session || string.IsNullOrWhiteSpace(TargetMaterial)) return;
        var material = TargetMaterial.Trim().ToLowerInvariant();
        session.QualityAdjustments[material] = Math.Clamp(session.QualityAdjustments.GetValueOrDefault(material) + delta, -session.Prospects.Count, session.Prospects.Count);
        Save(); Refresh(); Status = "Quality-hit adjustment saved; journal observations are unchanged.";
    }
    public void SaveRefineryEstimate()
    {
        if ((Current ?? SelectedSession) is not { } session) { Status = "Start or select a session first."; return; }
        if (string.IsNullOrWhiteSpace(RefineryMineral) || !double.IsFinite(RefineryTons) || RefineryTons is < 0 or > 16) { Status = "Enter a mineral and 0–16 pending tons."; return; }
        if (RefineryTons == 0) session.RefineryEstimates.Remove(RefineryMineral.Trim());
        else session.RefineryEstimates[RefineryMineral.Trim()] = RefineryTons;
        Save(); Changed(nameof(RefinerySummary));
    }
    public void RemoveFiregroup() { Settings.Firegroups.RemoveAll(g => g.Group == Firegroup); SaveSettings(); }
    public void SaveFiregroup()
    {
        if (Firegroup is < 0 or > 7) return;
        Settings.Firegroups.RemoveAll(g => g.Group == Firegroup);
        Settings.Firegroups.Add(new MiningFiregroup(Firegroup, Primary.Trim(), Secondary.Trim()));
        SaveSettings();
    }
    public async Task CalculateDistanceAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var query = Destination.Trim();
            var from = Origin.Trim();
            var current = from.Length == 0 || from.Equals(system, StringComparison.OrdinalIgnoreCase) ? position
                : (await resolver.SearchAsync(from, timeout.Token)).FirstOrDefault(s => s.Name.Equals(from, StringComparison.OrdinalIgnoreCase))?.Position;
            var result = (await resolver.SearchAsync(query, timeout.Token)).FirstOrDefault(s => s.Name.Equals(query, StringComparison.OrdinalIgnoreCase));
            DistanceResult = result is null ? "System not found." : current is { } coordinates ? $"{coordinates.DistanceTo(result.Position):N2} ly · {result.Position} · {result.Position.DistanceTo(new GalacticCoordinate(0, 0, 0)):N2} ly from Sol" : "Origin coordinates unavailable.";
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or ArgumentException) { DistanceResult = "System lookup unavailable: " + ex.Message; }
    }
    public void ImportReports(string csv)
    {
        if (!storageAvailable) { Status = "Connect a commander before importing reports."; return; }
        var incoming = MiningReportImport.ReadCsv(csv);
        var added = 0;
        foreach (var session in incoming)
            if (!History.Any(s => s.Id == session.Id || s.Started == session.Started && s.System == session.System && s.Ring == session.Ring)) { state.Data.History.Add(session); added++; }
        Save(); Refresh(); Status = $"Imported {added} reports; existing sessions retained.";
    }
    public void RemoveScreenshot(string path) { if (SelectedSession is { } session) { session.Screenshots.Remove(path); Save(); Changed(nameof(ReportScreenshots)); } }
    public void DeleteSelectedReport()
    {
        if (SelectedSession is not { } session) return;
        removedSession = session; state.Data.History.Remove(session); SelectedSession = null; Save(); Refresh(); Status = "Report removed. Undo remains available until the next deletion or restart.";
    }
    public void UndoDeleteReport()
    {
        if (removedSession is not { } session) return;
        state.Data.History.Add(session); SelectedSession = session; removedSession = null; Save(); Refresh();
    }
    public async Task ImportJournalsAsync(IReadOnlyList<string> paths)
    {
        if (!storageAvailable || commander is null) { Status = "Connect a commander before importing journals."; return; }
        var importingCommander = commander;
        Status = "Reading selected journals…";
        try
        {
            var imported = await Task.Run(() => MiningJournalImporter.ReadAsync(paths, importingCommander));
            if (commander != importingCommander) { Status = "Commander changed; import was not applied."; return; }
            state.Import(imported);
            Save(); Refresh(); Status = $"Imported {imported.Rings.Count} rings and {imported.Missions.Count} mission records. Existing newer observations retained.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Status = "Journal import failed: " + ex.Message; }
    }
    public Task<byte[]> BackupPackageAsync()
    {
        var snapshot = MiningStore.Parse(Backup());
        var bookmarkJson = bookmarks.Export();
        return Task.Run(() => MiningBackup.Create(snapshot, bookmarkJson));
    }
    public async Task RestorePackageAsync(byte[] bytes)
    {
        var targetCommander = commander;
        if (targetCommander is null || !storageAvailable) { Status = "Connect a commander before restoring."; return; }
        var contents = await Task.Run(() => MiningBackup.Read(bytes, attachmentDirectory));
        if (commander != targetCommander) { Status = "Commander changed; backup was not applied."; return; }
        if (!Restore(store.Export(contents.Data))) return;
        bookmarks.Restore(contents.Bookmarks);
        Status += " " + bookmarks.Status;
    }
    public string Backup() { state.Synchronize(); return store.Export(state.Data); }
    public bool Restore(string json)
    {
        if (!storageAvailable || commander is null) return false;
        try { state = new MiningWorkspaceState(store.Restore(commander, json)); PauseRecoveredSession(); Search.LoadOptions(Settings.SearchOptions); community.SetEnabled(Settings.ReceiveCommunityData); Changed(nameof(Settings)); Refresh(); Status = "Mining backup restored. Previous data retained in the backup folder."; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Status = ex.Message; return false; }
    }
    private void Load(string id)
    {
        commander = id;
        capacity = 0; system = body = ship = ""; position = null; cargo = null; eliteStatus = null;
        fullSince = null; fullNotified = false;
        removedSession = null;
        selectedSession = null; selectedRing = null; selectedMission = null;
        try { state = new MiningWorkspaceState(store.Load(id)); storageAvailable = true; PauseRecoveredSession(); Status = "Mining journal connected."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { storageAvailable = false; state = new(new MiningCommanderData()); Status = "Mining data could not be loaded: " + ex.Message; }
        Search.LoadOptions(Settings.SearchOptions);
        community.SetEnabled(Settings.ReceiveCommunityData);
        Changed(nameof(Settings));
    }
    private void PauseRecoveredSession()
    {
        if (Current is not { PausedAt: null } session) return;
        var lastActivity = session.LastObserved ?? session.Collections.Select(c => c.Time).Concat(session.Prospects.Select(p => p.Time)).Append(session.Started).Max();
        state.Session.Pause(lastActivity);
    }
    private void Start() { state.Session.Start(clock.GetUtcNow(), system, body, ship); Save(); Refresh(); }
    private void Stop() { state.Stop(clock.GetUtcNow()); Save(); SelectedSession = History.FirstOrDefault(); if (Settings.AutoSwitchTabs) SelectedTab = 3; Refresh(); }
    private void TogglePause() { if (Current?.PausedAt is null) state.Session.Pause(clock.GetUtcNow()); else state.Session.Resume(clock.GetUtcNow()); Save(); Refresh(); }
    private void AdjustAsteroids(int delta) { if (Current is { } session && session.Asteroids + delta >= 0) { session.AsteroidAdjustment += delta; Save(); Refresh(); } }
    private void SaveSettings() { Settings.SearchOptions = Search.SaveOptions(); community.SetEnabled(Settings.ReceiveCommunityData); Save(); Refresh(); }
    private void CacheRing(MiningRing ring)
    {
        state.CacheRing(ring);
        Save(); Refresh();
    }
    private void BookmarkRing()
    {
        if (SelectedRing is not { } ring) return;
        bookmarks.AddMiningLocation(new GalacticBookmark { System = ring.System, Body = ring.Body, Position = ring.Position, Category = "Mining", Minerals = ring.Minerals, RingType = ring.RingType, Reserve = ring.Reserve });
        Status = bookmarks.Status;
    }
    private void Save()
    {
        if (!storageAvailable || commander is null) return;
        try { state.Synchronize(); store.Save(commander, state.Data); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Status = "Mining data could not be saved: " + ex.Message; }
    }
    private void Refresh(bool dataChanged = true)
    {
        if (!dataChanged)
        {
            foreach (var name in new[] { nameof(CommunityStatus), nameof(SessionSummary), nameof(ShouldShowNotifications), nameof(ShouldShowFiregroups), nameof(VisibleNotices), nameof(ActiveFiregroup) }) Changed(name);
            return;
        }
        foreach (var name in new[] { nameof(ReportScreenshots), nameof(Current), nameof(Context), nameof(CurrentSystem), nameof(SessionSummary), nameof(CargoSummary), nameof(Cargo), nameof(Materials), nameof(EngineeringMaterials), nameof(Prospects), nameof(Missions), nameof(Notices), nameof(History), nameof(HistorySummary), nameof(MaximumHistoryRate), nameof(RefinerySummary), nameof(Rings), nameof(ThresholdSummary), nameof(ShouldShowNotifications), nameof(ShouldShowFiregroups), nameof(VisibleNotices), nameof(ActiveFiregroup) }) Changed(name);
        foreach (var command in new[] { StartCommand, PauseCommand, StopCommand }) ((WorkspaceCommand)command).Refresh();
    }
    private static string Text(JsonElement json, string name) => json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
