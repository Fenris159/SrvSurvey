using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SrvSurvey.Desktop.Platform.Inara;

public interface IInaraCommunityGoalClient
{
    Task<InaraCommunityGoalsResult> GetRecentAsync(
        CancellationToken cancellationToken = default);
}

public sealed record InaraCommunityGoalSnapshot(
    string Title,
    string Description,
    string Objective,
    string Reward,
    string System,
    string Station,
    DateTimeOffset? ExpiresAt,
    bool IsComplete,
    int? TierReached,
    int? TierMaximum,
    int? Contributors,
    long? ContributionsTotal,
    DateTimeOffset? LastUpdatedAt,
    string InaraUrl);

public sealed record InaraCommunityGoalsResult(
    IReadOnlyList<InaraCommunityGoalSnapshot> Goals,
    DateTimeOffset FetchedAt,
    bool IsStale,
    string Warning);

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The injected client is application-scoped and its gate may have in-flight waiters.")]
public sealed class InaraCommunityGoalClient : IInaraCommunityGoalClient
{
    public const string Endpoint = "https://inara.cz/inapi/v1/";
    public static readonly TimeSpan DefaultCacheAge = TimeSpan.FromMinutes(15);

    private const long MaximumResponseBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };

    private readonly HttpClient httpClient;
    private readonly string apiKey;
    private readonly string appVersion;
    private readonly string cachePath;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly TimeSpan cacheAge;
    private readonly SemaphoreSlim refreshGate = new(1, 1);

    public InaraCommunityGoalClient(
        HttpClient httpClient,
        string apiKey,
        string appVersion,
        string cachePath,
        Func<DateTimeOffset>? utcNow = null,
        TimeSpan? cacheAge = null)
    {
        this.httpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        this.apiKey = apiKey.Trim();
        this.appVersion = appVersion.Trim();
        this.cachePath = Path.GetFullPath(cachePath);
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        this.cacheAge = cacheAge ?? DefaultCacheAge;
    }

    public async Task<InaraCommunityGoalsResult> GetRecentAsync(
        CancellationToken cancellationToken = default)
    {
        var now = utcNow();
        var cached = await TryLoadCacheAsync(cancellationToken)
            .ConfigureAwait(false);
        if (cached is not null && now - cached.FetchedAt < cacheAge)
        {
            return ToResult(cached, isStale: false, string.Empty);
        }

        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = utcNow();
            cached = await TryLoadCacheAsync(cancellationToken)
                .ConfigureAwait(false);
            if (cached is not null && now - cached.FetchedAt < cacheAge)
            {
                return ToResult(cached, isStale: false, string.Empty);
            }

            try
            {
                var fetched = await FetchAsync(now, cancellationToken)
                    .ConfigureAwait(false);
                await SaveCacheAsync(fetched, cancellationToken)
                    .ConfigureAwait(false);
                return ToResult(fetched, isStale: false, string.Empty);
            }
            catch (Exception exception) when (
                IsRecoverable(exception, cancellationToken))
            {
                var warning =
                    "Inara Community Goal enrichment could not be refreshed: "
                    + exception.Message;
                return cached is null
                    ? new InaraCommunityGoalsResult([], now, false, warning)
                    : ToResult(cached, isStale: true, warning);
            }
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private async Task<InaraCommunityGoalCache> FetchAsync(
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            header = new
            {
                appName = "SrvSurvey",
                appVersion,
                isBeingDeveloped = true,
                APIkey = apiKey,
            },
            events = new[]
            {
                new
                {
                    eventName = "getCommunityGoalsRecent",
                    eventTimestamp = fetchedAt.ToString(
                        "O",
                        CultureInfo.InvariantCulture),
                    eventData = Array.Empty<object>(),
                },
            },
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new ByteArrayContent(payload),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
            "application/json")
        {
            CharSet = "utf-8",
        };
        using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Inara returned HTTP {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }

        var content = await ReadBoundedTextAsync(
                response.Content,
                cancellationToken)
            .ConfigureAwait(false);
        return ParseResponse(content, fetchedAt);
    }

    internal static InaraCommunityGoalCache ParseResponse(
        string content,
        DateTimeOffset fetchedAt)
    {
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var headerStatus = GetInt32(GetProperty(root, "header"), "eventStatus");
        if (headerStatus is null or < 200 or >= 300)
        {
            throw new InvalidDataException(
                $"Inara returned API status {headerStatus?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}.");
        }

        var events = GetProperty(root, "events");
        if (events is not { ValueKind: JsonValueKind.Array }
            || events.Value.GetArrayLength() == 0)
        {
            throw new InvalidDataException(
                "Inara returned no Community Goal response event.");
        }

        var responseEvent = events.Value[0];
        var eventStatus = GetInt32(responseEvent, "eventStatus");
        if (eventStatus is null or < 200 or >= 300)
        {
            throw new InvalidDataException(
                $"Inara returned Community Goal status {eventStatus?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}.");
        }

        var eventData = GetProperty(responseEvent, "eventData");
        if (eventData is not { ValueKind: JsonValueKind.Array })
        {
            throw new InvalidDataException(
                "Inara returned malformed Community Goal data.");
        }

        var goals = eventData.Value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Take(100)
            .Select(ParseGoal)
            .Where(goal => !string.IsNullOrWhiteSpace(goal.Title))
            .ToArray();
        return new InaraCommunityGoalCache(fetchedAt, goals);
    }

    private static InaraCommunityGoalSnapshot ParseGoal(JsonElement goal)
    {
        return new InaraCommunityGoalSnapshot(
            GetString(goal, "communitygoalName"),
            GetString(goal, "goalDescriptionText"),
            GetString(goal, "goalObjectiveText"),
            GetString(goal, "goalRewardText"),
            GetString(goal, "starsystemName"),
            GetString(goal, "stationName"),
            GetDateTimeOffset(goal, "goalExpiry"),
            GetBoolean(goal, "isCompleted") ?? false,
            GetInt32(goal, "tierReached"),
            GetInt32(goal, "tierMax"),
            GetInt32(goal, "contributorsNum"),
            GetInt64(goal, "contributionsTotal"),
            GetDateTimeOffset(goal, "lastUpdate"),
            GetString(goal, "inaraURL"));
    }

    private async Task<InaraCommunityGoalCache?> TryLoadCacheAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(cachePath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                cachePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            return await JsonSerializer.DeserializeAsync<InaraCommunityGoalCache>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task SaveCacheAsync(
        InaraCommunityGoalCache cache,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(cachePath)
            ?? throw new InvalidOperationException(
                "Inara Community Goal cache has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        cache,
                        JsonOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, cachePath, overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    internal static void TryDeleteTemporaryFile(
        string temporaryPath,
        Func<string, bool>? fileExists = null,
        Action<string>? deleteFile = null)
    {
        fileExists ??= File.Exists;
        deleteFile ??= File.Delete;
        try
        {
            if (fileExists(temporaryPath))
            {
                deleteFile(temporaryPath);
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
        {
            // Best-effort cleanup must not replace the cache save failure.
        }
    }

    private static async Task<string> ReadBoundedTextAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (destination.Length + read > MaximumResponseBytes)
            {
                throw new InvalidDataException(
                    $"Inara response exceeded {MaximumResponseBytes:N0} bytes.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }

        return System.Text.Encoding.UTF8.GetString(destination.ToArray());
    }

    private static bool IsRecoverable(
        Exception exception,
        CancellationToken cancellationToken)
    {
        return exception is HttpRequestException
            or IOException
            or InvalidDataException
            or JsonException
            || exception is OperationCanceledException
                && !cancellationToken.IsCancellationRequested;
    }

    private static InaraCommunityGoalsResult ToResult(
        InaraCommunityGoalCache cache,
        bool isStale,
        string warning) =>
        new(cache.Goals, cache.FetchedAt, isStale, warning);

    private static JsonElement? GetProperty(JsonElement? owner, string name)
    {
        if (owner is not { ValueKind: JsonValueKind.Object } value)
        {
            return null;
        }

        if (value.TryGetProperty(name, out var exact))
        {
            return exact;
        }

        foreach (var property in value.EnumerateObject())
        {
            if (string.Equals(
                property.Name,
                name,
                StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string GetString(JsonElement owner, string name)
    {
        var value = GetProperty(owner, name);
        return value is { ValueKind: JsonValueKind.String }
            ? value.Value.GetString()?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static int? GetInt32(JsonElement? owner, string name)
    {
        var value = GetProperty(owner, name);
        return value is { ValueKind: JsonValueKind.Number }
            && value.Value.TryGetInt32(out var number)
                ? number
                : null;
    }

    private static long? GetInt64(JsonElement owner, string name)
    {
        var value = GetProperty(owner, name);
        return value is { ValueKind: JsonValueKind.Number }
            && value.Value.TryGetInt64(out var number)
                ? number
                : null;
    }

    private static bool? GetBoolean(JsonElement owner, string name)
    {
        var value = GetProperty(owner, name);
        return value?.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static DateTimeOffset? GetDateTimeOffset(
        JsonElement owner,
        string name)
    {
        var value = GetProperty(owner, name);
        return value is { ValueKind: JsonValueKind.String }
            && value.Value.TryGetDateTimeOffset(out var timestamp)
                ? timestamp
                : null;
    }
}

internal sealed record InaraCommunityGoalCache(
    DateTimeOffset FetchedAt,
    IReadOnlyList<InaraCommunityGoalSnapshot> Goals);
