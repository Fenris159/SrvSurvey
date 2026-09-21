using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class VrPlatformProfileCatalogTests
{
    [Fact]
    public void EveryConnectionRouteExplainsCompatibilityAndActivation()
    {
        Assert.Equal(7, VrPlatformProfileCatalog.All.Count);
        Assert.Equal(
            VrPlatformProfileCatalog.All.Count,
            VrPlatformProfileCatalog.All.Select(profile => profile.Id).Distinct(StringComparer.Ordinal).Count()
        );
        Assert.All(
            VrPlatformProfileCatalog.All,
            profile =>
            {
                Assert.False(string.IsNullOrWhiteSpace(profile.DisplayName));
                Assert.False(string.IsNullOrWhiteSpace(profile.ConnectionRoute));
                Assert.False(string.IsNullOrWhiteSpace(profile.CompatibilityNote));
                Assert.True(profile.PairingSteps.Count >= 3);
            }
        );
    }

    [Fact]
    public void DirectRuntimeProfilesStateTheirSteamVrBoundary()
    {
        VrPlatformProfile meta = VrPlatformProfileCatalog.Get(VrPlatformProfileCatalog.MetaViaSteamVrProfileId);
        VrPlatformProfile mixedReality = VrPlatformProfileCatalog.Get(
            VrPlatformProfileCatalog.WindowsMixedRealityProfileId
        );

        Assert.Contains("OpenVR", meta.CompatibilityNote, StringComparison.Ordinal);
        Assert.Contains("Windows", meta.CompatibilityNote, StringComparison.Ordinal);
        Assert.Contains("24H2", mixedReality.CompatibilityNote, StringComparison.Ordinal);
        Assert.Contains("November 2026", mixedReality.CompatibilityNote, StringComparison.Ordinal);
    }

    [Fact]
    public void MetaProfilesExposeVerifiedSteamVrBridges()
    {
        string[] expectedProfiles =
        [
            VrPlatformProfileCatalog.MetaViaSteamVrProfileId,
            VrPlatformProfileCatalog.MetaSteamLinkProfileId,
            VrPlatformProfileCatalog.MetaVirtualDesktopProfileId,
            VrPlatformProfileCatalog.MetaAlvrProfileId,
        ];

        VrPlatformProfile[] metaProfiles = VrPlatformProfileCatalog
            .All.Where(profile => expectedProfiles.Contains(profile.Id, StringComparer.Ordinal))
            .ToArray();

        Assert.Equal(expectedProfiles.Length, metaProfiles.Length);
        Assert.All(
            metaProfiles,
            profile => Assert.Contains("SteamVR", profile.CompatibilityNote, StringComparison.Ordinal)
        );
        Assert.Contains(metaProfiles, profile => profile.CompatibilityNote.Contains("Linux", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownProfileFallsBackToSteamVr()
    {
        VrPlatformProfile profile = VrPlatformProfileCatalog.Get("future-profile");

        Assert.Equal(VrPlatformProfileCatalog.SteamVrProfileId, profile.Id);
    }
}
