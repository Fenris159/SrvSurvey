using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class UiSettingsDocumentStore
{
    private static readonly ConcurrentDictionary<string, object> FileLocks = new(
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
    private readonly object fileLock;

    public UiSettingsDocumentStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
        fileLock = FileLocks.GetOrAdd(Path, _ => new object());
    }

    public string Path { get; }

    public JsonObject Load()
    {
        lock (fileLock)
        {
            return ReadObject();
        }
    }

    public void Update(Action<JsonObject> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (fileLock)
        {
            var root = ReadObject();
            update(root);
            WriteObject(root);
        }
    }

    private JsonObject ReadObject()
    {
        if (!File.Exists(Path))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(Path)) as JsonObject ?? [];
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return [];
        }
    }

    private void WriteObject(JsonObject root)
    {
        var directory = System.IO.Path.GetDirectoryName(Path)
            ?? throw new InvalidOperationException(
                "The UI settings path has no directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{Path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var writer = new Utf8JsonWriter(
                       stream,
                       new JsonWriterOptions { Indented = true }))
            {
                root.WriteTo(writer);
            }

            File.Move(temporaryPath, Path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
