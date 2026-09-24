using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

public sealed record MiningAnnouncementOutputs(Platform.IMiningSpeechOutput Speech, Platform.IMiningChimeOutput Chime);

public sealed class MiningWorkspaceViewModel : WorkspaceObservable, IDisposable
{
    private readonly MiningStore store;
    private readonly FiregroupsWorkspaceViewModel? firegroups;
    private readonly TimeProvider clock;
    private DateTimeOffset lastRecoverySave;
    private readonly Platform.MiningCommunityListener community;
    private readonly string attachmentDirectory;
    private readonly Platform.IMiningSpeechOutput speech;
    private readonly Platform.IMiningChimeOutput chime;
    private readonly IReadOnlyList<string> chimeOptions = Platform.MiningChimeOutput.Chimes;
    private IReadOnlyList<string> voices = [];
    private IReadOnlyList<MiningAnnouncementPresetRowViewModel>? cachedAnnouncementPresets;
    private IReadOnlyList<MiningThresholdRowViewModel>? cachedThresholdRows;
    private IReadOnlyList<MiningProspectOverlayRowViewModel>? cachedPersistentProspects;
    private string presetName = "Laser mining";
    private readonly IStarSystemResolver resolver;
    private readonly BookmarksViewModel bookmarks;
    private readonly WorkspaceTableSorter cargoSorter = new();
    private readonly WorkspaceTableSorter materialsSorter = new();
    private readonly WorkspaceTableSorter prospectsSorter = new();
    private readonly WorkspaceTableSorter engineeringSorter = new();
    private readonly WorkspaceTableSorter noticesSorter = new();
    private readonly WorkspaceTableSorter historySorter = new();
    private readonly WorkspaceTableSorter missionsSorter = new();
    private readonly WorkspaceTableSorter ringsSorter = new();
    private MiningWorkspaceState state = new(new MiningCommanderData());
    private string? commander;
    private bool storageAvailable;
    private bool sessionAvailable;
    private string system = "",
        body = "",
        ship = "",
        status = "Waiting for commander journal data.";
    private GalacticCoordinate? position;
    private CargoSnapshot? cargo;
    private EliteStatus? eliteStatus;
    private int capacity,
        selectedTab;
    private string notes = "",
        targetMaterial = "platinum",
        filter = "",
        thresholdText = "20",
        destination = "",
        distanceResult = "";
    private double refineryTons;
    private string refineryMineral = "platinum";
    private MiningSession? selectedSession;
    private MiningSession? removedSession;
    private string origin = "";
    private MiningRing? selectedRing;
    private MiningMission? selectedMission;
    private MiningThresholdRowViewModel? selectedThreshold;
    private MiningAnnouncementPresetRowViewModel? selectedPreset;
    private DateTimeOffset? fullSince;
    private bool fullNotified;
    private DateTimeOffset lastAutoSearch;

    public MiningWorkspaceViewModel(
        string directory,
        IStarSystemResolver resolver,
        BookmarksViewModel bookmarks,
        HttpClient? networkClient = null,
        TimeProvider? clock = null,
        FiregroupsWorkspaceViewModel? firegroups = null,
        MiningAnnouncementOutputs? announcementOutputs = null
    )
    {
        this.clock = clock ?? TimeProvider.System;
        this.firegroups = firegroups;
        speech = announcementOutputs?.Speech ?? new Platform.MiningSpeechOutput();
        chime = announcementOutputs?.Chime ?? new Platform.MiningChimeOutput();
        store = new MiningStore(directory);
        community = new Platform.MiningCommunityListener(directory);
        attachmentDirectory = Path.Combine(directory, "mining", "attachments");
        this.resolver = resolver;
        this.bookmarks = bookmarks;
        Search = new MiningSearchViewModel(
            new MiningSearchClient(
                networkClient,
                commodityReportStore: new MiningCommodityPriceReportStore(directory),
                providerResponseCache: new MiningProviderResponseCache(directory)
            ),
            bookmarks,
            CacheRing,
            () => state.Data.Rings,
            resolver,
            community.Cache
        );
        RememberPledgedPower();
        Search.ConfigureResultCache(MiningSearchResultCache.ForDirectory(directory));
        StartCommand = new WorkspaceCommand(
            Start,
            () => sessionAvailable && storageAvailable && state.Session.Current is null
        );
        PauseCommand = new WorkspaceCommand(TogglePause, () => storageAvailable && state.Session.Current is not null);
        StopCommand = new WorkspaceCommand(Stop, () => storageAvailable && state.Session.Current is not null);
        SaveSettingsCommand = new WorkspaceCommand(SaveSettings);
        SaveNotesCommand = new WorkspaceCommand(() =>
        {
            if (SelectedSession is { } session)
            {
                session.Notes = Notes;
                Save();
                Refresh();
            }
        });
        BookmarkRingCommand = new WorkspaceCommand(BookmarkRing);
        MissionHotspotsCommand = new WorkspaceCommand(() =>
        {
            if (SelectedMission is { } mission)
            {
                Filter = mission.Commodity;
                Search.Mineral = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(mission.Commodity);
                SelectedTab = 4;
            }
        });
        AddAsteroidCommand = new WorkspaceCommand(() => AdjustAsteroids(1));
        RemoveAsteroidCommand = new WorkspaceCommand(() => AdjustAsteroids(-1));
        CargoSortCommand = CreateSortCommand(cargoSorter, nameof(Cargo), nameof(CargoSortIndicators));
        MaterialsSortCommand = CreateSortCommand(materialsSorter, nameof(Materials), nameof(MaterialsSortIndicators));
        ProspectsSortCommand = CreateSortCommand(prospectsSorter, nameof(Prospects), nameof(ProspectsSortIndicators));
        EngineeringSortCommand = CreateSortCommand(
            engineeringSorter,
            nameof(EngineeringMaterials),
            nameof(EngineeringSortIndicators)
        );
        NoticesSortCommand = CreateSortCommand(noticesSorter, nameof(Notices), nameof(NoticesSortIndicators));
        HistorySortCommand = CreateSortCommand(historySorter, nameof(History), nameof(HistorySortIndicators));
        MissionsSortCommand = CreateSortCommand(missionsSorter, nameof(Missions), nameof(MissionsSortIndicators));
        RingsSortCommand = CreateSortCommand(ringsSorter, nameof(Rings), nameof(RingsSortIndicators));
    }

    public string CommunityStatus => community.Status;
    public MiningSearchViewModel Search { get; }
    public IReadOnlyList<string> Voices
    {
        get => voices;
        private set => Set(ref voices, value);
    }
    public IReadOnlyList<string> ChimeOptions => chimeOptions;
    public IReadOnlyList<MiningRingReferenceViewModel> RingReferences { get; } = MiningRingReferenceViewModel.All;
    public string PresetName
    {
        get => presetName;
        set => Set(ref presetName, value);
    }
    private IReadOnlyList<string>? cachedPresetNames;
    public IReadOnlyList<string> PresetNames => cachedPresetNames ??= ReadPresetNames();

    public IReadOnlyList<MiningAnnouncementPresetRowViewModel> AnnouncementPresets =>
        cachedAnnouncementPresets ??= ReadAnnouncementPresets();

    private MiningAnnouncementPresetRowViewModel[] ReadAnnouncementPresets() =>
        Settings
            .AnnouncementPresets.OrderBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(pair => new MiningAnnouncementPresetRowViewModel(pair.Key, FormatPresetSummary(pair.Value)))
            .ToArray();

    public MiningAnnouncementPresetRowViewModel? SelectedAnnouncementPreset
    {
        get => selectedPreset;
        set
        {
            if (Set(ref selectedPreset, value) && value is not null)
            {
                PresetName = value.Name;
            }
        }
    }

    private string[] ReadPresetNames() => Settings.AnnouncementPresets.Keys.Order().ToArray();

    public IReadOnlyList<MiningThresholdRowViewModel> Thresholds => cachedThresholdRows ??= ReadThresholds();
    public IReadOnlyList<MiningThresholdGroupViewModel> ThresholdGroups => thresholdGroups ??= ReadThresholdGroups();
    private MiningThresholdGroupViewModel[]? thresholdGroups;
    public MiningChipBoxViewModel ThresholdMinerals { get; } =
        new("Mineral / metal", MiningReferenceData.Commodities.GetValueOrDefault("Mining") ?? [], "");
    private MiningThresholdGroupViewModel? selectedThresholdGroup;
    public MiningThresholdGroupViewModel? SelectedThresholdGroup
    {
        get => selectedThresholdGroup;
        set => Set(ref selectedThresholdGroup, value);
    }

    private MiningThresholdRowViewModel[] ReadThresholds() =>
        Settings
            .Thresholds.OrderBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(pair => new MiningThresholdRowViewModel(pair.Key, pair.Value))
            .ToArray();

    private MiningThresholdGroupViewModel[] ReadThresholdGroups() =>
        Settings
            .Thresholds.GroupBy(pair => pair.Value)
            .OrderBy(group => group.Key)
            .Select(group => new MiningThresholdGroupViewModel(
                group
                    .OrderBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase)
                    .Select(pair => pair.Key)
                    .ToArray(),
                group.Key
            ))
            .ToArray();

    public MiningThresholdRowViewModel? SelectedThreshold
    {
        get => selectedThreshold;
        set
        {
            if (Set(ref selectedThreshold, value) && value is not null)
            {
                TargetMaterial = value.Name;
                ThresholdText = value.MinimumPercentage.ToString("0.0", CultureInfo.CurrentCulture);
            }
        }
    }

    public ICommand StartCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand SaveNotesCommand { get; }
    public ICommand BookmarkRingCommand { get; }
    public ICommand MissionHotspotsCommand { get; }
    public ICommand AddAsteroidCommand { get; }
    public ICommand RemoveAsteroidCommand { get; }
    public ICommand CargoSortCommand { get; }
    public ICommand MaterialsSortCommand { get; }
    public ICommand ProspectsSortCommand { get; }
    public ICommand EngineeringSortCommand { get; }
    public ICommand NoticesSortCommand { get; }
    public ICommand HistorySortCommand { get; }
    public ICommand MissionsSortCommand { get; }
    public ICommand RingsSortCommand { get; }
    public WorkspaceSortIndicators CargoSortIndicators => new(cargoSorter.Indicator);
    public WorkspaceSortIndicators MaterialsSortIndicators => new(materialsSorter.Indicator);
    public WorkspaceSortIndicators ProspectsSortIndicators => new(prospectsSorter.Indicator);
    public WorkspaceSortIndicators EngineeringSortIndicators => new(engineeringSorter.Indicator);
    public WorkspaceSortIndicators NoticesSortIndicators => new(noticesSorter.Indicator);
    public WorkspaceSortIndicators HistorySortIndicators => new(historySorter.Indicator);
    public WorkspaceSortIndicators MissionsSortIndicators => new(missionsSorter.Indicator);
    public WorkspaceSortIndicators RingsSortIndicators => new(ringsSorter.Indicator);
    public MiningPreferences Settings => state.Data.Settings;
    public double RefineryTons
    {
        get => refineryTons;
        set => Set(ref refineryTons, value);
    }
    public string RefineryMineral
    {
        get => refineryMineral;
        set => Set(ref refineryMineral, value);
    }
    public string RefinerySummary =>
        string.Join(
            " · ",
            (Current ?? SelectedSession)?.RefineryEstimates.Select(p => $"{p.Key}: {p.Value:0.##} t") ?? []
        );
    public double MaximumHistoryRate => Math.Max(1, History.Select(s => s.TonsPerHour).DefaultIfEmpty(0).Max());
    public MiningSession? Current => state.Session.Current;
    public string CurrentSystem => system;
    public string Context => string.Join(" · ", new[] { system, body, ship }.Where(s => s.Length > 0));
    public string SessionSummary =>
        Current is not { } session
            ? "No active session"
            : $"{SessionStateLabel(session)} · {session.ActiveDuration:hh\\:mm\\:ss} · {session.RefinedTons:N0} t · {session.TonsPerHour:0.0} t/h · {session.Asteroids} asteroids · {session.CoreHits} cores";

    private static string SessionStateLabel(MiningSession session) => session.PausedAt is null ? "Active" : "Paused";

    private string CapacityLabel => capacity > 0 ? capacity.ToString("N0", CultureInfo.CurrentCulture) : "unknown";
    public string CargoSummary =>
        cargo is null
            ? "Cargo unavailable"
            : $"Cargo: {cargo.Count:N0} / {CapacityLabel} t · Limpets: {cargo.GetCount("drones")}";
    public IReadOnlyList<CargoItem> Cargo => cargoSorter.Apply(cargo?.Inventory ?? []);
    public int CargoUsed => cargo?.Count ?? 0;
    public int CargoCapacity => capacity;
    public int CargoRemaining => Math.Max(0, capacity - CargoUsed);
    public double CargoFillPercentage => capacity > 0 ? Math.Clamp(CargoUsed * 100d / capacity, 0, 100) : 0;
    public IReadOnlyList<MiningMaterialSummary> Materials =>
        materialsSorter.Apply(Current?.Summarize(Settings.Thresholds) ?? []);
    private IReadOnlyList<MiningCollectionEntry>? cachedEngineeringMaterials;
    public IReadOnlyList<MiningCollectionEntry> EngineeringMaterials =>
        engineeringSorter.Apply(cachedEngineeringMaterials ??= ReadEngineeringMaterials());

    private MiningCollectionEntry[] ReadEngineeringMaterials() =>
        Current?.Collections.Where(c => c.Engineering).ToArray() ?? [];

    private IReadOnlyList<MiningProspect>? cachedProspects;
    public IReadOnlyList<MiningProspect> Prospects => prospectsSorter.Apply(cachedProspects ??= ReadProspects());

    public IReadOnlyList<MiningProspectOverlayRowViewModel> PersistentProspects =>
        cachedPersistentProspects ??= ReadPersistentProspects();

    private MiningProspectOverlayRowViewModel[] ReadPersistentProspects() =>
        (Current?.ActiveProspects ?? [])
            .TakeLast(Math.Clamp(Settings.PersistentProspectSlots, 1, 8))
            .Reverse()
            .Select(ToOverlayProspect)
            .ToArray();

    private MiningProspect[] ReadProspects() => Current?.Prospects.AsEnumerable().Reverse().ToArray() ?? [];

    private IReadOnlyList<MiningMission>? cachedMissions;
    public IReadOnlyList<MiningMission> Missions => missionsSorter.Apply(cachedMissions ??= ReadMissions());

    private MiningMission[] ReadMissions() => state.Missions.Missions.Select(m => m with { }).ToArray();

    public IReadOnlyList<MiningNotice> Notices => noticesSorter.Apply(state.Notices);
    public IReadOnlyList<string> ReportScreenshots => SelectedSession?.Screenshots.ToArray() ?? [];
    public IReadOnlyList<MiningSession> History => historySorter.Apply(state.Data.History);
    private IReadOnlyList<MiningRing>? cachedRings;
    public IReadOnlyList<MiningRing> Rings => ringsSorter.Apply(cachedRings ??= ReadRings());

    private MiningRing[] ReadRings() =>
        state
            .Data.Rings.Where(r =>
                $"{r.System} {r.Body} {r.RingType} {r.Minerals}".Contains(Filter, StringComparison.OrdinalIgnoreCase)
            )
            .OrderBy(r => r.Position is { } p && position is { } current ? p.DistanceTo(current) : double.MaxValue)
            .ToArray();

    public string HistorySummary =>
        $"{History.Count} sessions · {History.Sum(s => s.RefinedTons):N0} t refined · {History.Sum(s => s.ActiveDuration.TotalHours):0.0} active hours";
    public string ThresholdSummary =>
        Settings.Thresholds.Count == 0
            ? "All minerals are announced."
            : string.Join(" · ", Settings.Thresholds.Select(p => $"{p.Key} ≥ {p.Value:0.0}%"));

    private MiningProspectOverlayRowViewModel ToOverlayProspect(MiningProspect prospect)
    {
        bool core = !string.IsNullOrEmpty(prospect.Core);
        MiningMaterial[] matches = prospect
            .Materials.Where(material =>
                Settings.Thresholds.Count == 0
                || Settings.Thresholds.TryGetValue(material.Name, out double threshold)
                    && material.Percentage >= threshold
            )
            .ToArray();
        bool kindEnabled = core ? Settings.AnnounceCores : Settings.AnnounceNonCores;
        bool qualifies = kindEnabled && (matches.Length > 0 || core);
        string summary = string.Join(" · ", prospect.Materials.Select(item => $"{item.Name} {item.Percentage:0.0}%"));
        if (core)
        {
            summary += (summary.Length == 0 ? "" : " · ") + $"Core: {prospect.Core}";
        }

        return new MiningProspectOverlayRowViewModel(
            prospect.Time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture),
            summary,
            prospect.Remaining,
            qualifies
        );
    }

    private static string FormatPresetSummary(MiningAnnouncementPreset preset)
    {
        string targets =
            preset.Thresholds.Count == 0
                ? "all minerals"
                : string.Join(
                    ", ",
                    preset.Thresholds.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key} ≥ {pair.Value:0.#}%")
                );
        string asteroidTypes = (preset.Cores, preset.NonCores) switch
        {
            (true, true) => "core + laser",
            (true, false) => "core only",
            (false, true) => "laser only",
            _ => "muted",
        };
        return $"{targets} · {asteroidTypes}";
    }

    public string Status
    {
        get => status;
        set => Set(ref status, value);
    }

    public void UseCommanderSystem(string? system)
    {
        if (!string.IsNullOrWhiteSpace(system))
        {
            Search.UpdateCurrentLocation(system);
        }
    }

    public int SelectedTab
    {
        get => selectedTab;
        set
        {
            if (Set(ref selectedTab, value) && value == 3)
            {
                Search.PreparePowerplay();
            }
        }
    }
    public string Notes
    {
        get => notes;
        set => Set(ref notes, value);
    }
    public string Filter
    {
        get => filter;
        set
        {
            if (Set(ref filter, value))
            {
                cachedRings = null;
                Changed(nameof(Rings));
            }
        }
    }
    public string TargetMaterial
    {
        get => targetMaterial;
        set => Set(ref targetMaterial, value);
    }
    public string ThresholdText
    {
        get => thresholdText;
        set => Set(ref thresholdText, value);
    }
    public string Origin
    {
        get => origin;
        set => Set(ref origin, value);
    }
    public string Destination
    {
        get => destination;
        set => Set(ref destination, value);
    }
    public string DistanceResult
    {
        get => distanceResult;
        private set => Set(ref distanceResult, value);
    }
    public MiningSession? SelectedSession
    {
        get => selectedSession;
        set
        {
            if (Set(ref selectedSession, value))
            {
                Notes = value?.Notes ?? "";
                Changed(nameof(RefinerySummary));
                Changed(nameof(ReportScreenshots));
            }
        }
    }
    public MiningRing? SelectedRing
    {
        get => selectedRing;
        set => Set(ref selectedRing, value);
    }
    public MiningMission? SelectedMission
    {
        get => selectedMission;
        set => Set(ref selectedMission, value);
    }

    private WorkspaceParameterCommand CreateSortCommand(
        WorkspaceTableSorter tableSorter,
        string rowsProperty,
        string indicatorsProperty
    ) =>
        new WorkspaceParameterCommand(parameter =>
        {
            tableSorter.Toggle(parameter);
            Changed(rowsProperty);
            Changed(indicatorsProperty);
        });

    public string CurrentProspectText => state.CurrentProspectText ?? "";
    public bool HasCurrentProspect => CurrentProspectText.Length > 0;
    public bool HasPersistentProspects => PersistentProspects.Any(prospect => prospect.Qualifies);
    public bool ShouldShowNotifications => CanShowShipOverlays && (HasPersistentProspects || VisibleNotices.Count > 0);
    public bool ShouldShowCargo => CanShowShipOverlays && cargo is not null;
    private bool CanShowShipOverlays =>
        sessionAvailable
        && storageAvailable
        && eliteStatus is { InMainShip: true, OnFoot: false, InSrv: false }
        && (!Settings.HideInSupercruise || !eliteStatus.Flags.HasFlag(StatusFlags.Supercruise))
        && (!Settings.OverlaysOnlyDuringSession || Current is not null);
    private IReadOnlyList<MiningNotice>? cachedVisibleNotices;
    public IReadOnlyList<MiningNotice> VisibleNotices => cachedVisibleNotices ??= ReadVisibleNotices();

    private MiningNotice[] ReadVisibleNotices()
    {
        MiningNotice[] active = Notices
            .Where(n =>
                clock.GetUtcNow() - n.Time < TimeSpan.FromSeconds(Math.Clamp(Settings.NotificationSeconds, 3, 120))
            )
            .ToArray();
        var selected = new List<MiningNotice>(5);
        foreach (MiningNotice? notice in active)
        {
            if (selected.Count == 5)
            {
                break;
            }

            if (!selected.Any(item => item.Kind == notice.Kind))
            {
                selected.Add(notice);
            }
        }
        foreach (MiningNotice? notice in active)
        {
            if (selected.Count == 5)
            {
                break;
            }

            if (!selected.Any(item => ReferenceEquals(item, notice)))
            {
                selected.Add(notice);
            }
        }
        return selected.OrderByDescending(item => item.Time).ToArray();
    }

    public void Apply(
        JournalMonitorUpdate update,
        JournalSessionState context,
        CargoSnapshot? currentCargo,
        EliteStatus? currentStatus
    )
    {
        NotePledgedPower(currentStatus);
        sessionAvailable =
            !update.IsAwaitingCommanderIdentity
            && !context.IsShutdown
            && !string.IsNullOrWhiteSpace(context.FrontierId);
        if (!sessionAvailable)
        {
            if (Current is { PausedAt: null })
            {
                state.Session.Pause(clock.GetUtcNow());
                Save();
            }
            Refresh();
            return;
        }
        if (commander != context.FrontierId)
        {
            Load(context.FrontierId!);
        }

        if (!storageAvailable)
        {
            return;
        }

        eliteStatus = currentStatus;
        bool cargoChanged = !Equals(cargo, currentCargo);
        cargo = currentCargo;
        bool dirty = false;
        MiningNotice? previousNotice = state.Notices.FirstOrDefault();
        // Preserve journal order: the outer projection represents the end of this batch.
        foreach (JournalEventEnvelope entry in update.JournalEvents)
        {
            ApplyContext(entry);
            dirty |= state.Apply(entry, update.IsBootstrapRead, system, body, ship, position);
        }
        system = context.SystemName ?? system;
        body = context.BodyName ?? body;
        ship = context.ShipType ?? ship;
        position = context.StarPosition ?? position;
        Search.UpdateCurrentLocation(system);

        state.Missions.UpdateCargo(Cargo);
        if (dirty)
        {
            Save();
        }

        ApplyAutomation(update, previousNotice);
        if (dirty || cargoChanged)
        {
            Refresh();
        }

        Tick();
    }

    private void ApplyContext(JournalEventEnvelope entry)
    {
        JsonElement json = entry.Payload;
        if (entry.EventName is "Location" or "FSDJump" or "CarrierJump")
        {
            system = Text(json, "StarSystem");
            body = Text(json, "Body");
            UpdatePosition(json);
        }
        if (entry.EventName is "SupercruiseExit" or "ApproachBody")
        {
            body = Text(json, "Body");
        }

        if (entry.EventName == "Loadout")
        {
            ship = Text(json, "Ship");
            if (json.TryGetProperty(nameof(CargoCapacity), out JsonElement c) && c.TryGetInt32(out int count))
            {
                capacity = count;
            }
        }
        if (entry.EventName is "FSDJump" or "Location" or "CarrierJump")
        {
            var broadcast = new Dictionary<string, object>
            {
                ["$schemaRef"] = "https://eddn.edcd.io/schemas/journal/1",
                ["message"] = entry.Payload,
            };
            community.Cache.Apply(JsonSerializer.Serialize(broadcast), clock.GetUtcNow());
        }
        if (entry.EventName is "Powerplay" or "PowerplayJoin")
        {
            Search.NoteDetectedPower(Text(entry.Payload, "Power"));
        }

        if (entry.EventName == "PowerplayLeave")
        {
            Search.NoteDetectedPower("");
        }
    }

    private void NotePledgedPower(EliteStatus? status)
    {
        if (status?.AdditionalProperties is not { } extra)
        {
            return;
        }

        if (!extra.TryGetValue("Powerplay", out JsonElement powerplay) || powerplay.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        Search.NoteDetectedPower(powerplay.TryGetProperty("Power", out JsonElement power) ? power.GetString() : "");
    }

    private void UpdatePosition(JsonElement json)
    {
        if (
            json.TryGetProperty("StarPos", out JsonElement starPos)
            && starPos.ValueKind == JsonValueKind.Array
            && starPos.GetArrayLength() == 3
        )
        {
            position = new GalacticCoordinate(starPos[0].GetDouble(), starPos[1].GetDouble(), starPos[2].GetDouble());
        }
    }

    private void ApplyAutomation(JournalMonitorUpdate update, MiningNotice? previousNotice)
    {
        ApplyAnnouncements(update, previousNotice);

        if (
            Settings.AutoSearch
            && !update.IsBootstrapRead
            && clock.GetUtcNow() - lastAutoSearch > TimeSpan.FromSeconds(10)
            && update.JournalEvents.Any(e => e.EventName is "FSDJump" or "SAASignalsFound")
        )
        {
            lastAutoSearch = clock.GetUtcNow();
            Search.Reference = system;
            _ = Search.SearchRingsAsync();
        }
        if (
            Settings.AutoSwitchTabs
            && update.JournalEvents.Any(e =>
                e.EventName == "LaunchDrone"
                && Text(e.Payload, "Type").Equals("Prospector", StringComparison.OrdinalIgnoreCase)
            )
            && !update.IsBootstrapRead
        )
        {
            SelectedTab = 0;
        }
    }

    private void ApplyAnnouncements(JournalMonitorUpdate update, MiningNotice? previousNotice)
    {
        if (update.IsBootstrapRead || !Runtime.DesktopExternalEffectPolicy.IsAllowed)
        {
            return;
        }

        foreach (MiningNotice? notice in state.Notices.TakeWhile(n => !ReferenceEquals(n, previousNotice)).Reverse())
        {
            if (Settings.SpeakAnnouncements)
            {
                speech.Speak(notice.Text, Settings.Voice, Settings.SpeechVolume, Settings.SpeechRate);
            }
            if (Settings.PlayProspectChime && notice.Kind == "Prospected")
            {
                chime.Play(Settings.Chime, Settings.ChimeVolume);
            }
        }
    }

    public void PreviewChime()
    {
        chime.Play(Settings.Chime, Settings.ChimeVolume);
        Status = $"Played the {Settings.Chime} chime at {Settings.ChimeVolume}% volume.";
    }

    public void Tick()
    {
        if (Current is { PausedAt: null } && capacity > 0 && cargo?.Count >= capacity && Settings.NotifyCargoFull)
        {
            fullSince ??= clock.GetUtcNow();
            DateTimeOffset latestActivity = Current.Collections.LastOrDefault()?.Time ?? Current.Started;
            if (
                !fullNotified
                && clock.GetUtcNow() - fullSince >= TimeSpan.FromMinutes(1)
                && clock.GetUtcNow() - latestActivity >= TimeSpan.FromMinutes(1)
            )
            {
                state.Notices.Insert(
                    0,
                    new MiningNotice(clock.GetUtcNow(), "Cargo", "Cargo is full. End the session when ready.")
                );
                fullNotified = true;
                Changed(nameof(Notices));
            }
        }
        else
        {
            fullSince = null;
            fullNotified = false;
        }
        if (
            sessionAvailable
            && Current is { PausedAt: null } session
            && clock.GetUtcNow() - lastRecoverySave >= TimeSpan.FromMinutes(1)
        )
        {
            session.LastObserved = clock.GetUtcNow();
            lastRecoverySave = clock.GetUtcNow();
            Save();
        }
        Refresh(false);
    }

    public async Task LoadVoicesAsync()
    {
        if (!speech.IsSupported)
        {
            Status = "No local speech service was found. Install Speech Dispatcher or eSpeak NG on Linux.";
            return;
        }
        try
        {
            Voices = await speech.GetVoicesAsync();
            Status =
                Voices.Count > 0
                    ? $"Local voices loaded from {speech.ProviderName}."
                    : "No local voices are available.";
        }
        catch (Exception ex)
            when (ex
                    is TimeoutException
                        or System.Runtime.InteropServices.COMException
                        or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException
            )
        {
            Status = "Speech unavailable: " + ex.Message;
        }
    }

    public void SaveAnnouncementPreset()
    {
        if (string.IsNullOrWhiteSpace(PresetName))
        {
            return;
        }

        string name = PresetName.Trim();
        if (SelectedAnnouncementPreset is { } selected && !selected.Name.Equals(name, StringComparison.Ordinal))
        {
            Settings.AnnouncementPresets.Remove(selected.Name);
        }
        Settings.AnnouncementPresets[name] = new MiningAnnouncementPreset(
            new Dictionary<string, double>(Settings.Thresholds),
            Settings.AnnounceCores,
            Settings.AnnounceNonCores
        );
        Save();
        RefreshAnnouncementEditors();
        SelectedAnnouncementPreset = AnnouncementPresets.Single(row => row.Name == name);
        Status = "Announcement preset saved.";
    }

    public void LoadAnnouncementPreset()
    {
        if (!Settings.AnnouncementPresets.TryGetValue(PresetName, out MiningAnnouncementPreset? preset))
        {
            return;
        }

        Settings.Thresholds = new Dictionary<string, double>(preset.Thresholds);
        Settings.AnnounceCores = preset.Cores;
        Settings.AnnounceNonCores = preset.NonCores;
        Changed(nameof(Settings));
        Save();
        SelectedThreshold = null;
        RefreshAnnouncementEditors();
        Refresh();
        Status = $"Announcement preset '{PresetName}' applied.";
    }

    private void RefreshAnnouncementEditors()
    {
        cachedPresetNames = null;
        cachedAnnouncementPresets = null;
        cachedThresholdRows = null;
        cachedPersistentProspects = null;
        thresholdGroups = null;
        Changed(nameof(PresetNames));
        Changed(nameof(AnnouncementPresets));
        Changed(nameof(Thresholds));
        Changed(nameof(ThresholdGroups));
        Changed(nameof(ThresholdSummary));
        Changed(nameof(PersistentProspects));
    }

    public void NewAnnouncementPreset()
    {
        SelectedAnnouncementPreset = null;
        PresetName = "";
        Status = "Enter a preset name, then save the current announcement filters.";
    }

    public void DeleteAnnouncementPreset()
    {
        string name = SelectedAnnouncementPreset?.Name ?? PresetName.Trim();
        if (name.Length == 0 || !Settings.AnnouncementPresets.Remove(name))
        {
            Status = "Select an announcement preset to delete.";
            return;
        }

        SelectedAnnouncementPreset = null;
        PresetName = "";
        Save();
        RefreshAnnouncementEditors();
        Status = $"Announcement preset '{name}' deleted.";
    }

    public void Dispose()
    {
        Search.Dispose();
        speech.Dispose();
        chime.Dispose();
        community.Dispose();
    }

    public void SetThreshold(bool remove)
    {
        if (string.IsNullOrWhiteSpace(TargetMaterial))
        {
            Status = "Enter a mineral name.";
            return;
        }
        string name = TargetMaterial.Trim().ToLowerInvariant();
        if (remove)
        {
            Settings.Thresholds.Remove(name);
        }
        else
        {
            if (
                !double.TryParse(ThresholdText, NumberStyles.Number, CultureInfo.CurrentCulture, out double threshold)
                || !double.IsFinite(threshold)
                || threshold is < 0 or > 100
            )
            {
                Status = "Enter a percentage from 0 to 100.";
                return;
            }
            Settings.Thresholds[name] = threshold;
        }
        Changed(nameof(Settings));
        SaveSettings();
        RefreshAnnouncementEditors();
        SelectedThreshold = Thresholds.FirstOrDefault(row => row.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        Status = remove ? $"Threshold for {name} removed." : $"Threshold for {name} saved.";
    }

    public void AddThresholdGroup()
    {
        if (ThresholdMinerals.Selected.Count == 0)
        {
            Status = "Choose one or more minerals.";
            return;
        }

        if (
            !double.TryParse(ThresholdText, NumberStyles.Number, CultureInfo.CurrentCulture, out double threshold)
            || !double.IsFinite(threshold)
            || threshold is < 0 or > 100
        )
        {
            Status = "Enter a percentage from 0 to 100.";
            return;
        }

        foreach (string mineral in ThresholdMinerals.Selected.ToArray())
        {
            Settings.Thresholds[mineral.Trim().ToLowerInvariant()] = threshold;
            ThresholdMinerals.Remove(mineral);
        }

        Changed(nameof(Settings));
        SaveSettings();
        RefreshAnnouncementEditors();
        Status = "Threshold group saved at " + threshold.ToString("0.#", CultureInfo.CurrentCulture) + "%.";
    }

    public void DeleteThresholdGroup()
    {
        if (SelectedThresholdGroup is not { } group)
        {
            Status = "Select a threshold group to delete.";
            return;
        }

        foreach (string name in group.Names)
        {
            Settings.Thresholds.Remove(name);
        }

        SelectedThresholdGroup = null;
        Changed(nameof(Settings));
        SaveSettings();
        RefreshAnnouncementEditors();
        Status = "Threshold group removed.";
    }

    public void NewThreshold()
    {
        SelectedThreshold = null;
        TargetMaterial = "";
        ThresholdText = "20";
        Status = "Enter a mineral and minimum percentage.";
    }

    public void DeleteSelectedThreshold()
    {
        if (SelectedThreshold is null)
        {
            Status = "Select a mineral threshold to delete.";
            return;
        }

        TargetMaterial = SelectedThreshold.Name;
        SetThreshold(true);
        SelectedThreshold = null;
    }

    public void AdjustQuality(int delta)
    {
        if ((Current ?? SelectedSession) is not { } session || string.IsNullOrWhiteSpace(TargetMaterial))
        {
            return;
        }

        string material = TargetMaterial.Trim().ToLowerInvariant();
        session.QualityAdjustments[material] = Math.Clamp(
            session.QualityAdjustments.GetValueOrDefault(material) + delta,
            -session.Prospects.Count,
            session.Prospects.Count
        );
        Save();
        Refresh();
        Status = "Quality-hit adjustment saved; journal observations are unchanged.";
    }

    public void SaveRefineryEstimate()
    {
        if ((Current ?? SelectedSession) is not { } session)
        {
            Status = "Start or select a session first.";
            return;
        }
        if (string.IsNullOrWhiteSpace(RefineryMineral) || !double.IsFinite(RefineryTons) || RefineryTons is < 0 or > 16)
        {
            Status = "Enter a mineral and 0–16 pending tons.";
            return;
        }
        if (RefineryTons == 0)
        {
            session.RefineryEstimates.Remove(RefineryMineral.Trim());
        }
        else
        {
            session.RefineryEstimates[RefineryMineral.Trim()] = RefineryTons;
        }

        Save();
        Changed(nameof(RefinerySummary));
    }

    public async Task CalculateDistanceAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string query = Destination.Trim();
            string from = Origin.Trim();
            GalacticCoordinate? current =
                from.Length == 0 || from.Equals(system, StringComparison.OrdinalIgnoreCase)
                    ? position
                    : (await resolver.SearchAsync(from, timeout.Token))
                        .FirstOrDefault(s => s.Name.Equals(from, StringComparison.OrdinalIgnoreCase))
                        ?.Position;
            StarSystemReference? result = (await resolver.SearchAsync(query, timeout.Token)).FirstOrDefault(s =>
                s.Name.Equals(query, StringComparison.OrdinalIgnoreCase)
            );
            if (result is null)
            {
                DistanceResult = "System not found.";
            }
            else if (current is { } coordinates)
            {
                DistanceResult =
                    $"{coordinates.DistanceTo(result.Position):N2} ly · {result.Position} · {result.Position.DistanceTo(new GalacticCoordinate(0, 0, 0)):N2} ly from Sol";
            }
            else
            {
                DistanceResult = "Origin coordinates unavailable.";
            }
        }
        catch (Exception ex)
            when (ex is HttpRequestException or OperationCanceledException or JsonException or ArgumentException)
        {
            DistanceResult = "System lookup unavailable: " + ex.Message;
        }
    }

    public void ImportReports(string csv)
    {
        if (!storageAvailable)
        {
            Status = "Connect a commander before importing reports.";
            return;
        }
        IReadOnlyList<MiningSession> incoming = MiningReportImport.ReadCsv(csv);
        int added = 0;
        foreach (
            MiningSession? session in incoming.Where(session =>
                !History.Any(s =>
                    s.Id == session.Id
                    || s.Started == session.Started && s.System == session.System && s.Ring == session.Ring
                )
            )
        )
        {
            state.Data.History.Add(session);
            added++;
        }
        Save();
        Refresh();
        Status = $"Imported {added} reports; existing sessions retained.";
    }

    public void RemoveScreenshot(string path)
    {
        if (SelectedSession is { } session)
        {
            session.Screenshots.Remove(path);
            Save();
            Changed(nameof(ReportScreenshots));
        }
    }

    public void DeleteSelectedReport()
    {
        if (SelectedSession is not { } session)
        {
            return;
        }

        removedSession = session;
        state.Data.History.Remove(session);
        SelectedSession = null;
        Save();
        Refresh();
        Status = "Report removed. Undo remains available until the next deletion or restart.";
    }

    public void UndoDeleteReport()
    {
        if (removedSession is not { } session)
        {
            return;
        }

        state.Data.History.Add(session);
        SelectedSession = session;
        removedSession = null;
        Save();
        Refresh();
    }

    public async Task ImportJournalsAsync(IReadOnlyList<string> paths)
    {
        if (!storageAvailable || commander is null)
        {
            Status = "Connect a commander before importing journals.";
            return;
        }
        string importingCommander = commander;
        Status = "Reading selected journals…";
        try
        {
            MiningCommanderData imported = await Task.Run(() =>
                MiningJournalImporter.ReadAsync(paths, importingCommander)
            );
            if (commander != importingCommander)
            {
                Status = "Commander changed; import was not applied.";
                return;
            }
            state.Import(imported);
            Save();
            Refresh();
            Status =
                $"Imported {imported.Rings.Count} rings and {imported.Missions.Count} mission records. Existing newer observations retained.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Status = "Journal import failed: " + ex.Message;
        }
    }

    public Task<byte[]> BackupPackageAsync()
    {
        MiningCommanderData snapshot = MiningStore.Parse(Backup());
        string bookmarkJson = bookmarks.Export();
        string? firegroupJson = commander is not null ? firegroups?.Backup(commander) : null;
        return Task.Run(() => MiningBackup.Create(snapshot, bookmarkJson, firegroupJson));
    }

    public async Task RestorePackageAsync(byte[] bytes)
    {
        string? targetCommander = commander;
        if (targetCommander is null || !storageAvailable)
        {
            Status = "Connect a commander before restoring.";
            return;
        }
        MiningBackupContents contents;
        try
        {
            contents = await Task.Run(() => MiningBackup.Read(bytes, attachmentDirectory));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Status = "Mining backup could not be read: " + ex.Message;
            return;
        }
        if (commander != targetCommander)
        {
            Status = "Commander changed; backup was not applied.";
            return;
        }

        string originalMining = Backup();
        if (
            !TryRestoreFiregroups(
                targetCommander,
                contents,
                out string? originalFiregroups,
                out bool firegroupsRestored
            )
        )
        {
            return;
        }

        if (!Restore(MiningStore.Export(contents.Data)))
        {
            ReportMiningRestoreFailure(targetCommander, originalFiregroups, firegroupsRestored);
            return;
        }

        if (!bookmarks.Restore(contents.Bookmarks))
        {
            ReportBookmarkRestoreFailure(targetCommander, originalMining, originalFiregroups, firegroupsRestored);
            return;
        }

        Status += " " + bookmarks.Status;
        if (contents.Firegroups is not null && firegroups is not null)
        {
            Status += " " + firegroups.Status;
        }
    }

    private bool TryRestoreFiregroups(
        string targetCommander,
        MiningBackupContents contents,
        out string? originalFiregroups,
        out bool firegroupsRestored
    )
    {
        originalFiregroups = firegroups?.Backup(targetCommander);
        firegroupsRestored = false;
        if (contents.Firegroups is null || firegroups is null)
        {
            return true;
        }

        if (!firegroups.Restore(targetCommander, contents.Firegroups))
        {
            Status = firegroups.Status;
            return false;
        }

        firegroupsRestored = true;
        return true;
    }

    private void ReportMiningRestoreFailure(string targetCommander, string? originalFiregroups, bool firegroupsRestored)
    {
        string failure = Status;
        bool rolledBack = RestorePreviousFiregroups(targetCommander, originalFiregroups, firegroupsRestored);
        Status =
            failure
            + (
                rolledBack
                    ? " Previous Firegroups were restored."
                    : " Firegroups rollback also failed; use the before-restore file to recover them."
            );
    }

    private void ReportBookmarkRestoreFailure(
        string targetCommander,
        string originalMining,
        string? originalFiregroups,
        bool firegroupsRestored
    )
    {
        string failure = bookmarks.Status;
        bool miningRolledBack = Restore(originalMining);
        bool firegroupsRolledBack = RestorePreviousFiregroups(targetCommander, originalFiregroups, firegroupsRestored);
        Status =
            failure
            + (
                miningRolledBack && firegroupsRolledBack
                    ? " Previous Mining and Firegroups data were restored."
                    : " Automatic rollback was incomplete; use the before-restore files to recover the previous data."
            );
    }

    private bool RestorePreviousFiregroups(string targetCommander, string? originalFiregroups, bool firegroupsRestored)
    {
        return !firegroupsRestored
            || originalFiregroups is not null && firegroups!.Restore(targetCommander, originalFiregroups);
    }

    public string Backup()
    {
        state.Synchronize();
        return MiningStore.Export(state.Data);
    }

    public bool Restore(string json)
    {
        if (!storageAvailable || commander is null)
        {
            return false;
        }

        try
        {
            state = new MiningWorkspaceState(store.Restore(commander, json));
            PauseRecoveredSession();
            Search.LoadOptions(Settings.SearchOptions);
            community.SetEnabled(Settings.ReceiveCommunityData);
            Changed(nameof(Settings));
            Refresh();
            Status = "Mining backup restored. Previous data retained in the backup folder.";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Status = ex.Message;
            return false;
        }
    }

    private void Load(string id)
    {
        commander = id;
        capacity = 0;
        system = body = ship = "";
        position = null;
        cargo = null;
        eliteStatus = null;
        fullSince = null;
        fullNotified = false;
        removedSession = null;
        selectedSession = null;
        selectedRing = null;
        selectedMission = null;
        try
        {
            state = new MiningWorkspaceState(store.Load(id));
            storageAvailable = true;
            PauseRecoveredSession();
            Status = "Mining journal connected.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            storageAvailable = false;
            state = new(new MiningCommanderData());
            Status = "Mining data could not be loaded: " + ex.Message;
        }
        Search.LoadOptions(Settings.SearchOptions);
        RememberPledgedPower();
        Search.RestoreLastCompletedPowerplaySearch();
        community.SetEnabled(Settings.ReceiveCommunityData);
        Changed(nameof(Settings));
    }

    private void RememberPledgedPower()
    {
        string pledged = JournalPowerplayPledge.ReadLatest(JournalFolderLocator.ResolveCurrent().AvailablePaths);
        if (pledged.Length > 0)
        {
            Search.NoteDetectedPower(pledged);
        }
    }

    private void PauseRecoveredSession()
    {
        if (Current is not { PausedAt: null } session)
        {
            return;
        }

        DateTimeOffset lastActivity =
            session.LastObserved
            ?? session
                .Collections.Select(c => c.Time)
                .Concat(session.Prospects.Select(p => p.Time))
                .Append(session.Started)
                .Max();
        state.Session.Pause(lastActivity);
    }

    private void Start()
    {
        state.Session.Start(clock.GetUtcNow(), system, body, ship);
        Save();
        Refresh();
    }

    private void Stop()
    {
        state.Stop(clock.GetUtcNow());
        Save();
        SelectedSession = History.Count > 0 ? History[0] : null;
        if (Settings.AutoSwitchTabs)
        {
            SelectedTab = 4;
        }
        Refresh();
    }

    private void TogglePause()
    {
        if (Current?.PausedAt is null)
        {
            state.Session.Pause(clock.GetUtcNow());
        }
        else
        {
            state.Session.Resume(clock.GetUtcNow());
        }
        Save();
        Refresh();
    }

    private void AdjustAsteroids(int delta)
    {
        if (Current is { } session && session.Asteroids + delta >= 0)
        {
            session.AsteroidAdjustment += delta;
            Save();
            Refresh();
        }
    }

    private void SaveSettings()
    {
        Settings.SearchOptions = Search.SaveOptions();
        community.SetEnabled(Settings.ReceiveCommunityData);
        Save();
        Refresh();
    }

    private void CacheRing(MiningRing ring)
    {
        state.CacheRing(ring);
        Save();
        Refresh();
    }

    private void BookmarkRing()
    {
        if (SelectedRing is not { } ring)
        {
            return;
        }

        bookmarks.AddMiningLocation(
            new GalacticBookmark
            {
                System = ring.System,
                Body = ring.Body,
                Position = ring.Position,
                Category = BookmarkCategoryCatalog.Mining,
                CategoryAssignments = [BookmarkCategoryCatalog.Mining],
                Minerals = ring.Minerals,
                RingType = ring.RingType,
                Reserve = ring.Reserve,
            }
        );
        Status = bookmarks.Status;
    }

    private void Save()
    {
        if (!storageAvailable || commander is null)
        {
            return;
        }

        try
        {
            state.Synchronize();
            store.Save(commander, state.Data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Status = "Mining data could not be saved: " + ex.Message;
        }
    }

    private void Refresh(bool dataChanged = true)
    {
        cachedVisibleNotices = null;
        if (!dataChanged)
        {
            foreach (
                string? name in new[]
                {
                    nameof(CommunityStatus),
                    nameof(SessionSummary),
                    nameof(ShouldShowNotifications),
                    nameof(ShouldShowCargo),
                    nameof(VisibleNotices),
                    nameof(CurrentProspectText),
                    nameof(HasCurrentProspect),
                    nameof(PersistentProspects),
                    nameof(HasPersistentProspects),
                    nameof(CargoUsed),
                    nameof(CargoCapacity),
                    nameof(CargoRemaining),
                    nameof(CargoFillPercentage),
                }
            )
            {
                Changed(name);
            }

            return;
        }
        cachedPresetNames = null;
        cachedAnnouncementPresets = null;
        cachedThresholdRows = null;
        cachedPersistentProspects = null;
        cachedEngineeringMaterials = null;
        cachedProspects = null;
        cachedMissions = null;
        cachedRings = null;
        foreach (
            string? name in new[]
            {
                nameof(ReportScreenshots),
                nameof(Current),
                nameof(Context),
                nameof(CurrentSystem),
                nameof(SessionSummary),
                nameof(CargoSummary),
                nameof(Cargo),
                nameof(CargoUsed),
                nameof(CargoCapacity),
                nameof(CargoRemaining),
                nameof(CargoFillPercentage),
                nameof(Materials),
                nameof(EngineeringMaterials),
                nameof(Prospects),
                nameof(Missions),
                nameof(Notices),
                nameof(History),
                nameof(HistorySummary),
                nameof(MaximumHistoryRate),
                nameof(RefinerySummary),
                nameof(Rings),
                nameof(ThresholdSummary),
                nameof(Thresholds),
                nameof(ThresholdGroups),
                nameof(AnnouncementPresets),
                nameof(ShouldShowNotifications),
                nameof(ShouldShowCargo),
                nameof(VisibleNotices),
                nameof(CurrentProspectText),
                nameof(HasCurrentProspect),
                nameof(PersistentProspects),
                nameof(HasPersistentProspects),
            }
        )
        {
            Changed(name);
        }

        foreach (ICommand? command in new[] { StartCommand, PauseCommand, StopCommand })
        {
            ((WorkspaceCommand)command).Refresh();
        }
    }

    private static string Text(JsonElement json, string name) =>
        json.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}

public sealed record MiningThresholdRowViewModel(string Name, double MinimumPercentage)
{
    public string DisplayName => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Name);
    public string MinimumLabel => $"≥ {MinimumPercentage:0.0}%";
}

public sealed record MiningThresholdGroupViewModel(IReadOnlyList<string> Names, double MinimumPercentage)
{
    public string Minerals =>
        string.Join(" · ", Names.Select(name => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name)));
    public string MinimumLabel => $"≥ {MinimumPercentage:0.0}%";
}

public sealed record MiningAnnouncementPresetRowViewModel(string Name, string Summary);

public sealed record MiningProspectOverlayRowViewModel(string Time, string Summary, double Remaining, bool Qualifies)
{
    public string RemainingLabel => Remaining <= 0 ? "DEPLETED" : $"{Remaining:0.#}% remaining";
}

public sealed record MiningReferenceCommodityRowViewModel(string Name, int AverageSellPrice)
{
    public string AverageSellPriceLabel => $"{AverageSellPrice:N0} CR/t";
}

public sealed record MiningRingReferenceViewModel(
    string Name,
    IReadOnlyList<MiningReferenceCommodityRowViewModel> Laser,
    IReadOnlyList<MiningReferenceCommodityRowViewModel> Core
)
{
    public static IReadOnlyList<MiningRingReferenceViewModel> All { get; } =
    [
        Ring(
            "Icy rings",
            [
                Item("Low Temperature Diamonds", 131607),
                Item("Bromellite", 33414),
                Item("Hydrogen Peroxide", 3119),
                Item("Liquid Oxygen", 1639),
                Item("Lithium Hydroxide", 5655),
                Item("Methane Clathrate", 1597),
                Item("Methanol Monohydrate Crystals", 2522),
                Item("Tritium", 53425),
                Item("Water", 496),
            ],
            [
                Item("Alexandrite", 227771),
                Item("Grandidierite", 211582),
                Item("Low Temperature Diamonds", 131607),
                Item("Void Opals", 150964),
                Item("Bromellite", 33414),
            ]
        ),
        Ring(
            "Metallic rings",
            [
                Item("Osmium", 55698),
                Item("Painite", 57890),
                Item("Platinum", 70136),
                Item("Bertrandite", 18476),
                Item("Gold", 47900),
                Item("Indite", 11268),
                Item("Palladium", 52064),
                Item("Praseodymium", 8636),
                Item("Samarium", 28658),
                Item("Silver", 37628),
            ],
            [
                Item("Monazite", 273262),
                Item("Rhodplumsite", 186345),
                Item("Serendibite", 186953),
                Item("Painite", 57890),
                Item("Platinum", 70136),
            ]
        ),
        Ring(
            "Metal-rich rings",
            [
                Item("Osmium", 55698),
                Item("Bertrandite", 18476),
                Item("Coltan", 6144),
                Item("Gallite", 12235),
                Item("Gold", 47900),
                Item("Indite", 11268),
                Item("Lepidolite", 1796),
                Item("Praseodymium", 8636),
                Item("Samarium", 28658),
                Item("Silver", 37628),
                Item("Uraninite", 3004),
            ],
            [
                Item("Alexandrite", 227771),
                Item("Benitoite", 164647),
                Item("Monazite", 273262),
                Item("Rhodplumsite", 186345),
                Item("Serendibite", 186953),
                Item("Painite", 57890),
                Item("Platinum", 70136),
            ]
        ),
        Ring(
            "Rocky rings",
            [
                Item("Bertrandite", 18476),
                Item("Bauxite", 2092),
                Item("Coltan", 6144),
                Item("Gallite", 12235),
                Item("Indite", 11268),
                Item("Lepidolite", 1796),
                Item("Rutile", 3084),
            ],
            [
                Item("Alexandrite", 227771),
                Item("Benitoite", 164647),
                Item("Monazite", 273262),
                Item("Musgravite", 220251),
                Item("Serendibite", 186953),
            ]
        ),
    ];

    private static MiningRingReferenceViewModel Ring(
        string name,
        IReadOnlyList<MiningReferenceCommodityRowViewModel> laser,
        IReadOnlyList<MiningReferenceCommodityRowViewModel> core
    ) => new(name, laser, core);

    private static MiningReferenceCommodityRowViewModel Item(string name, int averageSellPrice) =>
        new(name, averageSellPrice);
}
