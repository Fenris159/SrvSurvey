using System.Text.Json;
using System.Text.Json.Nodes;

namespace SrvSurvey.Core.Guardian;

public sealed class GuardianCommanderSurveyStore(string dataDirectory)
{
    private static readonly char[] CrossPlatformInvalidFileNameCharacters =
        ['<', '>', ':', '"', '/', '\\', '|', '?', '*', '\0'];
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };
    private readonly SemaphoreSlim saveLock = new(1, 1);
    private readonly string dataDirectory = GetFullPath(dataDirectory);

    public string GetSurveyPath(
        string frontierId,
        bool isOdyssey,
        string bodyName,
        int index,
        bool isRuins)
    {
        ValidateFrontierId(frontierId);
        ValidateBodyName(bodyName);
        if (index < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                "A Guardian site index must be positive.");
        }

        var folder = Path.Combine(dataDirectory, "guardian", frontierId);
        if (!isOdyssey)
        {
            folder = Path.Combine(folder, "legacy");
        }

        return Path.Combine(
            folder,
            $"{bodyName}-{(isRuins ? "ruins" : "structure")}-{index}.json");
    }

    public async Task<string> SaveAsync(
        string frontierId,
        bool isOdyssey,
        GuardianCommanderSiteSurvey survey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(survey);
        var path = GetSurveyPath(
            frontierId,
            isOdyssey,
            survey.BodyName,
            survey.Index,
            IsRuins(survey));
        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = File.Exists(path)
                ? await ReadExistingAsync(path, cancellationToken)
                    .ConfigureAwait(false)
                : new JsonObject();
            WriteSurvey(root, survey, !isOdyssey);
            await WriteAtomicAsync(path, root, cancellationToken)
                .ConfigureAwait(false);
            return path;
        }
        finally
        {
            saveLock.Release();
        }
    }

    private static async Task<JsonObject> ReadExistingAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var node = await JsonNode.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return node as JsonObject
                ?? throw new InvalidDataException(
                    $"The Guardian survey is not a JSON object and was not overwritten: {path}");
        }
        catch (Exception exception) when (
            exception is JsonException
                or IOException
                or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                $"The Guardian survey is malformed and was not overwritten: {path}",
                exception);
        }
    }

    private static void WriteSurvey(
        JsonObject root,
        GuardianCommanderSiteSurvey survey,
        bool isLegacy)
    {
        root["name"] = survey.Name;
        root["nameLocalised"] = survey.LocalizedName;
        root["commander"] = survey.Commander;
        root["firstVisited"] = survey.FirstVisited;
        root["lastVisited"] = survey.LastVisited;
        root["type"] = survey.SiteType;
        root["index"] = survey.Index;
        WriteLocation(root, survey.Survey.Location);
        root["systemAddress"] = survey.SystemAddress;
        root["systemName"] = survey.SystemName;
        root["bodyId"] = survey.BodyId;
        root["bodyName"] = survey.BodyName;
        root["siteHeading"] = survey.Survey.SiteHeading;
        root["relicTowerHeading"] = survey.Survey.RelicTowerHeading;
        root["notes"] = survey.Notes;
        root["legacy"] = isLegacy;
        root["obeliskGroups"] = string.Concat(
            survey.ObeliskGroups.Order());
        root["activeObelisks"] = WriteObelisks(survey.ActiveObelisks);
        root["relicHeadings"] = WriteRelicHeadings(
            survey.Survey.RelicHeadings);
        WritePoiStatuses(root, survey.Survey.PoiStatuses);
        root["rawPoi"] = survey.Survey.RawPointsOfInterest is null
            ? null
            : WriteRawPoints(survey.Survey.RawPointsOfInterest);
        WriteComponentMaterials(root, survey.Survey.ComponentMaterials);
    }

    private static void WriteLocation(
        JsonObject root,
        GuardianSurfaceLocation? location)
    {
        if (location is null)
        {
            root["location"] = null;
            return;
        }

        if (root["location"] is not JsonObject node)
        {
            node = [];
            root["location"] = node;
        }

        node["lat"] = location.Value.Latitude;
        node["long"] = location.Value.Longitude;
    }

    private static JsonArray WriteObelisks(
        IEnumerable<GuardianObelisk> obelisks)
    {
        var array = new JsonArray();
        foreach (var obelisk in obelisks.OrderBy(item => item.Name))
        {
            ValidateObelisk(obelisk);
            array.Add(
                obelisk.Name
                    + (obelisk.Scanned ? "!" : string.Empty)
                    + "-"
                    + string.Join(',', obelisk.ItemCodes)
                    + "-"
                    + obelisk.LogCode
                    + "-");
        }

        return array;
    }

    private static JsonObject WriteRelicHeadings(
        IReadOnlyDictionary<string, int> headings)
    {
        var node = new JsonObject();
        foreach (var heading in headings.OrderBy(pair => pair.Key))
        {
            node[heading.Key] = heading.Value;
        }

        return node;
    }

    private static void WritePoiStatuses(
        JsonObject root,
        IReadOnlyDictionary<string, GuardianPoiStatus> statuses)
    {
        root.Remove("poiStatus");
        root.Remove("confirmedPOI");
        root["poiPresent"] = JoinStatuses(
            statuses,
            GuardianPoiStatus.Present);
        root["poiAbsent"] = JoinStatuses(
            statuses,
            GuardianPoiStatus.Absent);
        root["poiEmpty"] = JoinStatuses(
            statuses,
            GuardianPoiStatus.Empty);
    }

    private static string JoinStatuses(
        IReadOnlyDictionary<string, GuardianPoiStatus> statuses,
        GuardianPoiStatus expected)
    {
        return string.Join(
            ',',
            statuses
                .Where(pair => pair.Value == expected)
                .Select(pair => pair.Key)
                .Order(StringComparer.Ordinal));
    }

    private static JsonArray WriteRawPoints(
        IEnumerable<GuardianPointOfInterest> points)
    {
        var array = new JsonArray();
        foreach (var point in points)
        {
            array.Add(new JsonObject
            {
                ["name"] = point.Name,
                ["type"] = GetLegacyPoiType(point.Type),
                ["angle"] = point.Angle,
                ["dist"] = point.Distance,
                ["rot"] = point.Rotation,
            });
        }

        return array;
    }

    private static void WriteComponentMaterials(
        JsonObject root,
        IReadOnlyDictionary<string, GuardianComponentLoadout> components)
    {
        var pending = new Dictionary<string, GuardianComponentLoadout>(
            components,
            StringComparer.Ordinal);
        var written = new HashSet<string>(StringComparer.Ordinal);
        var output = new JsonArray();
        if (root["components"] is { } existingNode)
        {
            if (existingNode is not JsonArray existing)
            {
                throw new InvalidDataException(
                    "The Guardian component-material data uses an unsupported JSON shape and was not overwritten.");
            }

            foreach (var node in existing)
            {
                if (node is JsonValue value
                    && value.TryGetValue<string>(out var encoded)
                    && GuardianComponentLoadout.TryParseLegacy(
                        encoded,
                        out var existingComponent))
                {
                    if (components.TryGetValue(
                            existingComponent.Name,
                            out var replacement)
                        && written.Add(existingComponent.Name))
                    {
                        output.Add(replacement.ToLegacyString());
                        pending.Remove(existingComponent.Name);
                    }
                }
                else
                {
                    output.Add(node?.DeepClone());
                }
            }
        }

        foreach (var component in pending.Values.OrderBy(
                     item => item.Name,
                     StringComparer.Ordinal))
        {
            output.Add(component.ToLegacyString());
        }

        root["components"] = output;
    }
    private static string GetLegacyPoiType(GuardianPoiType type)
    {
        return type switch
        {
            GuardianPoiType.BrokenObelisk => "brokeObelisk",
            GuardianPoiType.DestructiblePanel => "destructablePanel",
            _ => char.ToLowerInvariant(type.ToString()[0])
                + type.ToString()[1..],
        };
    }

    private static async Task WriteAtomicAsync(
        string path,
        JsonObject root,
        CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "The Guardian survey path has no parent folder.");
        Directory.CreateDirectory(folder);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        root,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool IsRuins(GuardianCommanderSiteSurvey survey)
    {
        return survey.Name.StartsWith(
                "$Ancient:#index=",
                StringComparison.Ordinal)
            || survey.SiteType is "Alpha" or "Beta" or "Gamma";
    }

    private static void ValidateObelisk(GuardianObelisk obelisk)
    {
        if (string.IsNullOrWhiteSpace(obelisk.Name)
            || obelisk.Name.Contains('-', StringComparison.Ordinal)
            || obelisk.LogCode.Contains('-', StringComparison.Ordinal)
            || obelisk.ItemCodes.Any(
                item => item.Contains('-', StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "A Guardian obelisk cannot be encoded in the legacy format.");
        }
    }

    private static void ValidateFrontierId(string frontierId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        if (!IsSingleFileName(frontierId))
        {
            throw new ArgumentException(
                "The Frontier ID must be a folder name, not a path.",
                nameof(frontierId));
        }
    }

    private static void ValidateBodyName(string bodyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyName);
        if (!IsSingleFileName(bodyName)
            || bodyName.IndexOfAny(CrossPlatformInvalidFileNameCharacters) >= 0)
        {
            throw new ArgumentException(
                "The body name cannot be represented by a cross-platform survey filename.",
                nameof(bodyName));
        }
    }

    private static bool IsSingleFileName(string value)
    {
        return value is not "." and not ".."
            && string.Equals(
                Path.GetFileName(value),
                value,
                StringComparison.Ordinal)
            && value.IndexOfAny(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0;
    }

    private static string GetFullPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }
}
