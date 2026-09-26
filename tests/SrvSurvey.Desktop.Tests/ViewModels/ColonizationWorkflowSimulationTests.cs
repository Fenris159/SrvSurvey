using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

/// <summary>
/// Local simulations of the colonization UI commands a commander actually clicks:
/// Load system, add/link planned sites, Prepare/Review/Confirm project create, and
/// dock-time project linking. Intended outcomes follow XP gates (architect vs helper)
/// while payload shape stays aligned with legacy FormNewProject.
/// </summary>
[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ColonizationWorkflowSimulationTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-colonization-workflow-sim",
        Guid.NewGuid().ToString("N")
    );

    private readonly ColonizationBuildCatalog catalog = ColonizationBuildCatalog.LoadEmbedded();

    [Fact]
    public async Task ArchitectCanScratchCreateAndTheProjectAppearsInTheActiveList()
    {
        var client = new WorkflowClient { Architect = "Test Cmdr" };
        ColonizationViewModel viewModel = await ReadyAsync(client);

        ColonizationProjectEditorViewModel editor = viewModel.ProjectEditor;
        Assert.True(editor.PrepareCommand.CanExecute(null));
        await editor.PrepareAsync();

        Assert.True(editor.IsEditorVisible);
        Assert.True(editor.IsBuildSelectionEnabled);
        Assert.Equal("None - configure manually", Assert.Single(editor.SystemSites).DisplayName);
        Assert.True(editor.ReviewCommand.CanExecute(null));

        editor.SelectedBuild = editor.BuildOptions.Single(option => option.Build.BuildType == "no_truss");
        editor.SelectedLayout = "no_truss";
        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();
        Assert.True(editor.IsConfirmationPending);
        Assert.True(editor.ConfirmCommand.CanExecute(null));

        await editor.ConfirmCreateAsync();

        ColonizationProjectCreate created = Assert.Single(client.Created);
        Assert.Null(created.SystemSiteId);
        Assert.Equal("Test Cmdr", Assert.Single(created.Commanders.Keys));
        Assert.Equal("no_truss", created.BuildType);
        Assert.Equal(10, created.MarketId);
        Assert.Equal(75, created.Commodities["steel"]);
        Assert.Equal("created-1", Assert.Single(viewModel.Projects).Project.BuildId);
        Assert.True(editor.HasCreatedProject);
        Assert.False(editor.IsEditorVisible);
        Assert.False(editor.IsBuildSelectionEnabled);
        Assert.False(editor.ReviewCommand.CanExecute(null));
    }

    [Fact]
    public async Task ArchitectLinkingAPlannedSiteLocksBuildControlsAndSendsSystemSiteId()
    {
        string layout = catalog.FindByBuildType("no_truss")!.Layouts[1];
        var client = new WorkflowClient
        {
            Architect = "Test Cmdr",
            Sites = [Plan("site-1", "Hope", layout), Plan("surface", "Camp", "Hestia")],
        };
        ColonizationViewModel viewModel = await ReadyAsync(client);
        ColonizationProjectEditorViewModel editor = viewModel.ProjectEditor;

        await editor.PrepareAsync();

        Assert.Equal(3, editor.SystemSites.Count);
        Assert.Null(editor.SelectedSystemSite?.Site);
        Assert.True(editor.IsBuildSelectionEnabled);

        editor.SelectedSystemSite = editor.SystemSites.Single(option => option.Site?.Id == "site-1");
        Assert.Equal("Hope", editor.SelectedSystemSite?.Site?.Name);
        Assert.False(editor.IsBuildSelectionEnabled);
        Assert.Equal(layout, editor.SelectedLayout);
        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();
        await editor.ConfirmCreateAsync();

        Assert.Equal("site-1", Assert.Single(client.Created).SystemSiteId);
        Assert.Equal("Test Cmdr", Assert.Single(client.Created[0].Commanders.Keys));
    }

    [Fact]
    public async Task HelperCanOnlyLinkAVisibleOrbitalPlanAndCannotScratchCreate()
    {
        string orbital = catalog.FindByBuildType("no_truss")!.Layouts[1];
        var client = new WorkflowClient
        {
            Architect = "Project Architect",
            Sites =
            [
                Plan("orbital", "Hope", orbital),
                Plan("surface", "Camp", "Hestia"),
                Plan("guess", "Outpost", "outpost?"),
            ],
        };
        ColonizationViewModel viewModel = await ReadyAsync(client);
        ColonizationProjectEditorViewModel editor = viewModel.ProjectEditor;

        await editor.PrepareAsync();

        Assert.Equal("Hope", Assert.Single(editor.SystemSites).Site!.Name);
        Assert.True(editor.IsPlannedSiteSelected);
        Assert.False(editor.IsBuildSelectionEnabled);
        Assert.True(editor.ReviewCommand.CanExecute(null));

        editor.SelectedSystemSite = ColonizationSystemSiteOptionViewModel.None;
        Assert.True(editor.IsPlannedSiteSelected);
        Assert.True(editor.ReviewCommand.CanExecute(null));

        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();
        await editor.ConfirmCreateAsync();

        ColonizationProjectCreate created = Assert.Single(client.Created);
        Assert.Equal("orbital", created.SystemSiteId);
        Assert.Equal("Test Cmdr", Assert.Single(created.Commanders.Keys));
        Assert.Equal(orbital.ToLowerInvariant(), created.BuildType);
    }

    [Fact]
    public async Task HelperWithNoVisiblePlanCannotEnableReviewOrPublish()
    {
        var client = new WorkflowClient
        {
            Architect = "Project Architect",
            Sites = [Plan("surface", "Camp", "Hestia")],
        };
        ColonizationViewModel viewModel = await ReadyAsync(client);
        ColonizationProjectEditorViewModel editor = viewModel.ProjectEditor;

        await editor.PrepareAsync();

        Assert.Empty(editor.SystemSites);
        Assert.False(editor.IsBuildSelectionEnabled);
        Assert.False(editor.ReviewCommand.CanExecute(null));
        Assert.False(editor.ConfirmCommand.CanExecute(null));

        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();
        await editor.ConfirmCreateAsync();

        Assert.Empty(client.Created);
        Assert.Contains("system architect", editor.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HelperMustChooseAmongMultipleOrbitalPlansBeforeTheReviewButtonWorks()
    {
        var client = new WorkflowClient
        {
            Architect = "Project Architect",
            Sites = [Plan("a", "Hope", "vesta"), Plan("b", "Nexus", "no_truss")],
        };
        ColonizationViewModel viewModel = await ReadyAsync(client);
        ColonizationProjectEditorViewModel editor = viewModel.ProjectEditor;
        await editor.PrepareAsync();

        Assert.Equal(2, editor.SystemSites.Count);
        Assert.False(editor.IsPlannedSiteSelected);
        Assert.False(editor.IsBuildSelectionEnabled);
        Assert.False(editor.ReviewCommand.CanExecute(null));

        editor.SelectedSystemSite = editor.SystemSites[1];
        Assert.True(editor.ReviewCommand.CanExecute(null));
        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();
        await editor.ConfirmCreateAsync();

        Assert.Equal("b", Assert.Single(client.Created).SystemSiteId);
    }

    [Fact]
    public async Task UnassignedCommanderCanPublishALocalOrbitalPlanThenLinkItFromTheCreateForm()
    {
        var client = new WorkflowClient { Architect = null };
        ColonizationViewModel viewModel = await ReadyAsync(client);

        ColonizationSystemEditorViewModel system = viewModel.SystemEditor;
        Assert.True(system.LoadCommand.CanExecute(null));
        await system.LoadAsync();
        Assert.True(system.IsLoaded);
        Assert.True(system.CanEdit);
        Assert.Equal("Unassigned", system.Architect);

        system.NewSiteName = "Hope";
        Assert.True(system.AddSiteCommand.CanExecute(null));
        system.AddSite();
        ColonizationSystemSiteRowViewModel row = Assert.Single(system.Sites);
        row.SelectedBuildType = row.AllowedBuildTypes.Single(choice =>
            choice.IsSelectable && string.Equals(choice.Value, "vesta", StringComparison.OrdinalIgnoreCase)
        );
        row.MarketIdText = "10-abc";
        Assert.Equal(10, row.MarketId);
        Assert.True(row.HasMarketIdWarning);

        Assert.True(system.ReviewCommand.CanExecute(null));
        await system.ReviewAsync();
        Assert.True(system.ConfirmPublishCommand.CanExecute(null));
        await system.ConfirmPublishAsync();
        Assert.Equal("vesta", Assert.Single(client.Sites).BuildType, StringComparer.OrdinalIgnoreCase);

        ColonizationProjectEditorViewModel editor = viewModel.ProjectEditor;
        await editor.PrepareAsync();
        Assert.False(editor.IsBuildSelectionEnabled);
        Assert.True(editor.IsPlannedSiteSelected);
        Assert.True(editor.ReviewCommand.CanExecute(null));
        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();
        await editor.ConfirmCreateAsync();

        ColonizationProjectCreate created = Assert.Single(client.Created);
        Assert.Equal(Assert.Single(client.Sites).Id, created.SystemSiteId);
        Assert.Equal("Test Cmdr", Assert.Single(created.Commanders.Keys));
        Assert.Equal("Test Cmdr", created.ArchitectName);
    }

    [Fact]
    public async Task HelperLoadSystemIsRefusedAndCannotAddSites()
    {
        var client = new WorkflowClient { Architect = "Other Cmdr", Sites = [Plan("orbital", "Hope", "vesta")] };
        ColonizationViewModel viewModel = await ReadyAsync(client);
        ColonizationSystemEditorViewModel system = viewModel.SystemEditor;

        Assert.True(system.LoadCommand.CanExecute(null));
        await system.LoadAsync();

        Assert.False(system.IsLoaded);
        Assert.False(system.CanEdit);
        Assert.False(system.AddSiteCommand.CanExecute(null));
        Assert.Contains("not the architect", system.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(system.Sites);
    }

    [Fact]
    public async Task DockingAutoLinksOnlyTheMatchingArchitectAndLeavesHelpersUntracked()
    {
        var helperClient = new WorkflowClient { SiteProject = Project("other-build", "Someone Else") };
        ColonizationViewModel helper = await ReadyAsync(helperClient, dockAndDepot: false);
        helper.ApplyJournalEvents([DockedEvent()]);
        await helper.SynchronizeLiveProjectsAsync([DockedEvent()], allowPublishing: true);
        Assert.Empty(helperClient.LinkRequests);
        Assert.Empty(helperClient.ProjectUpdates);
        Assert.Equal("other-build", Assert.Single(helper.Projects).Project.BuildId);
        Assert.Contains("untracked", helper.StatusMessage, StringComparison.OrdinalIgnoreCase);

        var architectClient = new WorkflowClient { SiteProject = Project("architect-build", "Test Cmdr") };
        ColonizationViewModel architect = await ReadyAsync(architectClient, dockAndDepot: false);
        architect.ApplyJournalEvents([DockedEvent()]);
        await architect.SynchronizeLiveProjectsAsync([DockedEvent()], allowPublishing: true);
        LinkCall link = Assert.Single(architectClient.LinkRequests);
        Assert.Equal("architect-build", link.BuildId);
        Assert.Equal("Test Cmdr", link.CommanderName);
        Assert.Contains("Linked Raven project", architect.StatusMessage);
    }

    [AvaloniaFact]
    public async Task ConfirmingADockedProjectLeavesTheWindowAbleToAcceptAnotherClick()
    {
        var client = new WorkflowClient { Architect = "Test Cmdr" };
        ColonizationViewModel viewModel = await ReadyAsync(client);
        ColonizationProjectEditorViewModel editor = viewModel.ProjectEditor;
        await editor.PrepareAsync();
        editor.SelectedBuild = editor.BuildOptions.Single(option => option.Build.BuildType == "no_truss");
        editor.SelectedLayout = "no_truss";
        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();

        int otherClicks = 0;
        var confirm = new Button
        {
            Content = "Confirm publish",
            Command = editor.ConfirmCommand,
            Width = 160,
            Height = 36,
        };
        var other = new Button
        {
            Content = "Still here",
            Width = 160,
            Height = 36,
        };
        other.Click += (_, _) => otherClicks++;
        var window = new Window
        {
            Width = 400,
            Height = 220,
            Content = new StackPanel
            {
                Spacing = 12,
                Margin = new Thickness(20),
                Children = { confirm, other },
            },
        };
        try
        {
            window.Show();
            Assert.NotNull(window.CaptureRenderedFrame());
            Point confirmClick = confirm.TranslatePoint(new Point(20, 12), window)!.Value;
            window.MouseDown(confirmClick, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(confirmClick, MouseButton.Left, RawInputModifiers.None);
            Assert.NotNull(window.CaptureRenderedFrame());

            Point otherClick = other.TranslatePoint(new Point(20, 12), window)!.Value;
            window.MouseDown(otherClick, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(otherClick, MouseButton.Left, RawInputModifiers.None);

            Assert.Equal(1, otherClicks);
            Assert.True(editor.HasCreatedProject);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public async Task PrepareStaysDisabledUntilALiveConstructionDepotMatchesTheDock()
    {
        var client = new WorkflowClient { Architect = "Test Cmdr" };
        ColonizationViewModel viewModel = Create(client);
        viewModel.IsEnabled = true;
        viewModel.SetCommanderProfile("F123", true, "secret-key");
        await viewModel.SetCommanderAsync("Test Cmdr");
        viewModel.UpdateSystemContext("Test System", new GalacticCoordinate(1, 2, 3), 20);

        Assert.False(viewModel.ProjectEditor.PrepareCommand.CanExecute(null));
        viewModel.ApplyJournalEvents([DockedEvent()]);
        Assert.False(viewModel.ProjectEditor.PrepareCommand.CanExecute(null));
        viewModel.ApplyJournalEvents([DepotEvent()]);
        Assert.True(viewModel.ProjectEditor.PrepareCommand.CanExecute(null));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private async Task<ColonizationViewModel> ReadyAsync(WorkflowClient client, bool dockAndDepot = true)
    {
        ColonizationViewModel viewModel = Create(client);
        viewModel.IsEnabled = true;
        viewModel.SetCommanderProfile("F123", true, "secret-key");
        await viewModel.SetCommanderAsync("Test Cmdr");
        viewModel.UpdateSystemContext("Test System", new GalacticCoordinate(1, 2, 3), 20);
        if (dockAndDepot)
        {
            viewModel.ApplyJournalEvents([DockedEvent(), DepotEvent()]);
        }

        return viewModel;
    }

    private ColonizationViewModel Create(WorkflowClient client)
    {
        return new ColonizationViewModel(
            new ColonizationSettingsStore(Path.Combine(directory, "ui.json")),
            client,
            catalog,
            new CommanderProfileStore(directory)
        );
    }

    private static ColonizationSystemSite Plan(string id, string name, string buildType)
    {
        return new ColonizationSystemSite
        {
            Id = id,
            Name = name,
            BuildType = buildType,
            Status = ColonizationSystemSiteStatus.Plan,
        };
    }

    private static ColonizationProject Project(string buildId, string architectName)
    {
        return new ColonizationProject
        {
            BuildId = buildId,
            BuildName = "Port",
            ArchitectName = architectName,
            MarketId = 10,
            SystemAddress = 20,
            SystemName = "Test System",
            Commodities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["steel"] = 75 },
        };
    }

    private static JournalEventEnvelope DockedEvent()
    {
        return Event(
            "Docked",
            """
            "MarketID":10,"SystemAddress":20,"StarSystem":"Test System",
            "StationName":"Orbital Construction Site: Hope",
            "StationFaction":{"Name":"Test Faction"},
            "StationServices":["colonisationcontribution"]
            """
        );
    }

    private static JournalEventEnvelope DepotEvent()
    {
        return Event(
            "ColonisationConstructionDepot",
            """
            "MarketID":10,"ConstructionProgress":0.25,
            "ResourcesRequired":[
              {"Name":"$steel_name;","Name_Localised":"Steel","RequiredAmount":100,"ProvidedAmount":25,"Payment":5000}
            ]
            """
        );
    }

    private static JournalEventEnvelope Event(string eventName, string properties)
    {
        string json = $$"""
            {"timestamp":"2026-07-24T12:00:00Z","event":"{{eventName}}",{{properties}}}
            """;
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? result, out string? error), error);
        return result!;
    }

    private sealed class WorkflowClient : IRavenColonialClient
    {
        public string? Architect { get; set; }

        public List<ColonizationSystemSite> Sites { get; set; } = [];

        public ColonizationProject? SiteProject { get; set; }

        public List<ColonizationProjectCreate> Created { get; } = [];

        public List<ColonizationProjectUpdate> ProjectUpdates { get; } = [];

        public List<LinkCall> LinkRequests { get; } = [];

        public Task<ColonizationCommanderProjects> GetCommanderProjectsAsync(
            string commanderName,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(new ColonizationCommanderProjects([], [], null, []));
        }

        public Task<string?> GetCommanderByApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>("Test Cmdr");
        }

        public Task<IReadOnlyList<string>> SaveHiddenProjectIdsAsync(
            string commanderName,
            IEnumerable<string> hiddenProjectIds,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult<IReadOnlyList<string>>(hiddenProjectIds.ToArray());
        }

        public Task<ColonizationProject?> GetProjectAsync(string buildId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ColonizationProject?>(null);
        }

        public Task<ColonizationProject?> GetProjectAsync(
            long systemAddress,
            long marketId,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(SiteProject);
        }

        public Task<ColonizationProject> UpdateProjectAsync(
            ColonizationProjectUpdate update,
            CancellationToken cancellationToken = default
        )
        {
            ProjectUpdates.Add(update);
            ColonizationProject source =
                SiteProject is { } site
                && string.Equals(site.BuildId, update.BuildId, StringComparison.OrdinalIgnoreCase)
                    ? site
                    : new ColonizationProject
                    {
                        BuildId = update.BuildId,
                        BuildName = update.BuildId,
                        Commodities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                    };
            var commodities = new Dictionary<string, int>(source.Commodities, StringComparer.OrdinalIgnoreCase);
            if (update.Commodities is not null)
            {
                foreach (KeyValuePair<string, int> pair in update.Commodities)
                {
                    commodities[pair.Key] = pair.Value;
                }
            }

            return Task.FromResult(source with { Commodities = commodities });
        }

        public Task MarkProjectCompleteAsync(string buildId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ContributeToProjectAsync(
            string buildId,
            string commanderName,
            IReadOnlyDictionary<string, int> contributions,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task SetPrimaryProjectAsync(
            string commanderName,
            string? buildId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task LinkCommanderAsync(
            string buildId,
            string commanderName,
            CancellationToken cancellationToken = default
        )
        {
            LinkRequests.Add(new LinkCall(buildId, commanderName));
            return Task.CompletedTask;
        }

        public Task UnlinkCommanderAsync(
            string buildId,
            string commanderName,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<ColonizationSystemSite>> GetSystemSitesAsync(
            string systemNameOrAddress,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult<IReadOnlyList<ColonizationSystemSite>>(Sites);
        }

        public Task<string?> GetSystemArchitectAsync(
            string systemNameOrAddress,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(Architect);
        }

        public Task<ColonizationSystemRecord> GetSystemAsync(
            string systemNameOrAddress,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(
                new ColonizationSystemRecord
                {
                    SystemAddress = 20,
                    Name = "Test System",
                    Architect = Architect,
                    Revision = 1,
                    Sites = [.. Sites],
                    Bodies =
                    [
                        new ColonizationSystemBody
                        {
                            Number = 1,
                            Name = "Test System A 1",
                            Type = "Planet",
                        },
                    ],
                }
            );
        }

        public Task<ColonizationSystemRecord> ImportSystemBodiesAsync(
            string systemNameOrAddress,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ColonizationSystemRecord> UpdateSystemSitesAsync(
            string systemNameOrAddress,
            ColonizationSystemSiteUpdate update,
            string apiKey,
            CancellationToken cancellationToken = default
        )
        {
            var deleted = update.DeletedSiteIds.ToHashSet(StringComparer.Ordinal);
            var sites = Sites
                .Where(site => !deleted.Contains(site.Id))
                .ToDictionary(site => site.Id, StringComparer.Ordinal);
            foreach (ColonizationSystemSite site in update.UpdatedSites)
            {
                sites[site.Id] = site;
            }

            if (update.OrderedSiteIds is { Count: > 0 } order)
            {
                Sites = order.Where(sites.ContainsKey).Select(id => sites[id]).ToList();
                foreach (ColonizationSystemSite site in sites.Values)
                {
                    if (!Sites.Any(existing => existing.Id == site.Id))
                    {
                        Sites.Add(site);
                    }
                }
            }
            else
            {
                Sites = sites.Values.ToList();
            }

            if (!string.IsNullOrWhiteSpace(update.Architect))
            {
                Architect = update.Architect;
            }

            return Task.FromResult(
                new ColonizationSystemRecord
                {
                    SystemAddress = 20,
                    Name = "Test System",
                    Architect = Architect,
                    Revision = 2,
                    Sites = [.. Sites],
                    Bodies =
                    [
                        new ColonizationSystemBody
                        {
                            Number = 1,
                            Name = "Test System A 1",
                            Type = "Planet",
                        },
                    ],
                }
            );
        }

        public Task PatchSystemSiteAsync(
            string systemNameOrAddress,
            string siteId,
            ColonizationSystemSitePatch patch,
            string apiKey,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ColonizationProject?> CreateProjectAsync(
            ColonizationProjectCreate project,
            CancellationToken cancellationToken = default
        )
        {
            Created.Add(project);
            if (!string.IsNullOrWhiteSpace(project.SystemSiteId) && Sites.All(site => site.Id != project.SystemSiteId))
            {
                return Task.FromResult<ColonizationProject?>(null);
            }

            if (string.IsNullOrWhiteSpace(project.SystemSiteId))
            {
                Sites.Add(
                    new ColonizationSystemSite
                    {
                        Id = "created-site",
                        BuildId = $"created-{Created.Count}",
                        Name = project.BuildName,
                        MarketId = project.MarketId,
                        Status = ColonizationSystemSiteStatus.Build,
                    }
                );
            }

            return Task.FromResult<ColonizationProject?>(
                new ColonizationProject
                {
                    BuildId = $"created-{Created.Count}",
                    BuildType = project.BuildType,
                    BuildName = project.BuildName,
                    ArchitectName = project.ArchitectName,
                    MarketId = project.MarketId,
                    SystemAddress = project.SystemAddress,
                    SystemName = project.SystemName,
                    StarPosition = project.StarPosition,
                    MaximumRequired = project.MaximumRequired,
                    RemainingRequired = project.Commodities.Values.Sum(),
                    Commodities = project.Commodities,
                    Commanders = project.Commanders,
                }
            );
        }

        public Task<ColonizationFleetCarrier?> GetFleetCarrierAsync(
            long marketId,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult<ColonizationFleetCarrier?>(null);
        }

        public Task<ColonizationFleetCarrier> PublishFleetCarrierAsync(
            ColonizationFleetCarrierRegistration carrier,
            string apiKey,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, int>> ReplaceFleetCarrierCargoAsync(
            long marketId,
            IReadOnlyDictionary<string, int> cargo,
            string apiKey,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, int>> AdjustFleetCarrierCargoAsync(
            long marketId,
            IReadOnlyDictionary<string, int> cargoChanges,
            string apiKey,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task PublishCurrentShipAsync(
            ColonizationCurrentShip ship,
            string apiKey,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }

    private sealed record LinkCall(string BuildId, string CommanderName);
}
