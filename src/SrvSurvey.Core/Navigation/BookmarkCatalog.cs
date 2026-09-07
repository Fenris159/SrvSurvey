using System.Text.Json;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Navigation;

public sealed record GalacticBookmark
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string System { get; init; } = "";
    public string Body { get; init; } = "";
    public GalacticCoordinate? Position { get; init; }
    public string Category { get; init; } = "Mining";
    public string Notes { get; init; } = "";
    public string LastMined { get; init; } = "";
    public string Hotspot { get; init; } = "";
    public string AverageYield { get; init; } = "";
    public List<string> Screenshots { get; init; } = [];
    public int Rating { get; init; }
    public string Minerals { get; init; } = "";
    public string RingType { get; init; } = "";
    public string Reserve { get; init; } = "";
    public string Overlaps { get; init; } = "";
    public string ResourceExtractionSites { get; init; } = "";
    public DateTimeOffset Updated { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>One location catalog shared by navigation and mining; importing never silently replaces an existing bookmark.</summary>
public sealed class BookmarkCatalog
{
    private readonly string path;
    private List<GalacticBookmark> items;
    public BookmarkCatalog(string directory)
    {
        path = Path.Combine(directory, "bookmarks.json");
        items = File.Exists(path) ? Parse(File.ReadAllText(path)) : [];
    }
    public IReadOnlyList<GalacticBookmark> Items => items;
    private IReadOnlyList<string>? categories;
    public IReadOnlyList<string> Categories => categories ??= ReadCategories();
    private string[] ReadCategories() => items.Select(b => b.Category).Append("Mining").Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
    public IReadOnlyList<GalacticBookmark> Filter(string? category, string? query) => items
        .Where(b => (string.IsNullOrEmpty(category) || category == "All" || b.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(query) || $"{b.System} {b.Body} {b.Notes} {b.Minerals}".Contains(query, StringComparison.OrdinalIgnoreCase)))
        .OrderBy(b => b.System).ThenBy(b => b.Body).ToArray();

    public void Save(GalacticBookmark bookmark)
    {
        Validate(bookmark);
        var next = items.Where(b => b.Id != bookmark.Id).Append(bookmark with { System = bookmark.System.Trim(), Category = bookmark.Category.Trim() }).ToList();
        Persist(next);
    }
    public void Delete(Guid id) => Persist(items.Where(b => b.Id != id).ToList());
    public string Export() => JsonSerializer.Serialize(items, JsonOptions);
    public static void ValidateImport(string json) => Parse(json);
    public void Restore(string json)
    {
        var restored = Parse(json);
        if (File.Exists(path)) File.Copy(path, path + ".before-restore", true);
        Persist(restored);
    }
    public void Import(string json)
    {
        var incoming = Parse(json);
        var next = items.ToList();
        foreach (var bookmark in incoming.Where(bookmark => !next.Any(b => b.Id == bookmark.Id || (b.System.Equals(bookmark.System, StringComparison.OrdinalIgnoreCase)
                && b.Body.Equals(bookmark.Body, StringComparison.OrdinalIgnoreCase) && b.Category.Equals(bookmark.Category, StringComparison.OrdinalIgnoreCase)))))
            next.Add(bookmark);
        Persist(next);
    }

    private void Persist(List<GalacticBookmark> next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(next, JsonOptions));
            File.Move(temporary, path, true);
            items = next;
            categories = null;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static List<GalacticBookmark> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Expected a bookmark list.");
        var parsed = new List<GalacticBookmark>();
        foreach (var row in document.RootElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) throw new JsonException("Expected a bookmark object.");
            if (row.TryGetProperty("system", out _))
            {
                parsed.Add(ReadLegacyBookmark(row));
            }
            else parsed.Add(row.Deserialize<GalacticBookmark>() ?? throw new JsonException("Empty bookmark."));
        }
        foreach (var bookmark in parsed) Validate(bookmark);
        return parsed;
    }
    private static GalacticBookmark ReadLegacyBookmark(JsonElement row)
    {
        string Text(string key) => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        var rating = row.TryGetProperty("rating", out var r) && int.TryParse(r.ToString(), out var n) ? n : 0;
        return new GalacticBookmark
        {
            System = Text("system"),
            Body = Text("body"),
            Category = "Mining",
            Minerals = Text("materials"),
            Notes = Text("notes"),
            Rating = Math.Clamp(rating, 0, 5),
            Hotspot = Text("hotspot"),
            AverageYield = Text("avg_yield"),
            LastMined = Text("last_mined"),
            Overlaps = $"{Text("target_material")} {Text("overlap_type")}".Trim(),
            ResourceExtractionSites = $"{Text("res_material")} {Text("res_site")}".Trim()
        };
    }
    private static void Validate(GalacticBookmark bookmark)
    {
        if (bookmark is null || string.IsNullOrWhiteSpace(bookmark.System) || string.IsNullOrWhiteSpace(bookmark.Category)
            || bookmark.Rating is < 0 or > 5 || bookmark.Body is null || bookmark.Notes is null || bookmark.Minerals is null || bookmark.Overlaps is null || bookmark.ResourceExtractionSites is null || bookmark.Screenshots is null || bookmark.Screenshots.Any(string.IsNullOrWhiteSpace) || bookmark.LastMined is null || bookmark.Hotspot is null || bookmark.AverageYield is null)
            throw new JsonException("Each bookmark needs a system, category and rating between 0 and 5.");
    }
}
