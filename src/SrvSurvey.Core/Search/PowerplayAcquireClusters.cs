namespace SrvSurvey.Core.Search;

/// <summary>Groups Acquire sell systems that share at least one mining system, keeping result order.</summary>
public static class PowerplayAcquireClusters
{
    public static IReadOnlyList<IReadOnlyList<T>> Group<T>(
        IReadOnlyList<T> sellSystems,
        Func<T, IEnumerable<string>> miningSystems
    )
    {
        int[] parents = Enumerable.Range(0, sellSystems.Count).ToArray();
        var firstByMiningSystem = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < sellSystems.Count; index++)
        {
            foreach (string system in miningSystems(sellSystems[index]))
            {
                if (firstByMiningSystem.TryGetValue(system, out int first))
                {
                    Join(parents, first, index);
                }
                else
                {
                    firstByMiningSystem.Add(system, index);
                }
            }
        }

        return sellSystems
            .Select((sell, index) => (Sell: sell, Index: index, Root: Root(parents, index)))
            .GroupBy(item => item.Root)
            .OrderBy(group => group.Min(item => item.Index))
            .Select(group => (IReadOnlyList<T>)group.OrderBy(item => item.Index).Select(item => item.Sell).ToArray())
            .ToArray();
    }

    private static int Root(int[] parents, int index)
    {
        while (parents[index] != index)
        {
            parents[index] = parents[parents[index]];
            index = parents[index];
        }

        return index;
    }

    private static void Join(int[] parents, int first, int second) =>
        parents[Root(parents, second)] = Root(parents, first);
}
