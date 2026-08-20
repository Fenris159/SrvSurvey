namespace SrvSurvey.Desktop.Platform.Overlay;

[Flags]
internal enum OverlayPriorityFact
{
    None = 0,
    FssInfoForced = 1 << 0,
    BodyInfoForced = 1 << 1,
}

internal sealed record OverlayPriorityRule(
    OverlayId Target,
    IReadOnlyList<OverlayId> PresentedBlockers,
    OverlayPriorityFact FactBlockers = OverlayPriorityFact.None,
    OverlayPriorityFact UnlessFacts = OverlayPriorityFact.None);

internal static class OverlayPriorityRules
{
    private static readonly IReadOnlyList<OverlayPriorityRule> rules =
    [
        Rule(
            "PlotFSSInfo",
            ["PlotGuardianSystem"],
            unlessFacts: OverlayPriorityFact.FssInfoForced),
        Rule(
            "PlotBodyInfo",
            ["PlotGuardianSystem"],
            unlessFacts: OverlayPriorityFact.BodyInfoForced),
        Rule("PlotBioSystem", ["PlotGuardians", "PlotHumanSite"]),
        Rule(
            "PlotBioStatus",
            ["PlotGuardians", "PlotHumanSite", "PlotJumpInfo"]),
        Rule(
            "PlotPriorScans",
            ["PlotGuardians", "PlotHumanSite", "PlotStationInfo"]),
        Rule("PlotGrounded", ["PlotGuardians", "PlotHumanSite"]),
        Rule("PlotGuardianStatus", ["PlotJumpInfo"]),
        Rule(
            "PlotGuardianSystem",
            [],
            factBlockers: OverlayPriorityFact.FssInfoForced
                | OverlayPriorityFact.BodyInfoForced),
    ];

    static OverlayPriorityRules()
    {
        ValidateAcyclic();
    }

    internal static bool IsObscured(
        OverlayId target,
        Func<OverlayId, bool> isPresented,
        OverlayPriorityFact facts)
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
        OverlayPriorityFact factBlockers = OverlayPriorityFact.None,
        OverlayPriorityFact unlessFacts = OverlayPriorityFact.None)
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
