using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SrvSurvey.Core.Firegroups;

public sealed class FiregroupStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public FiregroupDocument Load(string commander)
    {
        var path = GetPath(commander);
        if (!File.Exists(path))
        {
            return new();
        }

        return Parse(File.ReadAllText(path));
    }

    public static FiregroupDocument Parse(string json)
    {
        var document =
            JsonSerializer.Deserialize<FiregroupDocument>(json, Options)
            ?? throw new JsonException("Empty Firegroups document.");
        Validate(document);
        return document;
    }

    public static string Export(FiregroupDocument document)
    {
        Validate(document);
        return JsonSerializer.Serialize(document, Options);
    }

    public FiregroupDocument Restore(string commander, string json)
    {
        var restored = Parse(json);
        var path = GetPath(commander);
        if (File.Exists(path))
        {
            File.Copy(path, path + ".before-restore", true);
        }

        Save(commander, restored);
        return restored;
    }

    public void Save(string commander, FiregroupDocument document)
    {
        Validate(document);
        var path = GetPath(commander);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(document, Options));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private string GetPath(string commander)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commander);
        return Path.Combine(
            directory,
            "firegroups",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(commander)))[..24] + ".json"
        );
    }

    private static void Validate(FiregroupDocument document)
    {
        if (
            document.Version != 1
            || document.Ships is null
            || document.Profiles is null
            || document.ActiveProfiles is null
            || document.Ships.Any(InvalidShip)
            || document.Profiles.Any(p =>
                p is null
                || string.IsNullOrWhiteSpace(p.Id)
                || string.IsNullOrWhiteSpace(p.Name)
                || InvalidShip(p.Ship)
                || p.Groups is null
                || p.Groups.Select(g => g?.Number).Distinct().Count() != p.Groups.Count
                || p.Groups.Any(g =>
                    g is null || g.Number is < 0 or > 7 || InvalidModules(g.Primary) || InvalidModules(g.Secondary)
                )
            )
            || document.Profiles.Select(p => p.Id).Distinct().Count() != document.Profiles.Count
        )
        {
            throw new JsonException("Invalid Firegroups configuration.");
        }
    }

    private static bool InvalidShip(FiregroupShip ship) =>
        ship is null
        || string.IsNullOrWhiteSpace(ship.Key)
        || string.IsNullOrWhiteSpace(ship.Type)
        || ship.Name is null
        || InvalidModules(ship.Modules);

    private static bool InvalidModules(IReadOnlyList<FiregroupModule> modules) =>
        modules is null || modules.Any(m => m is null || m.Name is null || m.Slot is null || m.Symbol is null);
}
