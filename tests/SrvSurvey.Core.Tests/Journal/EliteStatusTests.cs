using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Journal;

public sealed class EliteStatusTests
{
    [Theory]
    [InlineData("$humanoid_sampletool_name;", true)]
    [InlineData("$HUMANOID_SAMPLETOOL_NAME;", false)]
    [InlineData("$humanoid_fists_name;", false)]
    [InlineData("wpn_s_pistol_kinetic_sauto", false)]
    [InlineData(null, false)]
    public void GeneticSamplerDrawnRequiresExactSelectedWeapon(
        string? selectedWeapon,
        bool expected)
    {
        var status = new EliteStatus { SelectedWeapon = selectedWeapon };

        Assert.Equal(expected, status.IsGeneticSamplerDrawn);
    }
}
