using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Settlements;

public sealed class HumanSiteActivityTracker
{
    private readonly HashSet<int> processedTerminalIndexes = [];
    private readonly List<HumanSiteCollectedMaterial> collectedMaterials = [];
    private string? siteKey;

    public IReadOnlySet<int> ProcessedTerminalIndexes =>
        processedTerminalIndexes;

    public IReadOnlyList<HumanSiteCollectedMaterial> CollectedMaterials =>
        collectedMaterials;

    public int Version { get; private set; }

    public HumanSiteActivityApplyResult Apply(
        JournalEventEnvelope journalEvent,
        HumanSiteLiveSnapshot? site,
        EliteStatus? status,
        bool trackMaterialCollection)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        var reset = SynchronizeSite(site);
        if (site is not { Template: not null, Heading: not null }
            || status is not { HasLatitudeLongitude: true }
            || status.PlanetRadius <= 0)
        {
            if (reset)
            {
                Version++;
            }

            return new HumanSiteActivityApplyResult(
                reset,
                false,
                []);
        }

        var commanderOffset = GetCommanderOffset(site, status);
        if (commanderOffset is null)
        {
            return HumanSiteActivityApplyResult.None;
        }

        var collectionOffset = MoveOneMeterAhead(
            commanderOffset.Value,
            status.NormalizedHeading,
            site.Heading.Value);
        var terminalsChanged = false;
        var added = Array.Empty<HumanSiteCollectedMaterial>();
        if (journalEvent.EventName == "BackpackChange")
        {
            var dataItems = ReadAddedItems(journalEvent.Payload)
                .Where(item => string.Equals(
                    item.Type,
                    "Data",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (dataItems.Length > 0)
            {
                terminalsChanged = MarkClosestTerminal(
                    site,
                    commanderOffset.Value);
                if (trackMaterialCollection)
                {
                    added = dataItems
                        .Select(item => CreateMaterial(
                            item,
                            collectionOffset,
                            journalEvent.Timestamp))
                        .ToArray();
                }
            }
        }
        else if (journalEvent.EventName == "CollectItems"
            && trackMaterialCollection
            && ReadCollectedItem(journalEvent.Payload) is { } item
            && !string.Equals(
                item.Type,
                "Data",
                StringComparison.OrdinalIgnoreCase))
        {
            added =
            [
                CreateMaterial(item, collectionOffset, journalEvent.Timestamp),
            ];
        }

        if (added.Length > 0)
        {
            collectedMaterials.AddRange(added);
        }

        var changed = reset || terminalsChanged || added.Length > 0;
        if (changed)
        {
            Version++;
        }

        return new HumanSiteActivityApplyResult(
            reset,
            terminalsChanged,
            added);
    }

    public bool ReplaceCollectedMaterials(
        IEnumerable<HumanSiteCollectedMaterial> materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        var replacement = materials.ToArray();
        if (collectedMaterials.SequenceEqual(replacement))
        {
            return false;
        }

        collectedMaterials.Clear();
        collectedMaterials.AddRange(replacement);
        Version++;
        return true;
    }

    private bool SynchronizeSite(HumanSiteLiveSnapshot? site)
    {
        var nextKey = site is null
            ? null
            : $"{site.SystemAddress}/{site.MarketId}";
        if (string.Equals(siteKey, nextKey, StringComparison.Ordinal))
        {
            return false;
        }

        siteKey = nextKey;
        processedTerminalIndexes.Clear();
        collectedMaterials.Clear();
        return true;
    }

    private bool MarkClosestTerminal(
        HumanSiteLiveSnapshot site,
        HumanSiteMapPoint currentOffset)
    {
        var terminals = site.Template!.DataTerminals;
        var closest = terminals
            .Select((terminal, index) => new
            {
                Index = index,
                Distance = GetDistance(terminal.Offset, currentOffset),
            })
            .Where(candidate => candidate.Distance < 5)
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();
        return closest is not null
            && processedTerminalIndexes.Add(closest.Index);
    }

    private static HumanSiteMapPoint? GetCommanderOffset(
        HumanSiteLiveSnapshot site,
        EliteStatus status)
    {
        try
        {
            var current = new SurfaceCoordinate(
                status.Latitude,
                status.Longitude);
            var origin = new SurfaceCoordinate(
                site.Location.Latitude,
                site.Location.Longitude);
            return HumanSiteNavigation.GetSiteOffset(
                origin,
                current,
                (double)status.PlanetRadius,
                site.Heading!.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static HumanSiteMapPoint MoveOneMeterAhead(
        HumanSiteMapPoint offset,
        double commanderHeading,
        double siteHeading)
    {
        var relativeHeading = SurfaceNavigation.NormalizeDegrees(
            commanderHeading - siteHeading);
        var radians = relativeHeading * Math.PI / 180;
        return new HumanSiteMapPoint(
            offset.X + Math.Sin(radians),
            offset.Y + Math.Cos(radians));
    }

    private static HumanSiteCollectedMaterial CreateMaterial(
        HumanSiteMaterialItem item,
        HumanSiteMapPoint offset,
        DateTimeOffset? timestamp)
    {
        return new HumanSiteCollectedMaterial(
            item.Name,
            item.LocalizedName,
            item.Type,
            Math.Max(1, item.Count),
            offset,
            timestamp);
    }

    private static HumanSiteMaterialItem[] ReadAddedItems(
        JsonElement root)
    {
        if (!root.TryGetProperty("Added", out var added)
            || added.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return added.EnumerateArray()
            .Select(ReadMaterialItem)
            .Where(item => item is not null)
            .Cast<HumanSiteMaterialItem>()
            .ToArray();
    }

    private static HumanSiteMaterialItem? ReadCollectedItem(JsonElement root)
    {
        return ReadMaterialItem(root);
    }

    private static HumanSiteMaterialItem? ReadMaterialItem(JsonElement root)
    {
        var name = GetString(root, "Name");
        var type = GetString(root, "Type");
        var count = GetInt32(root, "Count") ?? 0;
        return string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(type)
            || count <= 0
                ? null
                : new HumanSiteMaterialItem(
                    name,
                    GetString(root, "Name_Localised"),
                    type,
                    count);
    }

    private static double GetDistance(
        HumanSiteMapPoint left,
        HumanSiteMapPoint right)
    {
        var x = left.X - right.X;
        var y = left.Y - right.Y;
        return Math.Sqrt((x * x) + (y * y));
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static int? GetInt32(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && int.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out number)
                ? number
                : null;
    }
}

public sealed record HumanSiteCollectedMaterial(
    string Name,
    string? LocalizedName,
    string Type,
    int Count,
    HumanSiteMapPoint Offset,
    DateTimeOffset? Timestamp);

public sealed record HumanSiteActivityApplyResult(
    bool Reset,
    bool ProcessedTerminalsChanged,
    IReadOnlyList<HumanSiteCollectedMaterial> AddedMaterials)
{
    public static HumanSiteActivityApplyResult None { get; } = new(
        false,
        false,
        []);

    public bool Changed => Reset
        || ProcessedTerminalsChanged
        || AddedMaterials.Count > 0;
}

internal sealed record HumanSiteMaterialItem(
    string Name,
    string? LocalizedName,
    string Type,
    int Count);
