using System.Net;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ColonizationBodyRecoveryTests
{
    [Fact]
    public async Task DiagnosticReplayCannotWriteBodiesThroughEitherEditorAction()
    {
        using var fixture = new Fixture();
        using MainWindowViewModel main = fixture.CreateMain(diagnosticReplay: true);
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Test Cmdr");
        main.Colonization.UpdateSystemContext("Example", null, 42);
        await main.Colonization.SystemEditor.LoadAsync();
        Assert.True(main.Colonization.SystemEditor.IsLoaded);
        main.Colonization.SystemEditor.ApplyJournalEvents([
            .. LocalScans(),
            Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}"),
        ]);
        await main.Colonization.SystemEditor.BodyCatalogUpdateTask;
        await main.Colonization.SystemEditor.UploadScannedBodiesAsync();
        await main.Colonization.SystemEditor.RediscoverBodiesAsync();
        Assert.False(main.Colonization.SystemEditor.RediscoverBodiesCommand.CanExecute(null));
        Assert.Equal(0, fixture.BodyWrites);
    }

    [Fact]
    public async Task AnEntireAwaitingIdentityProjectionKeepsTheLoadedEditorsBodyCommandsSuspended()
    {
        using var fixture = new Fixture();
        using MainWindowViewModel main = fixture.CreateMain();
        await main.ApplyMonitorUpdateAsync(
            new(
                null,
                [
                    Event("{\"event\":\"Commander\",\"Name\":\"Test Cmdr\",\"FID\":\"F-A\"}"),
                    Event("{\"event\":\"Location\",\"StarSystem\":\"Example\",\"SystemAddress\":42}"),
                ],
                null,
                null,
                null,
                null,
                [],
                true
            ),
            isManualRefresh: true
        );
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Test Cmdr");
        main.Colonization.UpdateSystemContext("Example", null, 42);
        await main.Colonization.SystemEditor.LoadAsync();
        Assert.True(main.Colonization.SystemEditor.RediscoverBodiesCommand.CanExecute(null));

        await main.ApplyMonitorUpdateAsync(
            new(null, [], null, null, null, null, [], false) { IsAwaitingCommanderIdentity = true },
            isManualRefresh: true
        );

        Assert.True(main.Colonization.SystemEditor.IsLoaded);
        Assert.False(main.Colonization.SystemEditor.RediscoverBodiesCommand.CanExecute(null));
        Assert.Equal(0, fixture.BodyWrites);
    }

    [Fact]
    public async Task SameCommanderIdentityRecoveryRestoresTheLoadedCatalogForScannedUploads()
    {
        using var fixture = new Fixture();
        using ColonizationViewModel workspace = fixture.CreateWorkspace();
        workspace.IsEnabled = true;
        await workspace.SetCommanderAsync("Test Cmdr");
        workspace.UpdateSystemContext("Example", null, 42);
        workspace.SystemEditor.BodyJournalHistoryReader = (_, _) =>
            Task.FromResult<IReadOnlyList<JournalEventEnvelope>>([
                .. LocalScans(),
                Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}"),
            ]);
        await workspace.SystemEditor.LoadAsync();
        workspace.PrepareBodyCatalogJournalBatch([], "F-A", awaitingIdentity: true);
        Assert.False(workspace.SystemEditor.RediscoverBodiesCommand.CanExecute(null));
        await workspace.SetCommanderAsync("Test Cmdr");
        Assert.True(workspace.SystemEditor.RediscoverBodiesCommand.CanExecute(null));
        await workspace.SystemEditor.UploadScannedBodiesAsync();
        Assert.Equal(1, fixture.BodyWrites);
    }

    [Fact]
    public async Task AForeignCommanderBatchCannotPublishUsingThePreviousWorkspace()
    {
        using var fixture = new Fixture();
        using ColonizationViewModel workspace = fixture.CreateWorkspace();
        workspace.IsEnabled = true;
        await workspace.SetCommanderAsync("Test Cmdr");
        workspace.UpdateSystemContext("Example", null, 42);
        await workspace.SystemEditor.LoadAsync();
        workspace.SystemEditor.ApplyJournalEvents(
            [.. LocalScans(), Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}")],
            allowBodyPublishing: false
        );
        fixture.Record = fixture.Record with { Bodies = [fixture.Record.Bodies![0]] };

        workspace.ApplyJournalEvents(
            [Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}")],
            "Other Cmdr",
            allowBodyPublishing: true
        );
        await workspace.SystemEditor.BodyCatalogUpdateTask;

        Assert.Equal(0, fixture.BodyWrites);
        Assert.False(workspace.SystemEditor.RediscoverBodiesCommand.CanExecute(null));
    }

    [Fact]
    public async Task ACommanderBoundaryCancelsAnOldUploadBeforeAnyProjectionAwait()
    {
        using var fixture = new Fixture();
        using ColonizationViewModel workspace = fixture.CreateWorkspace();
        workspace.IsEnabled = true;
        await workspace.SetCommanderAsync("Test Cmdr");
        workspace.UpdateSystemContext("Example", null, 42);
        await workspace.SystemEditor.LoadAsync();
        workspace.SystemEditor.ApplyJournalEvents(
            [.. LocalScans(), Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}")],
            allowBodyPublishing: false
        );
        fixture.Record = fixture.Record with { Bodies = [fixture.Record.Bodies![0]] };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DelayRavenRead = async () =>
        {
            entered.TrySetResult();
            await release.Task;
        };
        Task upload = workspace.SystemEditor.UpdateScannedBodiesAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        workspace.PrepareBodyCatalogJournalBatch(
            [Event("{\"event\":\"Commander\",\"Name\":\"Other Cmdr\",\"FID\":\"F-B\"}")],
            "F-A"
        );
        release.SetResult();
        await upload;
        Assert.Equal(0, fixture.BodyWrites);
        Assert.False(workspace.SystemEditor.RediscoverBodiesCommand.CanExecute(null));
    }

    [Fact]
    public async Task LiveCompletionDuringInitialLoadIsCheckedAfterHydration()
    {
        using var fixture = new Fixture();
        fixture.Record = fixture.Record with { Bodies = [fixture.Record.Bodies![0]] };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DelayRavenRead = async () =>
        {
            entered.TrySetResult();
            await release.Task;
        };
        Task load = fixture.LoadAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Editor.ApplyJournalEvents([
            .. LocalScans(),
            Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}"),
        ]);
        release.SetResult();
        await load;
        await fixture.Editor.BodyCatalogUpdateTask;
        Assert.Equal(1, fixture.BodyWrites);
    }

    [Fact]
    public async Task CredentialChangesInvalidateAnInFlightRediscoveryWithoutRetargetingTheEditor()
    {
        using var fixture = new Fixture();
        await fixture.LoadAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DelayProvider = async () =>
        {
            entered.TrySetResult();
            await release.Task;
        };
        Task recovery = fixture.Editor.RediscoverBodiesAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Editor.UpdateContext(new(true, "Test Cmdr", "Example", 42, "changed-key"));
        release.SetResult();
        await recovery;
        Assert.True(fixture.Editor.IsLoaded);
        Assert.Equal(0, fixture.BodyWrites);
    }

    [Fact]
    public async Task CommanderChangeDoesNotReuseAnotherCommandersBufferedScans()
    {
        using var fixture = new Fixture();
        await fixture.LoadAsync();
        fixture.Editor.ApplyJournalEvents(
            [.. LocalScans(), Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}")],
            allowBodyPublishing: false
        );
        fixture.Record = fixture.Record with { Architect = "Other Cmdr", Bodies = [fixture.Record.Bodies![0]] };
        fixture.Editor.UpdateContext(new(true, "Other Cmdr", "Example", 42, "other-key"));
        await fixture.Editor.LoadAsync();
        await fixture.Editor.UploadScannedBodiesAsync();
        Assert.Equal(0, fixture.BodyWrites);
    }

    [Fact]
    public async Task RecoveredBodiesAreRecognizedBySubsequentJournalSiteUpdates()
    {
        using var fixture = new Fixture();
        fixture.Record = fixture.Record with
        {
            Bodies = [fixture.Record.Bodies![0]],
            Sites = [fixture.Record.Sites[0] with { BodyNumber = -1 }],
        };
        await fixture.LoadAsync();
        await fixture.Editor.RediscoverBodiesAsync();
        fixture.Editor.ApplyJournalEvents([
            Event(
                "{\"event\":\"ApproachSettlement\",\"SystemAddress\":42,\"BodyID\":1,\"Name\":\"Site\",\"MarketID\":123}"
            ),
        ]);
        Assert.Equal(1, fixture.Editor.Sites[0].BodyNumber);
        Assert.Equal(123, fixture.Editor.Sites[0].MarketId);
    }

    [Fact]
    public async Task BootstrapScansRemainLocalUntilALiveTriggerOrManualUpload()
    {
        using var fixture = new Fixture();
        fixture.Record = fixture.Record with { Bodies = [fixture.Record.Bodies![0]] };
        await fixture.LoadAsync();
        fixture.Editor.ApplyJournalEvents(
            [.. LocalScans(), Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}")],
            allowBodyPublishing: false
        );
        await fixture.Editor.BodyCatalogUpdateTask;
        Assert.Equal(0, fixture.BodyWrites);
        await fixture.Editor.UploadScannedBodiesAsync();
        Assert.Equal(1, fixture.BodyWrites);
    }

    [Fact]
    public async Task JournalHistoryFailureDoesNotBlockRemoteRediscovery()
    {
        using var fixture = new Fixture();
        fixture.Editor.BodyJournalHistoryReader = (_, _) => throw new IOException("Unavailable history");
        var logs = new List<string>();
        fixture.Editor.BodyCatalogLog = logs.Add;
        await fixture.LoadAsync();
        await fixture.Editor.RediscoverBodiesAsync();
        Assert.Equal(1, fixture.BodyWrites);
        Assert.Equal(["Raven body journal backfill could not be read: Unavailable history"], logs);
    }

    [Fact]
    public async Task HistoricalCompleteScansDoNotUploadJustByLoadingTheEditor()
    {
        using var fixture = new Fixture();
        fixture.Record = fixture.Record with { Bodies = [fixture.Record.Bodies![0]] };
        fixture.Editor.BodyJournalHistoryReader = (_, _) =>
            Task.FromResult<IReadOnlyList<JournalEventEnvelope>>([
                .. LocalScans(),
                Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}"),
            ]);
        await fixture.LoadAsync();
        Assert.Equal(0, fixture.BodyWrites);
        await fixture.Editor.UpdateScannedBodiesAsync();
        Assert.Equal(1, fixture.BodyWrites);
    }

    [Fact]
    public async Task AChangedArchitectStopsRediscoveryBeforePublishing()
    {
        using var fixture = new Fixture();
        await fixture.LoadAsync();
        fixture.Record = fixture.Record with { Architect = "Other Cmdr" };
        await fixture.Editor.RediscoverBodiesAsync();
        Assert.Equal(0, fixture.BodyWrites);
        Assert.Contains("architect changed", fixture.Editor.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RediscoveryUsesTheLoadedRemoteSystemAndPreservesSiteEdits()
    {
        using var fixture = new Fixture();
        fixture.Editor.UpdateContext(new(true, "Test Cmdr", "Live Elsewhere", 99, "test-key"));
        fixture.Editor.SystemNameInput = "Example";
        await fixture.Editor.LoadAsync();
        fixture.Editor.Sites[0].Name = "Edited locally";

        await fixture.Editor.RediscoverBodiesAsync();

        Assert.Equal(42, fixture.PublishedAddress);
        Assert.Equal([0, 1], fixture.Published!.Select(body => body.Number));
        Assert.Equal(1000000, fixture.Published!.Single(body => body.Number == 1).Radius);
        Assert.Equal("Edited locally", fixture.Editor.Sites[0].Name);
        Assert.True(fixture.Editor.HasLocalChanges);
        Assert.Contains("Updated 2 Raven bodies for Example", fixture.Editor.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(0, fixture.SiteWrites);
        Assert.Equal(2, fixture.ExternalReads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RediscoveryListsEachRemovalAndHonorsTheAnswer(bool accept)
    {
        using var fixture = new Fixture();
        fixture.Record = fixture.Record with
        {
            Bodies =
            [
                .. fixture.Record.Bodies!,
                new()
                {
                    Number = 2,
                    Name = "Example absent",
                    Type = "rb",
                },
            ],
        };
        await fixture.LoadAsync();
        RavenBodyRemovalWarning? warning = null;
        fixture.Editor.BodyRemovalConfirmationRequested += value =>
        {
            warning = value;
            return Task.FromResult(accept);
        };

        await fixture.Editor.RediscoverBodiesAsync();

        Assert.NotNull(warning);
        Assert.Equal("Example", warning.SystemName);
        Assert.Equal(42, warning.SystemAddress);
        Assert.Equal(["Example absent (BodyID 2)"], warning.BodyNames);
        Assert.Equal(accept ? 1 : 0, fixture.BodyWrites);
        Assert.Equal(0, fixture.SiteWrites);
        if (!accept)
        {
            Assert.Equal("Rediscovery cancelled. Raven's body catalog was not changed.", fixture.Editor.StatusMessage);
        }
    }

    [Fact]
    public async Task NoConfirmationHandlerCannotApproveRemovingBodies()
    {
        using var fixture = new Fixture();
        fixture.Record = fixture.Record with
        {
            Bodies =
            [
                new()
                {
                    Number = 2,
                    Name = "Example absent",
                    Type = "rb",
                },
            ],
        };
        await fixture.LoadAsync();

        await fixture.Editor.RediscoverBodiesAsync();

        Assert.Equal(0, fixture.BodyWrites);
    }

    [Fact]
    public async Task ANewlyLoadedSelectionBecomesTheRediscoveryTarget()
    {
        using var fixture = new Fixture();
        await fixture.LoadAsync();
        fixture.Record = new()
        {
            SystemAddress = 84,
            Name = "Other",
            Architect = "Test Cmdr",
            Bodies = [],
        };
        fixture.Editor.SystemNameInput = "Other";
        await fixture.Editor.LoadAsync();

        await fixture.Editor.RediscoverBodiesAsync();

        Assert.Equal(84, fixture.PublishedAddress);
        Assert.Equal(["Other", "Other 1"], fixture.Published!.Select(body => body.Name));
    }

    [Fact]
    public async Task ChangingTheEditorWhileSourcesAreLoadingPreventsAnObsoleteWrite()
    {
        using var fixture = new Fixture();
        await fixture.LoadAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DelayProvider = async () =>
        {
            entered.TrySetResult();
            await release.Task;
        };

        Task recovery = fixture.Editor.RediscoverBodiesAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Editor.SystemNameInput = "Other";
        release.SetResult();
        await recovery;

        Assert.Equal(0, fixture.BodyWrites);
        Assert.False(fixture.Editor.IsLoaded);
    }

    [Fact]
    public async Task RavenChangesDuringRemovalConfirmationRequireAnotherReview()
    {
        using var fixture = new Fixture();
        fixture.Record = fixture.Record with
        {
            Bodies =
            [
                .. fixture.Record.Bodies!,
                new()
                {
                    Number = 2,
                    Name = "Example absent",
                    Type = "rb",
                },
            ],
        };
        await fixture.LoadAsync();
        fixture.Editor.BodyRemovalConfirmationRequested += _ =>
        {
            fixture.Record = fixture.Record with
            {
                Bodies =
                [
                    .. fixture.Record.Bodies,
                    new()
                    {
                        Number = 3,
                        Name = "Example newly added",
                        Type = "rb",
                    },
                ],
            };
            return Task.FromResult(true);
        };

        await fixture.Editor.RediscoverBodiesAsync();

        Assert.Equal(0, fixture.BodyWrites);
        Assert.Contains("changed while the warning was open", fixture.Editor.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalUploadWaitsForCompletionAndDoesNotRunForEachNewScan()
    {
        using var fixture = new Fixture();
        fixture.Record = fixture.Record with { Bodies = [fixture.Record.Bodies![0]] };
        await fixture.LoadAsync();
        fixture.Editor.ApplyJournalEvents([
            Event("{\"event\":\"FSSDiscoveryScan\",\"SystemAddress\":42,\"BodyCount\":2,\"Progress\":0.5}"),
        ]);
        await fixture.Editor.BodyCatalogUpdateTask;
        Assert.Equal(0, fixture.BodyWrites);
        fixture.Editor.ApplyJournalEvents(LocalScans());
        await fixture.Editor.BodyCatalogUpdateTask;
        Assert.Equal(0, fixture.BodyWrites);

        fixture.Editor.ApplyJournalEvents([
            Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}"),
        ]);
        await fixture.Editor.BodyCatalogUpdateTask;

        Assert.Equal(1, fixture.BodyWrites);
        Assert.Equal([0, 1], fixture.Published!.Select(body => body.Number));
        fixture.Editor.ApplyJournalEvents([Event("{\"event\":\"NavBeaconScan\",\"SystemAddress\":42}")]);
        await fixture.Editor.BodyCatalogUpdateTask;
        Assert.Equal(1, fixture.BodyWrites);
    }

    [Fact]
    public async Task LocalParityDoesNotAskTheRediscoveryRemovalQuestion()
    {
        using var fixture = new Fixture();
        fixture.Record = fixture.Record with
        {
            Bodies =
            [
                new()
                {
                    Number = 99,
                    Name = "Example stale",
                    Type = "rb",
                },
            ],
        };
        await fixture.LoadAsync();
        fixture.Editor.BodyRemovalConfirmationRequested += _ =>
            throw new InvalidOperationException("Local scans must not ask this question.");
        fixture.Editor.ApplyJournalEvents([
            .. LocalScans(),
            Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}"),
        ]);
        await fixture.Editor.BodyCatalogUpdateTask;

        Assert.Equal(1, fixture.BodyWrites);
        Assert.Equal([0, 1], fixture.Published!.Select(body => body.Number));
    }

    [Fact]
    public async Task ManualScannedUploadCanRefreshAnEqualCountCatalog()
    {
        using var fixture = new Fixture();
        await fixture.LoadAsync();
        fixture.Editor.ApplyJournalEvents([
            .. LocalScans(),
            Event("{\"event\":\"FSSAllBodiesFound\",\"SystemAddress\":42,\"Count\":2}"),
        ]);
        await fixture.Editor.BodyCatalogUpdateTask;
        Assert.Equal(0, fixture.BodyWrites);

        await fixture.Editor.UploadScannedBodiesAsync();

        Assert.Equal(1, fixture.BodyWrites);
    }

    [Fact]
    public async Task ProviderFailureDoesNotPublishAPartialCatalog()
    {
        using var fixture = new Fixture { FailEdsm = true };
        await fixture.LoadAsync();

        await fixture.Editor.RediscoverBodiesAsync();

        Assert.Equal(0, fixture.BodyWrites);
        Assert.Contains("could not be completed", fixture.Editor.StatusMessage, StringComparison.Ordinal);
    }

    private static JournalEventEnvelope[] LocalScans() =>
        [
            Event("{\"event\":\"Scan\",\"SystemAddress\":42,\"BodyID\":0,\"BodyName\":\"Example\",\"StarType\":\"G\"}"),
            Event(
                "{\"event\":\"Scan\",\"SystemAddress\":42,\"BodyID\":1,\"BodyName\":\"Example 1\",\"PlanetClass\":\"Rocky body\",\"Radius\":1000000}"
            ),
        ];

    private static JournalEventEnvelope Event(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out _));
        return entry!;
    }

    private sealed class Fixture : HttpMessageHandler
    {
        private readonly HttpClient http;
        private readonly string workspaceDirectory = Path.Combine(
            Path.GetTempPath(),
            "raven-body-workspace-" + Guid.NewGuid().ToString("N")
        );

        public MainWindowViewModel CreateMain(bool diagnosticReplay = false) =>
            MainWindowViewModelTestBuilder.Create(
                null,
                builder =>
                {
                    builder.WithExternalNetworkClient(http);
                    if (diagnosticReplay)
                    {
                        builder.AsDiagnosticReplay("Body catalog replay test");
                    }
                }
            );

        public ColonizationViewModel CreateWorkspace()
        {
            var workspace = new ColonizationViewModel(
                new SrvSurvey.Desktop.Configuration.ColonizationSettingsStore(
                    Path.Combine(workspaceDirectory, "settings.json")
                ),
                new RavenColonialClient(http, new Uri("https://raven.test/"))
            );
            workspace.SystemEditor.BodyRecoveryClient = new ColonizationBodyRecoveryClient(http);
            return workspace;
        }

        public Fixture()
        {
            http = new HttpClient(this, disposeHandler: false);
            Editor = new(
                new RavenColonialClient(http, new Uri("https://raven.test/")),
                ColonizationBuildCatalog.LoadEmbedded()
            )
            {
                BodyRecoveryClient = new ColonizationBodyRecoveryClient(http),
            };
        }

        public ColonizationSystemEditorViewModel Editor { get; }
        public ColonizationSystemRecord Record { get; set; } =
            new()
            {
                SystemAddress = 42,
                Name = "Example",
                Architect = "Test Cmdr",
                Bodies =
                [
                    new()
                    {
                        Number = 0,
                        Name = "Example",
                        Type = "st",
                    },
                    new()
                    {
                        Number = 1,
                        Name = "Example 1",
                        Type = "rb",
                    },
                ],
                Sites =
                [
                    new()
                    {
                        Id = "site-1",
                        Name = "Site",
                        BodyNumber = 1,
                        BuildType = "outpost",
                        Status = ColonizationSystemSiteStatus.Complete,
                    },
                ],
            };
        public int BodyWrites { get; private set; }
        public int SiteWrites { get; private set; }
        public int ExternalReads { get; private set; }
        public long? PublishedAddress { get; private set; }
        public List<ColonizationSystemBody>? Published { get; private set; }
        public Func<Task>? DelayProvider { get; set; }
        public Func<Task>? DelayRavenRead { get; set; }
        public bool FailEdsm { get; set; }

        public async Task LoadAsync()
        {
            Editor.UpdateContext(new(true, "Test Cmdr", "Example", 42, "test-key"));
            await Editor.LoadAsync();
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Uri uri = request.RequestUri!;
            if (uri.Host != "raven.test" && !uri.AbsolutePath.StartsWith("/api/v2/system/", StringComparison.Ordinal))
            {
                ExternalReads++;
                if (DelayProvider is { } delay)
                {
                    await delay();
                }

                if (FailEdsm && uri.Host == "www.edsm.net")
                {
                    return new(HttpStatusCode.ServiceUnavailable);
                }

                string data = JsonSerializer.Serialize(
                    new
                    {
                        id64 = Record.SystemAddress,
                        name = Record.Name,
                        bodies = new object[]
                        {
                            new
                            {
                                bodyId = 0,
                                name = Record.Name,
                                type = "Star",
                                subType = "G (White-Yellow) Star",
                                solarRadius = 1,
                            },
                            new
                            {
                                bodyId = 1,
                                name = Record.Name + " 1",
                                type = "Planet",
                                subType = "Rocky body",
                                radius = 1000,
                            },
                        },
                    }
                );
                return Json(uri.Host == "spansh.co.uk" ? "{\"system\":" + data + "}" : data);
            }
            if (request.Method == HttpMethod.Put)
            {
                if (!uri.AbsolutePath.EndsWith("/bodies", StringComparison.Ordinal))
                {
                    SiteWrites++;
                    return Json("{}");
                }
                BodyWrites++;
                PublishedAddress = long.Parse(
                    uri.Segments[^2].Trim('/'),
                    System.Globalization.CultureInfo.InvariantCulture
                );
                string json = await request.Content!.ReadAsStringAsync(cancellationToken);
                Published = JsonSerializer.Deserialize<List<ColonizationSystemBody>>(json)!;
                Record = Record with { Bodies = Published };
                return Json(json);
            }
            if (DelayRavenRead is { } wait)
            {
                await wait();
            }

            return Json(JsonSerializer.Serialize(Record));
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Editor.Dispose();
                http.Dispose();
                if (Directory.Exists(workspaceDirectory))
                {
                    Directory.Delete(workspaceDirectory, recursive: true);
                }
            }
            base.Dispose(disposing);
        }
    }
}
