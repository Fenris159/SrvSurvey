using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SrvSurvey.Core.Search;

/// <summary>Stores completed mining search presentations by their complete filter key.</summary>
public sealed class MiningSearchResultCache(string dataDirectory)
{
    private const int SchemaVersion = 1;
    private static readonly ConcurrentDictionary<string, MiningSearchResultCache> SharedCaches = new(
        StringComparer.Ordinal
    );
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly string directory = Path.Combine(dataDirectory, "mining-search-cache");
    private bool? hideIrrelevantMaterialTags;

    public static MiningSearchResultCache ForDirectory(string dataDirectory) =>
        SharedCaches.GetOrAdd(Path.GetFullPath(dataDirectory), path => new MiningSearchResultCache(path));

    public event Action<bool>? HideIrrelevantMaterialTagsChanged;

    public bool HideIrrelevantMaterialTags
    {
        get => hideIrrelevantMaterialTags ??= Load<bool>("mining-display", "hide-irrelevant");
        set
        {
            if (HideIrrelevantMaterialTags == value)
            {
                return;
            }

            hideIrrelevantMaterialTags = value;
            Save("mining-display", "hide-irrelevant", value);
            HideIrrelevantMaterialTagsChanged?.Invoke(value);
        }
    }

    public T? Load<T>(string workspace, string key)
    {
        try
        {
            string path = EntryPath(workspace, key);
            if (!File.Exists(path))
            {
                return default;
            }

            CacheEntry<T>? entry = JsonSerializer.Deserialize<CacheEntry<T>>(File.ReadAllText(path), JsonOptions);
            return entry?.Version == SchemaVersion ? entry.Value : default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return default;
        }
    }

    public T? LoadLast<T>(string workspace)
    {
        try
        {
            string path = Path.Combine(directory, workspace + "-last.txt");
            return File.Exists(path) ? Load<T>(workspace, File.ReadAllText(path)) : default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return default;
        }
    }

    public void Save<T>(string workspace, string key, T snapshot)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string entry = EntryPath(workspace, key);
            string temporary = entry + ".tmp";
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(new CacheEntry<T>(SchemaVersion, snapshot), JsonOptions)
            );
            File.Move(temporary, entry, true);
            File.WriteAllText(Path.Combine(directory, workspace + "-last.txt"), key);
            foreach (
                FileInfo old in new DirectoryInfo(directory)
                    .GetFiles(workspace + "-*.json")
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Skip(32)
            )
            {
                old.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Search results remain usable in memory if the profile directory is unavailable.
        }
    }

    private string EntryPath(string workspace, string key) =>
        Path.Combine(
            directory,
            workspace + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json"
        );

    private sealed record CacheEntry<T>(int Version, T Value);
}
