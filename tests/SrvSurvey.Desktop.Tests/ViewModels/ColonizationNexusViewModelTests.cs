using System.Net;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ColonizationNexusViewModelTests
{
    [Fact]
    public async Task RequiresConsentCommanderAndCredentialAndLoadsOnlyTheSelectedNexus()
    {
        var reader = new NexusReader();
        using ColonizationNexusViewModel model = Create(reader);
        await model.RefreshAsync();
        model.SetContext(false, "Test", "key");
        await model.RefreshAsync();
        model.SetContext(true, " ", "key");
        await model.RefreshAsync();
        model.SetContext(true, "Test", " ");
        await model.RefreshAsync();
        Assert.Empty(reader.Requests);
        Assert.False(model.RefreshCommand.CanExecute(null));
        model.SetContext(true, " Test ", " key ");
        Assert.True(model.RefreshCommand.CanExecute(null));
        model.RefreshCommand.Execute(null);
        await model.ActiveTask;
        Assert.Equal(["index:Test:key", "a:Test:key"], reader.Requests);
        Assert.Equal(2, model.Nexuses.Count);
        Assert.Equal("a", model.SelectedNexus!.Id);
        Assert.True(model.HasDetail);
        Assert.False(model.IsBusy);
        Assert.Contains("Updated", model.StatusMessage);
        model.SetContext(true, "Test", "key");
        Assert.True(model.HasDetail);
        Assert.Equal(2, reader.Requests.Count);
        model.SelectedNexus = null;
        model.SelectedNexus = new() { Id = "foreign" };
        Assert.Equal("a", model.SelectedNexus.Id);
        model.SelectedNexus = model.Nexuses[1];
        await model.ActiveTask;
        Assert.Equal("b", model.Detail!.Nexus.Id);
        model.Detail.SelectedScope = model.Detail.Systems[0];
        await model.RefreshAsync();
        Assert.Equal(1, model.Detail.SelectedScope.SystemAddress);
        Assert.Equal("b", model.SelectedNexus.Id);
        Assert.Equal("b:Test:key", reader.Requests[^1]);
        model.Detail.SelectedSystem = model.Detail.Systems[1];
        model.Detail.SelectedScope = model.Detail.Scopes[0];
        await model.RefreshAsync();
        Assert.Equal(0, model.Detail.SelectedScope.SystemAddress);
        Assert.Equal(2, model.Detail.SelectedSystem.SystemAddress);
        model.SetReportOpener((_, _) => { });
        Assert.Equal(0, model.Detail.SelectedScope.SystemAddress);
        Assert.Equal(2, model.Detail.SelectedSystem.SystemAddress);
    }

    [Fact]
    public async Task EmptyMembershipAndMissingSnapshotHaveActionableMessages()
    {
        var reader = new NexusReader { Index = [] };
        using ColonizationNexusViewModel model = Create(reader);
        model.SetContext(true, "Test", "key");
        await model.RefreshAsync();
        Assert.Null(model.SelectedNexus);
        Assert.False(model.HasDetail);
        Assert.Contains("No Nexus projects", model.StatusMessage);
        reader.Index = [new() { Id = "missing", Name = "Missing" }];
        reader.Read = (_, _) => Task.FromResult<ColonizationNexus?>(null);
        await model.RefreshAsync();
        Assert.False(model.HasDetail);
        Assert.Contains("no longer available", model.StatusMessage);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ExplainsAuthenticationAndNetworkErrorsWithoutDisplayingSecrets(HttpStatusCode status)
    {
        var reader = new NexusReader
        {
            Read = (_, _) => throw new RavenColonialServiceException(status, "test", "credential-that-must-not-appear"),
        };
        using ColonizationNexusViewModel model = Create(reader);
        model.SetContext(true, "Test", "key");
        await model.RefreshAsync();
        Assert.DoesNotContain("credential-that", model.StatusMessage);
        Assert.Contains(
            status == HttpStatusCode.ServiceUnavailable ? "could not be loaded" : "denied access",
            model.StatusMessage
        );
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task RefreshFailureRetainsPreviousSnapshotButChangingProfileClearsIt()
    {
        var reader = new NexusReader();
        using ColonizationNexusViewModel model = Create(reader);
        model.SetContext(true, "First", "first-key");
        await model.RefreshAsync();
        ColonizationNexusDetailViewModel previous = model.Detail!;
        reader.Read = (_, _) => throw new IOException("offline");
        await model.RefreshAsync();
        Assert.Same(previous, model.Detail);
        Assert.Contains("previous snapshot", model.StatusMessage);
        model.SetContext(true, "Second", "second-key");
        Assert.Empty(model.Nexuses);
        Assert.Null(model.SelectedNexus);
        Assert.Null(model.Detail);
        Assert.Contains("Refresh Nexus", model.StatusMessage);
        reader.Read = (_, _) => throw new OperationCanceledException("timeout");
        await model.RefreshAsync();
        Assert.Contains("could not be loaded", model.StatusMessage);
    }

    [Fact]
    public async Task SupersededIndexAndDetailRequestsCannotOverwriteCurrentSelection()
    {
        var pendingIndex = new TaskCompletionSource<IReadOnlyList<ColonizationNexusSummary>>();
        var reader = new NexusReader { List = (_, _) => pendingIndex.Task };
        using ColonizationNexusViewModel model = Create(reader);
        model.SetContext(true, "First", "first-key");
        Task first = model.RefreshAsync();
        Assert.True(model.IsBusy);
        Assert.False(model.RefreshCommand.CanExecute(null));
        model.SetContext(true, "Second", "second-key");
        reader.List = null;
        await model.RefreshAsync();
        pendingIndex.SetResult([new() { Id = "stale", Name = "Stale" }]);
        await first;
        Assert.Equal("a", model.Detail!.Nexus.Id);
        var pendingDetail = new TaskCompletionSource<ColonizationNexus?>();
        reader.Read = (id, _) =>
            id == "a" ? pendingDetail.Task : Task.FromResult<ColonizationNexus?>(NexusFixture.Snapshot(id));
        Task refresh = model.RefreshAsync();
        model.SelectedNexus = model.Nexuses[1];
        await model.ActiveTask;
        pendingDetail.SetResult(NexusFixture.Snapshot("a"));
        await refresh;
        Assert.Equal("b", model.Detail.Nexus.Id);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task CallerCancellationAndDisposalDiscardEvenResponsesThatIgnoreCancellation()
    {
        var pending = new TaskCompletionSource<ColonizationNexus?>();
        var reader = new NexusReader { Read = (_, _) => pending.Task };
        using ColonizationNexusViewModel model = Create(reader);
        model.SetContext(true, "Test", "key");
        using var cancellation = new CancellationTokenSource();
        Task loading = model.RefreshAsync(cancellation.Token);
        await cancellation.CancelAsync();
        pending.SetResult(NexusFixture.Snapshot());
        await loading;
        Assert.Null(model.Detail);
        Assert.False(model.IsBusy);
        var next = new TaskCompletionSource<ColonizationNexus?>();
        reader.Read = (_, _) => next.Task;
        Task stopped = model.RefreshAsync();
        model.Dispose();
        model.SetContext(true, "Other", "other-key");
        next.SetResult(NexusFixture.Snapshot());
        await stopped;
        Assert.Null(model.Detail);
        Assert.Empty(model.Nexuses);
        Assert.False(model.RefreshCommand.CanExecute(null));
        await model.RefreshAsync();
    }

    [Fact]
    public void DetailScopesExposeNeedsMembersAndExistingBuildPreviewAction()
    {
        ColonizationProject? opened = null;
        var detail = new ColonizationNexusDetailViewModel(
            ColonizationNexusPlan.Create(NexusFixture.Snapshot()),
            ColonizationBuildCatalog.LoadEmbedded(),
            project => opened = project
        );
        Assert.Equal("Public Nexus", detail.Sharing);
        Assert.Equal("1 of 3 systems complete", detail.RouteSummary);
        Assert.Equal(1, detail.HubCount);
        Assert.Equal(100d / 3, detail.RouteCompletionPercent);
        Assert.True(detail.HasSelectedSystem);
        Assert.Same(detail.Systems[0], detail.SelectedSystem);
        Assert.Contains("delivered", detail.ProgressLabel);
        Assert.Contains("150", detail.CargoSummary);
        Assert.Equal(["Test (owner)", "Other"], detail.Members);
        Assert.Equal("All systems", detail.SelectedScope.Name);
        ColonizationNexusBuildRow build = Assert.Single(detail.SelectedScope.Builds);
        build.OpenCommand.Execute(null);
        Assert.Same(build.Project, opened);
        Assert.Equal("Test System", build.System);
        Assert.NotEmpty(build.Type);
        Assert.Equal("Test", build.Commanders);
        Assert.Equal(50, build.DeliveredPercent);
        Assert.Contains("50", build.ProgressLabel);
        Assert.False(detail.SelectedScope.HasNoBuilds);
        ColonizationNexusNeedRow need = Assert.Single(detail.SelectedScope.Needs);
        Assert.Equal(new ColonizationNexusNeedRow("Steel", 100, 30, 70), need);
        Assert.Empty(detail.SelectedScope.EmptyNeedsMessage);
        detail.Systems[0].SelectCommand.Execute(null);
        Assert.Equal("Test System", detail.SelectedScope.Name);
        Assert.Equal("Hub", detail.SelectedScope.Kind);
        Assert.Equal("Home", detail.SelectedScope.Nickname);
        Assert.Contains("70", detail.SelectedScope.CargoSummary);
        Assert.Contains("50", detail.SelectedScope.ProgressLabel);
        detail.Systems[1].SelectCommand.Execute(null);
        Assert.Same(detail.Systems[1], detail.SelectedSystem);
        Assert.True(detail.SelectedScope.HasNoBuilds);
        Assert.Equal("Complete", detail.SelectedScope.ProgressLabel);
        Assert.Contains("delivered", detail.SelectedScope.EmptyNeedsMessage);
        detail.Systems[2].SelectCommand.Execute(null);
        Assert.Contains("not reported", detail.SelectedScope.ProgressLabel);
        Assert.Contains("No commodity", detail.SelectedScope.EmptyNeedsMessage);
        detail.Scopes[0].SelectCommand.Execute(null);
        Assert.Same(detail.Scopes[0], detail.SelectedScope);
        Assert.Same(detail.Systems[2], detail.SelectedSystem);
        detail.SelectedSystem = detail.Systems[1];
        Assert.Same(detail.Systems[1], detail.SelectedScope);
        detail.SelectedSystem = UnknownScope();
        detail.SelectedSystem = null;
        Assert.Same(detail.Systems[1], detail.SelectedSystem);
        ColonizationNexusCarrierRow carrier = Assert.Single(detail.Carriers);
        Assert.Equal("Test Carrier (ABC-123)", carrier.Name);
        Assert.Equal("Test", carrier.Owner);
        Assert.Equal("Test System", carrier.Location);
        Assert.Equal("Test System", carrier.Assignments);
        Assert.Contains("updated", carrier.Updated);
        Assert.True(detail.HasIncompleteRequirements);
    }

    [Fact]
    public void UnreportedAndCompletedBuildsAndCarriersHaveHonestLabels()
    {
        var catalog = ColonizationBuildCatalog.LoadEmbedded();
        var unknown = new ColonizationNexusBuildRow(new(), "Test", catalog, _ => { });
        Assert.Equal("Progress not reported", unknown.ProgressLabel);
        Assert.Equal(0, unknown.DeliveredPercent);
        Assert.Empty(unknown.Commanders);
        var complete = new ColonizationNexusBuildRow(new() { IsComplete = true }, "Test", catalog, _ => { });
        Assert.Equal("Complete", complete.ProgressLabel);
        var carrier = new ColonizationNexusCarrierRow(new() { Name = "ABC-123" }, []);
        Assert.Equal("ABC-123", carrier.Name);
        Assert.Contains("not reported", carrier.Owner);
        Assert.Contains("not reported", carrier.Location);
        Assert.Contains("not reported", carrier.Updated);
        Assert.Equal("No system assignments", carrier.Assignments);
        var detail = new ColonizationNexusDetailViewModel(ColonizationNexusPlan.Create(new()), catalog, _ => { });
        Assert.True(detail.HasNoCarriers);
        Assert.False(detail.HasSelectedSystem);
        Assert.Null(detail.SelectedSystem);
        Assert.Equal(0, detail.RouteCompletionPercent);
        Assert.Equal("Private Nexus", detail.Sharing);
        Assert.Contains("not reported", detail.ProgressLabel);
        detail.SelectedScope = UnknownScope();
        Assert.Same(detail.Scopes[0], detail.SelectedScope);
    }

    private static ColonizationNexusScope UnknownScope() => new("Other", new(0, 0, 0, 0, false), [], [], () => { });

    private static ColonizationNexusViewModel Create(NexusReader reader) =>
        new(reader, ColonizationBuildCatalog.LoadEmbedded(), _ => { });

    internal sealed class NexusReader : IRavenColonialNexusReader
    {
        public IReadOnlyList<ColonizationNexusSummary> Index { get; set; } =
        [new() { Id = "a", Name = "Alpha" }, new() { Id = "b", Name = "Beta" }];
        public List<string> Requests { get; } = [];
        public Func<string, CancellationToken, Task<ColonizationNexus?>> Read { get; set; } =
            (id, _) => Task.FromResult<ColonizationNexus?>(NexusFixture.Snapshot(id));
        public Func<string, CancellationToken, Task<IReadOnlyList<ColonizationNexusSummary>>>? List { get; set; }

        public Task<IReadOnlyList<ColonizationNexusSummary>> GetCommanderNexusesAsync(
            string commander,
            string apiKey,
            CancellationToken cancellationToken = default
        )
        {
            Requests.Add($"index:{commander}:{apiKey}");
            return List?.Invoke(commander, cancellationToken) ?? Task.FromResult(Index);
        }

        public Task<ColonizationNexus?> GetNexusAsync(
            string id,
            string commander,
            string apiKey,
            CancellationToken cancellationToken = default
        )
        {
            Requests.Add($"{id}:{commander}:{apiKey}");
            return Read(id, cancellationToken);
        }
    }

    [Fact]
    public void SystemAndBuildLinksUseRavenRoutesAndEncodeUntrustedBuildIds()
    {
        var row = new ColonizationNexusBuildRow(
            new() { BuildId = "id /?#", SystemAddress = 42 },
            "Test System",
            ColonizationBuildCatalog.LoadEmbedded(),
            _ => { }
        );
        Assert.Equal(new Uri(RavenColonialClient.WebsiteUri, "#sys=42"), row.SystemUri);
        Assert.Equal(new Uri(RavenColonialClient.WebsiteUri, "#build=id%20%2F%3F%23"), row.BuildUri);
        var fallback = new ColonizationNexusBuildRow(
            new() { BuildId = "id" },
            "System",
            ColonizationBuildCatalog.LoadEmbedded(),
            _ => { },
            43
        );
        Assert.Equal(new Uri(RavenColonialClient.WebsiteUri, "#sys=43"), fallback.SystemUri);
        var unknown = new ColonizationNexusBuildRow(
            new() { BuildId = "id" },
            "Unknown",
            ColonizationBuildCatalog.LoadEmbedded(),
            _ => { }
        );
        Assert.Null(unknown.SystemUri);
        var detail = new ColonizationNexusDetailViewModel(
            ColonizationNexusPlan.Create(NexusFixture.Snapshot()),
            ColonizationBuildCatalog.LoadEmbedded(),
            _ => { }
        );
        Assert.Equal("Test System", detail.Systems[0].SystemName);
        Assert.Equal(new Uri(RavenColonialClient.WebsiteUri, "#sys=1"), detail.Systems[0].SystemUri);
        Assert.Null(detail.Scopes[0].SystemUri);
        Assert.True(detail.Carriers[0].HasLocation);
        Assert.Equal(new Uri(RavenColonialClient.WebsiteUri, "#sys=Test%20System"), detail.Carriers[0].SystemUri);
        Assert.Equal("Test System", Assert.Single(detail.Carriers[0].SystemLinks).Name);
        Assert.Equal(detail.Carriers[0].SystemUri, detail.Carriers[0].SystemLinks[0].SystemUri);
        var unassigned = new ColonizationNexusCarrierRow(new(), []);
        Assert.False(unassigned.HasLocation);
        Assert.Null(unassigned.SystemUri);
        Assert.True(unassigned.HasNoAssignments);
    }
}

internal static class NexusFixture
{
    internal static ColonizationNexus Snapshot(string id = "a") =>
        new()
        {
            Id = id,
            Name = "Test Nexus",
            Owner = "Test",
            Open = true,
            Commanders = ["Test", "Other"],
            Notes = "Building a shared route.",
            Hubs = [1],
            FleetCarriers =
            [
                new()
                {
                    MarketId = 42,
                    Name = "ABC-123",
                    DisplayName = "Test Carrier",
                    Owner = "Test",
                    SystemName = "Test System",
                    Cargo = new() { ["steel"] = 30 },
                    LastRefresh = DateTimeOffset.UtcNow,
                },
            ],
            Systems =
            [
                new()
                {
                    SystemAddress = 1,
                    Name = "Test System",
                    Nickname = "Home",
                    Total = 200,
                    Progress = 100,
                    Needs = new() { ["steel"] = 100 },
                    FleetCarrierIds = [42],
                    BuildTypes = new() { ["outpost"] = 1 },
                    Builds =
                    [
                        new()
                        {
                            BuildId = "build",
                            BuildName = "Test Port",
                            BuildType = "outpost",
                            MaximumRequired = 200,
                            RemainingRequired = 100,
                            Commanders = new() { ["Test"] = [] },
                        },
                    ],
                },
                new()
                {
                    SystemAddress = 2,
                    Name = "Complete System",
                    Total = 50,
                    Progress = 50,
                },
                new() { SystemAddress = 3, Name = "Unplanned System" },
            ],
        };
}
