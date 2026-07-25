namespace SrvSurvey.Core.Exobiology;

public enum CodexBingoNodeKind
{
    Root,
    HudCategory,
    SubClass,
    Group,
    Species,
    Entry,
}

public sealed record CodexBingoNode(
    string Key,
    string Name,
    CodexBingoNodeKind Kind,
    ExobiologyReference? Entry,
    string? Genus,
    string? Species,
    long Reward,
    IReadOnlyList<CodexBingoNode> Children)
{
    public bool IsLeaf => Children.Count == 0;
}

public sealed record CodexBingoProgress(
    CodexBingoNode Node,
    int DiscoveredCount,
    int TotalCount,
    IReadOnlyList<CodexBingoProgress> Children)
{
    public double Completion => TotalCount == 0
        ? 0
        : (double)DiscoveredCount / TotalCount;

    public bool IsComplete => TotalCount > 0 && DiscoveredCount == TotalCount;
}

public static class CodexBingoCatalog
{
    public static CodexBingoNode Build(
        IEnumerable<ExobiologyReference> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var root = new MutableNode(
            "root",
            "The Codex",
            CodexBingoNodeKind.Root);
        foreach (var entry in entries.OrderBy(
                     item => item.DisplayName ?? item.VariantName,
                     StringComparer.OrdinalIgnoreCase))
        {
            AddEntry(root, entry);
        }

        return Freeze(root);
    }

    public static CodexBingoProgress CalculateProgress(
        CodexBingoNode root,
        IReadOnlySet<long> discoveredEntryIds)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(discoveredEntryIds);
        if (root.IsLeaf)
        {
            if (root.Entry is null)
            {
                return new CodexBingoProgress(root, 0, 0, []);
            }

            var discovered = discoveredEntryIds.Contains(root.Entry.EntryId);
            return new CodexBingoProgress(root, discovered ? 1 : 0, 1, []);
        }

        var children = root.Children
            .Select(child => CalculateProgress(child, discoveredEntryIds))
            .ToArray();
        return new CodexBingoProgress(
            root,
            children.Sum(child => child.DiscoveredCount),
            children.Sum(child => child.TotalCount),
            children);
    }

    private static void AddEntry(
        MutableNode root,
        ExobiologyReference entry)
    {
        var hudCategoryName = ValueOrOther(entry.HudCategory);
        var hudCategoryDisplay = string.Equals(
                hudCategoryName,
                "None",
                StringComparison.OrdinalIgnoreCase)
            ? "None (More Thargoid)"
            : hudCategoryName;
        var category = root.GetOrAdd(
            "category:" + hudCategoryName,
            hudCategoryDisplay,
            CodexBingoNodeKind.HudCategory);

        var subClassName = ValueOrOther(entry.SubClass);
        var subClassDisplay = string.Equals(
                subClassName,
                "Shrubs",
                StringComparison.OrdinalIgnoreCase)
            ? "Frutexa (Shrubs)"
            : subClassName;
        var subClass = category.GetOrAdd(
            "subclass:" + subClassName,
            subClassDisplay,
            CodexBingoNodeKind.SubClass);

        if (IsOdysseyBiologyVariant(entry))
        {
            AddOdysseyBiologyVariant(subClass, entry);
            return;
        }

        var leafParent = subClass;
        var displayName = GetDisplayName(entry);
        if (displayName.Contains("Mollusc", StringComparison.OrdinalIgnoreCase)
            && displayName.IndexOf(' ') is var separator
            && separator >= 0
            && separator < displayName.Length - 1)
        {
            var groupName = displayName[(separator + 1)..];
            leafParent = subClass.GetOrAdd(
                "group:" + groupName,
                groupName,
                CodexBingoNodeKind.Group,
                species: groupName);
        }

        var leafName = displayName;
        var suffix = " " + leafParent.Name;
        if (leafName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(leafName, leafParent.Name, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(leafParent.Name, "Tubers", StringComparison.OrdinalIgnoreCase))
        {
            leafName = leafName[..^suffix.Length];
        }

        leafParent.AddEntry(entry, leafName);
        if (subClass.Genus is null && entry.IsBiology)
        {
            subClass.Genus = GetFirstWord(displayName);
        }
    }

    private static bool IsOdysseyBiologyVariant(ExobiologyReference entry)
    {
        return string.Equals(
                entry.Platform,
                "odyssey",
                StringComparison.OrdinalIgnoreCase)
            && entry.IsBiology
            && !entry.VariantName.Contains(
                "Ingensradices",
                StringComparison.OrdinalIgnoreCase)
            && GetDisplayName(entry).Contains(" - ", StringComparison.Ordinal);
    }

    private static void AddOdysseyBiologyVariant(
        MutableNode subClass,
        ExobiologyReference entry)
    {
        var displayName = GetDisplayName(entry);
        var variantSeparator = displayName.LastIndexOf(
            " - ",
            StringComparison.Ordinal);
        var speciesName = displayName[..variantSeparator].Trim();
        var speciesLabel = speciesName.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? speciesName;
        var variantLabel = displayName[(variantSeparator + 3)..].Trim();
        var genus = GetFirstWord(speciesName);
        subClass.Genus ??= genus;
        var species = subClass.GetOrAdd(
            "species:" + entry.SpeciesName,
            speciesLabel,
            CodexBingoNodeKind.Species,
            genus,
            speciesName,
            entry.Reward);
        species.AddEntry(entry, variantLabel, genus, speciesName);
    }

    private static string ValueOrOther(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Other" : value.Trim();
    }

    private static string GetDisplayName(ExobiologyReference entry)
    {
        return string.IsNullOrWhiteSpace(entry.DisplayName)
            ? entry.VariantName
            : entry.DisplayName.Trim();
    }

    private static string GetFirstWord(string value)
    {
        var separator = value.IndexOf(' ');
        return separator > 0 ? value[..separator] : value;
    }

    private static CodexBingoNode Freeze(MutableNode node)
    {
        return new CodexBingoNode(
            node.Key,
            node.Name,
            node.Kind,
            node.Entry,
            node.Genus,
            node.Species,
            node.Reward,
            node.Children.Values
                .OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(child => child.Key, StringComparer.Ordinal)
                .Select(Freeze)
                .ToArray());
    }

    private sealed class MutableNode(
        string key,
        string name,
        CodexBingoNodeKind kind,
        string? genus = null,
        string? species = null,
        long reward = 0)
    {
        public string Key { get; } = key;

        public string Name { get; } = name;

        public CodexBingoNodeKind Kind { get; } = kind;

        public ExobiologyReference? Entry { get; private set; }

        public string? Genus { get; set; } = genus;

        public string? Species { get; } = species;

        public long Reward { get; } = reward;

        public Dictionary<string, MutableNode> Children { get; } =
            new(StringComparer.Ordinal);

        public MutableNode GetOrAdd(
            string childKey,
            string childName,
            CodexBingoNodeKind childKind,
            string? genus = null,
            string? species = null,
            long reward = 0)
        {
            if (!Children.TryGetValue(childKey, out var child))
            {
                child = new MutableNode(
                    childKey,
                    childName,
                    childKind,
                    genus,
                    species,
                    reward);
                Children.Add(childKey, child);
            }

            return child;
        }

        public void AddEntry(
            ExobiologyReference entry,
            string name,
            string? genus = null,
            string? species = null)
        {
            var child = new MutableNode(
                "entry:" + entry.EntryId,
                name,
                CodexBingoNodeKind.Entry,
                genus,
                species,
                entry.Reward)
            {
                Entry = entry,
            };
            Children.Add(child.Key, child);
        }
    }
}
