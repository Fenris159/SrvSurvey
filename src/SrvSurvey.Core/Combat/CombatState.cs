using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Combat;

public sealed class CombatState(TimeProvider? timeProvider = null)
{
    private static readonly HashSet<string> MassacreMissionNames =
        new(StringComparer.Ordinal)
        {
            "Mission_Massacre",
            "Mission_MassacreWing",
        };

    private readonly TimeProvider timeProvider = timeProvider
        ?? TimeProvider.System;
    private readonly List<MassacreMissionSnapshot> massacreMissions = [];

    public string? SettlementName { get; private set; }

    public string? SettlementFactionState { get; private set; }

    public int FootCombatKills { get; private set; }

    public long FootCombatBonds { get; private set; }

    public int Version { get; private set; }

    public IReadOnlyList<MassacreMissionSnapshot> MassacreMissions =>
        massacreMissions;

    public bool IsAtWarSettlement =>
        !string.IsNullOrWhiteSpace(SettlementName)
        && SettlementFactionState is "War" or "CivilWar";

    public void Reset(CombatSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        massacreMissions.Clear();
        massacreMissions.AddRange(snapshot.MassacreMissions
            .Where(IsValidMission)
            .DistinctBy(mission => mission.MissionId));
        SettlementName = null;
        SettlementFactionState = null;
        FootCombatKills = 0;
        FootCombatBonds = 0;
        Version++;
    }

    public void ResetFootCombatSession()
    {
        if (FootCombatKills == 0 && FootCombatBonds == 0)
        {
            return;
        }

        FootCombatKills = 0;
        FootCombatBonds = 0;
        Version++;
    }

    public CombatApplyResult Apply(
        JournalEventEnvelope journalEvent,
        bool countProgress = true,
        bool countFootCombat = true)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        var result = journalEvent.EventName switch
        {
            "ApproachSettlement" => ApplySettlement(journalEvent.Payload),
            "StartJump" or "SupercruiseEntry" or "FSDJump" or "CarrierJump"
                or "Died" or "Resurrect" or "Shutdown" => ClearSettlement(),
            "Music" when string.Equals(
                GetString(journalEvent.Payload, "MusicTrack"),
                "MainMenu",
                StringComparison.Ordinal) => ClearSettlement(),
            "FactionKillBond" when countProgress && countFootCombat =>
                ApplyFactionKillBond(journalEvent.Payload),
            "MissionAccepted" => ApplyMissionAccepted(journalEvent.Payload),
            "MissionCompleted" or "MissionFailed" or "MissionAbandoned" =>
                ApplyMissionRemoved(journalEvent.Payload),
            "Missions" => ApplyMissionReconciliation(journalEvent.Payload),
            "Bounty" when countProgress => ApplyBounty(
                journalEvent.Payload,
                journalEvent.Timestamp ?? timeProvider.GetUtcNow()),
            _ => CombatApplyResult.None,
        };

        if (result.StateChanged)
        {
            Version++;
        }

        return result;
    }

    public CombatSnapshot CreateSnapshot()
    {
        return new CombatSnapshot(massacreMissions.ToArray());
    }

    private CombatApplyResult ApplySettlement(JsonElement root)
    {
        var name = GetString(root, "Name_Localised")
            ?? GetString(root, "Name");
        var factionState = GetNestedString(
            root,
            "StationFaction",
            "FactionState");
        if (string.Equals(name, SettlementName, StringComparison.Ordinal)
            && string.Equals(
                factionState,
                SettlementFactionState,
                StringComparison.Ordinal))
        {
            return CombatApplyResult.None;
        }

        SettlementName = name;
        SettlementFactionState = factionState;
        FootCombatKills = 0;
        FootCombatBonds = 0;
        return CombatApplyResult.SessionOnly;
    }

    private CombatApplyResult ClearSettlement()
    {
        if (SettlementName is null
            && SettlementFactionState is null
            && FootCombatKills == 0
            && FootCombatBonds == 0)
        {
            return CombatApplyResult.None;
        }

        SettlementName = null;
        SettlementFactionState = null;
        FootCombatKills = 0;
        FootCombatBonds = 0;
        return CombatApplyResult.SessionOnly;
    }

    private CombatApplyResult ApplyFactionKillBond(JsonElement root)
    {
        FootCombatKills++;
        FootCombatBonds += GetInt64(root, "Reward") ?? 0;
        return CombatApplyResult.SessionOnly;
    }

    private CombatApplyResult ApplyMissionAccepted(JsonElement root)
    {
        var missionName = GetString(root, "Name");
        var missionId = GetInt64(root, "MissionID") ?? 0;
        if (!MassacreMissionNames.Contains(missionName ?? string.Empty)
            || missionId <= 0
            || massacreMissions.Any(mission => mission.MissionId == missionId))
        {
            return CombatApplyResult.None;
        }

        var mission = new MassacreMissionSnapshot(
            missionId,
            GetString(root, "Faction") ?? string.Empty,
            GetString(root, "TargetFaction") ?? string.Empty,
            GetDateTimeOffset(root, "Expiry"),
            Math.Max(0, GetInt32(root, "KillCount") ?? 0),
            Math.Max(0, GetInt32(root, "KillCount") ?? 0));
        if (!IsValidMission(mission))
        {
            return CombatApplyResult.None;
        }

        massacreMissions.Add(mission);
        return CombatApplyResult.Persisted;
    }

    private CombatApplyResult ApplyMissionRemoved(JsonElement root)
    {
        var missionId = GetInt64(root, "MissionID") ?? 0;
        return missionId > 0
            && massacreMissions.RemoveAll(
                mission => mission.MissionId == missionId) > 0
                ? CombatApplyResult.Persisted
                : CombatApplyResult.None;
    }

    private CombatApplyResult ApplyMissionReconciliation(JsonElement root)
    {
        var knownMissionIds = ReadMissionIds(root, "Active")
            .Concat(ReadMissionIds(root, "Complete"))
            .ToHashSet();
        var removed = massacreMissions.RemoveAll(
            mission => !knownMissionIds.Contains(mission.MissionId));
        return removed > 0
            ? CombatApplyResult.Persisted
            : CombatApplyResult.None;
    }

    private CombatApplyResult ApplyBounty(
        JsonElement root,
        DateTimeOffset timestamp)
    {
        var victimFaction = GetString(root, "VictimFaction");
        if (string.IsNullOrWhiteSpace(victimFaction))
        {
            return CombatApplyResult.None;
        }

        var creditedMissionGivers = new HashSet<string>(StringComparer.Ordinal);
        var changed = false;
        for (var index = 0; index < massacreMissions.Count; index++)
        {
            var mission = massacreMissions[index];
            if (!string.Equals(
                    mission.TargetFaction,
                    victimFaction,
                    StringComparison.Ordinal)
                || mission.Remaining <= 0
                || mission.Expires is { } expires && timestamp > expires
                || !creditedMissionGivers.Add(mission.MissionGiver))
            {
                continue;
            }

            massacreMissions[index] = mission with
            {
                Remaining = mission.Remaining - 1,
            };
            changed = true;
        }

        return changed
            ? CombatApplyResult.Persisted
            : CombatApplyResult.None;
    }

    private static IEnumerable<long> ReadMissionIds(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var missions)
            || missions.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var mission in missions.EnumerateArray())
        {
            var missionId = GetInt64(mission, "MissionID") ?? 0;
            if (missionId > 0)
            {
                yield return missionId;
            }
        }
    }

    private static bool IsValidMission(MassacreMissionSnapshot mission)
    {
        return mission.MissionId > 0
            && !string.IsNullOrWhiteSpace(mission.MissionGiver)
            && !string.IsNullOrWhiteSpace(mission.TargetFaction)
            && mission.KillCount >= 0
            && mission.Remaining >= 0;
    }

    private static string? GetNestedString(
        JsonElement root,
        string objectName,
        string propertyName)
    {
        return root.TryGetProperty(objectName, out var nested)
            && nested.ValueKind == JsonValueKind.Object
                ? GetString(nested, propertyName)
                : null;
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), out number)
                ? number
                : null;
    }

    private static int? GetInt32(JsonElement root, string propertyName)
    {
        var value = GetInt64(root, propertyName);
        return value is >= int.MinValue and <= int.MaxValue
            ? (int)value.Value
            : null;
    }

    private static DateTimeOffset? GetDateTimeOffset(
        JsonElement root,
        string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            && value.TryGetDateTimeOffset(out var timestamp)
                ? timestamp
                : null;
    }
}

public sealed record MassacreMissionSnapshot(
    long MissionId,
    string MissionGiver,
    string TargetFaction,
    DateTimeOffset? Expires,
    int KillCount,
    int Remaining);

public sealed record CombatSnapshot(
    IReadOnlyList<MassacreMissionSnapshot> MassacreMissions)
{
    public static CombatSnapshot Empty { get; } = new([]);
}

public readonly record struct CombatApplyResult(
    bool StateChanged,
    bool PersistenceChanged)
{
    public static CombatApplyResult None { get; } = new(false, false);

    public static CombatApplyResult SessionOnly { get; } = new(true, false);

    public static CombatApplyResult Persisted { get; } = new(true, true);
}
