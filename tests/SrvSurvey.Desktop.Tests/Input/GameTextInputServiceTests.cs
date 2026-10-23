using SharpHook.Data;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed class GameTextInputServiceTests
{
    [Theory]
    [InlineData(OverlayHostKind.Windows, true)]
    [InlineData(OverlayHostKind.LinuxX11, true)]
    [InlineData(OverlayHostKind.LinuxXWayland, true)]
    [InlineData(OverlayHostKind.LinuxWayland, false)]
    [InlineData(OverlayHostKind.Other, false)]
    public void OrdinaryHostTextInputAvailabilityIsPreserved(OverlayHostKind host, bool available)
    {
        IGameTextInputService service = GameTextInputService.CreateForCapabilities(
            OverlayPlatformCapabilities.ForHost(host)
        );
        Assert.Equal(available, service.IsAvailable);
        if (!available)
        {
            Assert.False(service.EnterText("Sol").Succeeded);
        }
    }

    [Fact]
    public void ReviewRegressionExternalOverlayDoesNotAdvertiseTextInjectionOnThePrimaryDisplay()
    {
        IGameTextInputService service = GameTextInputService.CreateForCapabilities(
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.LinuxXWayland) with
            {
                UsesGamescopeExternalOverlay = true,
            }
        );
        Assert.False(service.IsAvailable);
        Assert.False(service.EnterText("Sol").Succeeded);
        Assert.Contains("Gamescope", service.UnavailableReason);
    }

    [Fact]
    public void SharpHookServiceReportsSuccessfulTextEntry()
    {
        string? entered = null;
        var service = new SharpHookGameTextInputService(text =>
        {
            entered = text;
            return UioHookResult.Success;
        });

        GameTextInputResult result = service.EnterText("Synuefe NL-N C23-4");

        Assert.True(result.Succeeded);
        Assert.Equal("Synuefe NL-N C23-4", entered);
    }

    [Fact]
    public void SharpHookFailureIsReturnedWithoutClaimingSuccess()
    {
        var service = new SharpHookGameTextInputService(_ => UioHookResult.ErrorXOpenDisplay);

        GameTextInputResult result = service.EnterText("Sol");

        Assert.False(result.Succeeded);
        Assert.Contains("ErrorXOpenDisplay", result.Status);
    }

    [Fact]
    public void UnavailableServiceExplainsThePlatformLimitation()
    {
        var service = new UnavailableGameTextInputService("Wayland synthetic input unavailable.");

        GameTextInputResult result = service.EnterText("Sol");

        Assert.False(service.IsAvailable);
        Assert.False(result.Succeeded);
        Assert.Contains("Wayland", result.Status);
    }

    [Theory]
    [InlineData(true, "Route A", "Boxel B", true, "Clipboard C", "Route A")]
    [InlineData(true, null, "Boxel B", true, "Clipboard C", "Boxel B")]
    [InlineData(true, null, "Boxel B", false, "Clipboard C", "Clipboard C")]
    [InlineData(false, "Route A", "Boxel B", true, "Clipboard C", null)]
    public void GalaxyMapTextUsesLegacyPrecedence(
        bool isGalaxyMapOpen,
        string? route,
        string? boxel,
        bool useBoxel,
        string? clipboard,
        string? expected
    )
    {
        Assert.Equal(expected, GalaxyMapTextResolver.Resolve(isGalaxyMapOpen, route, boxel, useBoxel, clipboard));
    }
}
