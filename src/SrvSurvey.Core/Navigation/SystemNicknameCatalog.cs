using System.Text.Json;
using System.Text.Json.Nodes;

namespace SrvSurvey.Core.Navigation;

public sealed class SystemNicknameCatalog
{
    private readonly IReadOnlyDictionary<string, string> localNames;
    private readonly IReadOnlyDictionary<string, string> ravenNames;

    private SystemNicknameCatalog(
        IReadOnlyDictionary<string, string> localNames,
        IReadOnlyDictionary<string, string> ravenNames,
        IReadOnlyList<string> warnings)
    {
        this.localNames = localNames;
        this.ravenNames = ravenNames;
        Warnings = warnings;
    }

    public IReadOnlyList<string> Warnings { get; }

    public int LocalCount => localNames.Count;

    public int RavenCount => ravenNames.Count;

    public static SystemNicknameCatalog Load(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var root = Path.GetFullPath(dataDirectory);
        var warnings = new List<string>();
        var local = LoadMap(
            Path.Combine(root, "system-nick-names.json"),
            nestedProperty: "map",
            warnings);
        var raven = LoadMap(
            Path.Combine(root, "pub", "nicknames.json"),
            nestedProperty: null,
            warnings);
        return new SystemNicknameCatalog(local, raven, warnings);
    }

    public string Resolve(string? systemName, bool enabled = true)
    {
        if (systemName is null)
        {
            return string.Empty;
        }

        if (!enabled)
        {
            return systemName;
        }

        return localNames.GetValueOrDefault(systemName)
            ?? ravenNames.GetValueOrDefault(systemName)
            ?? systemName;
    }

    private static IReadOnlyDictionary<string, string> LoadMap(
        string path,
        string? nestedProperty,
        ICollection<string> warnings)
    {
        var result = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
        {
            return result;
        }

        try
        {
            var parsed = JsonNode.Parse(
                    File.ReadAllText(path),
                    documentOptions: new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip,
                    })
                as JsonObject
                ?? throw new InvalidDataException(
                    "The nickname file is not a JSON object.");
            var map = nestedProperty is null
                ? parsed
                : parsed[nestedProperty] as JsonObject
                    ?? throw new InvalidDataException(
                        $"The nickname file has no '{nestedProperty}' object.");
            foreach (var entry in map)
            {
                if (entry.Value is JsonValue value
                    && value.TryGetValue<string>(out var nickname)
                    && !string.IsNullOrWhiteSpace(entry.Key)
                    && !string.IsNullOrWhiteSpace(nickname))
                {
                    result[entry.Key.Trim()] = nickname.Trim();
                }
                else
                {
                    warnings.Add(
                        $"Ignored invalid system nickname '{entry.Key}' in "
                        + Path.GetFileName(path)
                        + ".");
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or InvalidDataException)
        {
            warnings.Add(
                $"Could not read system nicknames from '{path}': "
                + exception.Message);
        }

        return result;
    }
}
