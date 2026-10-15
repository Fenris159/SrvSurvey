using System.Runtime.InteropServices;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class VrPlatformProfileCatalogTests
{
    [Fact]
    public void EveryConnectionRouteExplainsCompatibilityAndActivation()
    {
        Assert.Equal(8, VrPlatformProfileCatalog.All.Count);
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

    [Theory]
    [InlineData("linux", Architecture.X64, 0, 3)]
    [InlineData("windows", Architecture.X64, 22000, 8)]
    [InlineData("windows", Architecture.X64, 26100, 7)]
    [InlineData("windows", Architecture.Arm64, 26100, 6)]
    [InlineData("macos", Architecture.X64, 0, 0)]
    public void HostAvailabilityKeepsEveryRouteAndExplainsEachUnavailableChoice(
        string host,
        Architecture architecture,
        int windowsBuild,
        int available
    )
    {
        OSPlatform platform = host switch
        {
            "windows" => OSPlatform.Windows,
            "linux" => OSPlatform.Linux,
            _ => OSPlatform.OSX,
        };
        IReadOnlyList<VrPlatformProfile> profiles = VrPlatformProfileCatalog.ForPlatform(
            platform,
            architecture,
            windowsBuild
        );
        Assert.Equal(AllIds(), profiles.Select(profile => profile.Id));
        Assert.Equal(available, profiles.Count(profile => profile.IsAvailable));
        Assert.All(
            profiles.Where(profile => !profile.IsAvailable),
            profile => Assert.NotEmpty(profile.UnavailableReason)
        );
        Assert.All(profiles.Where(profile => profile.IsAvailable), profile => Assert.Empty(profile.UnavailableReason));
        Assert.All(VrPlatformProfileCatalog.All, profile => Assert.True(profile.IsAvailable));
        if (platform == OSPlatform.Linux)
        {
            Assert.Equal(
                new[]
                {
                    VrPlatformProfileCatalog.SteamVrProfileId,
                    VrPlatformProfileCatalog.MetaAlvrProfileId,
                    VrPlatformProfileCatalog.CustomOpenVrProfileId,
                },
                profiles.Where(profile => profile.IsAvailable).Select(profile => profile.Id)
            );
        }
    }

    private static IEnumerable<string> AllIds() => VrPlatformProfileCatalog.All.Select(profile => profile.Id);

    [Fact]
    public void UnknownProfileFallsBackToSteamVr()
    {
        VrPlatformProfile profile = VrPlatformProfileCatalog.Get("future-profile");

        Assert.Equal(VrPlatformProfileCatalog.SteamVrProfileId, profile.Id);
    }
}
