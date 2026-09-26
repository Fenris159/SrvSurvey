using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ColonizationProjectEditorViewModelTests
{
    private readonly ColonizationBuildCatalog catalog = ColonizationBuildCatalog.LoadEmbedded();

    [Fact]
    public async Task DoesNotReadOrWriteWithoutCompleteConsentedContext()
    {
        var client = new StubRavenColonialClient();
        ColonizationProjectEditorViewModel editor = Create(client);

        await editor.PrepareAsync();

        Assert.False(editor.CanPrepare);
        Assert.False(editor.IsPrepared);
        Assert.Equal(0, client.SiteReadCount);
        Assert.Equal(0, client.ArchitectReadCount);
        Assert.Equal(0, client.CreateCount);
        Assert.Contains("Enable", editor.StatusMessage);
    }

    [Fact]
    public async Task PrepareReadsContextAndMapsAPlannedSiteWithoutPublishing()
    {
        string layout = catalog.FindByBuildType("no_truss")!.Layouts[1];
        var client = new StubRavenColonialClient
        {
            Architect = "Project Architect",
            Sites =
            [
                new ColonizationSystemSite
                {
                    Id = "site-1",
                    Name = "Hope",
                    BodyNumber = 7,
                    BuildType = layout,
                    Status = ColonizationSystemSiteStatus.Plan,
                },
                new ColonizationSystemSite
                {
                    Id = "complete",
                    Name = "Already Built",
                    BuildType = layout,
                    Status = ColonizationSystemSiteStatus.Complete,
                },
            ],
        };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext());

        await editor.PrepareAsync();

        Assert.True(editor.IsPrepared);
        Assert.Equal(1, client.SiteReadCount);
        Assert.Equal(1, client.ArchitectReadCount);
        Assert.Equal(0, client.CreateCount);
        Assert.Equal("Hope", Assert.Single(editor.SystemSites).Site!.Name);
        Assert.Equal("Project Architect", editor.ArchitectName);
        Assert.True(editor.IsArchitectReadOnly);
        Assert.False(editor.ShowArchitectWarning);
        Assert.Equal(layout, editor.SelectedLayout);
        Assert.Equal("7", editor.BodyNumberText);
        Assert.True(editor.IsPlannedSiteSelected);
        Assert.False(editor.IsBuildSelectionEnabled);
    }

    [Fact]
    public async Task MissingRavenArchitectFallsBackToTheEditableCommanderName()
    {
        var client = new StubRavenColonialClient();
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext());
        await editor.PrepareAsync();

        Assert.Equal("Test Cmdr", editor.ArchitectName);
        Assert.False(editor.IsArchitectReadOnly);
        Assert.True(editor.ShowArchitectWarning);

        editor.ArchitectName = "Another Architect";

        Assert.Equal("Another Architect", editor.ArchitectName);
    }

    [Fact]
    public async Task ReviewIsLocalAndConfirmationPublishesExactlyOnce()
    {
        int createdCount = 0;
        var client = new StubRavenColonialClient { Architect = "Test Cmdr" };
        ColonizationProjectEditorViewModel editor = Create(
            client,
            _ =>
            {
                createdCount++;
                return Task.CompletedTask;
            }
        );
        editor.UpdateContext(ReadyContext());
        await editor.PrepareAsync();
        editor.SelectedBuild = editor.BuildOptions.Single(option => option.Build.BuildType == "no_truss");
        editor.SelectedLayout = "no_truss";

        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();

        Assert.True(editor.IsConfirmationPending);
        Assert.Equal(0, client.CreateCount);

        await editor.ConfirmCreateAsync();

        Assert.Equal(1, client.CreateCount);
        Assert.Equal(1, createdCount);
        Assert.False(editor.IsConfirmationPending);
        Assert.True(editor.HasCreatedProject);
        Assert.Equal("Test Cmdr", Assert.Single(client.LastCreated!.Commanders.Keys));
        Assert.Equal("no_truss", client.LastCreated.BuildType);
        Assert.Equal(42, client.LastCreated.MarketId);
    }

    [Fact]
    public async Task ConfirmationRestoresPrimaryOrderWithStoredRavenKey()
    {
        var primary = new ColonizationSystemSite
        {
            Id = "primary",
            Name = "Nexus Port",
            Status = ColonizationSystemSiteStatus.Complete,
        };
        var createdSite = new ColonizationSystemSite
        {
            Id = "created-site",
            BuildId = "created-1",
            MarketId = 42,
            Name = "Test Project",
            Status = ColonizationSystemSiteStatus.Build,
        };
        var client = new StubRavenColonialClient
        {
            Architect = "Test Cmdr",
            SiteResponses = new Queue<IReadOnlyList<ColonizationSystemSite>>([
                [primary],
                [primary],
                [createdSite, primary],
                [primary, createdSite],
            ]),
        };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext() with { RavenApiKey = "secret-key" });
        await editor.PrepareAsync();
        editor.SelectedBuild = editor.BuildOptions.Single(option => option.Build.BuildType == "no_truss");
        editor.SelectedLayout = "no_truss";
        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();

        await editor.ConfirmCreateAsync();

        Assert.Equal(1, client.CreateCount);
        Assert.Equal("secret-key", client.LastSiteUpdateApiKey);
        Assert.Equal(["primary", "created-site"], client.LastSiteUpdate!.OrderedSiteIds);
        Assert.Empty(client.LastSiteUpdate.UpdatedSites);
        Assert.Empty(client.LastSiteUpdate.DeletedSiteIds);
        Assert.Contains("restored", editor.StatusMessage);
    }

    [Fact]
    public async Task ConfirmationWithoutRavenKeyDoesNotRiskExistingPrimary()
    {
        var client = new StubRavenColonialClient
        {
            Architect = "Test Cmdr",
            Sites =
            [
                new ColonizationSystemSite
                {
                    Id = "primary",
                    Name = "Nexus Port",
                    Status = ColonizationSystemSiteStatus.Complete,
                },
            ],
        };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext());
        await editor.PrepareAsync();
        editor.SelectedBuild = editor.BuildOptions.Single(option => option.Build.BuildType == "no_truss");
        editor.SelectedLayout = "no_truss";
        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();

        await editor.ConfirmCreateAsync();

        Assert.Equal(0, client.CreateCount);
        Assert.Contains("API key", editor.StatusMessage);
        Assert.Contains("not created", editor.StatusMessage);
    }

    [Fact]
    public async Task InvalidBodyNumberCannotReachPublishConfirmation()
    {
        var client = new StubRavenColonialClient { Architect = "Test Cmdr" };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext());
        await editor.PrepareAsync();
        editor.BodyNumberText = "invalid";

        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();
        await editor.ConfirmCreateAsync();

        Assert.False(editor.IsConfirmationPending);
        Assert.Equal(0, client.CreateCount);
        Assert.Contains("Body ID", editor.StatusMessage);
    }

    [Fact]
    public async Task CurrentBodyIsPublishedAheadOfThePlannedSiteBody()
    {
        string layout = catalog.FindByBuildType("no_truss")!.Layouts[1];
        var client = new StubRavenColonialClient
        {
            Architect = "Project Architect",
            Sites =
            [
                new ColonizationSystemSite
                {
                    Id = "site-1",
                    Name = "Hope",
                    BodyNumber = 9,
                    BuildType = layout,
                    Status = ColonizationSystemSiteStatus.Plan,
                },
            ],
        };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext() with { CurrentBodyId = 12, CurrentBodyName = "Peralta 4 a" });

        await editor.PrepareAsync();

        Assert.Equal("12", editor.BodyNumberText);
        Assert.Equal("Peralta 4 a", editor.BodyName);
    }

    [Fact]
    public async Task UntouchedBodyNumberFollowsTheBodyTheCommanderIsOn()
    {
        string layout = catalog.FindByBuildType("no_truss")!.Layouts[1];
        var client = new StubRavenColonialClient
        {
            Architect = "Project Architect",
            Sites =
            [
                new ColonizationSystemSite
                {
                    Id = "site-1",
                    Name = "Hope",
                    BodyNumber = 9,
                    BuildType = layout,
                    Status = ColonizationSystemSiteStatus.Plan,
                },
            ],
        };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext());
        await editor.PrepareAsync();
        Assert.Equal("9", editor.BodyNumberText);

        editor.UpdateContext(ReadyContext() with { CurrentBodyId = 12, CurrentBodyName = "Peralta 4 a" });

        Assert.Equal("12", editor.BodyNumberText);
        Assert.Equal("Peralta 4 a", editor.BodyName);
    }

    [Fact]
    public async Task EditedBodyNumberIsNotReplacedWhenTheCurrentBodyArrives()
    {
        string layout = catalog.FindByBuildType("no_truss")!.Layouts[1];
        var client = new StubRavenColonialClient
        {
            Architect = "Project Architect",
            Sites =
            [
                new ColonizationSystemSite
                {
                    Id = "site-1",
                    Name = "Hope",
                    BodyNumber = 9,
                    BuildType = layout,
                    Status = ColonizationSystemSiteStatus.Plan,
                },
            ],
        };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext());
        await editor.PrepareAsync();
        editor.BodyNumberText = "3";

        editor.UpdateContext(ReadyContext() with { CurrentBodyId = 12, CurrentBodyName = "Peralta 4 a" });

        Assert.Equal("3", editor.BodyNumberText);
    }

    [Fact]
    public async Task CurrentBodyPublishesBodyIdAndFullName()
    {
        var client = new StubRavenColonialClient { Architect = "Test Cmdr" };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext() with { CurrentBodyId = 12, CurrentBodyName = "Peralta 4 a" });
        await editor.PrepareAsync();
        editor.SelectedBuild = editor.BuildOptions.Single(option => option.Build.BuildType == "no_truss");
        editor.SelectedLayout = "no_truss";

        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();
        await editor.ConfirmCreateAsync();

        Assert.Equal(1, client.CreateCount);
        Assert.Equal(12, client.LastCreated!.BodyNumber);
        Assert.Equal("Peralta 4 a", client.LastCreated.BodyName);
    }

    [Fact]
    public async Task MissingBodyNameIsHighlightedAndNotPublished()
    {
        var client = new StubRavenColonialClient { Architect = "Test Cmdr" };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext());
        await editor.PrepareAsync();
        editor.SelectedBuild = editor.BuildOptions.Single(option => option.Build.BuildType == "no_truss");
        editor.SelectedLayout = "no_truss";
        editor.BodyName = string.Empty;

        await editor.ReviewAsync();
        await editor.ConfirmCreateAsync();

        Assert.True(editor.IsBodyNameMissing);
        Assert.False(editor.IsConfirmationPending);
        Assert.Equal(0, client.CreateCount);
        Assert.Contains("required", editor.StatusMessage);
    }

    [Fact]
    public async Task BodyDesignationTextCannotReachPublishConfirmation()
    {
        var client = new StubRavenColonialClient { Architect = "Test Cmdr" };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext());
        await editor.PrepareAsync();
        editor.BodyNumberText = "4a";

        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();
        await editor.ConfirmCreateAsync();

        Assert.False(editor.IsConfirmationPending);
        Assert.Equal(0, client.CreateCount);
        Assert.Contains("Body ID", editor.StatusMessage);
    }

    [Fact]
    public async Task UnknownPlannedLayoutFallsBackToEditableManualSelection()
    {
        var client = new StubRavenColonialClient
        {
            Architect = "Test Cmdr",
            Sites =
            [
                new ColonizationSystemSite
                {
                    Id = "site-unknown",
                    Name = "Future Site",
                    BodyNumber = 4,
                    BuildType = "not-in-the-local-catalog",
                    Status = ColonizationSystemSiteStatus.Plan,
                },
            ],
        };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext());

        await editor.PrepareAsync();

        Assert.True(editor.IsPrepared);
        Assert.False(editor.IsPlannedSiteSelected);
        Assert.True(editor.IsBuildSelectionEnabled);
        Assert.Equal("-1", editor.BodyNumberText);
        Assert.Contains("could not be matched", editor.StatusMessage);
        Assert.Equal(0, client.CreateCount);
    }

    [Fact]
    public async Task PrepareShowsEveryPlannedSiteToTheSystemArchitect()
    {
        string orbitalLayout = catalog.FindByBuildType("no_truss")!.Layouts[1];
        var client = new StubRavenColonialClient
        {
            Architect = "Test Cmdr",
            Sites =
            [
                new ColonizationSystemSite
                {
                    Id = "orbital",
                    Name = "Hope",
                    BuildType = orbitalLayout,
                    Status = ColonizationSystemSiteStatus.Plan,
                },
                new ColonizationSystemSite
                {
                    Id = "surface",
                    Name = "Settlement",
                    BuildType = "hestia",
                    Status = ColonizationSystemSiteStatus.Plan,
                },
            ],
        };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext());

        await editor.PrepareAsync();

        Assert.Equal(3, editor.SystemSites.Count);
        Assert.Equal(["Hope", "Settlement"], editor.SystemSites.Skip(1).Select(option => option.Site!.Name));
        Assert.Contains("2 planned sites", editor.StatusMessage);
    }

    [Fact]
    public async Task PrepareShowsOnlyOrbitalPlannedSitesToNonArchitects()
    {
        string orbitalLayout = catalog.FindByBuildType("no_truss")!.Layouts[1];
        var client = new StubRavenColonialClient
        {
            Architect = "Project Architect",
            Sites =
            [
                new ColonizationSystemSite
                {
                    Id = "orbital",
                    Name = "Hope",
                    BuildType = orbitalLayout,
                    Status = ColonizationSystemSiteStatus.Plan,
                },
                new ColonizationSystemSite
                {
                    Id = "surface",
                    Name = "Settlement",
                    BuildType = "hestia",
                    Status = ColonizationSystemSiteStatus.Plan,
                },
            ],
        };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext());

        await editor.PrepareAsync();

        Assert.Equal("Hope", Assert.Single(editor.SystemSites).Site!.Name);
        Assert.True(editor.IsPlannedSiteSelected);
        Assert.Contains("orbital planned", editor.StatusMessage);
    }

    [Fact]
    public async Task PrepareHidesSurfaceOnlyPlansFromNonArchitects()
    {
        var client = new StubRavenColonialClient
        {
            Architect = "Project Architect",
            Sites =
            [
                new ColonizationSystemSite
                {
                    Id = "surface",
                    Name = "Settlement",
                    BuildType = "hestia",
                    Status = ColonizationSystemSiteStatus.Plan,
                },
            ],
        };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext());

        await editor.PrepareAsync();

        Assert.Empty(editor.SystemSites);
        Assert.False(editor.IsPlannedSiteSelected);
        Assert.False(editor.IsBuildSelectionEnabled);
        Assert.Contains("No orbital planned Raven sites", editor.StatusMessage);

        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();
        await editor.ConfirmCreateAsync();

        Assert.False(editor.IsConfirmationPending);
        Assert.Equal(0, client.CreateCount);
        Assert.Contains("system architect", editor.StatusMessage);
    }

    [Fact]
    public async Task NonArchitectCannotStartAProjectWithoutAPlannedSite()
    {
        var client = new StubRavenColonialClient { Architect = "Project Architect" };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext());
        await editor.PrepareAsync();
        editor.SelectedBuild = editor.BuildOptions.Single(option => option.Build.BuildType == "no_truss");
        editor.SelectedLayout = "no_truss";
        editor.SelectedSystemSite = ColonizationSystemSiteOptionViewModel.None;

        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();
        await editor.ConfirmCreateAsync();

        Assert.False(editor.IsPlannedSiteSelected);
        Assert.False(editor.IsBuildSelectionEnabled);
        Assert.False(editor.IsConfirmationPending);
        Assert.Equal(0, client.CreateCount);
        Assert.Contains("system architect", editor.StatusMessage);
    }

    [Fact]
    public async Task UnassignedArchitectCannotStartAProjectWithoutAPlannedSite()
    {
        var client = new StubRavenColonialClient();
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext());
        await editor.PrepareAsync();
        editor.SelectedBuild = editor.BuildOptions.Single(option => option.Build.BuildType == "no_truss");
        editor.SelectedLayout = "no_truss";

        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();
        await editor.ConfirmCreateAsync();

        Assert.False(editor.IsBuildSelectionEnabled);
        Assert.False(editor.IsConfirmationPending);
        Assert.Equal(0, client.CreateCount);
        Assert.Contains("system architect", editor.StatusMessage);
    }

    [Fact]
    public async Task NonArchitectCannotClearASelectedPlannedSite()
    {
        string orbitalLayout = catalog.FindByBuildType("no_truss")!.Layouts[1];
        var client = new StubRavenColonialClient
        {
            Architect = "Project Architect",
            Sites =
            [
                new ColonizationSystemSite
                {
                    Id = "site-1",
                    Name = "Hope",
                    BuildType = orbitalLayout,
                    Status = ColonizationSystemSiteStatus.Plan,
                },
            ],
        };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext());
        await editor.PrepareAsync();

        editor.SelectedSystemSite = ColonizationSystemSiteOptionViewModel.None;

        Assert.True(editor.IsPlannedSiteSelected);
        Assert.Equal("Hope", editor.SelectedSystemSite?.Site?.Name);
        Assert.False(editor.IsBuildSelectionEnabled);
    }

    [Fact]
    public async Task NonArchitectMustChooseAmongMultipleOrbitalPlansBeforeReview()
    {
        var client = new StubRavenColonialClient
        {
            Architect = "Project Architect",
            Sites =
            [
                new ColonizationSystemSite
                {
                    Id = "a",
                    Name = "Hope",
                    BuildType = "vesta",
                    Status = ColonizationSystemSiteStatus.Plan,
                },
                new ColonizationSystemSite
                {
                    Id = "b",
                    Name = "Nexus",
                    BuildType = "no_truss",
                    Status = ColonizationSystemSiteStatus.Plan,
                },
            ],
        };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext() with { RavenApiKey = "secret-key" });
        await editor.PrepareAsync();

        Assert.Equal(2, editor.SystemSites.Count);
        Assert.False(editor.IsPlannedSiteSelected);
        Assert.Contains("Choose one to link", editor.StatusMessage);

        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();
        await editor.ConfirmCreateAsync();

        Assert.False(editor.IsConfirmationPending);
        Assert.Equal(0, client.CreateCount);
        Assert.Contains("Choose a planned Raven site", editor.StatusMessage);

        editor.SelectedSystemSite = editor.SystemSites[0];
        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();
        await editor.ConfirmCreateAsync();

        Assert.Equal(1, client.CreateCount);
        Assert.Equal("a", client.LastCreated!.SystemSiteId);
    }

    [Fact]
    public async Task NonArchitectCanLinkNormalizedPrimaryOrbitalLayout()
    {
        var client = new StubRavenColonialClient
        {
            Architect = "Project Architect",
            Sites =
            [
                new ColonizationSystemSite
                {
                    Id = "site-1",
                    Name = "Hope",
                    BodyNumber = 3,
                    BuildType = "Vesta (primary)",
                    Status = ColonizationSystemSiteStatus.Plan,
                },
            ],
        };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext() with { RavenApiKey = "secret-key" });
        await editor.PrepareAsync();

        Assert.True(editor.IsPlannedSiteSelected);
        Assert.Equal("Vesta", editor.SelectedLayout, StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();
        await editor.ConfirmCreateAsync();

        Assert.Equal(1, client.CreateCount);
        Assert.Equal("site-1", client.LastCreated!.SystemSiteId);
        Assert.Equal("vesta", client.LastCreated.BuildType);
    }

    [Fact]
    public async Task NonArchitectDoesNotSeeUnresolvableOrbitalJournalGuesses()
    {
        var client = new StubRavenColonialClient
        {
            Architect = "Project Architect",
            Sites =
            [
                new ColonizationSystemSite
                {
                    Id = "guess",
                    Name = "Guessed Outpost",
                    BuildType = "outpost?",
                    Status = ColonizationSystemSiteStatus.Plan,
                },
            ],
        };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext());
        await editor.PrepareAsync();

        Assert.Empty(editor.SystemSites);
        Assert.False(editor.IsPlannedSiteSelected);
        Assert.False(editor.IsBuildSelectionEnabled);

        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();
        await editor.ConfirmCreateAsync();

        Assert.Equal(0, client.CreateCount);
        Assert.Contains("system architect", editor.StatusMessage);
    }

    [Fact]
    public async Task NonArchitectCanPublishWhenLinkingAVisiblePlannedSite()
    {
        string orbitalLayout = catalog.FindByBuildType("no_truss")!.Layouts[1];
        var client = new StubRavenColonialClient
        {
            Architect = "Project Architect",
            Sites =
            [
                new ColonizationSystemSite
                {
                    Id = "site-1",
                    Name = "Hope",
                    BodyNumber = 7,
                    BuildType = orbitalLayout,
                    Status = ColonizationSystemSiteStatus.Plan,
                },
            ],
        };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext() with { RavenApiKey = "secret-key" });
        await editor.PrepareAsync();

        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();
        await editor.ConfirmCreateAsync();

        Assert.Equal(1, client.CreateCount);
        Assert.Equal("site-1", client.LastCreated!.SystemSiteId);
        Assert.Equal(orbitalLayout.ToLowerInvariant(), client.LastCreated.BuildType);
    }

    [Fact]
    public async Task ContextChangeDiscardsStaleConfirmation()
    {
        var client = new StubRavenColonialClient { Architect = "Test Cmdr" };
        ColonizationProjectEditorViewModel editor = Create(client);
        editor.UpdateContext(ReadyContext());
        await editor.PrepareAsync();
        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();
        Assert.True(editor.IsConfirmationPending);

        editor.UpdateContext(ReadyContext() with { SystemName = "Other" });

        Assert.False(editor.IsPrepared);
        Assert.False(editor.IsConfirmationPending);
        await editor.ConfirmCreateAsync();
        Assert.Equal(0, client.CreateCount);
    }

    [Fact]
    public async Task DepotProgressOnlyChangePreservesPreparedEditor()
    {
        var client = new StubRavenColonialClient { Architect = "Test Cmdr" };
        ColonizationProjectEditorViewModel editor = Create(client);
        ColonizationProjectEditorContext ready = ReadyContext();
        editor.UpdateContext(ready);
        await editor.PrepareAsync();
        if (string.IsNullOrWhiteSpace(editor.BodyName))
        {
            editor.BodyName = "Peralta 4 a";
        }

        await editor.ReviewAsync();
        Assert.True(editor.IsPrepared);
        Assert.True(editor.IsConfirmationPending);

        ColonizationConstructionDepotSnapshot progressed = ready.Depot! with
        {
            Timestamp = ready.Depot.Timestamp?.AddSeconds(3),
            ReportedProgress = 0.4,
            Resources = [new ColonizationResourceRequirement("steel", "Steel", 100, 40, 1)],
        };
        editor.UpdateContext(ready with { Depot = progressed });

        Assert.True(editor.IsPrepared);
        Assert.True(editor.IsConfirmationPending);
        await editor.ConfirmCreateAsync();
        Assert.Equal(1, client.CreateCount);
        Assert.NotNull(client.LastCreated);
        Assert.Equal(60, client.LastCreated.Commodities["steel"]);
        Assert.Equal(100, client.LastCreated.MaximumRequired);
    }

    private ColonizationProjectEditorViewModel Create(
        StubRavenColonialClient client,
        Func<ColonizationProject, Task>? onCreated = null
    )
    {
        return new ColonizationProjectEditorViewModel(client, catalog, onCreated ?? (_ => Task.CompletedTask));
    }

    private static ColonizationProjectEditorContext ReadyContext()
    {
        return new ColonizationProjectEditorContext(
            true,
            "Test Cmdr",
            "Test System",
            [1, 2, 3],
            new ColonizationDockingSnapshot(
                42,
                99,
                "Test System",
                "Orbital Construction Site: Hope",
                "Test Faction",
                ["colonisationcontribution"]
            ),
            new ColonizationConstructionDepotSnapshot(
                DateTimeOffset.Parse("2026-07-24T12:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture),
                42,
                0.25,
                IsComplete: false,
                IsFailed: false,
                [new ColonizationResourceRequirement("steel", "Steel", 100, 25, 1)]
            )
        );
    }

    private sealed class StubRavenColonialClient : IRavenColonialClient
    {
        public IReadOnlyList<ColonizationSystemSite> Sites { get; set; } = [];

        public Queue<IReadOnlyList<ColonizationSystemSite>>? SiteResponses { get; init; }

        public string? Architect { get; set; }

        public int SiteReadCount { get; private set; }

        public int ArchitectReadCount { get; private set; }

        public int CreateCount { get; private set; }

        public ColonizationProjectCreate? LastCreated { get; private set; }

        public ColonizationSystemSiteUpdate? LastSiteUpdate { get; private set; }

        public string? LastSiteUpdateApiKey { get; private set; }

        public Task<ColonizationCommanderProjects> GetCommanderProjectsAsync(
            string commanderName,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(new ColonizationCommanderProjects([], [], null, []));
        }

        public Task<string?> GetCommanderByApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
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
        ) => throw new NotSupportedException();

        public Task<ColonizationProject> UpdateProjectAsync(
            ColonizationProjectUpdate update,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task MarkProjectCompleteAsync(string buildId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
        ) => throw new NotSupportedException();

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
            SiteReadCount++;
            return Task.FromResult(SiteResponses is { Count: > 0 } ? SiteResponses.Dequeue() : Sites);
        }

        public Task<string?> GetSystemArchitectAsync(
            string systemNameOrAddress,
            CancellationToken cancellationToken = default
        )
        {
            ArchitectReadCount++;
            return Task.FromResult(Architect);
        }

        public Task<ColonizationSystemRecord> GetSystemAsync(
            string systemNameOrAddress,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

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
            LastSiteUpdate = update;
            LastSiteUpdateApiKey = apiKey;
            return Task.FromResult(new ColonizationSystemRecord { SystemAddress = 99, Name = "Test System" });
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
            CreateCount++;
            LastCreated = project;
            return Task.FromResult<ColonizationProject?>(
                new ColonizationProject
                {
                    BuildId = "created-1",
                    BuildType = project.BuildType,
                    BuildName = project.BuildName,
                    MarketId = project.MarketId,
                    SystemAddress = project.SystemAddress,
                    SystemName = project.SystemName,
                    StarPosition = project.StarPosition,
                    MaximumRequired = project.MaximumRequired,
                    RemainingRequired = project.Commodities.Values.Sum(),
                    Commodities = project.Commodities,
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
        )
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyDictionary<string, int>> ReplaceFleetCarrierCargoAsync(
            long marketId,
            IReadOnlyDictionary<string, int> cargo,
            string apiKey,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(cargo);
        }

        public Task<IReadOnlyDictionary<string, int>> AdjustFleetCarrierCargoAsync(
            long marketId,
            IReadOnlyDictionary<string, int> cargoChanges,
            string apiKey,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(cargoChanges);
        }

        public Task PublishCurrentShipAsync(
            ColonizationCurrentShip ship,
            string apiKey,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}
