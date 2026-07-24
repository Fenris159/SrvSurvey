using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class BoxelAddressTests
{
    [Theory]
    [InlineData("Praea Euq IL-P c5-19", "Praea Euq IL-P c5-", 'c', 5, 19)]
    [InlineData("Praea Euq GG-Y e1", "Praea Euq GG-Y e", 'e', 0, 1)]
    [InlineData("Wregoe BU-Y b2-0", "Wregoe BU-Y b2-", 'b', 2, 0)]
    public void ParseMatchesLegacyGeneratedNameRules(
        string name,
        string prefix,
        char massCode,
        int n1,
        int n2)
    {
        var boxel = BoxelAddress.Parse(name);

        Assert.Equal(prefix, boxel.Prefix);
        Assert.Equal(massCode, boxel.MassCode);
        Assert.Equal(n1, boxel.N1);
        Assert.Equal(n2, boxel.N2);
        Assert.Equal(name, boxel.Name);
    }

    [Fact]
    public void StoredAddressRoundTripsWithoutChangingLegacyShape()
    {
        const string stored = "Praea Euq IL-P c5-19|84456510258";

        var boxel = BoxelAddress.Parse(stored);

        Assert.Equal(84456510258, boxel.SystemAddress);
        Assert.Equal(stored, boxel.ToStoredString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Sol")]
    [InlineData("Praea Euq IL-P z5-19")]
    [InlineData("Praea Euq IL-P c")]
    public void ParseRejectsNonGeneratedOrInvalidNames(string value)
    {
        Assert.False(BoxelAddress.TryParse(value, out _));
    }

    [Fact]
    public void ParentChildrenAndContainmentMatchLegacyGeometry()
    {
        var boxel = BoxelAddress.Parse("Praea Euq IL-P c5-19");
        var parent = boxel.Parent;

        Assert.Equal("Praea Euq RS-U d2-0", parent.Name);
        Assert.Equal(8, parent.Children.Count);
        Assert.Equal("Praea Euq IL-P c5-0", parent.Children[0].Name);
        Assert.True(parent.Contains(boxel));
        Assert.False(boxel.Contains(parent));
    }

    [Theory]
    [InlineData('a', 10)]
    [InlineData('c', 40)]
    [InlineData('h', 1280)]
    public void CubeSizeMatchesLegacyMassCodes(char massCode, int size)
    {
        Assert.Equal(size, BoxelAddress.GetCubeSize(massCode));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 9)]
    [InlineData(2, 73)]
    public void TotalChildCountIncludesEveryLevel(int difference, int count)
    {
        Assert.Equal(count, BoxelAddress.GetTotalChildCount(difference));
    }
}
