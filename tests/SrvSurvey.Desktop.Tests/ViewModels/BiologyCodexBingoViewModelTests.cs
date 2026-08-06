using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class BiologyCodexBingoViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-BiologyCodexBingo-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CalculatesScopesAndSupportsLegacyEntryActions()
    {
        var dataDirectory = Path.Combine(temporaryDirectory, "data");
        var journalDirectory = Path.Combine(temporaryDirectory, "journals");
        Directory.CreateDirectory(journalDirectory);
        var store = new CommanderCodexStore(dataDirectory);
        await store.TrackAsync(new CommanderCodexTrackRequest(
            "F123",
            "Cmdr Test",
            2310101,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            42,
            3));
        await store.TrackAsync(new CommanderCodexTrackRequest(
            "F123",
            "Cmdr Test",
            2310101,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            42,
            3,
            18,
            "Inner Orion Spur"));
        await store.SetManualDiscoveryAsync(
            "F123",
            "Cmdr Test",
            2320101,
            true,
            DateTimeOffset.Parse("2026-02-01T00:00:00Z"));
        var catalog = CreateCatalog();
        using var viewModel = CreateViewModel(
            store,
            catalog,
            journalDirectory);
        string? copied = null;
        var launched = new List<Uri>();
        CodexBingoNearestRequest? nearest = null;
        viewModel.SetPlatformServices(
            value =>
            {
                copied = value;
                return Task.CompletedTask;
            },
            uri =>
            {
                launched.Add(uri);
                return Task.FromResult(true);
            });
        viewModel.SetNearestSearchHandler(request =>
            {
                nearest = request;
                return Task.CompletedTask;
            });

        await viewModel.UpdateContextAsync(
            "F123",
            "Cmdr Test",
            "Test System",
            new GalacticCoordinate(0, 0, 0));

        Assert.Equal(3, viewModel.TotalCount);
        Assert.Equal(2, viewModel.DiscoveredCount);
        Assert.True(viewModel.SelectedCommander!.IsActive);
        Assert.Contains(viewModel.Regions, region =>
            region.RegionId == 18 && region.IsCurrent);
        var species = Assert.IsType<CodexBingoTreeNodeViewModel>(
            viewModel.RootNodes[0].Find(
                "species:$Codex_Ent_Aleoids_01_Name;"));
        viewModel.SelectedNode = species;

        await viewModel.FindMissingVariantsAsync();

        Assert.Equal(CodexBingoNearestMode.MissingVariants, nearest!.Mode);
        Assert.Equal("Aleoida", nearest.Genus);
        Assert.Equal("Aleoida Arcus", nearest.Species);
        Assert.Equal(["Blue"], nearest.Variants);

        var discovered = Assert.IsType<CodexBingoTreeNodeViewModel>(
            viewModel.RootNodes[0].Find("entry:2310101"));
        viewModel.SelectedNode = discovered;
        Assert.True(viewModel.SelectedIsJournalVerified);
        Assert.Equal("Test System 3", viewModel.DiscoveryBody);
        Assert.Equal("Inner Orion Spur", viewModel.DiscoveryRegion);
        Assert.True(viewModel.HasLocationLink);

        await viewModel.CopyNameAsync();
        await viewModel.OpenCanonnResearchAsync();
        await viewModel.OpenLocationAsync();

        Assert.Equal("Aleoida Arcus - Green", copied);
        Assert.Contains(launched, uri => uri.Host == "canonn-science.github.io");
        Assert.Contains(launched, uri => uri.AbsoluteUri.EndsWith(
            "/body/123456789",
            StringComparison.Ordinal));

        var missing = Assert.IsType<CodexBingoTreeNodeViewModel>(
            viewModel.RootNodes[0].Find("entry:2310102"));
        viewModel.SelectedNode = missing;
        await viewModel.RequestManualOverrideAsync();
        await viewModel.ConfirmManualOverrideAsync();
        Assert.True(viewModel.SelectedIsManual);
        Assert.Equal(3, viewModel.DiscoveredCount);
        await viewModel.RequestManualOverrideAsync();
        await viewModel.ConfirmManualOverrideAsync();
        Assert.False(viewModel.SelectedIsDiscovered);

        var regional = Assert.Single(
            viewModel.Regions,
            region => region.RegionId == 18);
        await viewModel.SelectRegionAsync(regional);
        Assert.Equal(1, viewModel.DiscoveredCount);
        Assert.Equal("Regional firsts in Inner Orion Spur", viewModel.RegionSummary);
    }

    [Fact]
    public async Task ImportsCanonnAndReportsOldJournalProgress()
    {
        var dataDirectory = Path.Combine(temporaryDirectory, "data");
        var journalDirectory = Path.Combine(temporaryDirectory, "journals");
        Directory.CreateDirectory(journalDirectory);
        await File.WriteAllLinesAsync(
            Path.Combine(journalDirectory, "Journal.01.log"),
        [
            """{"timestamp":"2026-07-24T10:00:00Z","event":"Commander","Name":"Cmdr Test","FID":"F123"}""",
            """{"timestamp":"2026-07-24T10:01:00Z","event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}""",
            """{"timestamp":"2026-07-24T10:02:00Z","event":"CodexEntry","EntryID":2310101,"SystemAddress":42,"BodyID":3}""",
        ]);
        var store = new CommanderCodexStore(dataDirectory);
        var catalog = CreateCatalog();
        using var viewModel = CreateViewModel(
            store,
            catalog,
            journalDirectory,
            new CanonnCodexChallengeLoadResult(
            [
                new CanonnCodexChallengeGroup(
                    "Biology",
                    ["Aleoida Arcus - Blue"]),
            ],
            null));
        await viewModel.UpdateContextAsync(
            "F123",
            "Cmdr Test",
            "Sol",
            new GalacticCoordinate(0, 0, 0));

        await viewModel.ImportCanonnAsync();
        await viewModel.ImportJournalsAsync();

        Assert.Equal(2, viewModel.DiscoveredCount);
        Assert.Contains("Scanned 1 journals", viewModel.StatusMessage);
        Assert.Contains("added 2 global/regional firsts", viewModel.StatusMessage);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private static BiologyCodexBingoViewModel CreateViewModel(
        CommanderCodexStore store,
        ExobiologyReferenceCatalog catalog,
        string journalDirectory,
        CanonnCodexChallengeLoadResult? challenge = null)
    {
        return new BiologyCodexBingoViewModel(
            store,
            catalog,
            new CanonnCodexChallengeImporter(
                new StubChallengeClient(challenge
                    ?? new CanonnCodexChallengeLoadResult([], null)),
                store,
                catalog),
            new CommanderCodexJournalImporter(journalDirectory, store),
            new StubLocationClient());
    }

    private static ExobiologyReferenceCatalog CreateCatalog()
    {
        return new ExobiologyReferenceCatalog(
        [
            Entry(
                2310101,
                "$Codex_Ent_Aleoids_01_B_Name;",
                "$Codex_Ent_Aleoids_01_Name;",
                "Aleoida Arcus - Green",
                "Aleoids"),
            Entry(
                2310102,
                "$Codex_Ent_Aleoids_01_C_Name;",
                "$Codex_Ent_Aleoids_01_Name;",
                "Aleoida Arcus - Blue",
                "Aleoids"),
            Entry(
                2320101,
                "$Codex_Ent_Bacterial_01_A_Name;",
                "$Codex_Ent_Bacterial_01_Name;",
                "Bacterium Aurasus - Teal",
                "Bacterial"),
        ]);
    }

    private static ExobiologyReference Entry(
        long entryId,
        string variant,
        string species,
        string display,
        string subClass)
    {
        return new ExobiologyReference(
            entryId,
            variant,
            species,
            display,
            1_000_000,
            HudCategory: "Biology",
            SubClass: subClass,
            Platform: "odyssey");
    }

    private sealed class StubChallengeClient(
        CanonnCodexChallengeLoadResult result)
        : ICanonnCodexChallengeClient
    {
        public Task<CanonnCodexChallengeLoadResult> GetAsync(
            string commanderName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class StubLocationClient : ICodexDiscoveryLocationClient
    {
        public Task<CodexDiscoveryLocationLoadResult> GetAsync(
            long systemAddress,
            int bodyId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CodexDiscoveryLocationLoadResult(
                new CodexDiscoveryLocation(
                    systemAddress,
                    bodyId,
                    "Test System",
                    "Test System 3",
                    new GalacticRegion(18, "Inner Orion Spur"),
                    new GalacticCoordinate(0, 0, 0),
                    new Uri("https://spansh.co.uk/body/123456789")),
                null));
        }
    }
}
