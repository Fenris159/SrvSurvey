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
}
