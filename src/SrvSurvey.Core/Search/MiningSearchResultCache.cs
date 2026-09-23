using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SrvSurvey.Core.Search;

/// <summary>Stores completed mining search presentations by their complete filter key.</summary>
public sealed class MiningSearchResultCache(string dataDirectory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly string directory = Path.Combine(dataDirectory, "mining-search-cache");

    public T? Load<T>(string workspace, string key)
    {
        try
        {
            string path = EntryPath(workspace, key);
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : default;
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
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, JsonOptions));
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
}
