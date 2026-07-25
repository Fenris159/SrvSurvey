using SrvSurvey.Core.Exobiology;

namespace SrvSurvey.Core.Tests.Exobiology;

public sealed class CodexBingoCatalogTests
{
    [Fact]
    public void EmptyCatalogHasZeroProgress()
    {
        var root = CodexBingoCatalog.Build([]);

        var progress = CodexBingoCatalog.CalculateProgress(
            root,
            new HashSet<long>());

        Assert.Equal(0, progress.TotalCount);
        Assert.Equal(0, progress.Completion);
        Assert.False(progress.IsComplete);
    }

    [Fact]
    public void EmbeddedCatalogBuildsCompleteLegacyHierarchy()
    {
        var references = ExobiologyReferenceCatalog.LoadEmbedded();

        var root = CodexBingoCatalog.Build(references.Entries);
        var leaves = Flatten(root)
            .Where(node => node.Entry is not null)
            .ToArray();

        Assert.Equal("The Codex", root.Name);
        Assert.Equal(references.Count, leaves.Length);
        Assert.Equal(
            references.Count,
            leaves.Select(node => node.Entry!.EntryId).Distinct().Count());
        var biology = Assert.Single(
            root.Children,
            node => node.Name == "Biology");
        var aleoids = Assert.Single(
            biology.Children,
            node => node.Name == "Aleoids");
        var arcus = Assert.Single(
            aleoids.Children,
            node => node.Name == "Arcus");
        Assert.Contains(arcus.Children, node => node.Name == "Green");
        Assert.All(arcus.Children, node => Assert.NotNull(node.Entry));
    }

    [Fact]
    public void ProgressAggregatesOnlyTheSelectedLedger()
    {
        var references = ExobiologyReferenceCatalog.LoadEmbedded();
        var root = CodexBingoCatalog.Build(references.Entries);
        var discovered = references.Entries.Take(2)
            .Select(entry => entry.EntryId)
            .ToHashSet();

        var progress = CodexBingoCatalog.CalculateProgress(root, discovered);

        Assert.Equal(references.Count, progress.TotalCount);
        Assert.Equal(2, progress.DiscoveredCount);
        Assert.Equal(2d / references.Count, progress.Completion, 10);
        Assert.Equal(
            2,
            Flatten(progress).Count(node =>
                node.Node.Entry is not null && node.IsComplete));
    }

    private static IEnumerable<CodexBingoNode> Flatten(CodexBingoNode root)
    {
        yield return root;
        foreach (var child in root.Children.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    private static IEnumerable<CodexBingoProgress> Flatten(
        CodexBingoProgress root)
    {
        yield return root;
        foreach (var child in root.Children.SelectMany(Flatten))
        {
            yield return child;
        }
    }
}
