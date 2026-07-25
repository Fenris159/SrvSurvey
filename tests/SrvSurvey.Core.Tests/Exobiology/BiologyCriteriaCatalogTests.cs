using System.Text;
using SrvSurvey.Core.Exobiology;

namespace SrvSurvey.Core.Tests.Exobiology;

public sealed class BiologyCriteriaCatalogTests
{
    [Fact]
    public void EmbeddedCatalogLoadsEveryShippedCriteriaFile()
    {
        var catalog = BiologyCriteriaCatalog.LoadEmbedded();

        Assert.Equal(4, BiologyCriteriaCatalog.EngineVersion);
        Assert.Equal(21, catalog.Roots.Count);
        Assert.Equal(21, catalog.SourceNames.Count);
        Assert.Contains(catalog.Roots, root => root.Genus == "Aleoida");
        Assert.Contains(catalog.Roots, root => root.Genus == "Brain Trees");
    }

    [Fact]
    public void ParserSupportsAliasesAndEveryClauseOperator()
    {
        var range = BiologyCriteriaClause.Parse(" gravity [0.1 ~ 0.3]");
        var any = BiologyCriteriaClause.Parse("body [HMC,RockyIce]");
        var all = BiologyCriteriaClause.Parse("mats &[Iron,Nickel]");
        var none = BiologyCriteriaClause.Parse("regions ![CentreTop]");
        var composition = BiologyCriteriaClause.Parse(
            "atmosComp [Argon >= 100 | Nitrogen >= 0.5]");
        var comment = BiologyCriteriaClause.Parse("# observation count");

        Assert.Equal(BiologyCriteriaOperator.Range, range.Operator);
        Assert.Equal(0.1, range.Minimum);
        Assert.Equal(0.3, range.Maximum);
        Assert.Equal(
            ["High metal content ", "Rocky ice "],
            any.Values);
        Assert.Equal(BiologyCriteriaOperator.All, all.Operator);
        Assert.Equal(BiologyCriteriaOperator.Not, none.Operator);
        Assert.Equal(["1", "3", "7"], none.Values);
        Assert.Equal(BiologyCriteriaOperator.Composition, composition.Operator);
        Assert.Equal(100, composition.Compositions["Argon"]);
        Assert.Equal(0.5, composition.Compositions["Nitrogen"]);
        Assert.Equal(BiologyCriteriaOperator.Comment, comment.Operator);
    }

    [Fact]
    public void CatalogRejectsChildrenAndCommonChildrenOnSameNode()
    {
        using var stream = JsonStream(
            """
            {
              "genus": "Test",
              "useCommonChildren": true,
              "children": [ { "species": "One" } ]
            }
            """);

        var error = Assert.Throws<InvalidDataException>(
            () => BiologyCriteriaCatalog.Load(stream));

        Assert.Contains("both children and useCommonChildren", error.Message);
    }

    [Fact]
    public void CatalogRejectsUnknownRegionAliases()
    {
        using var stream = JsonStream(
            """
            { "genus": "Test", "query": [ "regions [UnknownArm]" ] }
            """);

        Assert.Throws<InvalidDataException>(
            () => BiologyCriteriaCatalog.Load(stream));
    }

    private static MemoryStream JsonStream(string json)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }
}
