using System.Text.Json;
using System.Text.Json.Nodes;

namespace SrvSurvey.Core.Updates;

public sealed record PublishedReferenceVersions(
    int CodexReference,
    int BiologyCriteria,
    int BiologyEngine,
    int SettlementTemplate,
    int Guardian,
    int Settlements,
    int Nicknames,
    int GreenGasGiants)
{
    public static PublishedReferenceVersions Empty { get; } = new(
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0);
}

public sealed class PublishedReferenceVersionStore
{
    public const string ManifestFileName = ".cross-platform-reference-index.json";

    private const int ManifestVersion = 1;

    public PublishedReferenceVersions Load(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var root = Path.GetFullPath(dataDirectory);
        var manifestPath = Path.Combine(root, "pub", ManifestFileName);
        if (File.Exists(manifestPath))
        {
            try
            {
                var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))
                    as JsonObject;
                if (manifest is not null
                    && ReadInt(manifest, "Version") == ManifestVersion)
                {
                    return ReadVersions(manifest);
                }
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or JsonException)
            {
                // A malformed cross-platform manifest must not hide a valid
                // imported WinForms version record.
            }
        }

        var legacySettingsPath = Path.Combine(root, "settings.json");
        if (!File.Exists(legacySettingsPath))
        {
            return PublishedReferenceVersions.Empty;
        }

        try
        {
            var settings = JsonNode.Parse(File.ReadAllText(legacySettingsPath))
                as JsonObject;
            return settings is null
                ? PublishedReferenceVersions.Empty
                : new PublishedReferenceVersions(
                    ReadInt(settings, "pubCodexRef"),
                    ReadInt(settings, "pubBioCriteria"),
                    ReadInt(settings, "pubBioEngine"),
                    ReadInt(settings, "pubDataSettlementTemplate"),
                    ReadInt(settings, "pubDataGuardian"),
                    ReadInt(settings, "pubSettlements"),
                    ReadInt(settings, "pubNicknames"),
                    ReadInt(settings, "pubGGG"));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            return PublishedReferenceVersions.Empty;
        }
    }

    public async Task WriteAsync(
        string publishedDataDirectory,
        PublishedReferenceVersions versions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publishedDataDirectory);
        ArgumentNullException.ThrowIfNull(versions);
        var directory = Path.GetFullPath(publishedDataDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ManifestFileName);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        var root = new JsonObject
        {
            ["Version"] = ManifestVersion,
            ["CodexReference"] = versions.CodexReference,
            ["BiologyCriteria"] = versions.BiologyCriteria,
            ["BiologyEngine"] = versions.BiologyEngine,
            ["SettlementTemplate"] = versions.SettlementTemplate,
            ["Guardian"] = versions.Guardian,
            ["Settlements"] = versions.Settlements,
            ["Nicknames"] = versions.Nicknames,
            ["GreenGasGiants"] = versions.GreenGasGiants,
        };

        try
        {
            await File.WriteAllTextAsync(
                    temporaryPath,
                    root.ToJsonString(new JsonSerializerOptions
                    {
                        WriteIndented = true,
                    }),
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static PublishedReferenceVersions ReadVersions(JsonObject root)
    {
        return new PublishedReferenceVersions(
            ReadInt(root, "CodexReference"),
            ReadInt(root, "BiologyCriteria"),
            ReadInt(root, "BiologyEngine"),
            ReadInt(root, "SettlementTemplate"),
            ReadInt(root, "Guardian"),
            ReadInt(root, "Settlements"),
            ReadInt(root, "Nicknames"),
            ReadInt(root, "GreenGasGiants"));
    }

    private static int ReadInt(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<int>(out var number)
            && number >= 0
                ? number
                : 0;
    }
}
