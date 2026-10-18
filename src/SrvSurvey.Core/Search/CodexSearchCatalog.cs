using System.Text.Json;
using SrvSurvey.Core.Exobiology;

namespace SrvSurvey.Core.Search;

/// <summary>A selected entry or group retains its explicit descendants and whether individual colors matter.</summary>
public sealed record CodexSearchQuery(string Label, IReadOnlyList<CodexSearchEntry> Entries, bool MatchVariants);

public sealed record CodexSearchEntry(string Name, string? HudCategory);

public sealed record CodexSearchOption(string Label, CodexSearchQuery Query);

/// <summary>One Spansh landmark predicate; value lists within each field are OR, separate predicates are AND.</summary>
public sealed record CodexLandmarkFilter(string? Type, IReadOnlyList<string> Subtypes, IReadOnlyList<string> Variants);

public sealed record CodexSearchPlan(
    IReadOnlyList<CodexLandmarkFilter> Landmarks,
    IReadOnlyList<string> CanonnNames,
    IReadOnlyList<string> UnsupportedNames
);

/// <summary>Maps Codex names using first-party Spansh values without inventing genus or color relationships.</summary>
public sealed class CodexSearchCatalog
{
    private readonly HashSet<string> subtypes;
    private readonly HashSet<string> variants;

    public CodexSearchCatalog()
    {
        using Stream stream =
            typeof(CodexSearchCatalog).Assembly.GetManifestResourceStream(
                "SrvSurvey.Core.Resources.spansh-codex-landmarks.json"
            ) ?? throw new InvalidOperationException("The Spansh Codex landmark catalog is missing.");
        using var document = JsonDocument.Parse(stream);
        JsonElement values = document.RootElement.GetProperty("values");
        Types = ReadValues(values, "type");
        Subtypes = ReadValues(values, "subtype");
        Variants = ReadValues(values, "variant");
        subtypes = new(Subtypes, StringComparer.OrdinalIgnoreCase);
        variants = new(Variants, StringComparer.OrdinalIgnoreCase);
        CodexBingoNode root = CodexBingoCatalog.Build(ExobiologyReferenceCatalog.LoadEmbedded().Entries);
        Options = Nodes(root)
            .SelectMany(node => OptionsForNode(node))
            .DistinctBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
            .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> Types { get; }
    public IReadOnlyList<string> Subtypes { get; }
    public IReadOnlyList<string> Variants { get; }
    public IReadOnlyList<CodexSearchOption> Options { get; }

    /// <summary>Unknown typed names remain searchable through Canonn rather than being rejected by a local list.</summary>
    public CodexSearchQuery Resolve(string name) =>
        Options.FirstOrDefault(option => option.Label.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))?.Query
        ?? new(name.Trim(), [new(name.Trim(), null)], true);

    public static CodexSearchQuery FromNode(CodexBingoNode node) =>
        FromEntries(Label(node), Entries(node), node.Entry is not null);

    public static CodexSearchQuery FromEntries(
        string label,
        IEnumerable<ExobiologyReference> entries,
        bool matchVariants
    ) =>
        new(
            label,
            entries
                .Select(entry => new CodexSearchEntry(entry.DisplayName ?? entry.VariantName, entry.HudCategory))
                .DistinctBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            matchVariants
        );

    /// <summary>Uses a single subtype OR list for broad groups, retaining color subsets only for exact-entry searches.</summary>
    public CodexSearchPlan Plan(CodexSearchQuery query)
    {
        var matched = new List<(string Subtype, string? Variant)>();
        var canonn = new List<string>();
        var unsupported = new List<string>();
        foreach (
            CodexSearchEntry entry in query.Entries.DistinctBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
        )
        {
            if (subtypes.Contains(entry.Name))
            {
                matched.Add((entry.Name, null));
                continue;
            }
            int separator = entry.Name.LastIndexOf(" - ", StringComparison.Ordinal);
            if (
                separator > 0
                && subtypes.Contains(entry.Name[..separator])
                && variants.Contains(entry.Name[(separator + 3)..])
            )
            {
                matched.Add((entry.Name[..separator], query.MatchVariants ? entry.Name[(separator + 3)..] : null));
            }
            else if (string.Equals(entry.HudCategory, "None", StringComparison.OrdinalIgnoreCase))
            {
                unsupported.Add(entry.Name);
            }
            else
            {
                canonn.Add(entry.Name);
            }
        }

        // Keep species-specific color sets together; multiple landmark objects would incorrectly mean AND.
        CodexLandmarkFilter[] filters = matched
            .GroupBy(match => match.Subtype, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Subtype = group.Key,
                Colors = group.Any(match => match.Variant is null)
                    ? []
                    : group
                        .Select(match => match.Variant!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
            })
            .GroupBy(group => string.Join('\n', group.Colors), StringComparer.OrdinalIgnoreCase)
            .Select(group => new CodexLandmarkFilter(
                null,
                group.Select(item => item.Subtype).ToArray(),
                group.First().Colors
            ))
            .ToArray();
        return new(filters, canonn, unsupported);
    }

    private static string[] ReadValues(JsonElement values, string name) =>
        values
            .GetProperty(name)
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string Label(CodexBingoNode node) => node.Entry?.DisplayName ?? node.Species ?? node.Name;

    private static IEnumerable<CodexSearchOption> OptionsForNode(CodexBingoNode node)
    {
        CodexSearchQuery query = FromNode(node);
        yield return new(Label(node), query);
        if (
            !string.IsNullOrWhiteSpace(node.Genus)
            && node.Children.Any(child => child.Kind == CodexBingoNodeKind.Species)
        )
        {
            yield return new(node.Genus, query with { Label = node.Genus });
        }
    }

    private static IEnumerable<CodexBingoNode> Nodes(CodexBingoNode node) =>
        new[] { node }.Concat(node.Children.SelectMany(Nodes));

    private static IEnumerable<ExobiologyReference> Entries(CodexBingoNode node) =>
        node.Entry is { } entry ? [entry] : node.Children.SelectMany(Entries);
}
