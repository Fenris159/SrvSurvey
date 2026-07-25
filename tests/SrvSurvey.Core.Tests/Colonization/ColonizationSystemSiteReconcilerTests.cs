using System.Text.Json;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationSystemSiteReconcilerTests
{
    [Fact]
    public void LocalEditMergesOverUnrelatedRemoteChangeAndPreservesExtensions()
    {
        var baseline = Site("site-1", "Port", body: 1, buildType: "outpost");
        var latest = baseline with
        {
            MarketId = 42,
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["remoteFuture"] = JsonSerializer.SerializeToElement(true),
            },
        };
        var edited = baseline with { BodyNumber = 2 };

        var plan = ColonizationSystemSiteReconciler.CreatePlan(
            [baseline],
            [latest],
            [edited]);

        Assert.True(plan.CanPublish);
        var update = Assert.Single(plan.Update.UpdatedSites);
        Assert.Equal(2, update.BodyNumber);
        Assert.Equal(42, update.MarketId);
        Assert.True(update.ExtensionData["remoteFuture"].GetBoolean());
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void SameFieldConcurrentEditProducesConflictInsteadOfOverwrite()
    {
        var baseline = Site("site-1", "Port", body: 1);
        var latest = baseline with { BodyNumber = 2 };
        var edited = baseline with { BodyNumber = 3 };

        var plan = ColonizationSystemSiteReconciler.CreatePlan(
            [baseline],
            [latest],
            [edited]);

        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal("bodyNum", conflict.Field);
        Assert.Empty(plan.Update.UpdatedSites);
        Assert.False(plan.CanPublish);
    }

    [Fact]
    public void DeletionRequiresStableRemoteSiteAndPersistedId()
    {
        var stable = Site("stable", "Stable", body: 1);
        var changed = Site("changed", "Changed", body: 2);
        var latestChanged = changed with { BuildType = "orbis" };
        var noId = Site(string.Empty, "No Id", body: 3);

        var plan = ColonizationSystemSiteReconciler.CreatePlan(
            [stable, changed, noId],
            [stable, latestChanged, noId],
            []);

        Assert.Equal(["stable"], plan.Update.DeletedSiteIds);
        Assert.Equal(2, plan.Conflicts.Count);
        Assert.Contains(plan.Conflicts, conflict => conflict.Site.Contains("Changed"));
        Assert.Contains(plan.Conflicts, conflict => conflict.Site == "No Id");
    }

    [Fact]
    public void RemoteOnlySitesAreUntouchedAndNewLocalSiteIsAdded()
    {
        var baseline = Site("known", "Known", body: 1);
        var remoteOnly = Site("remote", "Remote", body: 2);
        var localNew = Site("y123", "Local", body: 3);

        var plan = ColonizationSystemSiteReconciler.CreatePlan(
            [baseline],
            [baseline, remoteOnly],
            [baseline, localNew]);

        Assert.Equal("Local", Assert.Single(plan.Update.UpdatedSites).Name);
        Assert.Empty(plan.Update.DeletedSiteIds);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void DuplicateNamesAreRejectedBeforePlanning()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            ColonizationSystemSiteReconciler.CreatePlan(
                [],
                [],
                [
                    Site("one", "Port", body: 1),
                    Site("two", "PORT", body: 2),
                ]));

        Assert.Contains("duplicate name", exception.Message);
    }

    private static ColonizationSystemSite Site(
        string id,
        string name,
        int body,
        string? buildType = null)
    {
        return new ColonizationSystemSite
        {
            Id = id,
            Name = name,
            BodyNumber = body,
            BuildType = buildType,
            Status = ColonizationSystemSiteStatus.Complete,
        };
    }
}
