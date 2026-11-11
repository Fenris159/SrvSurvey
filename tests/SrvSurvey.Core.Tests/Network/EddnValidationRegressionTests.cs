using Newtonsoft.Json.Linq;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Tests.Network;

[Trait("Category", "JournalUploadScenario")]
public sealed class EddnValidationRegressionTests
{
    [Theory]
    [InlineData("Location")]
    [InlineData("Docked")]
    [InlineData("DockingGranted")]
    [InlineData("DockingDenied")]
    public void TemporaryOperationsStationsAreExcludedFromGalaxyPublication(string eventName)
    {
        JObject raw = Station(eventName, 127000512);
        Assert.False(EddnMessageSanitizer.tryBuildJournal(raw, Context(), out _, out string reason));
        Assert.Contains("temporary", reason);
    }

    [Theory]
    [InlineData(126999999, true)]
    [InlineData(127000000, false)]
    [InlineData(127999999, false)]
    [InlineData(128000000, true)]
    public void TemporaryMarketRangeDoesNotExcludePersistentStations(long marketId, bool expected)
    {
        Assert.Equal(
            expected,
            EddnMessageSanitizer.tryBuildJournal(Station("Docked", marketId), Context(), out _, out _)
        );
    }

    [Fact]
    public void CodexUsesKnownStatusNameWhenJournalBodyContextIsUnavailable()
    {
        var context = new EddnMessageContext(Location(), true, true, "Test A 2");
        JObject raw = Codex();
        Assert.True(
            EddnMessageSanitizer.tryBuildJournal(raw, context, out EddnPreparedMessage? prepared, out string reason),
            reason
        );
        Assert.Equal("Test A 2", prepared!.message.Value<string>("BodyName"));
        Assert.Null(prepared.message["BodyID"]);
    }

    [Theory]
    [InlineData("Market")]
    [InlineData("Outfitting")]
    [InlineData("Shipyard")]
    [InlineData("FCMaterials")]
    public void TemporaryStationCompanionFilesAreNotPublished(string eventName)
    {
        Assert.False(
            EddnMessageSanitizer.tryBuildCompanion(Station(eventName, 127000512), Context(), out _, out string reason)
        );
        Assert.Contains("temporary", reason);
    }

    [Fact]
    public void CodexKeepsExplicitBodyIdInsteadOfCombiningItWithAnotherBodyName()
    {
        JObject raw = Codex();
        raw["BodyID"] = 9;
        var context = new EddnMessageContext(Location(), true, true, "Test A 1", "Test A 1", 4);
        Assert.True(
            EddnMessageSanitizer.tryBuildJournal(raw, context, out EddnPreparedMessage? prepared, out string reason),
            reason
        );
        Assert.Equal(9, prepared!.message.Value<int>("BodyID"));
        Assert.Null(prepared.message["BodyName"]);
    }

    [Theory]
    [InlineData("Log")]
    [InlineData("Sample")]
    public void OrganicScansUseTheirLiveSchemaAndMatchingSurfaceCoordinates(string scanType)
    {
        JObject raw = Organic(scanType);
        var status = new EliteStatus
        {
            Timestamp = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero),
            BodyName = "Test A 1",
            Latitude = 12.5,
            Longitude = -43.25,
            Flags = StatusFlags.HasLatLong,
        };
        EddnMessageContext context = Context() with
        {
            statusBodyName = "Test A 1",
            trackedBodyName = "Test A 1",
            trackedBodyId = 4,
            status = status,
        };
        Assert.True(
            EddnMessageSanitizer.tryBuildJournal(raw, context, out EddnPreparedMessage? prepared, out string reason),
            reason
        );
        Assert.Equal("https://eddn.edcd.io/schemas/scanorganic/1", prepared!.schemaRef);
        Assert.Equal(4, prepared.message.Value<int>("BodyID"));
        Assert.Null(prepared.message["Body"]);
        Assert.Equal("Test A 1", prepared.message.Value<string>("BodyName"));
        Assert.Equal(12.5, prepared.message.Value<double>("Latitude"));
        Assert.Equal(-43.25, prepared.message.Value<double>("Longitude"));
        Assert.Equal("$Genus_Name;", prepared.message.Value<string>("Genus"));
        Assert.Null(prepared.message["Genus_Localised"]);
    }

    [Theory]
    [InlineData("Analyse", 123)]
    [InlineData("Log", 999)]
    public void OrganicAnalysisAndMismatchedSystemsAreNotPublished(string scanType, long systemAddress)
    {
        JObject raw = Organic(scanType);
        raw["SystemAddress"] = systemAddress;
        Assert.False(EddnMessageSanitizer.tryBuildJournal(raw, Context(), out _, out _));
    }

    [Fact]
    public void OrganicScansNeverBorrowCoordinatesFromAnotherBody()
    {
        EddnMessageContext context = Context() with
        {
            statusBodyName = "Test A 2",
            trackedBodyName = "Test A 2",
            trackedBodyId = 9,
            status = new EliteStatus
            {
                BodyName = "Test A 2",
                Latitude = 1,
                Longitude = 2,
                Flags = StatusFlags.HasLatLong,
            },
        };
        Assert.True(
            EddnMessageSanitizer.tryBuildJournal(
                Organic("Log"),
                context,
                out EddnPreparedMessage? prepared,
                out string reason
            ),
            reason
        );
        Assert.Equal(4, prepared!.message.Value<int>("BodyID"));
        Assert.Null(prepared.message["BodyName"]);
        Assert.Null(prepared.message["Latitude"]);
        Assert.Null(prepared.message["Longitude"]);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(20, 20)]
    public void OutfittingPublishesModuleIdentityAndBothPrices(int? mercCoins, int expectedCoins)
    {
        JObject raw = Station("Outfitting", 128000123);
        var module = new JObject
        {
            ["id"] = 128049271,
            ["Name"] = "Int_CargoRack_Size2_Class1",
            ["BuyPrice"] = 3250,
        };
        if (mercCoins.HasValue)
        {
            module["BuyMercCoinsPrice"] = mercCoins.Value;
        }
        raw["Items"] = new JArray(module);
        Assert.True(
            EddnMessageSanitizer.tryBuildCompanion(
                raw,
                Context(),
                out EddnPreparedMessage? prepared,
                out string reason
            ),
            reason
        );
        Assert.Equal("https://eddn.edcd.io/schemas/outfitting/3", prepared!.schemaRef);
        JObject item = Assert.IsType<JObject>(Assert.Single(prepared.message["modules"]!));
        Assert.Equal(128049271, item.Value<int>("id"));
        Assert.Equal("Int_CargoRack_Size2_Class1", item.Value<string>("Name"));
        Assert.Equal(3250, item.Value<int>("BuyPrice"));
        Assert.Equal(expectedCoins, item.Value<int>("BuyMercCoinsPrice"));
    }

    private static EddnLocationContext Location() => new("Test A", 123, [1.5, -2, 3]);

    private static EddnMessageContext Context() => new(Location(), true, true);

    private static JObject Station(string eventName, long marketId) =>
        new()
        {
            ["timestamp"] = "2026-11-10T12:00:00Z",
            ["event"] = eventName,
            ["MarketID"] = marketId,
            ["StationName"] = "Test Port",
            ["StationType"] = "SurfaceStation",
            ["StarSystem"] = "Test A",
            ["SystemAddress"] = 123,
            ["StarPos"] = new JArray(1.5, -2, 3),
            ["Reason"] = "NoSpace",
        };

    private static JObject Codex() =>
        JObject.Parse(
            """{"timestamp":"2026-11-10T12:00:00Z","event":"CodexEntry","System":"Test A","SystemAddress":123,"EntryID":10,"Name":"$Codex_Ent_Bacterial_01_Name;","Region":"$Codex_RegionName_18;","Category":"$Codex_Category_Biology;","SubCategory":"$Codex_SubCategory_Organic_Structures;"}"""
        );

    private static JObject Organic(string scanType) =>
        new()
        {
            ["timestamp"] = "2026-11-10T12:00:00Z",
            ["event"] = "ScanOrganic",
            ["ScanType"] = scanType,
            ["Genus"] = "$Genus_Name;",
            ["Genus_Localised"] = "Translated genus",
            ["Species"] = "$Species_Name;",
            ["Variant"] = "$Variant_Name;",
            ["SystemAddress"] = 123,
            ["Body"] = 4,
        };
}
