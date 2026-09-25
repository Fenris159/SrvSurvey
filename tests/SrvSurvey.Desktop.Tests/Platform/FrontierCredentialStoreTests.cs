using SrvSurvey.Desktop.Platform.Frontier;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class FrontierCredentialStoreTests
{
    [Fact]
    public void LinuxUnavailableMessageExplainsTheKeyringServiceFailure()
    {
        string message = LinuxSecretServiceFrontierCredentialStore.UnavailableMessage;

        Assert.Contains("Secret Service", message);
        Assert.Contains("KDE Wallet", message);
        Assert.Contains("Secret Service interface", message);
        Assert.Contains("sign out and back in", message);
        Assert.Contains("login integration", message);
        Assert.Contains("SDDM", message);
        Assert.Contains("ksecretd", message);
        Assert.DoesNotContain("sudo pacman", message);
    }

    [Fact]
    public void DefaultResolverRecognizesLinuxHomebrewSecretTool()
    {
        const string homebrewTool = "/home/linuxbrew/.linuxbrew/bin/secret-tool";

        string resolved = LinuxSecretServiceFrontierCredentialStore.ResolveSecretToolPath(
            paths: null,
            fileExists: path => path == homebrewTool
        );

        Assert.Equal(homebrewTool, resolved);
    }

    [Fact]
    public async Task InstalledSecretToolWithFailedStoreExplainsTheKeyringService()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-secret-tool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string tool = Path.Combine(root, "secret-tool");
            await File.WriteAllTextAsync(tool, "#!/bin/sh\nexit 1\n");
            File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            var store = new LinuxSecretServiceFrontierCredentialStore(Path.Combine(root, "lock"), [tool]);

            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.SaveAsync(new FrontierCredentialDocument())
            );

            Assert.Contains("KDE Wallet", error.Message);
            Assert.DoesNotContain("sudo pacman", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MissingSecretToolExplainsWhichPackageProvidesIt()
    {
        var store = new LinuxSecretServiceFrontierCredentialStore("unused.lock", ["/nonexistent/secret-tool"]);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(new FrontierCredentialDocument())
        );

        Assert.Contains("Arch/Manjaro/CachyOS: sudo pacman -S --needed libsecret", error.Message);
    }

    [Fact]
    public async Task InstalledSecretToolThatCannotStartReportsTheExecutionFailure()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-secret-tool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string tool = Path.Combine(root, "secret-tool");
            await File.WriteAllTextAsync(tool, "#!/bin/sh\nexit 0\n");
            File.SetUnixFileMode(tool, UnixFileMode.UserRead);
            var store = new LinuxSecretServiceFrontierCredentialStore(Path.Combine(root, "lock"), [tool]);

            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.SaveAsync(new FrontierCredentialDocument())
            );

            Assert.Contains("secret-tool could not start", error.Message);
            Assert.DoesNotContain("sudo pacman", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
