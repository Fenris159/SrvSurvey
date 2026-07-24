using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Guardian;

public sealed class RamTahState
{
    public const int AncientRuinsLogCount = 101;
    public const int GuardianLogsCount = 28;

    private static readonly HashSet<string> ValidAncientRuinsLogs =
        BuildAncientRuinsLogs().ToHashSet(StringComparer.Ordinal);
    private static readonly HashSet<string> ValidGuardianLogs =
        Enumerable.Range(1, GuardianLogsCount)
            .Select(index => $"#{index}")
            .ToHashSet(StringComparer.Ordinal);
    private readonly HashSet<string> ancientRuinsLogs = new(
        StringComparer.Ordinal);
    private readonly HashSet<string> guardianLogs = new(
        StringComparer.Ordinal);

    public RamTahMissionStatus AncientRuinsMissionStatus { get; private set; }

    public RamTahMissionStatus GuardianLogsMissionStatus { get; private set; }

    public IReadOnlySet<string> AncientRuinsLogs => ancientRuinsLogs;

    public IReadOnlySet<string> GuardianLogs => guardianLogs;

    public int Version { get; private set; }

    public bool IsAnyMissionActive =>
        AncientRuinsMissionStatus == RamTahMissionStatus.Active
        || GuardianLogsMissionStatus == RamTahMissionStatus.Active;

    public double AncientRuinsProgress =>
        GetProgress(ancientRuinsLogs.Count, AncientRuinsLogCount);

    public double GuardianLogsProgress =>
        GetProgress(guardianLogs.Count, GuardianLogsCount);

    public void Reset(RamTahSnapshot? snapshot = null)
    {
        snapshot ??= RamTahSnapshot.Empty;
        ancientRuinsLogs.Clear();
        ancientRuinsLogs.UnionWith(snapshot.AncientRuinsLogs);
        guardianLogs.Clear();
        guardianLogs.UnionWith(snapshot.GuardianLogs);
        AncientRuinsMissionStatus = snapshot.AncientRuinsMissionStatus;
        GuardianLogsMissionStatus = snapshot.GuardianLogsMissionStatus;
        Version++;
    }

    public bool Apply(JournalEventEnvelope journalEvent)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        var changed = journalEvent.EventName switch
        {
            "Missions" => ApplyMissions(journalEvent.Payload),
            "MissionAccepted" => ApplyMissionState(
                GetString(journalEvent.Payload, "Name"),
                RamTahMissionStatus.Active),
            "MissionCompleted" => ApplyMissionState(
                GetString(journalEvent.Payload, "Name"),
                RamTahMissionStatus.Complete),
            "MissionFailed" or "MissionAbandoned" => ApplyMissionState(
                GetString(journalEvent.Payload, "Name"),
                RamTahMissionStatus.NotStarted),
            _ => false,
        };
        if (changed)
        {
            Version++;
        }

        return changed;
    }

    public bool SetLog(
        RamTahMission mission,
        string code,
        bool completed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        var target = mission == RamTahMission.AncientRuins
            ? ancientRuinsLogs
            : guardianLogs;
        var valid = mission == RamTahMission.AncientRuins
            ? ValidAncientRuinsLogs
            : ValidGuardianLogs;
        if (!valid.Contains(code))
        {
            throw new ArgumentOutOfRangeException(
                nameof(code),
                $"{code} is not a valid {mission} log code.");
        }

        var changed = completed ? target.Add(code) : target.Remove(code);
        if (changed)
        {
            Version++;
        }

        return changed;
    }

    public bool ToggleLog(RamTahMission mission, string code)
    {
        var target = mission == RamTahMission.AncientRuins
            ? ancientRuinsLogs
            : guardianLogs;
        return SetLog(mission, code, !target.Contains(code));
    }

    public bool Clear(RamTahMission mission)
    {
        var target = mission == RamTahMission.AncientRuins
            ? ancientRuinsLogs
            : guardianLogs;
        if (target.Count == 0)
        {
            return false;
        }

        target.Clear();
        Version++;
        return true;
    }

    public RamTahSnapshot CreateSnapshot()
    {
        return new RamTahSnapshot(
            AncientRuinsMissionStatus,
            GuardianLogsMissionStatus,
            ancientRuinsLogs.Order(LogCodeComparer.Instance).ToArray(),
            guardianLogs.Order(LogCodeComparer.Instance).ToArray());
    }

    public static IReadOnlyList<string> GetAncientRuinsLogCodes()
    {
        return ValidAncientRuinsLogs.Order(LogCodeComparer.Instance).ToArray();
    }

    public static IReadOnlyList<string> GetGuardianLogCodes()
    {
        return ValidGuardianLogs.Order(LogCodeComparer.Instance).ToArray();
    }

    private bool ApplyMissions(JsonElement root)
    {
        if (!root.TryGetProperty("Active", out var active)
            || active.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var changed = false;
        foreach (var mission in active.EnumerateArray())
        {
            changed |= ApplyMissionState(
                GetString(mission, "Name"),
                RamTahMissionStatus.Active);
        }

        return changed;
    }

    private bool ApplyMissionState(
        string? missionName,
        RamTahMissionStatus status)
    {
        if (IsAncientRuinsMission(missionName))
        {
            if (AncientRuinsMissionStatus == status)
            {
                return false;
            }

            AncientRuinsMissionStatus = status;
            return true;
        }

        if (IsGuardianLogsMission(missionName))
        {
            if (GuardianLogsMissionStatus == status)
            {
                return false;
            }

            GuardianLogsMissionStatus = status;
            return true;
        }

        return false;
    }

    private static bool IsAncientRuinsMission(string? name)
    {
        return name is "Mission_TheDead" or "Mission_TheDead_name";
    }

    private static bool IsGuardianLogsMission(string? name)
    {
        return name is "Mission_TheDead_002" or "Mission_TheDead_002_name";
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static double GetProgress(int completed, int total)
    {
        return Math.Min(100, 100d / total * completed);
    }

    private static IEnumerable<string> BuildAncientRuinsLogs()
    {
        return BuildCategory('B', 19)
            .Concat(BuildCategory('C', 20))
            .Concat(BuildCategory('H', 21))
            .Concat(BuildCategory('L', 21))
            .Concat(BuildCategory('T', 20));
    }

    private static IEnumerable<string> BuildCategory(char category, int count)
    {
        return Enumerable.Range(1, count)
            .Select(index => $"{category}{index}");
    }

    private sealed class LogCodeComparer : IComparer<string>
    {
        public static LogCodeComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var prefix = x[0].CompareTo(y[0]);
            if (prefix != 0)
            {
                return prefix;
            }

            return int.TryParse(x.AsSpan(1), out var xNumber)
                && int.TryParse(y.AsSpan(1), out var yNumber)
                    ? xNumber.CompareTo(yNumber)
                    : string.Compare(x, y, StringComparison.Ordinal);
        }
    }
}

public enum RamTahMissionStatus
{
    NotStarted,
    Active,
    Complete,
}

public enum RamTahMission
{
    AncientRuins,
    GuardianLogs,
}

public sealed record RamTahSnapshot(
    RamTahMissionStatus AncientRuinsMissionStatus,
    RamTahMissionStatus GuardianLogsMissionStatus,
    IReadOnlyList<string> AncientRuinsLogs,
    IReadOnlyList<string> GuardianLogs)
{
    public static RamTahSnapshot Empty { get; } = new(
        RamTahMissionStatus.NotStarted,
        RamTahMissionStatus.NotStarted,
        [],
        []);
}
