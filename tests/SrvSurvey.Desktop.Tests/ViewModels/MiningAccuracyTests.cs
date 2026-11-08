using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class MiningAccuracyTests
{
    private static readonly string[] RingTypes = ["Metallic", "MetalRich", "Icy", "Rocky", "Unknown"];
    private static readonly string[] ExpectedPlatinumSites = ["Single", "Many", "Common"];
    private static readonly string[] ExpectedNearestPlatinumSites = ["Common", "Single", "Many"];

    [Theory]
    [InlineData("Platinum", "Core", "Metallic", "MetalRich")]
    [InlineData("Platinum", "Laser Surface", "Metallic")]
    [InlineData("Painite", "Core", "Metallic", "MetalRich")]
    [InlineData("Bromellite", "Laser Surface", "Icy")]
    [InlineData("Low Temperature Diamonds", "Core", "Icy")]
    [InlineData("Monazite", "Laser Surface")]
    public async Task RingSearchMatchesTheMineralAndItsExtractionMethod(
        string mineral,
        string method,
        params string[] expected
    )
    {
        using var fixture = new Fixture(
            RingTypes.Select(type => Ring(type, mineral) with { RingType = type }).ToArray()
        );
        fixture.Model.MiningType = method;
        fixture.Model.RingMineralChips.Add(mineral);

        await fixture.Model.SearchRingsAsync();

        Assert.Equal(expected.Order(), fixture.Model.Rings.Select(ring => ring.Body).Order());
    }

    [Fact]
    public async Task GermanJournalHotspotsStaySearchableByCanonicalMineral()
    {
        var state = new MiningWorkspaceState(new MiningCommanderData { Rings = [Ring("Fixture A Ring", "Platin")] });
        foreach (
            string json in new[]
            {
                """{"timestamp":"2026-11-08T00:00:00Z","event":"Scan","ReserveLevel":"Pristine","Rings":[{"Name":"Fixture A Ring","RingClass":"eRingClass_Metallic"}]}""",
                """{"timestamp":"2026-11-08T00:01:00Z","event":"SAASignalsFound","BodyName":"Fixture A Ring","Signals":[{"Type":"Platinum","Type_Localised":"Platin","Count":1}]}""",
            }
        )
        {
            Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out string? error), error);
            state.Apply(entry!, false, "Mining accuracy fixture", "", "");
        }
        using var fixture = new Fixture(state.Data.Rings.ToArray());
        fixture.Model.MiningType = "Laser Surface";

        await fixture.Model.SearchRingsAsync();

        Assert.Equal("Fixture A Ring", Assert.Single(fixture.Model.Rings).Body);
        Assert.Equal(1, Assert.Single(state.Data.Rings).Hotspots["Platinum"]);
        Assert.Single(Assert.Single(state.Data.Rings).Hotspots);
    }

    [Fact]
    public async Task PreviouslySavedLocalizedHotspotsRemainSearchable()
    {
        using var fixture = new Fixture(Ring("Legacy A Ring", "Platin"));
        fixture.Model.MiningType = "Laser Surface";
        fixture.Model.RingMineralChips.Add("Platinum");

        await fixture.Model.SearchRingsAsync();

        Assert.Equal("Legacy A Ring", Assert.Single(fixture.Model.Rings).Body);
    }

    [Fact]
    public async Task DefaultPlatinumResultsIncludeSinglesAndPreferPristineThenNearbySites()
    {
        using var fixture = new Fixture(
            Ring("Single", "Platinum") with
            {
                ArrivalLs = 10,
            },
            Ring("Many", "Platinum") with
            {
                Hotspots = new() { ["Platinum"] = 5 },
                ArrivalLs = 100,
            },
            Ring("Common", "Platinum") with
            {
                Reserve = "Common",
                ArrivalLs = 5,
            },
            Ring("Wrong composition", "Platinum") with
            {
                RingType = "MetalRich",
            }
        );

        await fixture.Model.SearchPlatinumAsync();

        Assert.Equal(ExpectedPlatinumSites, fixture.Model.PlatinumSpots.Select(spot => spot.Body));
        fixture.Model.PlatinumMode = "All platinum";
        await fixture.Model.SearchPlatinumAsync();
        Assert.Equal(ExpectedNearestPlatinumSites, fixture.Model.PlatinumSpots.Select(spot => spot.Body));
        fixture.Model.MinimumHotspots = 2;
        await fixture.Model.SearchPlatinumAsync();
        Assert.Equal("Many", Assert.Single(fixture.Model.PlatinumSpots).Body);
    }

    [Fact]
    public async Task SavedResModeRestoresTheRenamedFilterAndStillRequiresResEvidence()
    {
        using var fixture = new Fixture(
            Ring("RES", "Platinum") with
            {
                ResourceExtractionSites = "High",
            },
            Ring("Unannotated", "Platinum")
        );
        fixture.Model.LoadOptions(fixture.Model.SaveOptions() with { PlatinumMode = "RES mapped" });

        Assert.Equal("RES sites", fixture.Model.PlatinumMode);
        Assert.Equal("RES sites", fixture.Model.SaveOptions().PlatinumMode);
        await fixture.Model.SearchPlatinumAsync();
        Assert.Equal("RES", Assert.Single(fixture.Model.PlatinumSpots).Body);
    }

    private static MiningRing Ring(string body, string mineral) =>
        new()
        {
            System = "Mining accuracy fixture",
            Body = body,
            RingType = "Metallic",
            Reserve = "Pristine",
            Hotspots = new() { [mineral] = 1 },
        };

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        public Fixture(params MiningRing[] rings)
        {
            Model = new(
                new MiningSearchClient(),
                new BookmarksViewModel(directory),
                _ => { },
                () => rings,
                new Resolver()
            )
            {
                Reference = "Mining accuracy fixture",
                Source = "Local",
                Radius = 1,
            };
        }

        public MiningSearchViewModel Model { get; }

        public void Dispose()
        {
            Model.Dispose();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private sealed class Resolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
    }
}
