using System.Text.Json;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationProjectScopeTests
{
    private static ColonizationProject Project(string id, string system = "Colony", bool plan = false) =>
        new()
        {
            BuildId = id,
            SystemName = system,
            BuildName = id,
            IsPlanned = plan,
            RemainingRequired = 100,
        };

    [Fact]
    public void UnknownAffiliationsPreserveLegacyRowsButExpireNewTemporaryObservations()
    {
        var scope = new ColonizationProjectScope { BrowseEnabled = true };
        ColonizationProject legacy = Project("legacy");
        ColonizationProject docked = Project("docked");
        ColonizationProject browsed = Project("browsed");
        scope.Merge([legacy], []);
        scope.EnterSystem("Colony");
        scope.SetDockedProject(legacy);
        scope.SetDockedProject(docked);
        scope.SetCurrentProjects([browsed]);
        Assert.Equal(3, scope.Merge([legacy, docked, browsed], []).Count);
        scope.SetDockedProject(null);
        scope.EnterSystem("Next");
        Assert.Equal("legacy", Assert.Single(scope.Merge([legacy, docked, browsed], [])).BuildId);
    }

    [Fact]
    public void SuccessfulAffiliationsFilterLegacyProjectsAndFailedPlanningDiscoveryPreservesKnownSystems()
    {
        var scope = new ColonizationProjectScope();
        ColonizationProject own = Project("own", "Home");
        ColonizationProject foreign = Project("foreign");
        Assert.Equal(2, scope.Merge([own, foreign], []).Count);
        scope.SetAffiliations([own], ["Shared Colony"]);
        Assert.True(scope.HasSystemAffiliations);
        Assert.True(scope.IsAffiliated(Project("plan", "Shared Colony", true)));
        Assert.Equal("own", Assert.Single(scope.Merge([own, foreign], [])).BuildId);
        scope.SetAffiliations([own], null);
        Assert.Contains("Shared Colony", scope.AffiliatedSystems);
        var missing = new ColonizationProjectScope();
        missing.SetAffiliations([own], null);
        Assert.False(missing.HasSystemAffiliations);
        Assert.Contains("Home", missing.AffiliatedSystems);
        missing.AddAffiliatedProject(foreign);
        Assert.True(missing.IsAffiliated(foreign));
    }

    [Fact]
    public void BrowsingExpiresUnselectedProjectsAndNeverPersistsVisitedResults()
    {
        var scope = new ColonizationProjectScope { BrowseEnabled = true };
        ColonizationProject tracked = Project("tracked");
        ColonizationProject focused = Project("focused", plan: true);
        ColonizationProject transient = Project("transient");
        scope.SetAffiliations([], []);
        scope.EnterSystem("Colony");
        scope.SetCurrentProjects([tracked, focused, transient, Project("wrong", "Elsewhere")]);
        Assert.Equal(3, scope.Merge([], []).Count);
        Assert.False(scope.IsShown(transient));
        Assert.Contains("transient", scope.HiddenLocalIds);
        scope.SetShown(tracked, true, null);
        scope.RetainFocus(tracked);
        scope.RetainFocus(focused);
        scope.EnterSystem("Colony");
        Assert.Equal(3, scope.Merge([], []).Count);
        scope.EnterSystem("Next");
        Assert.Equal(2, scope.Merge([], []).Count);
        Assert.DoesNotContain("transient", scope.HiddenLocalIds);
        Assert.True(scope.IsLocal(focused));
        ColonizationProjectScopeState state = JsonSerializer.Deserialize<ColonizationProjectScopeState>(
            JsonSerializer.Serialize(scope.Capture())
        )!;
        var restored = new ColonizationProjectScope(state);
        Assert.True(restored.BrowseEnabled);
        Assert.True(Assert.Single(restored.Merge([], []), project => project.BuildId == "focused").IsPlanned);
        Assert.True(restored.IsShown(tracked));
        restored.ReleaseUnselected(null);
        Assert.Equal("tracked", Assert.Single(restored.Merge([], [])).BuildId);
        restored.SetShown(tracked, false, "tracked");
        Assert.Single(restored.Merge([], []));
        restored.ReleaseUnselected(null);
        Assert.Empty(restored.Merge([], []));
    }

    [Fact]
    public void DisablingBrowsingClearsCurrentDataButKeepsExplicitTracking()
    {
        var scope = new ColonizationProjectScope { BrowseEnabled = true };
        scope.EnterSystem("Colony");
        scope.SetCurrentProjects([Project("first"), Project("second")]);
        scope.SetShown(Project("first"), true, null);
        scope.BrowseEnabled = false;
        scope.EnterSystem("Colony");
        scope.SetCurrentProjects([Project("third")]);
        Assert.Equal("first", Assert.Single(scope.Merge([], [])).BuildId);
        Assert.Empty(scope.HiddenLocalIds);
    }

    [Fact]
    public void LiveDeliveryUpdatesAndCompletionReplaceRetainedSnapshotsAndAffiliationDeduplicatesThem()
    {
        var scope = new ColonizationProjectScope { BrowseEnabled = true };
        scope.SetAffiliations([], []);
        scope.EnterSystem("Colony");
        ColonizationProject project = Project("local");
        scope.SetCurrentProjects([project]);
        scope.SetShown(project, true, null);
        scope.SetCurrentProjects([project with { RemainingRequired = 50 }]);
        Assert.Equal(50, Assert.Single(scope.GetRetainedLiveProjects()).RemainingRequired);
        ColonizationProject complete = project with { RemainingRequired = 0, IsComplete = true };
        Assert.True(Assert.Single(scope.Merge([complete], [])).IsComplete);
        Assert.True(Assert.Single(scope.GetRetainedLiveProjects()).IsComplete);
        scope.EnterSystem("Elsewhere");
        Assert.True(Assert.Single(scope.Merge([], [])).IsComplete);
        scope.SetAffiliations([complete], []);
        Assert.Empty(scope.Capture().RetainedProjects);
        Assert.False(scope.IsLocal(complete));
        Assert.Single(scope.Merge([complete], []));
    }

    [Fact]
    public void AffiliatedPlansAreNeverHiddenByTheTemporaryBrowseSelection()
    {
        var scope = new ColonizationProjectScope { BrowseEnabled = true };
        ColonizationProject plan = Project("plan:1:a", plan: true);
        scope.EnterSystem("Colony");
        scope.SetCurrentProjects([plan]);
        scope.RetainFocus(plan);
        scope.SetAffiliations([], ["Colony"]);
        Assert.Empty(scope.HiddenLocalIds);
        Assert.Empty(scope.Capture().RetainedProjects);
        Assert.Single(scope.Merge([], [plan]));
    }

    [Fact]
    public void DockedProjectIsVisibleWithoutBrowsingAndExpiresUnlessDeliberatelyRetained()
    {
        var scope = new ColonizationProjectScope();
        scope.SetAffiliations([], []);
        scope.EnterSystem("Colony");
        ColonizationProject docked = Project("docked");
        scope.SetDockedProject(docked);
        Assert.True(scope.IsLocal(docked));
        Assert.Equal("docked", Assert.Single(scope.Merge([docked], [])).BuildId);
        Assert.Empty(scope.Capture().RetainedProjects);
        scope.SetDockedProject(null);
        Assert.Empty(scope.Merge([docked], []));
        scope.SetDockedProject(docked);
        scope.RetainFocus(docked);
        scope.EnterSystem("Next");
        Assert.Single(scope.Merge([], []));
        scope.ReleaseUnselected(null);
        Assert.Empty(scope.Merge([], []));
    }
}
