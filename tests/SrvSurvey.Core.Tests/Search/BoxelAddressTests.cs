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

    [Fact]
    public void StoredHandAuthoredAddressRoundTripsWithoutLosingItsGeometry()
    {
        const string stored = "Sol|10477373803";

        var boxel = BoxelAddress.Parse(stored);

        Assert.Equal("Sol", boxel.Name);
        Assert.Equal(10477373803, boxel.SystemAddress);
        Assert.NotEqual("Sol", boxel.GeneratedName);
        Assert.Equal(stored, boxel.ToStoredString());
    }

    [Theory]
    [InlineData(685451322393, "Wregoe BU-Y b2-0")]
    [InlineData(1184840454858, "Synuefe NL-N c23-4")]
    [InlineData(9420415411, "Pyraea Euq ZK-P d5-0")]
    public void SystemAddressDecodesGeneratedBoxelGeometry(
        long systemAddress,
        string expectedName)
    {
        var decoded = BoxelAddress.TryFromSystemAddress(
            systemAddress,
            null,
            out var boxel);

        Assert.True(decoded);
        Assert.Equal(expectedName, boxel?.GeneratedName);
        Assert.Equal(systemAddress, boxel?.SystemAddress);
    }

    [Fact]
    public void SystemAddressPreservesHandAuthoredPublicName()
    {
        var decoded = BoxelAddress.TryFromSystemAddress(
            10477373803,
            "Sol",
            out var boxel);

        Assert.True(decoded);
        Assert.Equal("Sol", boxel?.Name);
        Assert.NotEqual("Sol", boxel?.GeneratedName);
        Assert.Equal(10477373803, boxel?.SystemAddress);
    }

    [Fact]
    public void SystemAddressDecodesGeneratedShapedHandAuthoredSectorName()
    {
        const string publicName = "Col 173 Sector JX-K b24-0";

        var decoded = BoxelAddress.TryFromSystemAddress(
            684107179361,
            publicName,
            out var boxel);

        Assert.True(decoded);
        Assert.Equal(publicName, boxel?.Name);
        Assert.NotEqual(publicName, boxel?.GeneratedName);
        Assert.NotEqual("Col 173 Sector", boxel?.Sector);
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
