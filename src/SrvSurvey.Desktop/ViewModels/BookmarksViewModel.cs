using System.Text.Json;
using System.Windows.Input;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class BookmarksViewModel : WorkspaceObservable
{
    private static readonly StringComparer FileSystemPathComparer =
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    private readonly BookmarkCatalog? catalog;
    private readonly Action<Guid> openSurfaceMiningMap;
    private readonly WorkspaceTableSorter sorter = new();
    private GalacticBookmark? selected;
    private GalacticBookmark? deleted;
    private string categoryFilter = "All", query = "", system = "", body = "", ring = "", category = BookmarkCategoryCatalog.Mining, notes = "", minerals = "", overlaps = "", res = "";
    private readonly HashSet<string> categoryAssignments = new(StringComparer.OrdinalIgnoreCase)
    {
        BookmarkCategoryCatalog.Mining,
    };
    private int rating;
    private string lastMined = "", hotspot = "", averageYield = "";
    private int surfaceSignal = 1;
    private string surfaceBodyType = "", surfaceMineralAmount = "High", surfaceDensity = "Low";
    private double surfaceArrivalDistanceLs;
    private string status = "Save systems and mining rings in your own categories.";
    public BookmarksViewModel(
        string directory,
        Action<Guid>? openSurfaceMiningMap = null)
    {
        this.openSurfaceMiningMap = openSurfaceMiningMap ?? (_ => { });
        try { catalog = new BookmarkCatalog(directory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { status = "Bookmarks could not be loaded: " + ex.Message; }
        NewCommand = new WorkspaceCommand(() =>
        {
            Selected = null;
            System = Body = Ring = Notes = Minerals = Overlaps = ResourceExtractionSites = LastMined = Hotspot = AverageYield = SurfaceBodyType = "";
            SetCategoryAssignments([BookmarkCategoryCatalog.Mining]);
            Rating = 0;
            SurfaceSignal = 1;
            SurfaceArrivalDistanceLs = 0;
            SurfaceMineralAmount = "High";
            SurfaceDensity = "Low";
        });
        SaveCommand = new WorkspaceCommand(() => Run(() =>
        {
            var surfaceMap = selected?.SurfaceMiningMap;
            if (surfaceMap is not null)
            {
                surfaceMap = surfaceMap with
                {
                    SystemName = System,
                    BodyName = Body,
                    BodyType = SurfaceBodyType,
                    ArrivalDistanceLs = SurfaceArrivalDistanceLs,
                    LocationSignal = SurfaceSignal,
                    MineralAmount = ParseSurfaceRating(SurfaceMineralAmount),
                    Density = ParseSurfaceRating(SurfaceDensity),
                    Notes = Notes,
                    UpdatedAt = DateTimeOffset.UtcNow,
                };
            }
            var saved = new GalacticBookmark
            {
                Id = selected?.Id ?? Guid.NewGuid(),
                System = System,
                Body = Body,
                Ring = Ring,
                Category = CategoryAssignments[0],
                CategoryAssignments = CategoryAssignments,
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
                Reserve = selected?.Reserve ?? "",
                SurfaceMiningMap = surfaceMap,
                Updated = DateTimeOffset.UtcNow,
            };
            catalog!.Save(saved);
            Selected = catalog.Items.Single(b => b.Id == saved.Id);
            Status = "Bookmark saved.";
        }));
        DeleteCommand = new WorkspaceCommand(() => Run(() => { if (selected is not null) { deleted = selected; catalog!.Delete(selected.Id); } Selected = null; Status = "Bookmark removed."; }));
        SortCommand = new WorkspaceParameterCommand(parameter =>
        {
            sorter.Toggle(parameter);
            Changed(nameof(Items));
            Changed(nameof(SortIndicators));
        });
        if (catalog is not null) catalog.Changed += OnCatalogChanged;
    }
    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand SortCommand { get; }
    public WorkspaceSortIndicators SortIndicators => new(sorter.Indicator);
    public IReadOnlyList<GalacticBookmark> All => catalog?.Items ?? [];
    public BookmarkCatalog? Catalog => catalog;
    public IReadOnlyList<GalacticBookmark> Items => sorter.Apply(
        catalog?.Filter(CategoryFilter, Query) ?? []);
    private IReadOnlyList<string>? categories;
    public IReadOnlyList<string> Categories => categories ??= ReadCategories();
    private string[] ReadCategories() => (catalog?.Categories ?? ["Mining"]).Prepend("All").ToArray();
    public IReadOnlyList<string> SelectedScreenshots => selected?.Screenshots ?? [];
    public string CategoryFilter { get => categoryFilter; set { if (Set(ref categoryFilter, value)) Changed(nameof(Items)); } }
    public string Query { get => query; set { if (Set(ref query, value)) Changed(nameof(Items)); } }
    public string System { get => system; set => Set(ref system, value); }
    public string Body { get => body; set => Set(ref body, value); }
    public string Ring { get => ring; set => Set(ref ring, value); }
    public string Category
    {
        get => category;
        set => SetCategoryAssignments([value]);
    }
    public IReadOnlyList<string> CategoryAssignments => BookmarkCategoryCatalog.Normalize(
        categoryAssignments,
        category);
    public string CategorySummary => string.Join(", ", CategoryAssignments);
    public bool CategoryMining { get => HasCategory(BookmarkCategoryCatalog.Mining); set => SetCategory(BookmarkCategoryCatalog.Mining, value); }
    public bool CategorySurfaceMining { get => HasCategory(BookmarkCategoryCatalog.SurfaceMining); set => SetCategory(BookmarkCategoryCatalog.SurfaceMining, value); }
    public bool CategoryLocation { get => HasCategory(BookmarkCategoryCatalog.Location); set => SetCategory(BookmarkCategoryCatalog.Location, value); }
    public bool CategoryPoi { get => HasCategory(BookmarkCategoryCatalog.Poi); set => SetCategory(BookmarkCategoryCatalog.Poi, value); }
    public bool CategoryOther { get => HasCategory(BookmarkCategoryCatalog.Other); set => SetCategory(BookmarkCategoryCatalog.Other, value); }
    public string Notes { get => notes; set => Set(ref notes, value); }
    public string Minerals { get => minerals; set => Set(ref minerals, value); }
    public string Overlaps { get => overlaps; set => Set(ref overlaps, value); }
    public string ResourceExtractionSites { get => res; set => Set(ref res, value); }
    public int Rating { get => rating; set => Set(ref rating, value); }
    public string LastMined { get => lastMined; set => Set(ref lastMined, value); }
    public string Hotspot { get => hotspot; set => Set(ref hotspot, value); }
    public string AverageYield { get => averageYield; set => Set(ref averageYield, value); }
    public int SurfaceSignal { get => surfaceSignal; set => Set(ref surfaceSignal, Math.Max(1, value)); }
    public string SurfaceBodyType { get => surfaceBodyType; set => Set(ref surfaceBodyType, value); }
    public double SurfaceArrivalDistanceLs { get => surfaceArrivalDistanceLs; set => Set(ref surfaceArrivalDistanceLs, Math.Max(0, value)); }
    public string SurfaceMineralAmount { get => surfaceMineralAmount; set => Set(ref surfaceMineralAmount, value); }
    public string SurfaceDensity { get => surfaceDensity; set => Set(ref surfaceDensity, value); }
    public IReadOnlyList<string> SurfaceRatingOptions { get; } = ["Low", "High"];
    public bool IsSurfaceMiningMap => Selected?.SurfaceMiningMap is not null;
    public string Status { get => status; set => Set(ref status, value); }
    public GalacticBookmark? Selected
    {
        get => selected;
        set
        {
            if (!Set(ref selected, value)) return;
            Changed(nameof(SelectedScreenshots));
            Changed(nameof(IsSurfaceMiningMap));
            if (value is null) return;
            System = value.System; Body = value.Body; Ring = value.DisplayRing; SetCategoryAssignments(value.EffectiveCategoryAssignments); Notes = value.Notes;
            Minerals = value.Minerals; Rating = value.Rating; LastMined = value.LastMined; Hotspot = value.Hotspot; AverageYield = value.AverageYield; Overlaps = value.Overlaps; ResourceExtractionSites = value.ResourceExtractionSites;
            if (value.SurfaceMiningMap is { } map)
            {
                SurfaceSignal = map.LocationSignal;
                SurfaceBodyType = map.BodyType;
                SurfaceArrivalDistanceLs = map.ArrivalDistanceLs;
                SurfaceMineralAmount = map.MineralAmount.ToString();
                SurfaceDensity = map.Density.ToString();
            }
        }
    }
    public bool SelectBookmark(Guid id)
    {
        var bookmark = catalog?.Items.FirstOrDefault(candidate => candidate.Id == id);
        if (bookmark is null) return false;
        Selected = bookmark;
        Status = "Surface mining bookmark opened for editing.";
        return true;
    }
    public bool OpenSelectedSurfaceMiningMap()
    {
        return Selected is { } bookmark
            && OpenSurfaceMiningMap(bookmark.Id);
    }

    public bool OpenSurfaceMiningMap(Guid bookmarkId)
    {
        var bookmark = catalog?.Items.FirstOrDefault(candidate =>
            candidate.Id == bookmarkId);
        if (bookmark is not { IsSurfaceMiningMap: true })
        {
            return false;
        }

        openSurfaceMiningMap(bookmark.Id);
        return true;
    }
    public void UndoDelete() => Run(() => { if (deleted is { } bookmark) { catalog!.Save(bookmark); Selected = bookmark; deleted = null; Status = "Bookmark restored."; } });
    public void RemoveScreenshot(string path) => Run(() => { if (Selected is { } bookmark) { var updated = bookmark with { Screenshots = bookmark.Screenshots.Where(p => p != path).ToList() }; catalog!.Save(updated); Selected = updated; } });
    public void AttachScreenshots(IEnumerable<string> paths) => Run(() => { if (Selected is { } bookmark) { var updated = bookmark with { Screenshots = bookmark.Screenshots.Concat(paths).Distinct(FileSystemPathComparer).ToList() }; catalog!.Save(updated); Selected = updated; Status = $"{updated.Screenshots.Count} screenshots attached."; } });
    public void AddMiningLocation(GalacticBookmark bookmark) => Run(() => { catalog!.Save(bookmark); Status = "Mining location bookmarked."; });
    public string Export() => catalog?.Export() ?? throw new InvalidOperationException(Status);
    public bool Restore(string json)
    {
        if (catalog is null) return false;
        try
        {
            catalog.Restore(json);
            Selected = null;
            Status = "Shared bookmarks restored; previous catalog retained in bookmarks.json.before-restore.";
            categories = null;
            Changed(nameof(Items));
            Changed(nameof(Categories));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Status = ex.Message;
            return false;
        }
    }
    public void Import(string json) => Run(() => { catalog!.Import(json); Status = "Bookmarks imported; existing locations retained."; });
    private void Run(Action action)
    {
        if (catalog is null) return;
        try { action(); categories = null; Changed(nameof(Items)); Changed(nameof(Categories)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Status = ex.Message; }
    }

    private void OnCatalogChanged(object? sender, EventArgs eventArgs)
    {
        categories = null;
        var selectedId = selected?.Id;
        if (selectedId is { } id)
        {
            Selected = catalog?.Items.FirstOrDefault(candidate => candidate.Id == id);
        }
        Changed(nameof(All));
        Changed(nameof(Items));
        Changed(nameof(Categories));
        if (selectedId is null)
        {
            Changed(nameof(Selected));
            Changed(nameof(SelectedScreenshots));
            Changed(nameof(IsSurfaceMiningMap));
        }
    }

    private static MineMapRating ParseSurfaceRating(string value) =>
        value.Equals("Low", StringComparison.OrdinalIgnoreCase)
            ? MineMapRating.Low
            : MineMapRating.High;

    private bool HasCategory(string value) => categoryAssignments.Contains(value);

    private void SetCategory(string value, bool enabled)
    {
        if (enabled)
        {
            categoryAssignments.Add(value);
        }
        else
        {
            categoryAssignments.Remove(value);
        }

        if (categoryAssignments.Count == 0)
        {
            categoryAssignments.Add(BookmarkCategoryCatalog.Other);
        }

        category = CategoryAssignments[0];
        RaiseCategoryAssignments();
    }

    private void SetCategoryAssignments(IEnumerable<string> values)
    {
        categoryAssignments.Clear();
        foreach (var value in BookmarkCategoryCatalog.Normalize(values, category))
        {
            categoryAssignments.Add(value);
        }

        category = CategoryAssignments[0];
        Changed(nameof(Category));
        RaiseCategoryAssignments();
    }

    private void RaiseCategoryAssignments()
    {
        Changed(nameof(CategoryAssignments));
        Changed(nameof(CategorySummary));
        Changed(nameof(CategoryMining));
        Changed(nameof(CategorySurfaceMining));
        Changed(nameof(CategoryLocation));
        Changed(nameof(CategoryPoi));
        Changed(nameof(CategoryOther));
    }
}
