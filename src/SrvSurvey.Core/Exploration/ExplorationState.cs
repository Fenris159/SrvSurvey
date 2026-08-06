using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Exploration;

public sealed class ExplorationState
{
    private readonly Dictionary<BodyKey, BodyExplorationState> bodies = [];
    private readonly HashSet<BodyKey> landedBodies = [];
    private bool isOdyssey = true;

    public ExplorationState(ExplorationSnapshot? seed = null)
    {
        Reset(seed);
    }

    public long EstimatedRewards { get; private set; }

    public double DistanceTravelled { get; private set; }

    public int JumpCount { get; private set; }

    public int ScanCount { get; private set; }

    public int DetailedSurfaceScanCount { get; private set; }

    public int LandedBodyCount { get; private set; }

    public bool Apply(JournalEventEnvelope journalEvent)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        var root = journalEvent.Payload;

        switch (journalEvent.EventName)
        {
            case "Fileheader":
            case "LoadGame":
                isOdyssey = GetBoolean(root, "Odyssey") ?? isOdyssey;
                return true;

            case "StartJump":
                if (GetString(root, "JumpType") == "Hyperspace")
                {
                    JumpCount++;
                }

                return true;

            case "FSDJump":
                DistanceTravelled += GetDouble(root, "JumpDist") ?? 0;
                return true;

            case "Scan":
                ApplyScan(root);
                return true;

            case "SAAScanComplete":
                ApplyDetailedSurfaceScan(root);
                return true;

            case "Touchdown":
                ApplyTouchdown(root);
                return true;

            default:
                return false;
        }
    }

    public ExplorationSnapshot CreateSnapshot()
    {
        return new ExplorationSnapshot(
            EstimatedRewards,
            DistanceTravelled,
            JumpCount,
            ScanCount,
            DetailedSurfaceScanCount,
            LandedBodyCount);
    }

    public void Reset(ExplorationSnapshot? seed = null)
    {
        seed ??= ExplorationSnapshot.Empty;
        EstimatedRewards = seed.EstimatedRewards;
        DistanceTravelled = seed.DistanceTravelled;
        JumpCount = seed.JumpCount;
        ScanCount = seed.ScanCount;
        DetailedSurfaceScanCount = seed.DetailedSurfaceScanCount;
        LandedBodyCount = seed.LandedBodyCount;
        bodies.Clear();
        landedBodies.Clear();
    }

    private void ApplyScan(JsonElement root)
    {
        var key = GetBodyKey(root);
        if (key is null)
        {
            return;
        }

        var bodyClass = GetString(root, "PlanetClass")
            ?? GetString(root, "StarType");
        if (bodyClass is null)
        {
            return;
        }

        var body = bodies.GetValueOrDefault(key.Value) ?? new BodyExplorationState();
        body.BodyClass = bodyClass;
        body.IsTerraformable = GetString(root, "TerraformState") == "Terraformable";
        var planetMass = GetDouble(root, "MassEM");
        body.Mass = planetMass is > 0
            ? planetMass.Value
            : GetDouble(root, "StellarMass") ?? 0;
        body.IsFirstDiscoverer = !(GetBoolean(root, "WasDiscovered") ?? false);
        body.IsFirstMapped = !(GetBoolean(root, "WasMapped") ?? false);
        bodies[key.Value] = body;

        var reward = CalculateReward(body, isMapped: false, withEfficiencyBonus: false);
        if (reward > body.Reward)
        {
            body.Reward = reward;
            ScanCount++;
            EstimatedRewards += reward;
        }
    }

    private void ApplyDetailedSurfaceScan(JsonElement root)
    {
        var key = GetBodyKey(root);
        if (key is null || !bodies.TryGetValue(key.Value, out var body))
        {
            return;
        }

        var probesUsed = GetInt32(root, "ProbesUsed") ?? int.MaxValue;
        var efficiencyTarget = GetInt32(root, "EfficiencyTarget") ?? -1;
        var reward = CalculateReward(
            body,
            isMapped: true,
            withEfficiencyBonus: probesUsed <= efficiencyTarget);
        if (reward > body.Reward)
        {
            body.Reward = reward;
            body.IsMapped = true;
            DetailedSurfaceScanCount++;
            EstimatedRewards += reward;
        }
    }

    private void ApplyTouchdown(JsonElement root)
    {
        if (!(GetBoolean(root, "OnPlanet") ?? false))
        {
            return;
        }

        var key = GetBodyKey(root);
        if (key is not null && landedBodies.Add(key.Value))
        {
            LandedBodyCount++;
        }
    }

    private int CalculateReward(
        BodyExplorationState body,
        bool isMapped,
        bool withEfficiencyBonus)
    {
        return ExplorationValueCalculator.Calculate(
            new ExplorationValueRequest
    {
        BodyClass = body.BodyClass,
        IsTerraformable = body.IsTerraformable,
        Mass = body.Mass,
        IsFirstDiscoverer = body.IsFirstDiscoverer,
        IsMapped = isMapped,
        IsFirstMapped = body.IsFirstMapped,
        IsOdyssey = isOdyssey,
        WithEfficiencyBonus = withEfficiencyBonus
    });
    }

    private static BodyKey? GetBodyKey(JsonElement root)
    {
        var systemAddress = GetInt64(root, "SystemAddress");
        var bodyId = GetInt32(root, "BodyID");
        return systemAddress is null || bodyId is null
            ? null
            : new BodyKey(systemAddress.Value, bodyId.Value);
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static bool? GetBoolean(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;
    }

    private static double? GetDouble(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number)
                ? number
                : null;
    }

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number)
                ? number
                : null;
    }

    private static int? GetInt32(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
                ? number
                : null;
    }

    private readonly record struct BodyKey(long SystemAddress, int BodyId);

    private sealed class BodyExplorationState
    {
        public string? BodyClass { get; set; }

        public bool IsTerraformable { get; set; }

        public double Mass { get; set; }

        public bool IsFirstDiscoverer { get; set; }

        public bool IsFirstMapped { get; set; }

        public bool IsMapped { get; set; }

        public int Reward { get; set; }
    }
}

public sealed record ExplorationSnapshot(
    long EstimatedRewards,
    double DistanceTravelled,
    int JumpCount,
    int ScanCount,
    int DetailedSurfaceScanCount,
    int LandedBodyCount)
{
    public static ExplorationSnapshot Empty { get; } = new(0, 0, 0, 0, 0, 0);
}
