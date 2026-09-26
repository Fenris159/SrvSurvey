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
    public void PreferredSecretIsTheNewestKeyringRevision()
    {
        const string searchOutput = """
            [/1]
            label = older
            secret = {"version":3,"keyringRevision":4}
            modified = 2026-09-26 12:00:00
            [/2]
            label = newer
            secret = {"version":3,"keyringRevision":9,"accounts":{"F456":{}}}
            modified = 2026-09-26 12:00:01

            """;

        IReadOnlyList<string> secrets = LinuxSecretServiceFrontierCredentialStore.ParseSearchSecrets(searchOutput);
        string? selected = LinuxSecretServiceFrontierCredentialStore.SelectPreferredSecret(secrets);

        Assert.NotNull(selected);
        Assert.Contains("F456", selected, StringComparison.Ordinal);
        Assert.Equal(
            selected,
            LinuxSecretServiceFrontierCredentialStore.SelectPreferredSecret(secrets.Reverse().ToArray())
        );
    }

    [Fact]
    public async Task EmptyKeyringSearchLoadsNoAuthorization()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-secret-tool-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            LinuxSecretServiceFrontierCredentialStore store = await CreateLinuxStoreAsync(
                root,
                """
                #!/bin/sh
                if [ "$1" = "search" ]; then
                  exit 0
                fi
                exit 1
                """
            );

            Assert.Null(await store.LoadAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UnreadableKeyringSearchIsRejected()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-secret-tool-invalid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            LinuxSecretServiceFrontierCredentialStore store = await CreateLinuxStoreAsync(
                root,
                """
                #!/bin/sh
                if [ "$1" = "search" ]; then
                  printf '%s\n' '[/1]' 'secret = not-json'
                  exit 0
                fi
                exit 1
                """
            );

            InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
            Assert.Equal(LinuxSecretServiceFrontierCredentialStore.InvalidKeyringMessage, error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task KeyringSearchFailureIncludesTheSecretServiceError()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-secret-tool-locked-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            LinuxSecretServiceFrontierCredentialStore store = await CreateLinuxStoreAsync(
                root,
                """
                #!/bin/sh
                echo 'wallet locked' >&2
                exit 1
                """
            );

            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.LoadAsync()
            );
            Assert.Contains("wallet locked", error.Message, StringComparison.Ordinal);
            Assert.Contains("KDE Wallet", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SecretLargerThanSecretToolCanStoreIsRejected()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-secret-tool-large-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string tool = Path.Combine(root, "secret-tool");
            await File.WriteAllTextAsync(
                tool,
                """
                #!/bin/sh
                if [ "$1" = "search" ]; then
                  exit 0
                fi
                echo 'store was called' >&2
                exit 1
                """
            );
            File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var store = new LinuxSecretServiceFrontierCredentialStore(Path.Combine(root, "lock"), [tool]);

            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.SaveAsync(
                    new FrontierCredentialDocument
                    {
                        Accounts = new Dictionary<string, FrontierAccountCredential>
                        {
                            ["F123"] = new() { AccessToken = new string('a', 9000) },
                        },
                    }
                )
            );

            Assert.Equal(LinuxSecretServiceFrontierCredentialStore.SecretTooLargeMessage, error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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

    private static async Task<LinuxSecretServiceFrontierCredentialStore> CreateLinuxStoreAsync(
        string root,
        string script
    )
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException();
        }

        string tool = Path.Combine(root, "secret-tool");
        await File.WriteAllTextAsync(tool, script);
        File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return new LinuxSecretServiceFrontierCredentialStore(Path.Combine(root, "lock"), [tool]);
    }
}
