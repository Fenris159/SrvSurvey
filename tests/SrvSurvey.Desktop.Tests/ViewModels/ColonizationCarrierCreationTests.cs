using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationProjectEditorViewModelTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 43)]
    public async Task CarrierCreationWaitsForRealBuildIdForManualAndPlannedBuilds(bool fromPlan, long carrierId)
    {
        var response = new TaskCompletionSource<ColonizationProject?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new StubRavenColonialClient { CreationResponse = response.Task, CreationStarted = started };
        ColonizationProject? installed = null;
        ColonizationProjectEditorViewModel editor = await PrepareCarrierCreationAsync(
            client,
            fromPlan,
            project =>
            {
                installed = project;
                return Task.CompletedTask;
            }
        );
        editor.IncludeCarrierLinks = true;
        editor.SelectedCreationCarrierMarketId = carrierId;
        await editor.ReviewAsync();
        Assert.Contains(carrierId == 0 ? "Link 2 carriers" : "Link 1 carrier", editor.ConfirmationSummary);

        Task publish = editor.ConfirmCreateAsync();
        await started.Task;
        int prematureLinks = client.CreatedCarrierLinks.Count;
        bool prematurelyCreated = editor.HasCreatedProject;
        // An updated carrier list must not expand the user's reviewed selection while creation is in flight.
        editor.UpdateCarrierOptions([.. CreationCarriers(), new() { MarketId = 44, Name = "CCC-333" }]);
        response.SetResult(CreatedCarrierBuild());
        await publish;

        Assert.Equal(0, prematureLinks);
        Assert.False(prematurelyCreated);

        Assert.Equal(
            carrierId == 0 ? new[] { ("created-build", 42L), ("created-build", 43L) } : [("created-build", 43L)],
            client.CreatedCarrierLinks
        );
        Assert.NotNull(installed);
        Assert.Equal(
            carrierId == 0 ? new long[] { 42, 43 } : [43],
            installed.LinkedFleetCarriers.Select(carrier => carrier.MarketId)
        );
        Assert.Equal("created-build", editor.CreatedProjectId);
        Assert.Equal(fromPlan ? "planned-site" : null, client.LastCreated!.SystemSiteId);
        Assert.False(editor.IsConfirmationPending);
        await editor.ConfirmCreateAsync();
        Assert.Equal(1, client.CreateCount);
    }

    /// <summary>A failure before or after another link succeeds retains that success and identifies the failed carrier.</summary>
    [Theory]
    [InlineData(42L, 43L, "AAA-111")]
    [InlineData(43L, 42L, "BBB-222")]
    public async Task CarrierCreationKeepsTheCreatedBuildAndSuccessfulLinksWhenAnotherCarrierFails(
        long failingId,
        long retainedId,
        string failingName
    )
    {
        var client = new StubRavenColonialClient
        {
            LinkCreatedCarrier = (_, marketId) =>
                marketId == failingId
                    ? Task.FromException(new HttpRequestException("Raven rejected this carrier"))
                    : Task.CompletedTask,
        };
        ColonizationProject? installed = null;
        ColonizationProjectEditorViewModel editor = await PrepareCarrierCreationAsync(
            client,
            true,
            project =>
            {
                installed = project;
                return Task.CompletedTask;
            }
        );
        editor.IncludeCarrierLinks = true;
        await editor.ReviewAsync();
        await editor.ConfirmCreateAsync();
        await editor.ConfirmCreateAsync();

        Assert.True(editor.HasCreatedProject);
        Assert.Equal("created-1", editor.CreatedProjectId);
        Assert.Equal(retainedId, Assert.Single(installed!.LinkedFleetCarriers).MarketId);
        Assert.Contains("Carriers linked: 1 of 2.", editor.StatusMessage);
        Assert.Contains($"{failingName}: Raven rejected this carrier", editor.StatusMessage);
        Assert.Contains("Retry failed links", editor.StatusMessage);
        Assert.False(editor.IsConfirmationPending);
        Assert.Equal(1, client.CreateCount);
    }

    /// <summary>Unchecked carrier linking adds no confirmation suffix and issues no carrier requests.</summary>
    [Fact]
    public async Task CarrierCreationIsOptionalAndStartsUnchecked()
    {
        var client = new StubRavenColonialClient();
        ColonizationProjectEditorViewModel editor = await PrepareCarrierCreationAsync(client, false);
        Assert.False(editor.IncludeCarrierLinks);
        Assert.False(editor.CanChooseCreationCarrier);
        await editor.ReviewAsync();
        Assert.DoesNotContain("after creation.", editor.ConfirmationSummary);
        await editor.ConfirmCreateAsync();
        Assert.Equal("created-1", editor.CreatedProjectId);
        Assert.Empty(client.CreatedCarrierLinks);
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("missing")]
    [InlineData("invalid-id")]
    public async Task CarrierCreationNeverLinksWhenCreationFailsOrDoesNotReturnARealBuildId(string outcome)
    {
        var client = new StubRavenColonialClient
        {
            CreationResponse = outcome switch
            {
                "failure" => Task.FromException<ColonizationProject?>(new HttpRequestException("Creation rejected")),
                "missing" => Task.FromResult<ColonizationProject?>(null),
                _ => Task.FromResult<ColonizationProject?>(CreatedCarrierBuild() with { BuildId = "plan:99:pending" }),
            },
        };
        ColonizationProjectEditorViewModel editor = await PrepareCarrierCreationAsync(client, false);
        editor.IncludeCarrierLinks = true;
        await editor.ReviewAsync();
        await editor.ConfirmCreateAsync();
        Assert.Empty(client.CreatedCarrierLinks);
        Assert.Equal(1, client.CreateCount);
        string expectedMessage = outcome switch
        {
            "invalid-id" => "no valid build ID",
            "missing" => "did not create",
            _ => "Creation rejected",
        };
        Assert.Contains(expectedMessage, editor.StatusMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CarrierCreationStopsWhenTheOwningCommanderChanges(bool duringLink)
    {
        var response = new TaskCompletionSource<ColonizationProject?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var creationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var linkStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var linkResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new StubRavenColonialClient
        {
            CreationStarted = creationStarted,
            CreationResponse = duringLink ? null : response.Task,
        };
        client.LinkCreatedCarrier = (_, _) =>
        {
            linkStarted.TrySetResult();
            return linkResponse.Task;
        };
        int installed = 0;
        ColonizationProjectEditorViewModel editor = await PrepareCarrierCreationAsync(
            client,
            false,
            _ =>
            {
                installed++;
                return Task.CompletedTask;
            }
        );
        editor.IncludeCarrierLinks = true;
        await editor.ReviewAsync();
        Task publish = editor.ConfirmCreateAsync();
        await (duringLink ? linkStarted.Task : creationStarted.Task);
        editor.UpdateContext(ReadyContext() with { CommanderName = "Other Cmdr" });
        response.TrySetResult(CreatedCarrierBuild());
        linkResponse.TrySetResult();
        await publish;
        Assert.Equal(duringLink ? new[] { ("created-1", 42L) } : [], client.CreatedCarrierLinks);
        Assert.Equal(0, installed);
        Assert.False(editor.HasCreatedProject);
        Assert.False(editor.IncludeCarrierLinks);
    }

    [Fact]
    public async Task CarrierCreationRequiresReviewAgainWhenTheSelectionOrAvailableCarriersChange()
    {
        var client = new StubRavenColonialClient();
        ColonizationProjectEditorViewModel editor = await PrepareCarrierCreationAsync(client, false);
        editor.IncludeCarrierLinks = true;
        await editor.ReviewAsync();
        Assert.True(editor.IsConfirmationPending);
        editor.SelectedCreationCarrierMarketId = 43;
        Assert.False(editor.IsConfirmationPending);
        await editor.ReviewAsync();
        Assert.True(editor.IsConfirmationPending);
        editor.UpdateCarrierOptions([new() { MarketId = 42, Name = "AAA-111" }]);
        Assert.False(editor.IsConfirmationPending);
        Assert.Equal(43, editor.SelectedCreationCarrierMarketId);
        await editor.ReviewAsync();
        Assert.False(editor.IsConfirmationPending);
        Assert.Contains("Choose an available carrier", editor.StatusMessage);
        Assert.Equal(0, client.CreateCount);
        editor.IncludeCarrierLinks = false;
        await editor.ReviewAsync();
        Assert.True(editor.IsConfirmationPending);
    }

    private async Task<ColonizationProjectEditorViewModel> PrepareCarrierCreationAsync(
        StubRavenColonialClient client,
        bool fromPlan,
        Func<ColonizationProject, Task>? onCreated = null
    )
    {
        client.Architect = "Test Cmdr";
        if (fromPlan)
        {
            client.Sites =
            [
                new()
                {
                    Id = "planned-site",
                    Name = "Hope",
                    BuildType = "no_truss",
                    Status = ColonizationSystemSiteStatus.Plan,
                    BodyNumber = 12,
                    BodyName = "Test System 1",
                },
            ];
        }
        ColonizationProjectEditorViewModel editor = Create(client, onCreated);
        editor.UpdateContext(
            ReadyContext() with
            {
                RavenApiKey = "test-key",
                CurrentBodyId = 12,
                CurrentBodyName = "Test System 1",
            }
        );
        editor.UpdateCarrierOptions(CreationCarriers());
        await editor.PrepareAsync();
        if (!fromPlan)
        {
            editor.SelectedBuild = editor.BuildOptions.Single(option => option.Build.BuildType == "no_truss");
            editor.SelectedLayout = "no_truss";
        }
        return editor;
    }

    private static ColonizationProject CreatedCarrierBuild() =>
        new()
        {
            BuildId = "created-build",
            BuildName = "Hope",
            BuildType = "no_truss",
            MarketId = 42,
            SystemAddress = 99,
            SystemName = "Test System",
        };

    private static IReadOnlyList<ColonizationFleetCarrier> CreationCarriers() =>
        [
            new() { MarketId = 42, Name = "AAA-111" },
            new() { MarketId = 43, Name = "BBB-222" },
            new() { MarketId = 42, Name = "Duplicate" },
            new() { MarketId = 0, Name = "Invalid" },
        ];

    private sealed partial class StubRavenColonialClient : IRavenColonialProjectCarrierClient
    {
        public TaskCompletionSource? CreationStarted { get; set; }
        public Task<ColonizationProject?>? CreationResponse { get; set; }
        public Func<string, long, Task>? LinkCreatedCarrier { get; set; }
        public List<(string BuildId, long MarketId)> CreatedCarrierLinks { get; } = [];

        public Task LinkProjectCarrierAsync(
            string buildId,
            long marketId,
            CancellationToken cancellationToken = default
        )
        {
            CreatedCarrierLinks.Add((buildId, marketId));
            return LinkCreatedCarrier?.Invoke(buildId, marketId) ?? Task.CompletedTask;
        }

        public Task UnlinkProjectCarrierAsync(
            string buildId,
            long marketId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}
