using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Quests;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class JournalInspectorViewModelTests
{
    [Fact]
    public void RetainsNewestOneHundredTwentyEvents()
    {
        var viewModel = new JournalInspectorViewModel();
        var events = Enumerable.Range(0, 125)
            .Select(index => Event(
                $$"""{"timestamp":"2026-07-25T12:00:00Z","event":"Event{{index}}"}"""))
            .ToArray();

        viewModel.ApplyUpdate(events, null);

        Assert.Equal(120, viewModel.Events.Count);
        Assert.Equal("Event124", viewModel.Events[0].EventName);
        Assert.Equal("Event5", viewModel.Events[^1].EventName);
        Assert.Equal("Event124", viewModel.SelectedEvent!.EventName);
    }

    [Fact]
    public void ScalarSelectionsGenerateEscapedRuntimeCompatibleLua()
    {
        var viewModel = new JournalInspectorViewModel();
        viewModel.ApplyUpdate(
            [Event(
                "{\"timestamp\":\"2026-07-25T12:00:00Z\","
                    + "\"event\":\"Scan\",\"Body Name\":\"A \\\"quote\\\"\","
                    + "\"Nested\":{\"Flag\":true,\"end\":\"done\"},"
                    + "\"Values\":[42],\"A.B\":\"dot\"}")],
            null);

        Assert.False(viewModel.Properties.Single(
            property => property.Path == "timestamp").IsSelectable);
        viewModel.Properties.Single(
            property => property.Path == "Body Name").IsIncluded = true;
        viewModel.Properties.Single(
            property => property.Path == "Nested.Flag").IsIncluded = true;
        viewModel.Properties.Single(
            property => property.Path == "Values[0]").IsIncluded = true;
        viewModel.Properties.Single(
            property => property.Path == "Nested.end").IsIncluded = true;
        viewModel.Properties.Single(
            property => property.Path == "A.B").IsIncluded = true;

        Assert.Contains("function on_Scan(entry)", viewModel.CodeText);
        Assert.Contains(
            "entry[\"Body Name\"] == \"A \\\"quote\\\"\"",
            viewModel.CodeText);
        Assert.Contains("entry.Nested.Flag == true", viewModel.CodeText);
        Assert.Contains("entry.Values[1] == 42", viewModel.CodeText);
        Assert.Contains("entry.Nested[\"end\"] == \"done\"", viewModel.CodeText);
        Assert.Contains("entry[\"A.B\"] == \"dot\"", viewModel.CodeText);
    }

    [Fact]
    public async Task StatusSummaryAndClipboardMatchLegacyInspectorValues()
    {
        var copied = new List<string>();
        var viewModel = new JournalInspectorViewModel();
        viewModel.SetClipboardWriter(value =>
        {
            copied.Add(value);
            return Task.CompletedTask;
        });
        viewModel.ApplyUpdate(
            [Event("{\"event\":\"Scan\",\"BodyName\":\"Body A\"}")],
            new EliteStatus
            {
                Flags = StatusFlags.HasLatLong | StatusFlags.InSrv,
                Flags2 = StatusFlags2.OnFoot,
                Pips = [4, 2, 0],
                FireGroup = 2,
                GuiFocus = GuiFocus.SystemMap,
                BodyName = "Test 1",
                Latitude = 12.5,
                Longitude = -45.25,
                Heading = 361,
                Altitude = 15,
                Temperature = 200,
                Destination = new StatusDestination
                {
                    Name = "Destination",
                    Body = 3,
                    System = 42,
                },
                SelectedWeapon = "$humanoid_companalyser_name;",
                SelectedWeaponLocalised = "Genetic Sampler",
            });

        await viewModel.CopyCoordinatesAsync();
        await viewModel.CopyCodeAsync();

        Assert.Contains("Destination: Destination body:3 id64:42", viewModel.StatusText);
        Assert.Contains("GuiFocus: SystemMap, Pips: 4, 2, 0", viewModel.StatusText);
        Assert.Contains("Lat/Long: 12.5, -45.25, Heading: 1 deg", viewModel.StatusText);
        Assert.Equal("12.5, -45.25", copied[0]);
        Assert.Equal(viewModel.CodeText, copied[1]);
    }

    [Fact]
    public async Task ReplayRequiresFreshConfirmationAndReportsResult()
    {
        var replayed = new List<JournalEventEnvelope>();
        var viewModel = new JournalInspectorViewModel(journalEvent =>
        {
            replayed.Add(journalEvent);
            return Task.FromResult(new QuestRuntimeUpdateResult([], [], 1));
        });
        viewModel.ApplyUpdate(
            [Event("{\"event\":\"Scan\",\"BodyName\":\"Body A\"}")],
            null);

        await viewModel.ReplayAsync();

        Assert.Empty(replayed);
        Assert.Contains("confirm", viewModel.StatusMessage);

        viewModel.ReplayConfirmed = true;
        await viewModel.ReplayAsync();

        Assert.Single(replayed);
        Assert.False(viewModel.ReplayConfirmed);
        Assert.Contains("Replayed Scan", viewModel.StatusMessage);

        viewModel.ReplayConfirmed = true;
        viewModel.ApplyUpdate(Enumerable.Range(0, 120)
            .Select(index => Event($"{{\"event\":\"Live{index}\"}}"))
            .ToArray(), null);

        Assert.False(viewModel.ReplayConfirmed);
        Assert.Equal("Live119", viewModel.SelectedEvent!.EventName);
    }

    private static JournalEventEnvelope Event(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var result, out var error),
            error);
        return result!;
    }
}
