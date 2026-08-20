using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class OverlayVisibilityPolicyTests
{
    [Fact]
    public void OverlayIdentityIsValidatedAndOrdinal()
    {
        var identity = new OverlayId("PlotJumpInfo");

        Assert.Equal("PlotJumpInfo", identity.Value);
        Assert.Equal("PlotJumpInfo", identity.ToString());
        Assert.Equal(identity, new OverlayId("PlotJumpInfo"));
        Assert.NotEqual(identity, new OverlayId("plotjumpinfo"));
        Assert.Throws<ArgumentException>(() => new OverlayId(" "));
    }

    [Fact]
    public void CatalogResolvesDefinitionsByValidatedIdentity()
    {
        var identity = new OverlayId("PlotStationInfo");

        var definition = OverlayLayoutCatalog.GetRequired(identity);

        Assert.Equal(identity, definition.Id);
        Assert.Equal(identity.Value, definition.Name);
    }

    [Fact]
    public void RequestedEligibleOverlayCanBeHostedAndPresented()
    {
        var decision = OverlayVisibilityPolicy.Evaluate(
            CreateVisibleFacts());

        Assert.True(decision.ShouldHost);
        Assert.True(decision.ShouldPresent);
        Assert.Equal(OverlayVisibilityReason.None, decision.Reasons);
    }

    [Fact]
    public void PolicyPermissionIsSeparateFromIntentAndHostEligibility()
    {
        var notRequested = OverlayVisibilityPolicy.Evaluate(
            CreateVisibleFacts() with { Requested = false });
        var hostIneligible = OverlayVisibilityPolicy.Evaluate(
            CreateVisibleFacts() with { HostEligible = false });
        var userDisabled = OverlayVisibilityPolicy.Evaluate(
            CreateVisibleFacts() with { UserEnabled = false });
        var globallySuppressed = OverlayVisibilityPolicy.Evaluate(
            CreateVisibleFacts() with { ManualSuppressed = true });

        Assert.True(notRequested.Permitted);
        Assert.True(hostIneligible.Permitted);
        Assert.False(userDisabled.Permitted);
        Assert.False(globallySuppressed.Permitted);
    }

    [Fact]
    public void LifecycleBlockerPreventsHostingAndPresentation()
    {
        (OverlayVisibilityFacts Facts, OverlayVisibilityReason Reason)[] cases =
        {
            (
                CreateVisibleFacts() with { Requested = false },
                OverlayVisibilityReason.DomainNotRequested),
            (
                CreateVisibleFacts() with { HostEligible = false },
                OverlayVisibilityReason.HostIneligible),
            (
                CreateVisibleFacts() with { ManualSuppressed = true },
                OverlayVisibilityReason.ManualSuppressed),
            (
                CreateVisibleFacts() with { SuitSuppressed = true },
                OverlayVisibilityReason.SuitSuppressed),
            (
                CreateVisibleFacts() with { SessionSuppressed = true },
                OverlayVisibilityReason.SessionSuppressed),
            (
                CreateVisibleFacts() with { PriorityObscured = true },
                OverlayVisibilityReason.PriorityObscured),
        };

        foreach (var (facts, expectedReason) in cases)
        {
            var decision = OverlayVisibilityPolicy.Evaluate(facts);

            Assert.False(decision.ShouldHost);
            Assert.False(decision.ShouldPresent);
            Assert.Equal(expectedReason, decision.Reasons);
        }
    }

    [Fact]
    public void PresentationBlockerKeepsTheHostedLifecycleAlive()
    {
        (OverlayVisibilityFacts Facts, OverlayVisibilityReason Reason)[] cases =
        {
            (
                CreateVisibleFacts() with { UserEnabled = false },
                OverlayVisibilityReason.UserDisabled),
            (
                CreateVisibleFacts() with { GalaxyMapAllowed = false },
                OverlayVisibilityReason.GalaxyMapExcluded),
            (
                CreateVisibleFacts() with { EditorSuppressed = true },
                OverlayVisibilityReason.EditorSuppressed),
        };

        foreach (var (facts, expectedReason) in cases)
        {
            var decision = OverlayVisibilityPolicy.Evaluate(facts);

            Assert.True(decision.ShouldHost);
            Assert.False(decision.ShouldPresent);
            Assert.Equal(expectedReason, decision.Reasons);
        }
    }

    [Fact]
    public void DecisionRetainsEveryActiveBlockingReason()
    {
        var facts = CreateVisibleFacts() with
        {
            HostEligible = false,
            UserEnabled = false,
            GalaxyMapAllowed = false,
            EditorSuppressed = true,
            ManualSuppressed = true,
            SuitSuppressed = true,
            SessionSuppressed = true,
            PriorityObscured = true,
        };

        var decision = OverlayVisibilityPolicy.Evaluate(facts);

        Assert.False(decision.ShouldHost);
        Assert.False(decision.ShouldPresent);
        Assert.Equal(
            OverlayVisibilityReason.HostIneligible
                | OverlayVisibilityReason.UserDisabled
                | OverlayVisibilityReason.GalaxyMapExcluded
                | OverlayVisibilityReason.EditorSuppressed
                | OverlayVisibilityReason.ManualSuppressed
                | OverlayVisibilityReason.SuitSuppressed
                | OverlayVisibilityReason.SessionSuppressed
                | OverlayVisibilityReason.PriorityObscured,
            decision.Reasons);
    }

    [Fact]
    public void ClearingTemporaryBlockersRestoresPresentationFromCurrentFacts()
    {
        var blockedFacts = CreateVisibleFacts() with
        {
            UserEnabled = false,
            GalaxyMapAllowed = false,
        };

        var blocked = OverlayVisibilityPolicy.Evaluate(blockedFacts);
        var restored = OverlayVisibilityPolicy.Evaluate(blockedFacts with
        {
            UserEnabled = true,
            GalaxyMapAllowed = true,
        });

        Assert.True(blocked.ShouldHost);
        Assert.False(blocked.ShouldPresent);
        Assert.True(restored.ShouldHost);
        Assert.True(restored.ShouldPresent);
        Assert.Equal(OverlayVisibilityReason.None, restored.Reasons);
    }

    private static OverlayVisibilityFacts CreateVisibleFacts() => new(
        Requested: true,
        HostEligible: true,
        UserEnabled: true,
        GalaxyMapAllowed: true,
        EditorSuppressed: false,
        ManualSuppressed: false,
        SuitSuppressed: false,
        SessionSuppressed: false,
        PriorityObscured: false);
}
