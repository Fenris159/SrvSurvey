namespace SrvSurvey.Desktop.Platform.Overlay;

[Flags]
internal enum OverlayVisibilityReasons
{
    None = 0,
    DomainNotRequested = 1 << 0,
    HostIneligible = 1 << 1,
    ManualSuppressed = 1 << 2,
    SuitSuppressed = 1 << 3,
    SessionSuppressed = 1 << 4,
    PriorityObscured = 1 << 5,
    UserDisabled = 1 << 6,
    GalaxyMapExcluded = 1 << 7,
    EditorSuppressed = 1 << 8,
}

internal readonly record struct OverlayVisibilityFacts(
    bool Requested,
    bool HostEligible,
    bool UserEnabled,
    bool GalaxyMapAllowed,
    bool EditorSuppressed,
    bool ManualSuppressed,
    bool SuitSuppressed,
    bool SessionSuppressed,
    bool PriorityObscured);

internal readonly record struct OverlayVisibilityDecision(
    bool Permitted,
    bool ShouldHost,
    bool ShouldPresent,
    OverlayVisibilityReasons Reasons);

internal static class OverlayVisibilityPolicy
{
    private const OverlayVisibilityReasons LifecycleReasons =
        OverlayVisibilityReasons.DomainNotRequested
        | OverlayVisibilityReasons.HostIneligible
        | OverlayVisibilityReasons.ManualSuppressed
        | OverlayVisibilityReasons.SuitSuppressed
        | OverlayVisibilityReasons.SessionSuppressed
        | OverlayVisibilityReasons.PriorityObscured;
    private const OverlayVisibilityReasons PolicyReasons =
        OverlayVisibilityReasons.ManualSuppressed
        | OverlayVisibilityReasons.SuitSuppressed
        | OverlayVisibilityReasons.SessionSuppressed
        | OverlayVisibilityReasons.PriorityObscured
        | OverlayVisibilityReasons.UserDisabled
        | OverlayVisibilityReasons.GalaxyMapExcluded
        | OverlayVisibilityReasons.EditorSuppressed;

    internal static OverlayVisibilityDecision Evaluate(
        OverlayVisibilityFacts facts)
    {
        var reasons = OverlayVisibilityReasons.None;
        if (!facts.Requested)
        {
            reasons |= OverlayVisibilityReasons.DomainNotRequested;
        }

        if (!facts.HostEligible)
        {
            reasons |= OverlayVisibilityReasons.HostIneligible;
        }

        if (facts.ManualSuppressed)
        {
            reasons |= OverlayVisibilityReasons.ManualSuppressed;
        }

        if (facts.SuitSuppressed)
        {
            reasons |= OverlayVisibilityReasons.SuitSuppressed;
        }

        if (facts.SessionSuppressed)
        {
            reasons |= OverlayVisibilityReasons.SessionSuppressed;
        }

        if (facts.PriorityObscured)
        {
            reasons |= OverlayVisibilityReasons.PriorityObscured;
        }

        if (!facts.UserEnabled)
        {
            reasons |= OverlayVisibilityReasons.UserDisabled;
        }

        if (!facts.GalaxyMapAllowed)
        {
            reasons |= OverlayVisibilityReasons.GalaxyMapExcluded;
        }

        if (facts.EditorSuppressed)
        {
            reasons |= OverlayVisibilityReasons.EditorSuppressed;
        }

        var shouldHost = (reasons & LifecycleReasons) == 0;
        return new OverlayVisibilityDecision(
            (reasons & PolicyReasons) == 0,
            shouldHost,
            shouldHost && reasons == OverlayVisibilityReasons.None,
            reasons);
    }
}
