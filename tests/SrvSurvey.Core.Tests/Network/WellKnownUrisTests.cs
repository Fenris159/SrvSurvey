using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Tests.Network;

public sealed class WellKnownUrisTests
{
    [Fact]
    public void GuardianScienceCorpsDiscordUsesConfiguredInvite()
    {
        Assert.Equal(
            "https://discord.com/invite/GJjTFa9fsz",
            WellKnownUris.GuardianScienceCorpsDiscord.OriginalString);
    }

    [Fact]
    public void EdsmCommanderSettingsUsesConfiguredApiPage()
    {
        Assert.Equal(
            "https://www.edsm.net/settings/api",
            WellKnownUris.EdsmCommanderApiSettings.OriginalString);
    }
}
