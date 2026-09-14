using System.IO.Compression;
using System.Text.Json;
using SrvSurvey.Core.Firegroups;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Mining;

public sealed record MiningBackupContents(MiningCommanderData Data, string Bookmarks, string? Firegroups = null);

public static class MiningBackup
{
    private const int MaximumBytes = 256 * 1024 * 1024;
    private static readonly StringComparer FileSystemPathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static byte[] Create(MiningCommanderData data, string bookmarks, string? firegroups = null)
    {
        if (firegroups is not null)
        {
            _ = FiregroupStore.Parse(firegroups);
        }

        List<GalacticBookmark> locations = BookmarkCatalog.Parse(bookmarks);
        MiningCommanderData copy = MiningStore.Parse(JsonSerializer.Serialize(data));
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            AddScreenshots(
                archive,
                Sessions(copy).Select(s => s.Screenshots).Concat(locations.Select(b => b.Screenshots))
            );
            Write(archive, "mining.json", JsonSerializer.Serialize(copy));
            Write(archive, "bookmarks.json", JsonSerializer.Serialize(locations));
            if (firegroups is not null)
            {
                Write(archive, "firegroups.json", firegroups);
            }
        }
        if (buffer.Length > MaximumBytes)
        {
            throw new IOException("Mining backup exceeds 256 MB. Export fewer screenshot attachments.");
        }

        return buffer.ToArray();
    }

    private static void AddScreenshots(ZipArchive archive, IEnumerable<List<string>> screenshotsByRecord)
    {
        var imageNames = new Dictionary<string, string>(FileSystemPathComparer);
        foreach (List<string> screenshots in screenshotsByRecord)
        {
            for (int index = 0; index < screenshots.Count; index++)
            {
                string path = screenshots[index];
                if (!File.Exists(path))
                {
                    continue;
                }

                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (
                    extension is not (".png" or ".jpg" or ".jpeg" or ".webp")
                    || new FileInfo(path).Length > 20 * 1024 * 1024
                )
                {
                    continue;
                }

                if (!imageNames.TryGetValue(path, out string? name))
                {
                    name = "images/" + Guid.NewGuid().ToString("N") + extension;
                    ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                    using Stream output = entry.Open();
                    using FileStream input = File.OpenRead(path);
                    input.CopyTo(output);
                    imageNames[path] = name;
                }
                screenshots[index] = name;
            }
        }
    }

    public static MiningBackupContents Read(byte[] bytes, string attachmentDirectory)
    {
        if (bytes.Length > MaximumBytes)
        {
            throw new IOException("Mining backup exceeds 256 MB.");
        }

        using var buffer = new MemoryStream(bytes, false);
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
        if (archive.Entries.Sum(e => e.Length) > MaximumBytes)
        {
            throw new IOException("Expanded mining backup exceeds 256 MB.");
        }

        string? firegroups = archive.GetEntry("firegroups.json") is null ? null : ReadText(archive, "firegroups.json");
        if (firegroups is not null)
        {
            _ = FiregroupStore.Parse(firegroups);
        }

        MiningCommanderData data = MiningStore.Parse(ReadText(archive, "mining.json"));
        string bookmarks = ReadText(archive, "bookmarks.json");
        List<GalacticBookmark> locations = BookmarkCatalog.Parse(bookmarks);
        string destination = Path.Combine(attachmentDirectory, Guid.NewGuid().ToString("N"));
        foreach (
            List<string>? screenshots in Sessions(data)
                .Select(s => s.Screenshots)
                .Concat(locations.Select(b => b.Screenshots))
        )
        {
            for (int index = 0; index < screenshots.Count; index++)
            {
                string name = screenshots[index];
                if (!name.StartsWith("images/", StringComparison.Ordinal))
                {
                    continue;
                }

                ZipArchiveEntry entry =
                    archive.GetEntry(name)
                    ?? throw new InvalidDataException("A screenshot is missing from the mining backup.");
                string extension = Path.GetExtension(name).ToLowerInvariant();
                if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp"))
                {
                    throw new InvalidDataException("Unsupported screenshot format.");
                }
                // Archive paths never become filesystem paths: each image receives a new generated name.
                Directory.CreateDirectory(destination);
                string path = Path.Combine(destination, Guid.NewGuid().ToString("N") + extension);
                using Stream input = entry.Open();
                using FileStream output = File.Create(path);
                input.CopyTo(output);
                screenshots[index] = path;
            }
        }
        return new(data, JsonSerializer.Serialize(locations), firegroups);
    }

    private static IEnumerable<MiningSession> Sessions(MiningCommanderData data) =>
        data.Current is { } current ? data.History.Append(current) : data.History;

    private static void Write(ZipArchive archive, string name, string text)
    {
        using Stream stream = archive.CreateEntry(name).Open();
        using var writer = new StreamWriter(stream);
        writer.Write(text);
    }

    private static string ReadText(ZipArchive archive, string name)
    {
        using Stream stream = (
            archive.GetEntry(name) ?? throw new InvalidDataException($"Missing {name} in mining backup.")
        ).Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
