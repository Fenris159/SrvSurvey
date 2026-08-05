using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Exploration;

public interface ISystemBodyDataClient
{
    Task<SystemBodyDataLoadResult> GetAsync(
        string systemName,
        long systemAddress,
        CancellationToken cancellationToken = default);
}

public sealed record SystemBodyDataProviderSnapshot(
    string Provider,
    SystemScanSnapshot Snapshot);

public sealed record SystemBodyDataLoadResult(
    IReadOnlyList<SystemBodyDataProviderSnapshot> Providers,
    IReadOnlyList<string> Warnings);

public sealed class SystemBodyDataClient : ISystemBodyDataClient
{
    private const int MaximumResponseBytes = 16 * 1024 * 1024;
    private const double LightSecondMeters = 299_792_458d;
    private const double SolarRadiusMeters = 695_700_000d;
    private static readonly DateTimeOffset BiologicalSignalCutoff =
        new(2022, 11, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly Uri DefaultEdsmBaseUri = new(
        "https://www.edsm.net/");
    private static readonly Uri DefaultSpanshBaseUri = new(
        "https://spansh.co.uk/api/");
    private static readonly HttpClient SharedClient = CreateSharedClient();

    private readonly HttpClient client;
    private readonly Uri edsmBaseUri;
    private readonly Uri spanshBaseUri;
    private readonly TimeSpan requestTimeout;

    public SystemBodyDataClient(
        HttpClient? client = null,
        Uri? edsmBaseUri = null,
        Uri? spanshBaseUri = null,
        TimeSpan? requestTimeout = null)
    {
        this.client = client ?? SharedClient;
        this.edsmBaseUri = edsmBaseUri ?? DefaultEdsmBaseUri;
        this.spanshBaseUri = spanshBaseUri ?? DefaultSpanshBaseUri;
        this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(20);
        if (this.requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestTimeout),
                "The system-body request timeout must be positive.");
        }
    }

    public async Task<SystemBodyDataLoadResult> GetAsync(
        string systemName,
        long systemAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(systemAddress);
        var normalizedName = systemName.Trim();
        var edsmTask = FetchAsync(
            "EDSM",
            new Uri(
                edsmBaseUri,
                "api-system-v1/bodies?systemName="
                    + Uri.EscapeDataString(normalizedName)),
            root => ParseEdsm(root, normalizedName, systemAddress),
            cancellationToken);
        var spanshTask = FetchAsync(
            "Spansh",
            new Uri(
                spanshBaseUri,
                "dump/"
                    + systemAddress.ToString(CultureInfo.InvariantCulture)
                    + "/"),
            root => ParseSpansh(root, normalizedName, systemAddress),
            cancellationToken);
        await Task.WhenAll(edsmTask, spanshTask).ConfigureAwait(false);
        var results = new[]
        {
            await edsmTask.ConfigureAwait(false),
            await spanshTask.ConfigureAwait(false),
        };
        return new SystemBodyDataLoadResult(
            results
                .Where(result => result.Snapshot is not null)
                .Select(result => new SystemBodyDataProviderSnapshot(
                    result.Provider,
                    result.Snapshot!))
                .ToArray(),
            results
                .Where(result => result.Warning is not null)
                .Select(result => result.Warning!)
                .ToArray());
    }

    private async Task<ProviderResult> FetchAsync(
        string provider,
        Uri requestUri,
        Func<JsonElement, SystemScanSnapshot> parser,
        CancellationToken cancellationToken)
    {
        using var timeoutCancellation = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(requestTimeout);
        try
        {
            using var response = await client.GetAsync(
                    requestUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCancellation.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var bytes = await ReadBoundedAsync(
                    response.Content,
                    timeoutCancellation.Token)
                .ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes);
            return new ProviderResult(
                provider,
                parser(document.RootElement),
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new ProviderResult(
                provider,
                null,
                $"{provider} body data timed out safely.");
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or JsonException
                or InvalidDataException)
        {
            return new ProviderResult(
                provider,
                null,
                $"{provider} body data is unavailable: {exception.Message}");
        }
    }

    private static SystemScanSnapshot ParseEdsm(
        JsonElement root,
        string expectedName,
        long expectedAddress)
    {
        RequireObject(root, "EDSM bodies response");
        ValidateAddress(root, expectedAddress, "EDSM bodies response");
        var systemName = GetString(root, "name") ?? expectedName;
        var bodies = ReadBodyArray(root, "EDSM bodies response")
            .Select(body => ParseBody(body, systemName, BodyProvider.Edsm))
            .ToArray();
        ValidateUniqueBodyIds(bodies, "EDSM bodies response");
        return CreateSnapshot(
            systemName,
            expectedAddress,
            null,
            GetInt32(root, "bodyCount") ?? 0,
            bodies);
    }

    private static SystemScanSnapshot ParseSpansh(
        JsonElement root,
        string expectedName,
        long expectedAddress)
    {
        RequireObject(root, "Spansh system dump response");
        if (!TryGetObject(root, "system", out var system))
        {
            throw new InvalidDataException(
                "The Spansh system dump has no system object.");
        }

        ValidateAddress(system, expectedAddress, "Spansh system dump");
        var systemName = GetString(system, "name") ?? expectedName;
        var bodies = ReadBodyArray(system, "Spansh system dump")
            .Select(body => ParseBody(body, systemName, BodyProvider.Spansh))
            .ToArray();
        ValidateUniqueBodyIds(bodies, "Spansh system dump");
        GalacticCoordinate? position = null;
        if (TryGetObject(system, "coords", out var coords)
            && GetDouble(coords, "x") is { } x
            && GetDouble(coords, "y") is { } y
            && GetDouble(coords, "z") is { } z)
        {
            position = new GalacticCoordinate(x, y, z);
        }

        return CreateSnapshot(
            systemName,
            expectedAddress,
            position,
            GetInt32(system, "bodyCount") ?? 0,
            bodies);
    }

    private static SystemScanSnapshot CreateSnapshot(
        string systemName,
        long systemAddress,
        GalacticCoordinate? position,
        int expectedBodyCount,
        IReadOnlyList<SystemScanBodySnapshot> bodies)
    {
        return new SystemScanSnapshot(
            systemName,
            systemAddress,
            position,
            0,
            Math.Max(0, expectedBodyCount),
            false,
            false,
            bodies.Count(body => body.CountsTowardFss),
            0,
            0,
            0,
            0,
            0,
            null,
            null,
            bodies);
    }

    private static SystemScanBodySnapshot ParseBody(
        JsonElement body,
        string systemName,
        BodyProvider provider)
    {
        RequireObject(body, provider + " body");
        var bodyId = GetInt32(body, "bodyId")
            ?? throw new InvalidDataException(
                $"A {provider} body has no bodyId.");
        var name = GetString(body, "name")
            ?? throw new InvalidDataException(
                $"A {provider} body has no name.");
        if (bodyId < 0)
        {
            throw new InvalidDataException(
                $"The {provider} body '{name}' has an invalid bodyId.");
        }

        var type = GetString(body, "type");
        var subType = GetString(body, "subType");
        var isLandable = GetBoolean(body, "isLandable") == true;
        var kind = GetBodyKind(type, subType, isLandable, name);
        var parents = ReadParents(body, provider);
        var biologicalSignalCount = provider == BodyProvider.Spansh
            ? ReadBiologicalSignalCount(body)
            : 0;
        var organisms = provider == BodyProvider.Spansh
            ? ReadOrganisms(body)
            : [];
        var atmosphereType = NormalizeAtmosphereType(
            GetString(body, "atmosphereType"));
        var radius = ReadRadius(body, provider);
        var mass = GetDouble(body, "earthMasses") is > 0 and var earthMasses
            ? earthMasses
            : GetDouble(body, "solarMasses") ?? 0;
        return new SystemScanBodySnapshot(
            bodyId,
            name,
            GetShortName(name, systemName),
            kind,
            kind == SystemBodyKind.Star ? ParseStarClass(subType) : null,
            kind is SystemBodyKind.Star or SystemBodyKind.Barycentre
                ? null
                : NormalizePlanetClass(subType),
            isLandable,
            string.Equals(
                GetString(body, "terraformingState"),
                "Candidate for terraforming",
                StringComparison.Ordinal),
            false,
            false,
            false,
            false,
            null,
            false,
            parents.Count > 0
                && parents[0].Kind == SystemBodyParentKind.Ring,
            ReadTidalLock(body, provider),
            mass,
            GetDouble(body, "distanceToArrival") ?? 0,
            radius,
            (GetDouble(body, "gravity") ?? 0) * 10d,
            GetDouble(body, "surfaceTemperature") ?? 0,
            (GetDouble(body, "surfacePressure") ?? 0) * 100_000d,
            (GetDouble(body, "semiMajorAxis") ?? 0) * LightSecondMeters,
            GetDouble(body, "absoluteMagnitude") ?? 0,
            atmosphereType == "None"
                ? string.Empty
                : GetString(body, "atmosphereType"),
            atmosphereType,
            NormalizeVolcanism(GetString(body, "volcanismType")),
            biologicalSignalCount,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            ReadDictionary(
                body,
                "atmosphereComposition",
                provider == BodyProvider.Edsm),
            ReadDictionary(
                body,
                "materials",
                normalizeCompositionKeys: false,
                lowerCaseKeys: provider == BodyProvider.Edsm),
            ReadRings(body, provider),
            parents,
            organisms,
            []);
    }

    private static double ReadRadius(JsonElement body, BodyProvider provider)
    {
        if (GetDouble(body, "radius") is > 0 and var radius)
        {
            return radius * 1_000d;
        }

        return provider == BodyProvider.Spansh
            && GetDouble(body, "solarRadius") is > 0 and var solarRadius
                ? solarRadius * SolarRadiusMeters
                : 0;
    }

    private static IReadOnlyList<SystemOrganismSnapshot> ReadOrganisms(
        JsonElement body)
    {
        if (!TryGetObject(body, "signals", out var signals)
            || !signals.TryGetProperty("genuses", out var genuses))
        {
            return [];
        }

        if (genuses.ValueKind is JsonValueKind.Null)
        {
            return [];
        }

        if (genuses.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "The Spansh body genuses value is not an array.");
        }

        return genuses.EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Select(genus => new SystemOrganismSnapshot(
                genus,
                ExobiologyReferenceCatalog.GetGenusDisplayName(genus),
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                false,
                false))
            .ToArray();
    }

    private static int ReadBiologicalSignalCount(JsonElement body)
    {
        if (!TryGetObject(body, "signals", out var signals)
            || GetDateTimeOffset(signals, "updateTime") is not { } updated
            || updated <= BiologicalSignalCutoff
            || !TryGetObject(signals, "signals", out var counts))
        {
            return 0;
        }

        return Math.Max(
            0,
            GetInt32(counts, "$SAA_SignalType_Biological;") ?? 0);
    }

    private static bool? ReadTidalLock(
        JsonElement body,
        BodyProvider provider)
    {
        var value = GetBoolean(body, "rotationalPeriodTidallyLocked");
        return provider == BodyProvider.Edsm && value != true ? null : value;
    }

    private static IReadOnlyList<SystemBodyParentSnapshot> ReadParents(
        JsonElement body,
        BodyProvider provider)
    {
        if (!body.TryGetProperty("parents", out var parents)
            || parents.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (parents.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"The {provider} body parents value is not an array.");
        }

        var result = new List<SystemBodyParentSnapshot>();
        foreach (var parent in parents.EnumerateArray())
        {
            if (parent.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"A {provider} body parent is not an object.");
            }

            string? kindText;
            int? parentBodyId;
            if (GetString(parent, "type") is { } storedKind)
            {
                kindText = storedKind;
                parentBodyId = GetInt32(parent, "id");
            }
            else
            {
                var property = parent.EnumerateObject().FirstOrDefault();
                kindText = property.Name;
                parentBodyId = property.Value.ValueKind == JsonValueKind.Number
                    && property.Value.TryGetInt32(out var value)
                        ? value
                        : null;
            }

            if (!Enum.TryParse<SystemBodyParentKind>(
                    kindText,
                    ignoreCase: true,
                    out var kind)
                || parentBodyId is null or < 0)
            {
                throw new InvalidDataException(
                    $"A {provider} body parent is invalid.");
            }

            result.Add(new SystemBodyParentSnapshot(kind, parentBodyId.Value));
        }

        return result;
    }

    private static IReadOnlyList<SystemRingSnapshot> ReadRings(
        JsonElement body,
        BodyProvider provider)
    {
        if (!body.TryGetProperty("rings", out var rings)
            || rings.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (rings.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"The {provider} body rings value is not an array.");
        }

        return rings.EnumerateArray()
            .Select(ring =>
            {
                RequireObject(ring, provider + " body ring");
                return new SystemRingSnapshot(
                    GetString(ring, "name")
                        ?? throw new InvalidDataException(
                            $"A {provider} body ring has no name."),
                    GetString(ring, "type"),
                    GetDouble(ring, "innerRadius") ?? 0,
                    GetDouble(ring, "outerRadius") ?? 0);
            })
            .ToArray();
    }

    private static IReadOnlyDictionary<string, double> ReadDictionary(
        JsonElement owner,
        string propertyName,
        bool normalizeCompositionKeys,
        bool lowerCaseKeys = false)
    {
        if (!owner.TryGetProperty(propertyName, out var values)
            || values.ValueKind == JsonValueKind.Null)
        {
            return new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase);
        }

        if (values.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"The external {propertyName} value is not an object.");
        }

        var result = new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values.EnumerateObject())
        {
            if (pair.Value.ValueKind != JsonValueKind.Number
                || !pair.Value.TryGetDouble(out var value)
                || !double.IsFinite(value))
            {
                throw new InvalidDataException(
                    $"The external {propertyName} value '{pair.Name}' is invalid.");
            }

            var key = normalizeCompositionKeys
                ? NormalizeCompositionKey(pair.Name)
                : pair.Name;
            result[lowerCaseKeys ? key.ToLowerInvariant() : key] = value;
        }

        return result;
    }

    private static SystemBodyKind GetBodyKind(
        string? type,
        string? subType,
        bool isLandable,
        string name)
    {
        if (isLandable)
        {
            return SystemBodyKind.LandablePlanet;
        }

        if (string.Equals(type, "Star", StringComparison.OrdinalIgnoreCase))
        {
            return SystemBodyKind.Star;
        }

        if (string.Equals(type, "Barycentre", StringComparison.OrdinalIgnoreCase))
        {
            return SystemBodyKind.Barycentre;
        }

        if (name.Contains("cluster", StringComparison.OrdinalIgnoreCase))
        {
            return SystemBodyKind.Asteroid;
        }

        if (name.EndsWith("Ring", StringComparison.OrdinalIgnoreCase))
        {
            return SystemBodyKind.Ring;
        }

        if (string.IsNullOrWhiteSpace(subType))
        {
            return SystemBodyKind.Barycentre;
        }

        return subType.Contains("giant", StringComparison.OrdinalIgnoreCase)
            ? SystemBodyKind.GasGiant
            : SystemBodyKind.Planet;
    }

    private static string? NormalizePlanetClass(string? value)
    {
        return value == "Metal-rich body" ? "Metal rich body" : value;
    }

    private static string NormalizeAtmosphereType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value == "No atmosphere")
        {
            return "None";
        }

        return string.Concat(value
            .Replace("Thin ", string.Empty, StringComparison.Ordinal)
            .Replace("Thick ", string.Empty, StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal)
            .Replace(" atmosphere", string.Empty, StringComparison.Ordinal)
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static string? NormalizeVolcanism(string? value)
    {
        return value == "No volcanism" ? string.Empty : value;
    }

    private static string NormalizeCompositionKey(string value)
    {
        var normalized = value.Replace(
            "-rich",
            "Rich",
            StringComparison.Ordinal);
        var separator = normalized.IndexOf(' ');
        return separator < 0 || separator == normalized.Length - 1
            ? normalized
            : normalized[..separator]
                + char.ToUpperInvariant(normalized[separator + 1])
                + normalized[(separator + 2)..];
    }

    private static string? ParseStarClass(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var start = value.IndexOf('(');
        var end = start < 0 ? -1 : value.IndexOf(')', start + 1);
        if (start >= 0 && end > start + 1 && end - start - 1 <= 3)
        {
            return value[(start + 1)..end];
        }

        return value == "T Tauri Star" ? "TTS" : value[..1];
    }

    private static string GetShortName(string bodyName, string systemName)
    {
        var shortName = bodyName.StartsWith(
            systemName,
            StringComparison.Ordinal)
                ? bodyName[systemName.Length..]
                : bodyName;
        return shortName.Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static void ValidateAddress(
        JsonElement owner,
        long expectedAddress,
        string source)
    {
        var address = GetInt64(owner, "id64");
        if (address != expectedAddress)
        {
            throw new InvalidDataException(
                $"{source} contains system address {address?.ToString(CultureInfo.InvariantCulture) ?? "none"}, not {expectedAddress.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private static IReadOnlyList<JsonElement> ReadBodyArray(
        JsonElement owner,
        string source)
    {
        if (!owner.TryGetProperty("bodies", out var bodies)
            || bodies.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"The {source} bodies value is not an array.");
        }

        return bodies.EnumerateArray().ToArray();
    }

    private static void ValidateUniqueBodyIds(
        IReadOnlyList<SystemScanBodySnapshot> bodies,
        string source)
    {
        if (bodies.Select(body => body.BodyId).Distinct().Count() != bodies.Count)
        {
            throw new InvalidDataException(
                $"The {source} contains duplicate body IDs.");
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new InvalidDataException(
                "The external body response exceeds the 16 MiB safety limit.");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var block = new byte[81920];
        while (true)
        {
            var count = await stream.ReadAsync(block, cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + count > MaximumResponseBytes)
            {
                throw new InvalidDataException(
                    "The external body response exceeds the 16 MiB safety limit.");
            }

            await buffer.WriteAsync(
                    block.AsMemory(0, count),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static void RequireObject(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"The {name} is not an object.");
        }
    }

    private static bool TryGetObject(
        JsonElement owner,
        string propertyName,
        out JsonElement value)
    {
        return owner.TryGetProperty(propertyName, out value)
            && value.ValueKind == JsonValueKind.Object;
    }

    private static string? GetString(JsonElement owner, string propertyName)
    {
        return owner.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static bool? GetBoolean(JsonElement owner, string propertyName)
    {
        return owner.TryGetProperty(propertyName, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;
    }

    private static int? GetInt32(JsonElement owner, string propertyName)
    {
        return owner.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var result)
                ? result
                : null;
    }

    private static long? GetInt64(JsonElement owner, string propertyName)
    {
        return owner.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var result)
                ? result
                : null;
    }

    private static double? GetDouble(JsonElement owner, string propertyName)
    {
        return owner.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var result)
            && double.IsFinite(result)
                ? result
                : null;
    }

    private static DateTimeOffset? GetDateTimeOffset(
        JsonElement owner,
        string propertyName)
    {
        return GetString(owner, propertyName) is { } value
            && DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var result)
                    ? result
                    : null;
    }

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(25),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "SrvSurvey-Avalonia/1.0");
        return client;
    }

    private enum BodyProvider
    {
        Edsm,
        Spansh,
    }

    private sealed record ProviderResult(
        string Provider,
        SystemScanSnapshot? Snapshot,
        string? Warning);
}
