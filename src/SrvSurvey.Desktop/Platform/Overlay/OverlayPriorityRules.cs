namespace SrvSurvey.Desktop.Platform.Overlay;

[Flags]
internal enum OverlayPriorityFacts
{
    None = 0,
    FssInfoForced = 1 << 0,
    BodyInfoForced = 1 << 1,
}

internal sealed record OverlayPriorityRule(
    OverlayId Target,
    IReadOnlyList<OverlayId> PresentedBlockers,
    OverlayPriorityFacts FactBlockers = OverlayPriorityFacts.None,
    OverlayPriorityFacts UnlessFacts = OverlayPriorityFacts.None);

internal static class OverlayPriorityRules
{
    private const string Guardians = "PlotGuardians";
    private const string HumanSite = "PlotHumanSite";

    private static readonly IReadOnlyList<OverlayPriorityRule> rules =
    [
        Rule(
            "PlotFSSInfo",
            ["PlotGuardianSystem"],
            unlessFacts: OverlayPriorityFacts.FssInfoForced),
        Rule(
            "PlotBodyInfo",
            ["PlotGuardianSystem"],
            unlessFacts: OverlayPriorityFacts.BodyInfoForced),
        Rule("PlotBioSystem", [Guardians, HumanSite]),
        Rule(
            "PlotBioStatus",
            [Guardians, HumanSite, "PlotJumpInfo"]),
        Rule(
            "PlotPriorScans",
            [Guardians, HumanSite, "PlotStationInfo"]),
        Rule("PlotGrounded", [Guardians, HumanSite]),
        Rule("PlotGuardianStatus", ["PlotJumpInfo"]),
        Rule(
            "PlotGuardianSystem",
            [],
            factBlockers: OverlayPriorityFacts.FssInfoForced
                | OverlayPriorityFacts.BodyInfoForced),
    ];

    static OverlayPriorityRules()
    {
        ValidateAcyclic();
    }

    internal static bool IsObscured(
        OverlayId target,
        Func<OverlayId, bool> isPresented,
        OverlayPriorityFacts facts)
    {
        ArgumentNullException.ThrowIfNull(isPresented);
        var rule = rules.SingleOrDefault(candidate => candidate.Target == target);
        if (rule is null)
        {
            return false;
        }

        if ((rule.FactBlockers & facts) != 0)
        {
            return true;
        }

        return (rule.UnlessFacts & facts) == 0
            && rule.PresentedBlockers.Any(isPresented);
    }

    internal static void ValidateAcyclic()
    {
        var edges = rules
            .SelectMany(rule => rule.PresentedBlockers.Select(source =>
                (Source: source, rule.Target)))
            .GroupBy(edge => edge.Source)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.Target).ToArray());
        var visited = new HashSet<OverlayId>();
        var visiting = new HashSet<OverlayId>();
        foreach (var overlay in rules.Select(rule => rule.Target)
                     .Concat(rules.SelectMany(rule => rule.PresentedBlockers))
                     .Distinct())
        {
            Visit(overlay, edges, visiting, visited);
        }
    }

    private static void Visit(
        OverlayId overlay,
        IReadOnlyDictionary<OverlayId, OverlayId[]> edges,
        HashSet<OverlayId> visiting,
        HashSet<OverlayId> visited)
    {
        if (visited.Contains(overlay))
        {
            return;
        }

        if (!visiting.Add(overlay))
        {
            throw new InvalidOperationException(
                $"Overlay priority rules contain a cycle at '{overlay}'.");
        }

        if (edges.TryGetValue(overlay, out var targets))
        {
            foreach (var target in targets)
            {
                Visit(target, edges, visiting, visited);
            }
        }

        visiting.Remove(overlay);
        visited.Add(overlay);
    }

    private static OverlayPriorityRule Rule(
        string target,
        IReadOnlyList<string> presentedBlockers,
        OverlayPriorityFacts factBlockers = OverlayPriorityFacts.None,
        OverlayPriorityFacts unlessFacts = OverlayPriorityFacts.None)
    {
        return new OverlayPriorityRule(
            GetId(target),
            presentedBlockers.Select(GetId).ToArray(),
            factBlockers,
            unlessFacts);
    }

    private static OverlayId GetId(string plotterName) =>
        OverlayLayoutCatalog.GetRequired(plotterName).Id;
}
