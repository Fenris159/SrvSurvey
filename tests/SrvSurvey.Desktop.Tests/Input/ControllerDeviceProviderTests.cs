using SrvSurvey.Desktop.Input;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed class ControllerDeviceProviderTests
{
    [Fact]
    public void BundledSdlRuntimeCanEnumerateWithoutHardware()
    {
        var result = new SdlControllerDeviceProvider().Discover();

        Assert.True(result.IsAvailable, result.ErrorMessage);
    }

    [Fact]
    public void PrefersPlatformPathForStableIdentity()
    {
        Assert.Equal(
            "path:/dev/input/event5",
            SdlControllerDeviceProvider.CreateStableId(
                "/dev/input/event5",
                0x1234,
                0x5678,
                1,
                "HOTAS"));
    }

    [Fact]
    public void FallsBackToHardwareIdentityWithoutPath()
    {
        Assert.Equal(
            "usb:1234:5678:0001:HOTAS",
            SdlControllerDeviceProvider.CreateStableId(
                path: null,
                0x1234,
                0x5678,
                1,
                "HOTAS"));
    }
}
