using System.Text.Json;
using System.Windows.Input;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class BookmarksViewModel : WorkspaceObservable
{
    private readonly BookmarkCatalog? catalog;
    private GalacticBookmark? selected;
    private GalacticBookmark? deleted;
    private string categoryFilter = "All", query = "", system = "", body = "", category = "Mining", notes = "", minerals = "", overlaps = "", res = "";
    private int rating;
    private string lastMined = "", hotspot = "", averageYield = "";
    private string status = "Save systems and mining rings in your own categories.";
    public BookmarksViewModel(string directory)
    {
        try { catalog = new BookmarkCatalog(directory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { status = "Bookmarks could not be loaded: " + ex.Message; }
        NewCommand = new WorkspaceCommand(() => { Selected = null; System = Body = Notes = Minerals = Overlaps = ResourceExtractionSites = LastMined = Hotspot = AverageYield = ""; Rating = 0; });
        SaveCommand = new WorkspaceCommand(() => Run(() =>
        {
            var saved = new GalacticBookmark
            {
                Id = selected?.Id ?? Guid.NewGuid(),
                System = System,
                Body = Body,
                Category = Category,
                Notes = Notes,
                Minerals = Minerals,
                Rating = Rating,
                Overlaps = Overlaps,
                ResourceExtractionSites = ResourceExtractionSites,
                LastMined = LastMined,
                Hotspot = Hotspot,
                AverageYield = AverageYield,
                Screenshots = selected?.Screenshots ?? [],
                Position = selected?.Position,
                RingType = selected?.RingType ?? "",
                Reserve = selected?.Reserve ?? ""
            };
            catalog!.Save(saved);
            Selected = catalog.Items.Single(b => b.Id == saved.Id);
            Status = "Bookmark saved.";
        }));
        DeleteCommand = new WorkspaceCommand(() => Run(() => { if (selected is not null) { deleted = selected; catalog!.Delete(selected.Id); } Selected = null; Status = "Bookmark removed."; }));
    }
    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public IReadOnlyList<GalacticBookmark> All => catalog?.Items ?? [];
    public IReadOnlyList<GalacticBookmark> Items => catalog?.Filter(CategoryFilter, Query) ?? [];
    public IReadOnlyList<string> Categories => new[] { "All" }.Concat(catalog?.Categories ?? ["Mining"]).ToArray();
    public string CategoryFilter { get => categoryFilter; set { if (Set(ref categoryFilter, value)) Changed(nameof(Items)); } }
    public string Query { get => query; set { if (Set(ref query, value)) Changed(nameof(Items)); } }
    public string System { get => system; set => Set(ref system, value); }
    public string Body { get => body; set => Set(ref body, value); }
    public string Category { get => category; set => Set(ref category, value); }
    public string Notes { get => notes; set => Set(ref notes, value); }
    public string Minerals { get => minerals; set => Set(ref minerals, value); }
    public string Overlaps { get => overlaps; set => Set(ref overlaps, value); }
    public string ResourceExtractionSites { get => res; set => Set(ref res, value); }
    public int Rating { get => rating; set => Set(ref rating, value); }
    public string LastMined { get => lastMined; set => Set(ref lastMined, value); }
    public string Hotspot { get => hotspot; set => Set(ref hotspot, value); }
    public string AverageYield { get => averageYield; set => Set(ref averageYield, value); }
    public string Status { get => status; set => Set(ref status, value); }
    public GalacticBookmark? Selected
    {
        get => selected;
        set
        {
            if (!Set(ref selected, value) || value is null) return;
            System = value.System; Body = value.Body; Category = value.Category; Notes = value.Notes;
            Minerals = value.Minerals; Rating = value.Rating; LastMined = value.LastMined; Hotspot = value.Hotspot; AverageYield = value.AverageYield; Overlaps = value.Overlaps; ResourceExtractionSites = value.ResourceExtractionSites;
        }
    }
    public void UndoDelete() => Run(() => { if (deleted is { } bookmark) { catalog!.Save(bookmark); Selected = bookmark; deleted = null; Status = "Bookmark restored."; } });
    public void RemoveScreenshot(string path) => Run(() => { if (Selected is { } bookmark) { var updated = bookmark with { Screenshots = bookmark.Screenshots.Where(p => p != path).ToList() }; catalog!.Save(updated); Selected = updated; } });
    public void AttachScreenshots(IEnumerable<string> paths) => Run(() => { if (Selected is { } bookmark) { var updated = bookmark with { Screenshots = bookmark.Screenshots.Concat(paths).Distinct(StringComparer.OrdinalIgnoreCase).ToList() }; catalog!.Save(updated); Selected = updated; Status = $"{updated.Screenshots.Count} screenshots attached."; } });
    public void AddMiningLocation(GalacticBookmark bookmark) => Run(() => { catalog!.Save(bookmark); Status = "Mining location bookmarked."; });
    public string Export() => catalog?.Export() ?? throw new InvalidOperationException(Status);
    public void Restore(string json) => Run(() => { catalog!.Restore(json); Selected = null; Status = "Shared bookmarks restored; previous catalog retained in bookmarks.json.before-restore."; });
    public void Import(string json) => Run(() => { catalog!.Import(json); Status = "Bookmarks imported; existing locations retained."; });
    private void Run(Action action)
    {
        if (catalog is null) return;
        try { action(); Changed(nameof(Items)); Changed(nameof(Categories)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Status = ex.Message; }
    }
}
