using SrvSurvey.Desktop.Platform.Frontier;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class FrontierCredentialStoreTests
{
    [Fact]
    public void LinuxUnavailableMessageNamesTheExecutableAndDistributionPackages()
    {
        string message = LinuxSecretServiceFrontierCredentialStore.UnavailableMessage;

        Assert.Contains("secret-tool", message);
        Assert.Contains("Debian/Ubuntu: sudo apt install libsecret-tools", message);
        Assert.Contains("Arch/Manjaro/CachyOS: sudo pacman -S --needed libsecret", message);
    }
}
