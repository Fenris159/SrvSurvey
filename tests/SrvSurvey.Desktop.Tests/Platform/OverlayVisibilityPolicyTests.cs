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

        OverlayLayoutDefinition definition = OverlayLayoutCatalog.GetRequired(identity);

        Assert.Equal(identity, definition.Id);
        Assert.Equal(identity.Value, definition.Name);
    }

    [Fact]
    public void RequestedEligibleOverlayCanBeHostedAndPresented()
    {
        OverlayVisibilityDecision decision = OverlayVisibilityPolicy.Evaluate(CreateVisibleFacts());

        Assert.True(decision.ShouldHost);
        Assert.True(decision.ShouldPresent);
        Assert.Equal(OverlayVisibilityReasons.None, decision.Reasons);
    }

    [Fact]
    public void PolicyPermissionIsSeparateFromIntentAndHostEligibility()
    {
        OverlayVisibilityDecision notRequested = OverlayVisibilityPolicy.Evaluate(
            CreateVisibleFacts() with
            {
                Requested = false,
            }
        );
        OverlayVisibilityDecision hostIneligible = OverlayVisibilityPolicy.Evaluate(
            CreateVisibleFacts() with
            {
                HostEligible = false,
            }
        );
        OverlayVisibilityDecision userDisabled = OverlayVisibilityPolicy.Evaluate(
            CreateVisibleFacts() with
            {
                UserEnabled = false,
            }
        );
        OverlayVisibilityDecision globallySuppressed = OverlayVisibilityPolicy.Evaluate(
            CreateVisibleFacts() with
            {
                ManualSuppressed = true,
            }
        );

        Assert.True(notRequested.Permitted);
        Assert.True(hostIneligible.Permitted);
        Assert.False(userDisabled.Permitted);
        Assert.False(globallySuppressed.Permitted);
    }

    [Fact]
    public void LifecycleBlockerPreventsHostingAndPresentation()
    {
        (OverlayVisibilityFacts Facts, OverlayVisibilityReasons Reason)[] cases =
        {
            (CreateVisibleFacts() with { Requested = false }, OverlayVisibilityReasons.DomainNotRequested),
            (CreateVisibleFacts() with { HostEligible = false }, OverlayVisibilityReasons.HostIneligible),
            (CreateVisibleFacts() with { ManualSuppressed = true }, OverlayVisibilityReasons.ManualSuppressed),
            (CreateVisibleFacts() with { SuitSuppressed = true }, OverlayVisibilityReasons.SuitSuppressed),
            (CreateVisibleFacts() with { SessionSuppressed = true }, OverlayVisibilityReasons.SessionSuppressed),
            (CreateVisibleFacts() with { PriorityObscured = true }, OverlayVisibilityReasons.PriorityObscured),
        };

        foreach ((OverlayVisibilityFacts facts, OverlayVisibilityReasons expectedReason) in cases)
        {
            OverlayVisibilityDecision decision = OverlayVisibilityPolicy.Evaluate(facts);

            Assert.False(decision.ShouldHost);
            Assert.False(decision.ShouldPresent);
            Assert.Equal(expectedReason, decision.Reasons);
        }
    }

    [Fact]
    public void PresentationBlockerKeepsTheHostedLifecycleAlive()
    {
        (OverlayVisibilityFacts Facts, OverlayVisibilityReasons Reason)[] cases =
        {
            (CreateVisibleFacts() with { UserEnabled = false }, OverlayVisibilityReasons.UserDisabled),
            (CreateVisibleFacts() with { GalaxyMapAllowed = false }, OverlayVisibilityReasons.GalaxyMapExcluded),
            (CreateVisibleFacts() with { EditorSuppressed = true }, OverlayVisibilityReasons.EditorSuppressed),
        };

        foreach ((OverlayVisibilityFacts facts, OverlayVisibilityReasons expectedReason) in cases)
        {
            OverlayVisibilityDecision decision = OverlayVisibilityPolicy.Evaluate(facts);

            Assert.True(decision.ShouldHost);
            Assert.False(decision.ShouldPresent);
            Assert.Equal(expectedReason, decision.Reasons);
        }
    }

    [Fact]
    public void DecisionRetainsEveryActiveBlockingReason()
    {
        OverlayVisibilityFacts facts = CreateVisibleFacts() with
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

        OverlayVisibilityDecision decision = OverlayVisibilityPolicy.Evaluate(facts);

        Assert.False(decision.ShouldHost);
        Assert.False(decision.ShouldPresent);
        Assert.Equal(
            OverlayVisibilityReasons.HostIneligible
                | OverlayVisibilityReasons.UserDisabled
                | OverlayVisibilityReasons.GalaxyMapExcluded
                | OverlayVisibilityReasons.EditorSuppressed
                | OverlayVisibilityReasons.ManualSuppressed
                | OverlayVisibilityReasons.SuitSuppressed
                | OverlayVisibilityReasons.SessionSuppressed
                | OverlayVisibilityReasons.PriorityObscured,
            decision.Reasons
        );
    }

    [Fact]
    public void ClearingTemporaryBlockersRestoresPresentationFromCurrentFacts()
    {
        OverlayVisibilityFacts blockedFacts = CreateVisibleFacts() with
        {
            UserEnabled = false,
            GalaxyMapAllowed = false,
        };

        OverlayVisibilityDecision blocked = OverlayVisibilityPolicy.Evaluate(blockedFacts);
        OverlayVisibilityDecision restored = OverlayVisibilityPolicy.Evaluate(
            blockedFacts with
            {
                UserEnabled = true,
                GalaxyMapAllowed = true,
            }
        );

        Assert.True(blocked.ShouldHost);
        Assert.False(blocked.ShouldPresent);
        Assert.True(restored.ShouldHost);
        Assert.True(restored.ShouldPresent);
        Assert.Equal(OverlayVisibilityReasons.None, restored.Reasons);
    }

    private static OverlayVisibilityFacts CreateVisibleFacts() =>
        new(
            Requested: true,
            HostEligible: true,
            UserEnabled: true,
            GalaxyMapAllowed: true,
            EditorSuppressed: false,
            ManualSuppressed: false,
            SuitSuppressed: false,
            SessionSuppressed: false,
            PriorityObscured: false
        );
}
