using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Routes;

public sealed class FollowRouteStore(string dataDirectory)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string dataDirectory = GetFullPath(dataDirectory);
    private readonly SemaphoreSlim saveLock = new(1, 1);

    public async Task<FollowRouteLoadResult> LoadAsync(
        string frontierId,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(frontierId);
        if (!File.Exists(path))
        {
            return new FollowRouteLoadResult(
                path,
                false,
                CreateDefault(frontierId, path),
                null);
        }

        var readResult = await ReadObjectAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (readResult.Root is null)
        {
            return new FollowRouteLoadResult(
                path,
                true,
                null,
                readResult.Error);
        }

        try
        {
            return new FollowRouteLoadResult(
                path,
                true,
                Parse(frontierId, path, readResult.Root),
                null);
        }
        catch (InvalidDataException exception)
        {
            return new FollowRouteLoadResult(
                path,
                true,
                null,
                exception.Message);
        }
    }

    public async Task SaveAsync(
        FollowRouteDocument route,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        var path = GetPath(route.FrontierId);
        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            JsonObject root;
            if (File.Exists(path))
            {
                var readResult = await ReadObjectAsync(path, cancellationToken)
                    .ConfigureAwait(false);
                root = readResult.Root
                    ?? throw new InvalidDataException(
                        "The route file is malformed and was not overwritten: "
                            + readResult.Error);
            }
            else
            {
                root = [];
            }

            root["active"] = route.IsActive;
            root["autoCopy"] = route.AutoCopy;
            root["last"] = route.LastReachedIndex;
            root["hops"] = MergeHops(root["hops"] as JsonArray, route.Hops);
            await WriteObjectAsync(path, root, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            saveLock.Release();
        }
    }

    public string GetPath(string frontierId)
    {
        ValidateFileName(frontierId, nameof(frontierId));
        return Path.Combine(dataDirectory, "routes", frontierId + ".json");
    }

    private static FollowRouteDocument CreateDefault(
        string frontierId,
        string path)
    {
        return new FollowRouteDocument(
            frontierId,
            path,
            true,
            true,
            -1,
            []);
    }

    private static FollowRouteDocument Parse(
        string frontierId,
        string path,
        JsonObject root)
    {
        var hops = new List<FollowRouteHop>();
        if (root["hops"] is JsonArray hopArray)
        {
            for (var index = 0; index < hopArray.Count; index++)
            {
                if (hopArray[index] is not JsonObject hop)
                {
                    throw InvalidRoute(path, $"hops[{index}] is not an object");
                }

                hops.Add(ParseHop(path, index, hop));
            }
        }
        else if (root.ContainsKey("hops"))
        {
            throw InvalidRoute(path, "hops is not an array");
        }

        return new FollowRouteDocument(
            frontierId,
            path,
            GetBoolean(root, "active") ?? true,
            GetBoolean(root, "autoCopy") ?? true,
            GetInt32(root, "last") ?? -1,
            hops);
    }

    private static FollowRouteHop ParseHop(
        string path,
        int index,
        JsonObject root)
    {
        var name = GetString(root, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw InvalidRoute(path, $"hops[{index}] has no valid name");
        }

        GalacticCoordinate? position = null;
        var x = GetDouble(root, "x");
        var y = GetDouble(root, "y");
        var z = GetDouble(root, "z");
        if (x is not null && y is not null && z is not null)
        {
            try
            {
                position = new GalacticCoordinate(x.Value, y.Value, z.Value);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw InvalidRoute(
                    path,
                    $"hops[{index}] has invalid coordinates: {exception.Message}");
            }
        }

        return new FollowRouteHop(
            name,
            GetInt64(root, "id64"),
            position,
            GetString(root, "notes"),
            GetBoolean(root, "refuel") ?? false,
            GetBoolean(root, "neutron") ?? false);
    }

    private static JsonArray MergeHops(
        JsonArray? existing,
        IReadOnlyList<FollowRouteHop> hops)
    {
        var existingRows = existing?
            .Select((node, index) => new ExistingHop(
                index,
                node as JsonObject,
                node is JsonObject row ? GetIdentity(row) : null))
            .ToArray() ?? [];
        var used = new HashSet<int>();
        var result = new JsonArray();
        foreach (var hop in hops)
        {
            var identity = GetIdentity(hop);
            var match = existingRows.FirstOrDefault(candidate =>
                !used.Contains(candidate.Index)
                && string.Equals(
                    candidate.Identity,
                    identity,
                    StringComparison.OrdinalIgnoreCase));
            JsonObject row;
            if (match?.Root is not null)
            {
                used.Add(match.Index);
                row = match.Root.DeepClone().AsObject();
            }
            else
            {
                row = [];
            }

            WriteHop(row, hop);
            result.Add(row);
        }

        return result;
    }

    private static void WriteHop(JsonObject root, FollowRouteHop hop)
    {
        if (string.IsNullOrWhiteSpace(hop.Name))
        {
            throw new InvalidDataException("A route hop name cannot be blank.");
        }

        root["name"] = hop.Name;
        WriteOptional(root, "id64", hop.SystemAddress);
        if (hop.Position is { } position)
        {
            root["x"] = position.X;
            root["y"] = position.Y;
            root["z"] = position.Z;
        }
        else
        {
            root.Remove("x");
            root.Remove("y");
            root.Remove("z");
        }

        WriteOptional(root, "notes", hop.Notes);
        WriteTrue(root, "refuel", hop.Refuel);
        WriteTrue(root, "neutron", hop.Neutron);
    }

    private static string GetIdentity(FollowRouteHop hop)
    {
        return hop.SystemAddress is { } address
            ? $"address:{address}"
            : $"name:{hop.Name}";
    }

    private static string? GetIdentity(JsonObject root)
    {
        var address = GetInt64(root, "id64");
        var name = GetString(root, "name");
        return address is not null
            ? $"address:{address}"
            : string.IsNullOrWhiteSpace(name)
                ? null
                : $"name:{name}";
    }

    private static void WriteOptional<T>(
        JsonObject root,
        string propertyName,
        T? value)
    {
        if (value is null)
        {
            root.Remove(propertyName);
        }
        else
        {
            root[propertyName] = JsonValue.Create(value);
        }
    }

    private static void WriteTrue(
        JsonObject root,
        string propertyName,
        bool value)
    {
        if (value)
        {
            root[propertyName] = true;
        }
        else
        {
            root.Remove(propertyName);
        }
    }

    private static InvalidDataException InvalidRoute(
        string path,
        string detail)
    {
        return new InvalidDataException($"The route {path} is invalid: {detail}.");
    }

    private static bool? GetBoolean(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<bool>(out var result)
                ? result
                : null;
    }

    private static string? GetString(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<string>(out var result)
                ? result
                : null;
    }

    private static int? GetInt32(JsonObject root, string propertyName)
    {
        var value = GetInt64(root, propertyName);
        return value is >= int.MinValue and <= int.MaxValue
            ? (int)value.Value
            : null;
    }

    private static long? GetInt64(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<long>(out var result)
                ? result
                : null;
    }

    private static double? GetDouble(JsonObject root, string propertyName)
    {
        if (root[propertyName] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<double>(out var result))
        {
            return result;
        }

        return value.TryGetValue<long>(out var integer) ? integer : null;
    }

    private static void ValidateFileName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value is "." or ".."
            || !string.Equals(
                Path.GetFileName(value),
                value,
                StringComparison.Ordinal)
            || value.IndexOfAny(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0
            || value.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                "The value must be a file name, not a path.",
                parameterName);
        }
    }

    private static string GetFullPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    private static async Task<JsonObjectReadResult> ReadObjectAsync(
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
            return node is JsonObject root
                ? new JsonObjectReadResult(root, null)
                : new JsonObjectReadResult(
                    null,
                    $"{path} does not contain a JSON object.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return new JsonObjectReadResult(
                null,
                $"Could not read {path}: {exception.Message}");
        }
    }

    private static async Task WriteObjectAsync(
        string path,
        JsonObject root,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                $"The route path has no parent directory: {path}");
        Directory.CreateDirectory(directory);
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

    private sealed record ExistingHop(
        int Index,
        JsonObject? Root,
        string? Identity);

    private sealed record JsonObjectReadResult(JsonObject? Root, string? Error);
}
