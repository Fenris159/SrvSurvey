using System.IO.Compression;
using System.Text.Json;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Firegroups;

namespace SrvSurvey.Core.Mining;

public sealed record MiningBackupContents(MiningCommanderData Data, string Bookmarks, string? Firegroups = null);

public static class MiningBackup
{
    private const int MaximumBytes = 256 * 1024 * 1024;
    public static byte[] Create(MiningCommanderData data, string bookmarks, string? firegroups = null)
    {
        if (firegroups is not null) _ = FiregroupStore.Parse(firegroups);
        var locations = BookmarkCatalog.Parse(bookmarks);
        var copy = MiningStore.Parse(JsonSerializer.Serialize(data));
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            var imageNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var screenshots in Sessions(copy).Select(s => s.Screenshots).Concat(locations.Select(b => b.Screenshots)))
            {
                for (var index = 0; index < screenshots.Count; index++)
                {
                    var path = screenshots[index];
                    if (!File.Exists(path)) continue;
                    var extension = Path.GetExtension(path).ToLowerInvariant();
                    if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp") || new FileInfo(path).Length > 20 * 1024 * 1024) continue;
                    if (!imageNames.TryGetValue(path, out var name))
                    {
                        name = "images/" + Guid.NewGuid().ToString("N") + extension;
                        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                        using var output = entry.Open();
                        using var input = File.OpenRead(path);
                        input.CopyTo(output);
                        imageNames[path] = name;
                    }
                    screenshots[index] = name;
                }
            }
            Write(archive, "mining.json", JsonSerializer.Serialize(copy));
            Write(archive, "bookmarks.json", JsonSerializer.Serialize(locations));
            if (firegroups is not null) Write(archive, "firegroups.json", firegroups);
        }
        if (buffer.Length > MaximumBytes) throw new IOException("Mining backup exceeds 256 MB. Export fewer screenshot attachments.");
        return buffer.ToArray();
    }
    public static MiningBackupContents Read(byte[] bytes, string attachmentDirectory)
    {
        if (bytes.Length > MaximumBytes) throw new IOException("Mining backup exceeds 256 MB.");
        using var buffer = new MemoryStream(bytes, false);
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
        if (archive.Entries.Sum(e => e.Length) > MaximumBytes) throw new IOException("Expanded mining backup exceeds 256 MB.");
        var firegroups = archive.GetEntry("firegroups.json") is null ? null : ReadText(archive, "firegroups.json");
        if (firegroups is not null) _ = FiregroupStore.Parse(firegroups);
        var data = MiningStore.Parse(ReadText(archive, "mining.json"));
        var bookmarks = ReadText(archive, "bookmarks.json");
        var locations = BookmarkCatalog.Parse(bookmarks);
        var destination = Path.Combine(attachmentDirectory, Guid.NewGuid().ToString("N"));
        foreach (var screenshots in Sessions(data).Select(s => s.Screenshots).Concat(locations.Select(b => b.Screenshots)))
        {
            for (var index = 0; index < screenshots.Count; index++)
            {
                var name = screenshots[index];
                if (!name.StartsWith("images/", StringComparison.Ordinal)) continue;
                var entry = archive.GetEntry(name) ?? throw new InvalidDataException("A screenshot is missing from the mining backup.");
                var extension = Path.GetExtension(name).ToLowerInvariant();
                if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp")) throw new InvalidDataException("Unsupported screenshot format.");
                // Archive paths never become filesystem paths: each image receives a new generated name.
                Directory.CreateDirectory(destination);
                var path = Path.Combine(destination, Guid.NewGuid().ToString("N") + extension);
                using var input = entry.Open();
                using var output = File.Create(path);
                input.CopyTo(output);
                screenshots[index] = path;
            }
        }
        return new(data, JsonSerializer.Serialize(locations), firegroups);
    }
    private static IEnumerable<MiningSession> Sessions(MiningCommanderData data) => data.Current is { } current ? data.History.Append(current) : data.History;
    private static void Write(ZipArchive archive, string name, string text)
    {
        using var stream = archive.CreateEntry(name).Open();
        using var writer = new StreamWriter(stream);
        writer.Write(text);
    }
    private static string ReadText(ZipArchive archive, string name)
    {
        using var stream = (archive.GetEntry(name) ?? throw new InvalidDataException($"Missing {name} in mining backup.")).Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
