using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Ships;

namespace SrvSurvey.Core.Tests.Ships;

public sealed class ShipLoadoutTests
{
    private const string Loadout = """
        {"event":"Loadout","Ship":"LakonMiner","ShipID":12,"ShipName":"Miner","ShipIdent":"SRV-12","HullValue":123,
        "Modules":[
          {"Slot":"SmallMiningHardpoint1","Item":"Hpt_MiningLaser_Fixed_Small","On":false,"Priority":4,
           "Engineering":{"BlueprintName":"Weapon_LongRangeMiningLaser","Level":3,"Quality":0.8,"ExperimentalEffect":"special_incendiary_rounds",
             "Modifiers":[{"Label":"Range","Value":1500,"OriginalValue":500}]},"AmmoInClip":3},
          {"Slot":"FighterBay01","Item":"Int_MkIILargeBuggyBay_Size4_Class3","On":true,"Priority":0},
          {"Slot":"Slot01_Size6","Item":"Int_CargoRack_Size6_Class1","Engineering":{"BlueprintName":"CargoRack_Extended","Level":1}},
          {"Slot":"Radar","Item":"Int_Sensors_Size3_Class5"},
          {"Slot":"Slot02_Size6","Item":"Future_Module","CustomModuleField":"retained"}],
        "CustomShipField":{"data":"retained"}}
        """;

    /// <summary>
    /// Checks that SLEF preserves the entire loadout including internals unknown modules and original engineering.
    /// </summary>
    [Fact]
    public void SlefPreservesTheEntireLoadoutIncludingInternalsUnknownModulesAndOriginalEngineering()
    {
        ShipLoadout loadout;
        using (var source = JsonDocument.Parse(Loadout))
        {
            loadout = ShipLoadout.Parse(source.RootElement)!;
        }
        using var output = JsonDocument.Parse(loadout.ExportSlef("test-version"));
        JsonElement entry = output.RootElement[0];
        Assert.Equal("SrvSurvey", entry.GetProperty("header").GetProperty("appName").GetString());
        Assert.Equal("test-version", entry.GetProperty("header").GetProperty("appVersion").GetString());
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(Loadout), JsonNode.Parse(entry.GetProperty("data").GetRawText()))
        );
        Assert.Equal(12, loadout.ShipId);
        Assert.Equal("LakonMiner", loadout.ShipType);
        Assert.Equal("Miner", loadout.Name);
        Assert.Equal(5, loadout.ModuleCount);
        Assert.Null(loadout.MaximumJumpRange);
    }

    /// <summary>Reads only positive numeric journal ranges without guessing for missing or malformed data.</summary>
    [Theory]
    [InlineData("73.456789", "73.456789")]
    [InlineData("8.25", "8.25")]
    [InlineData("105.5", "105.5")]
    [InlineData("0", null)]
    [InlineData("-1", null)]
    [InlineData("null", null)]
    [InlineData("\"73.5\"", null)]
    [InlineData("{}", null)]
    [InlineData("1e100", null)]
    public void MaximumJumpRangeRequiresAPositiveNumericJournalValue(string value, string? expected)
    {
        using var document = JsonDocument.Parse(
            $$"""{"Ship":"python","MaxJumpRange":{{value}},"Modules":[{"Slot":"PowerPlant","Item":"Int_PowerPlant_Size3_Class5"}]}"""
        );
        Assert.Equal(
            expected,
            ShipLoadout
                .Parse(document.RootElement)!
                .MaximumJumpRange?.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
    }

    /// <summary>
    /// Checks that invalid or incomplete loadout is not exportable.
    /// </summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"Ship\":\"python\"}")]
    [InlineData("{\"Ship\":\"python\",\"Modules\":{}}")]
    [InlineData("{\"Ship\":\"python\",\"Modules\":[]}")]
    [InlineData("{\"Ship\":\"python\",\"Modules\":[null]}")]
    [InlineData("{\"Ship\":\"python\",\"Modules\":[{\"Item\":\"test\"}]}")]
    [InlineData("{\"Ship\":\"python\",\"Modules\":[{\"Slot\":\"test\"}]}")]
    public void InvalidOrIncompleteLoadoutIsNotExportable(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Null(ShipLoadout.Parse(document.RootElement));
    }

    /// <summary>
    /// Checks that malformed optional ship id does not throw.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("\"not-a-number\"")]
    [InlineData("1.5")]
    public void MalformedOptionalShipIdDoesNotThrow(string id)
    {
        using var document = JsonDocument.Parse(
            $$"""{"Ship":"python","ShipID":{{id}},"Modules":[{"Slot":"PowerPlant","Item":"Int_PowerPlant_Size3_Class5"}]}"""
        );
        Assert.Null(ShipLoadout.Parse(document.RootElement)!.ShipId);
    }

    /// <summary>
    /// Checks that ship and outfitting changes invalidate export until a full loadout arrives.
    /// </summary>
    [Theory]
    [InlineData("ShipyardSwap")]
    [InlineData("ShipyardBuy")]
    [InlineData("ShipyardNew")]
    [InlineData("ModuleBuy")]
    [InlineData("ModuleSell")]
    [InlineData("ModuleStore")]
    [InlineData("ModuleRetrieve")]
    [InlineData("ModuleSwap")]
    [InlineData("MassModuleStore")]
    [InlineData("EngineerCraft")]
    [InlineData("EngineerLegacyConvert")]
    [InlineData("ApplyExperimentalEffect")]
    [InlineData("Died")]
    [InlineData("Shutdown")]
    [InlineData("Fileheader")]
    public void ShipAndOutfittingChangesInvalidateExportUntilAFullLoadoutArrives(string eventName)
    {
        ShipLoadoutState state = CreateState();
        Assert.True(state.Apply(Parse($$"""{"event":"{{eventName}}"}""")));
        Assert.Null(state.Current);
        state.Apply(Parse(Loadout));
        Assert.Equal(12, state.Current!.ShipId);
    }

    /// <summary>
    /// Checks that commander changes ship switches and multicrew cannot export another ship.
    /// </summary>
    [Fact]
    public void CommanderChangesShipSwitchesAndMulticrewCannotExportAnotherShip()
    {
        ShipLoadoutState state = CreateState();
        state.Apply(Parse("""{"event":"Commander","FID":"F2"}"""));
        Assert.Null(state.Current);
        state.Apply(Parse(Loadout));
        state.Apply(Parse("""{"event":"LoadGame","FID":"F2","Ship":"LakonMiner","ShipID":99}"""));
        Assert.Null(state.Current);
        state.Apply(Parse(Loadout));
        state.Apply(Parse("""{"event":"LoadGame","FID":"F2","Ship":"LakonMiner","ShipID":12}"""));
        Assert.NotNull(state.Current);
        state.Apply(Parse("""{"event":"JoinACrew"}"""));
        Assert.True(state.IsInCrew);
        state.Apply(Parse(Loadout));
        Assert.Null(state.Current);
        state.Apply(Parse("""{"event":"QuitACrew"}"""));
        Assert.False(state.IsInCrew);
        Assert.Null(state.Current);
        state.Apply(Parse(Loadout));
        Assert.NotNull(state.Current);
    }

    /// <summary>
    /// Checks that same commander and unrelated events keep loadout and matching renames update the export.
    /// </summary>
    [Fact]
    public void SameCommanderAndUnrelatedEventsKeepLoadoutAndMatchingRenamesUpdateTheExport()
    {
        ShipLoadoutState state = CreateState();
        ShipLoadout original = state.Current!;
        state.Apply(Parse("""{"event":"Commander","FID":"F1"}"""));
        state.Apply(Parse("""{"event":"Commander"}"""));
        Assert.False(state.Apply(Parse("""{"event":"Scan"}""")));
        state.Apply(Parse("""{"event":"SetUserShipName","Ship":"LakonMiner","ShipID":99,"UserShipName":"Other"}"""));
        Assert.Same(original, state.Current);
        state.Apply(
            Parse(
                """{"event":"SetUserShipName","Ship":"LakonMiner","ShipID":12,"UserShipName":"Renamed","UserShipId":"NEW-12"}"""
            )
        );
        Assert.Equal("Renamed", state.Current!.Name);
        Assert.Equal("NEW-12", state.Current.Payload.GetProperty("ShipIdent").GetString());
        Assert.True(
            JsonNode.DeepEquals(
                JsonNode.Parse(original.Payload.GetProperty("Modules").GetRawText()),
                JsonNode.Parse(state.Current.Payload.GetProperty("Modules").GetRawText())
            )
        );
    }

    /// <summary>
    /// Checks that actual engineering updates recorded modifiers and keeps existing experimental effect.
    /// </summary>
    [Theory]
    [InlineData("EngineerCraft")]
    [InlineData("EngineerLegacyConvert")]
    public void ActualEngineeringUpdatesRecordedModifiersAndKeepsExistingExperimentalEffect(string eventName)
    {
        ShipLoadoutState state = CreateState();
        state.Apply(
            Parse(
                $$"""{"event":"{{eventName}}","Slot":"SmallMiningHardpoint1","Module":"Hpt_MiningLaser_Fixed_Small","BlueprintName":"Weapon_LongRangeMiningLaser","Level":4,"Quality":1,"Modifiers":[{"Label":"Range","Value":2000,"OriginalValue":500}]}"""
            )
        );
        JsonElement engineering = state.Current!.Payload.GetProperty("Modules")[0].GetProperty("Engineering");
        Assert.Equal(4, engineering.GetProperty("Level").GetInt32());
        Assert.Equal(2000, engineering.GetProperty("Modifiers")[0].GetProperty("Value").GetInt32());
        Assert.Equal("special_incendiary_rounds", engineering.GetProperty("ExperimentalEffect").GetString());
        state.Apply(
            Parse(
                $$"""{"event":"{{eventName}}","Slot":"SmallMiningHardpoint1","Module":"Hpt_MiningLaser_Fixed_Small","ExperimentalEffect":"special_new_effect","Modifiers":[]}"""
            )
        );
        Assert.Equal(
            "special_new_effect",
            state
                .Current.Payload.GetProperty("Modules")[0]
                .GetProperty("Engineering")
                .GetProperty("ExperimentalEffect")
                .GetString()
        );
        Assert.Equal(5, state.Current.ModuleCount);
    }

    /// <summary>
    /// Checks that engineering preview does not change loadout and missing original engineering can be added.
    /// </summary>
    [Fact]
    public void EngineeringPreviewDoesNotChangeLoadoutAndMissingOriginalEngineeringCanBeAdded()
    {
        ShipLoadoutState state = CreateState();
        ShipLoadout original = state.Current!;
        state.Apply(Parse("""{"event":"EngineerLegacyConvert","IsPreview":true}"""));
        Assert.Same(original, state.Current);
        state.Apply(
            Parse(
                """{"event":"EngineerCraft","Slot":"Radar","Module":"Int_Sensors_Size3_Class5","BlueprintName":"Sensor_LightWeight","Level":1,"Modifiers":[{"Label":"Mass","Value":1}]}"""
            )
        );
        Assert.Equal(
            "Sensor_LightWeight",
            state
                .Current!.Payload.GetProperty("Modules")[3]
                .GetProperty("Engineering")
                .GetProperty("BlueprintName")
                .GetString()
        );
        state.Apply(Parse("""{"event":"EngineerCraft","Slot":"unknown","Module":"unknown","Modifiers":[]}"""));
        Assert.Null(state.Current);
        state.Apply(Parse("""{"event":"EngineerCraft"}"""));
        Assert.Null(state.Current);
    }

    /// <summary>
    /// Checks that commander name changes without an id cannot retain the previous ship.
    /// </summary>
    [Fact]
    public void CommanderNameChangesWithoutAnIdCannotRetainThePreviousShip()
    {
        ShipLoadoutState state = CreateState();
        state.Apply(Parse("""{"event":"Commander","Name":"First"}"""));
        Assert.NotNull(state.Current);
        state.Apply(Parse("""{"event":"Commander","Name":"Second"}"""));
        Assert.Null(state.Current);
    }

    /// <summary>
    /// Checks that engineering updates unladen mass using actual module mass changes.
    /// </summary>
    [Theory]
    [InlineData(null, 3.25, 40.75)]
    [InlineData(3.25, null, 39.25)]
    [InlineData(3.0, 2.0, 39.0)]
    [InlineData(2.0, 2.0, 40.0)]
    public void EngineeringUpdatesUnladenMassUsingActualModuleMassChanges(
        double? previous,
        double? next,
        double expected
    )
    {
        var state = new ShipLoadoutState();
        string before = previous is { } oldMass
            ? JsonSerializer.Serialize(new[] { new { Label = "Mass", Value = oldMass } })
            : "[]";
        string after = next is { } newMass
            ? JsonSerializer.Serialize(new[] { new { Label = "Mass", Value = newMass } })
            : "[]";
        state.Apply(
            Parse(
                $$$"""{"event":"Loadout","Ship":"sidewinder","ShipID":1,"UnladenMass":40,"Modules":[{"Slot":"FrameShiftDrive","Item":"Int_Hyperdrive_Size2_Class5","Engineering":{"Modifiers":{{{before}}}}}]}"""
            )
        );
        state.Apply(
            Parse(
                $$$"""{"event":"EngineerCraft","Slot":"FrameShiftDrive","Module":"Int_Hyperdrive_Size2_Class5","Modifiers":{{{after}}}}"""
            )
        );
        Assert.Equal(expected, state.Current!.Payload.GetProperty("UnladenMass").GetDouble(), 6);
    }

    /// <summary>
    /// Checks that unverifiable engineering mass changes wait for a complete loadout.
    /// </summary>
    [Theory]
    [InlineData("unknown_module", "[]", 40)]
    [InlineData("Int_Hyperdrive_Size2_Class5", "[{\"Label\":\"Mass\",\"Value\":100}]", 0)]
    public void UnverifiableEngineeringMassChangesWaitForACompleteLoadout(string item, string modifiers, double mass)
    {
        var state = new ShipLoadoutState();
        state.Apply(
            Parse(
                $$$"""{"event":"Loadout","Ship":"sidewinder","ShipID":1,"UnladenMass":{{{mass}}},"Modules":[{"Slot":"FrameShiftDrive","Item":"{{{item}}}","Engineering":{"Modifiers":{{{modifiers}}}}}]}"""
            )
        );
        state.Apply(
            Parse(
                $$$"""{"event":"EngineerCraft","Slot":"FrameShiftDrive","Module":"{{{item}}}","Modifiers":[{"Label":"Mass","Value":1}]}"""
            )
        );
        Assert.Null(state.Current);
    }

    /// <summary>
    /// Checks that malformed retained engineering cannot interrupt subsequent journal processing.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Modifiers\":null}")]
    [InlineData("{\"Modifiers\":{}}")]
    [InlineData("{\"Modifiers\":true}")]
    [InlineData("{\"Modifiers\":[{\"Label\":\"Mass\",\"Value\":null}]}")]
    [InlineData("true")]
    public void MalformedPreviousEngineeringDoesNotBreakJournalProjection(string engineering)
    {
        var state = new ShipLoadoutState();
        state.Apply(
            Parse(
                $$$"""{"event":"Loadout","Ship":"sidewinder","ShipID":1,"UnladenMass":40,"Modules":[{"Slot":"FrameShiftDrive","Item":"Int_Hyperdrive_Size2_Class5","Engineering":{{{engineering}}}}]}"""
            )
        );

        state.Apply(
            Parse(
                """{"event":"EngineerCraft","Slot":"FrameShiftDrive","Module":"Int_Hyperdrive_Size2_Class5","Modifiers":[{"Label":"Mass","Value":1}]}"""
            )
        );

        Assert.Equal(38.5, state.Current!.Payload.GetProperty("UnladenMass").GetDouble(), 6);
    }

    /// <summary>
    /// Checks that journal session retains export data without changing existing ship identity handling.
    /// </summary>
    [Fact]
    public void JournalSessionRetainsExportDataWithoutChangingExistingShipIdentityHandling()
    {
        var session = new JournalSessionState();
        session.Apply(Parse("""{"event":"Commander","FID":"F1"}"""));
        session.Apply(Parse(Loadout));
        Assert.Equal(session.ShipId, session.ShipLoadout.Current!.ShipId);
        Assert.Equal(session.ShipType, session.ShipLoadout.Current.ShipType);
        session.Apply(Parse("""{"event":"ModuleSell"}"""));
        Assert.Null(session.ShipLoadout.Current);
    }

    /// <summary>
    /// Seeds a tracked loadout for journal mutation and export tests.
    /// </summary>
    private static ShipLoadoutState CreateState()
    {
        var state = new ShipLoadoutState();
        state.Apply(Parse("""{"event":"Commander","FID":"F1"}"""));
        state.Apply(Parse(Loadout));
        return state;
    }

    /// <summary>
    /// Parses a journal fixture and includes parser diagnostics in any test failure.
    /// </summary>
    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out string? error), error);
        return entry!;
    }
}
