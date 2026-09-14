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

    public IReadOnlySet<int> ProcessedTerminalIndexes => processedTerminalIndexes;

    public IReadOnlyList<HumanSiteCollectedMaterial> CollectedMaterials => collectedMaterials;

    public int Version { get; private set; }

    public HumanSiteActivityApplyResult Apply(
        JournalEventEnvelope journalEvent,
        HumanSiteLiveSnapshot? site,
        EliteStatus? status,
        bool trackMaterialCollection
    )
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        bool reset = SynchronizeSite(site);
        if (!CanTrackSite(site, status))
        {
            return CompleteIfReset(reset);
        }

        HumanSiteMapPoint? commanderOffset = GetCommanderOffset(site!, status!);
        if (commanderOffset is null)
        {
            return HumanSiteActivityApplyResult.None;
        }

        return ApplyWithOffset(journalEvent, site!, status!, commanderOffset.Value, trackMaterialCollection, reset);
    }

    private static bool CanTrackSite(HumanSiteLiveSnapshot? site, EliteStatus? status)
    {
        return site is { Template: not null, Heading: not null }
            && status is { HasLatitudeLongitude: true }
            && status.PlanetRadius > 0;
    }

    private HumanSiteActivityApplyResult CompleteIfReset(bool reset)
    {
        if (reset)
        {
            Version++;
        }

        return new HumanSiteActivityApplyResult(reset, false, []);
    }

    private HumanSiteActivityApplyResult ApplyWithOffset(
        JournalEventEnvelope journalEvent,
        HumanSiteLiveSnapshot site,
        EliteStatus status,
        HumanSiteMapPoint commanderOffset,
        bool trackMaterialCollection,
        bool reset
    )
    {
        HumanSiteMapPoint collectionOffset = MoveOneMeterAhead(
            commanderOffset,
            status.NormalizedHeading,
            site.Heading!.Value
        );
        (bool terminalsChanged, HumanSiteCollectedMaterial[]? added) = ApplyCollectionEvents(
            journalEvent,
            site,
            commanderOffset,
            collectionOffset,
            trackMaterialCollection
        );

        if (added.Length > 0)
        {
            collectedMaterials.AddRange(added);
        }

        if (reset || terminalsChanged || added.Length > 0)
        {
            Version++;
        }

        return new HumanSiteActivityApplyResult(reset, terminalsChanged, added);
    }

    private (bool TerminalsChanged, HumanSiteCollectedMaterial[] Added) ApplyCollectionEvents(
        JournalEventEnvelope journalEvent,
        HumanSiteLiveSnapshot site,
        HumanSiteMapPoint commanderOffset,
        HumanSiteMapPoint collectionOffset,
        bool trackMaterialCollection
    )
    {
        if (journalEvent.EventName == "BackpackChange")
        {
            return ApplyBackpackChange(journalEvent, site, commanderOffset, collectionOffset, trackMaterialCollection);
        }

        if (
            journalEvent.EventName == "CollectItems"
            && trackMaterialCollection
            && ReadCollectedItem(journalEvent.Payload) is { } item
            && !string.Equals(item.Type, "Data", StringComparison.OrdinalIgnoreCase)
        )
        {
            return (false, [CreateMaterial(item, collectionOffset, journalEvent.Timestamp)]);
        }

        return (false, []);
    }

    private (bool TerminalsChanged, HumanSiteCollectedMaterial[] Added) ApplyBackpackChange(
        JournalEventEnvelope journalEvent,
        HumanSiteLiveSnapshot site,
        HumanSiteMapPoint commanderOffset,
        HumanSiteMapPoint collectionOffset,
        bool trackMaterialCollection
    )
    {
        HumanSiteMaterialItem[] dataItems = ReadAddedItems(journalEvent.Payload)
            .Where(item => string.Equals(item.Type, "Data", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (dataItems.Length == 0)
        {
            return (false, []);
        }

        bool terminalsChanged = MarkClosestTerminal(site, commanderOffset);
        HumanSiteCollectedMaterial[] added = trackMaterialCollection
            ? dataItems.Select(item => CreateMaterial(item, collectionOffset, journalEvent.Timestamp)).ToArray()
            : [];
        return (terminalsChanged, added);
    }

    public bool ReplaceCollectedMaterials(IEnumerable<HumanSiteCollectedMaterial> materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        HumanSiteCollectedMaterial[] replacement = materials.ToArray();
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
        string? nextKey = site is null ? null : $"{site.SystemAddress}/{site.MarketId}";
        if (string.Equals(siteKey, nextKey, StringComparison.Ordinal))
        {
            return false;
        }

        siteKey = nextKey;
        processedTerminalIndexes.Clear();
        collectedMaterials.Clear();
        return true;
    }

    private bool MarkClosestTerminal(HumanSiteLiveSnapshot site, HumanSiteMapPoint currentOffset)
    {
        IReadOnlyList<HumanSitePointOfInterest> terminals = site.Template!.DataTerminals;
        var closest = terminals
            .Select((terminal, index) => new { Index = index, Distance = GetDistance(terminal.Offset, currentOffset) })
            .Where(candidate => candidate.Distance < 5)
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();
        return closest is not null && processedTerminalIndexes.Add(closest.Index);
    }

    private static HumanSiteMapPoint? GetCommanderOffset(HumanSiteLiveSnapshot site, EliteStatus status)
    {
        try
        {
            var current = new SurfaceCoordinate(status.Latitude, status.Longitude);
            var origin = new SurfaceCoordinate(site.Location.Latitude, site.Location.Longitude);
            return HumanSiteNavigation.GetSiteOffset(origin, current, (double)status.PlanetRadius, site.Heading!.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static HumanSiteMapPoint MoveOneMeterAhead(
        HumanSiteMapPoint offset,
        double commanderHeading,
        double siteHeading
    )
    {
        double relativeHeading = SurfaceNavigation.NormalizeDegrees(commanderHeading - siteHeading);
        double radians = relativeHeading * Math.PI / 180;
        return new HumanSiteMapPoint(offset.X + Math.Sin(radians), offset.Y + Math.Cos(radians));
    }

    private static HumanSiteCollectedMaterial CreateMaterial(
        HumanSiteMaterialItem item,
        HumanSiteMapPoint offset,
        DateTimeOffset? timestamp
    )
    {
        return new HumanSiteCollectedMaterial(
            item.Name,
            item.LocalizedName,
            item.Type,
            Math.Max(1, item.Count),
            offset,
            timestamp
        );
    }

    private static HumanSiteMaterialItem[] ReadAddedItems(JsonElement root)
    {
        if (!root.TryGetProperty("Added", out JsonElement added) || added.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return added
            .EnumerateArray()
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
        string? name = GetString(root, "Name");
        string? type = GetString(root, "Type");
        int count = GetInt32(root, "Count") ?? 0;
        return string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type) || count <= 0
            ? null
            : new HumanSiteMaterialItem(name, GetString(root, "Name_Localised"), type, count);
    }

    private static double GetDistance(HumanSiteMapPoint left, HumanSiteMapPoint right)
    {
        double x = left.X - right.X;
        double y = left.Y - right.Y;
        return Math.Sqrt((x * x) + (y * y));
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? GetInt32(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
        {
            return number;
        }

        return
            value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
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
    DateTimeOffset? Timestamp
);

public sealed record HumanSiteActivityApplyResult(
    bool Reset,
    bool ProcessedTerminalsChanged,
    IReadOnlyList<HumanSiteCollectedMaterial> AddedMaterials
)
{
    public static HumanSiteActivityApplyResult None { get; } = new(false, false, []);

    public bool Changed => Reset || ProcessedTerminalsChanged || AddedMaterials.Count > 0;
}

internal sealed record HumanSiteMaterialItem(string Name, string? LocalizedName, string Type, int Count);
