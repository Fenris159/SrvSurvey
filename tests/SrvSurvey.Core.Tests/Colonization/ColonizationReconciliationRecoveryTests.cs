using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationReconciliationRecoveryTests
{
    /// <summary>Rejects concurrent additions with the same name even when each editor assigned a different ID.</summary>
    [Theory]
    [InlineData("Port")]
    [InlineData("PORT")]
    public void RefusesConcurrentAdditionWithSameName(string remoteName)
    {
        var local = new ColonizationSystemSite { Id = "local", Name = "Port" };
        ColonizationSystemSiteReconciliationPlan plan = ColonizationSystemSiteReconciler.CreatePlan(
            [],
            [local with { Id = "remote", Name = remoteName }],
            [local]
        );
        Assert.False(plan.CanPublish);
        Assert.Equal("site", Assert.Single(plan.Conflicts).Field);
        Assert.Empty(plan.Update.UpdatedSites);
    }

    /// <summary>Treats replacement of a persisted site ID as deletion rather than continuity by name.</summary>
    [Fact]
    public void RefusesEditsToDeletedAndRecreatedSite()
    {
        var original = new ColonizationSystemSite
        {
            Id = "old",
            Name = "Port",
            BodyNumber = 1,
        };
        ColonizationSystemSiteReconciliationPlan plan = ColonizationSystemSiteReconciler.CreatePlan(
            [original],
            [original with { Id = "new" }],
            [original with { BodyNumber = 2 }]
        );
        Assert.False(plan.CanPublish);
        Assert.NotEmpty(plan.Conflicts);
        Assert.Empty(plan.Update.UpdatedSites);
    }
}
