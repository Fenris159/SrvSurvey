using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationReconciliationRecoveryTests
{
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
