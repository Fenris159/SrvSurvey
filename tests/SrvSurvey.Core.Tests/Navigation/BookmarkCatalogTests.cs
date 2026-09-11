using System.Text.Json;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Navigation;

public sealed class BookmarkCatalogTests
{
    [Fact]
    public void DisplayBodyOmitsRepeatedSystemPrefix()
    {
        var bookmark = new GalacticBookmark { System = "LTT 4428", Body = "LTT 4428 E 5 a" };

        Assert.Equal("E 5 a", bookmark.DisplayBody);
        Assert.Equal("LTT 4428 E 5 a", bookmark.CombinedBodyAndRing);
    }

    [Fact]
    public void SurfaceMiningCoordinatesRoundTripThroughSharedBookmarkJson()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var center = new SurfaceCoordinate(12.345, -67.89);
            var markerLocation = new SurfaceCoordinate(12.355, -67.88);
            var id = Guid.NewGuid();
            new BookmarkCatalog(directory).Save(
                new GalacticBookmark
                {
                    Id = id,
                    System = "Wille",
                    Body = "Wille 2 d",
                    CategoryAssignments = [BookmarkCategoryCatalog.SurfaceMining],
                    SurfaceMiningMap = new MineMapSurvey
                    {
                        Id = id,
                        FrontierId = "F123",
                        SystemName = "Wille",
                        SystemAddress = 42,
                        SystemPosition = new GalacticCoordinate(1, 2, 3),
                        BodyId = 2,
                        BodyName = "Wille 2 d",
                        BodyType = "Rocky body",
                        LocationSignal = 4,
                        PlanetRadiusMeters = 855_573,
                        Center = center,
                        Markers = [new MineMapMarker { Material = "Ruby", Location = markerLocation }],
                    },
                }
            );

            var restored = Assert.Single(new BookmarkCatalog(directory).Items).SurfaceMiningMap!;
            Assert.Equal(center, restored.Center);
            Assert.Equal(markerLocation, Assert.Single(restored.Markers).Location);
            var json = File.ReadAllText(Path.Combine(directory, "bookmarks.json"));
            Assert.Contains("\"MineralAmount\": \"Low\"", json);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void ImportRetainsDistinctSurfaceSignalsOnTheSameBody()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var catalog = new BookmarkCatalog(directory);
            var first = SurfaceBookmark(Guid.NewGuid(), 4, new SurfaceCoordinate(1, 2));
            var second = SurfaceBookmark(Guid.NewGuid(), 5, new SurfaceCoordinate(1.01, 2.01));
            catalog.Save(first);

            catalog.Import(JsonSerializer.Serialize(new[] { second }));

            Assert.Equal(2, catalog.Items.Count);
            Assert.Contains(catalog.Items, item => item.SurfaceMiningMap?.LocationSignal == 4);
            Assert.Contains(catalog.Items, item => item.SurfaceMiningMap?.LocationSignal == 5);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void RejectsInvalidNestedSurfaceMapWithoutChangingCatalog()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var catalog = new BookmarkCatalog(directory);
            catalog.Save(new GalacticBookmark { System = "Sol" });
            var invalid = SurfaceBookmark(Guid.NewGuid(), 4, new SurfaceCoordinate(1, 2)) with
            {
                SurfaceMiningMap = SurfaceBookmark(Guid.NewGuid(), 4, new SurfaceCoordinate(1, 2)).SurfaceMiningMap,
            };

            Assert.Throws<JsonException>(() => catalog.Import(JsonSerializer.Serialize(new[] { invalid })));
            Assert.Single(catalog.Items);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void ParseReportsInvalidSurfaceCoordinatesAsJsonErrors()
    {
        var bookmark = SurfaceBookmark(Guid.NewGuid(), 4, new SurfaceCoordinate(1, 2));
        var json = JsonSerializer
            .Serialize(new[] { bookmark })
            .Replace("\"Latitude\":1", "\"Latitude\":91", StringComparison.Ordinal);

        var exception = Assert.Throws<JsonException>(() => BookmarkCatalog.Parse(json));

        Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
    }

    [Fact]
    public void ValidationMessageDescribesCurrentRequirements()
    {
        var exception = Assert.Throws<JsonException>(() => BookmarkCatalog.Parse("[{\"Rating\":6}]"));

        Assert.Contains("system and rating between 0 and 5", exception.Message);
        Assert.DoesNotContain("category", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RestoreRecoversBackedUpEditsAndRetainsPreviousCatalogOnDisk()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var catalog = new BookmarkCatalog(directory);
            var bookmark = new GalacticBookmark { System = "Sol", Notes = "Original" };
            catalog.Save(bookmark);
            var backup = catalog.Export();
            catalog.Save(bookmark with { Notes = "Edited" });
            catalog.Restore(backup);
            Assert.Equal("Original", Assert.Single(catalog.Items).Notes);
            Assert.Contains("Edited", File.ReadAllText(Path.Combine(directory, "bookmarks.json.before-restore")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void ImportsReferenceBookmarksAndRejectsNonObjectsWithoutChangingCatalog()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var catalog = new BookmarkCatalog(directory);
            catalog.Import(
                """[{"system":"Sol","body":"Earth A Ring","materials":"Platinum","rating":"4","overlap_type":"2x","notes":"Test"}]"""
            );
            Assert.Equal("2x", Assert.Single(catalog.Items).Overlaps);
            Assert.ThrowsAny<System.Text.Json.JsonException>(() => catalog.Import("[42]"));
            Assert.Single(catalog.Items);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void CategoriesSurviveRestartAndImportMergesWithoutDuplicatingLocations()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var catalog = new BookmarkCatalog(directory);
            catalog.Save(
                new GalacticBookmark
                {
                    System = "Sol",
                    Body = "Earth A Ring",
                    Category = "Mining",
                    Notes = "Test",
                    Rating = 4,
                }
            );
            catalog.Save(new GalacticBookmark { System = "Achenar", Category = "Location" });
            var backup = catalog.Export();
            var restored = new BookmarkCatalog(directory);
            restored.Import(backup);
            Assert.Equal(2, restored.Items.Count);
            Assert.Single(restored.Filter("Mining", "earth"));
            Assert.Contains("Location", restored.Categories);
            Assert.Equal(4, restored.Filter("Mining", "")[0].Rating);
            Assert.ThrowsAny<System.Text.Json.JsonException>(() => restored.Import("not json"));
            Assert.Equal(2, new BookmarkCatalog(directory).Items.Count);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void OneBookmarkCanAppearInEveryAssignedFixedCategory()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var catalog = new BookmarkCatalog(directory);
            catalog.Save(new GalacticBookmark { System = "LTT 4428", CategoryAssignments = ["Mining", "POI"] });

            Assert.Single(catalog.Filter("Mining", string.Empty));
            Assert.Single(catalog.Filter("POI", string.Empty));
            Assert.Empty(catalog.Filter("Location", string.Empty));
            var restored = Assert.Single(new BookmarkCatalog(directory).Items);
            Assert.Equal(["Mining", "POI"], restored.EffectiveCategoryAssignments);
            Assert.Equal(BookmarkCategoryCatalog.All, catalog.Categories);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void BodyAndRingPersistSeparatelyWhileLegacyCombinedNamesStillDisplayCorrectly()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var catalog = new BookmarkCatalog(directory);
            catalog.Save(
                new GalacticBookmark
                {
                    System = "Delkar",
                    Body = "Delkar 7",
                    Ring = "A Ring",
                }
            );

            var restored = Assert.Single(new BookmarkCatalog(directory).Items);
            Assert.Equal("7", restored.DisplayBody);
            Assert.Equal("A Ring", restored.DisplayRing);

            var legacy = new GalacticBookmark { Body = "Delkar 7 B Ring" };
            Assert.Equal("Delkar 7", legacy.DisplayBody);
            Assert.Equal("B Ring", legacy.DisplayRing);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static GalacticBookmark SurfaceBookmark(Guid id, int signal, SurfaceCoordinate center)
    {
        var map = new MineMapSurvey
        {
            Id = id,
            FrontierId = "F123",
            SystemName = "Wille",
            SystemAddress = 42,
            SystemPosition = new GalacticCoordinate(1, 2, 3),
            BodyId = 2,
            BodyName = "Wille 2 d",
            BodyType = "Rocky body",
            LocationSignal = signal,
            PlanetRadiusMeters = 855_573,
            Center = center,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        return new GalacticBookmark
        {
            Id = id,
            System = map.SystemName,
            Body = map.BodyName,
            Position = map.SystemPosition,
            CategoryAssignments = [BookmarkCategoryCatalog.SurfaceMining],
            SurfaceMiningMap = map,
        };
    }
}
