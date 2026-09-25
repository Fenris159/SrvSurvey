using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SrvSurvey.Core.Search;

/// <summary>Short-lived provider responses shared by mining workspaces and retained across launches.</summary>
public sealed class MiningProviderResponseCache(string dataDirectory, TimeProvider? clock = null)
{
    private const int SchemaVersion = 2;
    private const int MaximumEntries = 1024;
    private const long MaximumBytes = 128L * 1024 * 1024;
    private readonly string directory = Path.Combine(dataDirectory, "mining-provider-cache");
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    public JsonDocument? Load(string key, TimeSpan maximumAge)
    {
        try
        {
            string path = EntryPath(key);
            if (!File.Exists(path))
            {
                return null;
            }

            CacheEntry? entry = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(path));
            DateTimeOffset age = clock.GetUtcNow();
            TimeSpan lifetime = entry?.MaximumAge is { } savedAge && savedAge < maximumAge ? savedAge : maximumAge;
            return entry is { Version: SchemaVersion } && entry.FetchedAt <= age && age - entry.FetchedAt < lifetime
                ? JsonDocument.Parse(entry.Body)
                : null;
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    public void Save(string key, JsonDocument response, TimeSpan? maximumAge = null)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string path = EntryPath(key);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(
                    temporary,
                    JsonSerializer.Serialize(
                        new CacheEntry(SchemaVersion, clock.GetUtcNow(), response.RootElement.GetRawText(), maximumAge)
                    )
                );
                File.Move(temporary, path, true);
                Prune();
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A read-only profile must not make a successful provider response fail.
        }
    }

    private void Prune()
    {
        FileInfo[] entries = new DirectoryInfo(directory)
            .GetFiles("*.json")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();
        long bytes = 0;
        for (int index = 0; index < entries.Length; index++)
        {
            bytes += entries[index].Length;
            if (index >= MaximumEntries || bytes > MaximumBytes)
            {
                entries[index].Delete();
            }
        }
    }

    private string EntryPath(string key) =>
        Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");

    private sealed record CacheEntry(int Version, DateTimeOffset FetchedAt, string Body, TimeSpan? MaximumAge);
}
