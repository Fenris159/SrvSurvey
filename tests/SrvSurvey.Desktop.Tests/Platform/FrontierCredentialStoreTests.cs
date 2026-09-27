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
    public async Task PortableKeyringKeepsBothCommandersAndLoadsNewestRevision()
    {
        var tool = new FakeSecretTool();
        var store = new LinuxSecretServiceFrontierCredentialStore("unused.lock", runTool: tool.RunAsync);
        await store.SaveAsync(
            new FrontierCredentialDocument
            {
                Accounts = new Dictionary<string, FrontierAccountCredential> { ["F123"] = new() },
            }
        );
        await store.SaveAsync(
            new FrontierCredentialDocument
            {
                Accounts = new Dictionary<string, FrontierAccountCredential> { ["F123"] = new(), ["F456"] = new() },
            }
        );

        FrontierCredentialDocument? loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Accounts.Count);
        Assert.True(loaded.Accounts.ContainsKey("F123"));
        Assert.True(loaded.Accounts.ContainsKey("F456"));
        Assert.Equal(2, tool.AccountItemCount);
        Assert.Equal(2, tool.StateItemCount);
        Assert.True(tool.SearchUsedUnlock);
    }

    [Fact]
    public async Task FailedKeyringRewritePreservesExistingCommander()
    {
        var tool = new FakeSecretTool();
        var store = new LinuxSecretServiceFrontierCredentialStore("unused.lock", runTool: tool.RunAsync);
        await store.SaveAsync(
            new FrontierCredentialDocument
            {
                Accounts = new Dictionary<string, FrontierAccountCredential> { ["F123"] = new() },
            }
        );
        tool.RejectStores = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(
                new FrontierCredentialDocument
                {
                    Accounts = new Dictionary<string, FrontierAccountCredential> { ["F123"] = new(), ["F456"] = new() },
                }
            )
        );

        FrontierCredentialDocument? loaded = await store.LoadAsync();
        Assert.NotNull(loaded);
        Assert.True(loaded.Accounts.ContainsKey("F123"));
        Assert.False(loaded.Accounts.ContainsKey("F456"));
    }

    [Fact]
    public async Task EmptyKeyringWriteFailsVerification()
    {
        var tool = new FakeSecretTool { StoreEmpty = true };
        var store = new LinuxSecretServiceFrontierCredentialStore("unused.lock", runTool: tool.RunAsync);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(new FrontierCredentialDocument())
        );

        Assert.Equal(LinuxSecretServiceFrontierCredentialStore.VerificationFailedMessage, error.Message);
        Assert.True(tool.StoreUsedDefaultCollection);
    }

    [Fact]
    public async Task LegacyDocumentMigratesIntoSeparateCommanderItems()
    {
        var tool = new FakeSecretTool();
        tool.SeedLegacySecret(
            """{"version":3,"accounts":{"F123":{"accessToken":"steam"},"F456":{"accessToken":"epic"}}}"""
        );
        var store = new LinuxSecretServiceFrontierCredentialStore("unused.lock", runTool: tool.RunAsync);

        FrontierCredentialDocument? legacy = await store.LoadAsync();
        Assert.NotNull(legacy);
        await store.SaveAsync(legacy);

        Assert.Equal(2, tool.AccountItemCount);
        Assert.Equal(1, tool.StateItemCount);
        Assert.Equal(0, tool.LegacyItemCount);
        FrontierCredentialDocument? migrated = await store.LoadAsync();
        Assert.NotNull(migrated);
        Assert.Equal("steam", migrated.Accounts["F123"].AccessToken);
        Assert.Equal("epic", migrated.Accounts["F456"].AccessToken);
    }

    [Fact]
    public async Task EmptyReplacementLeavesPreviousCommanderCredentialReadable()
    {
        var tool = new FakeSecretTool();
        var store = new LinuxSecretServiceFrontierCredentialStore("unused.lock", runTool: tool.RunAsync);
        await store.SaveAsync(
            new FrontierCredentialDocument
            {
                Accounts = new Dictionary<string, FrontierAccountCredential>
                {
                    ["F123"] = new() { AccessToken = "original" },
                },
            }
        );
        tool.EmptyStoreService = "frontier_capi_account";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(
                new FrontierCredentialDocument
                {
                    Accounts = new Dictionary<string, FrontierAccountCredential>
                    {
                        ["F123"] = new() { AccessToken = "replacement" },
                    },
                }
            )
        );

        Assert.Equal("original", (await store.LoadAsync())!.Accounts["F123"].AccessToken);
    }

    [Fact]
    public async Task EmptyStateReplacementLeavesPreviousConnectionStateReadable()
    {
        var tool = new FakeSecretTool();
        var store = new LinuxSecretServiceFrontierCredentialStore("unused.lock", runTool: tool.RunAsync);
        await store.SaveAsync(new FrontierCredentialDocument());
        tool.EmptyStoreService = "frontier_capi_state";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(
                new FrontierCredentialDocument
                {
                    PendingAuthorizations = new Dictionary<string, FrontierPendingAuthorization>
                    {
                        ["new"] = new("new", "verifier", DateTimeOffset.UtcNow, "F456", "Epic"),
                    },
                }
            )
        );

        Assert.Empty((await store.LoadAsync())!.PendingAuthorizations);
    }

    [Fact]
    public async Task RemovingOneCommanderClearsOnlyThatCommandersItems()
    {
        var tool = new FakeSecretTool();
        var store = new LinuxSecretServiceFrontierCredentialStore("unused.lock", runTool: tool.RunAsync);
        var steam = new FrontierAccountCredential { AccessToken = "steam" };
        await store.SaveAsync(
            new FrontierCredentialDocument
            {
                Accounts = new Dictionary<string, FrontierAccountCredential>
                {
                    ["F123"] = steam,
                    ["F456"] = new() { AccessToken = "epic" },
                },
            }
        );

        await store.SaveAsync(
            new FrontierCredentialDocument
            {
                Accounts = new Dictionary<string, FrontierAccountCredential> { ["F123"] = steam },
            }
        );

        Assert.Equal(1, tool.AccountItemCount);
        FrontierCredentialDocument? remaining = await store.LoadAsync();
        Assert.NotNull(remaining);
        Assert.Equal("steam", remaining.Accounts["F123"].AccessToken);
        Assert.False(remaining.Accounts.ContainsKey("F456"));
    }

    [Fact]
    public async Task PortableKeyringRejectsOversizedSecretBeforeWriting()
    {
        var tool = new FakeSecretTool();
        var store = new LinuxSecretServiceFrontierCredentialStore("unused.lock", runTool: tool.RunAsync);

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
        Assert.Equal(0, tool.ItemCount);
    }

    [Fact]
    public async Task PortableKeyringClearRemovesAllStoredAuthorizations()
    {
        var tool = new FakeSecretTool();
        var store = new LinuxSecretServiceFrontierCredentialStore("unused.lock", runTool: tool.RunAsync);
        await store.SaveAsync(new FrontierCredentialDocument());

        await store.ClearAsync();

        Assert.Null(await store.LoadAsync());
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
    public async Task EmptyKeyringSecretAllowsACommanderToReconnect()
    {
        var tool = new FakeSecretTool();
        tool.SeedLegacySecret(string.Empty);
        var store = new LinuxSecretServiceFrontierCredentialStore("unused.lock", runTool: tool.RunAsync);

        Assert.Null(await store.LoadAsync());

        await store.SaveAsync(
            new FrontierCredentialDocument
            {
                Accounts = new Dictionary<string, FrontierAccountCredential> { ["F123"] = new() },
            }
        );

        FrontierCredentialDocument? loaded = await store.LoadAsync();
        Assert.NotNull(loaded);
        Assert.True(loaded.Accounts.ContainsKey("F123"));
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

    private sealed class FakeSecretTool
    {
        private sealed record Item(Dictionary<string, string> Attributes, string Secret);

        private readonly List<Item> items = [];

        public int ItemCount => items.Count;

        public int AccountItemCount => CountService("frontier_capi_account");

        public int StateItemCount => CountService("frontier_capi_state");

        public int LegacyItemCount => CountService("frontier-capi");

        public bool RejectStores { get; set; }

        public bool StoreEmpty { get; set; }

        public string? EmptyStoreService { get; set; }

        public bool StoreUsedDefaultCollection { get; private set; }

        public bool SearchUsedUnlock { get; private set; }

        public void SeedLegacySecret(string secret) =>
            items.Add(
                new Item(
                    new Dictionary<string, string> { ["application"] = "SrvSurvey", ["service"] = "frontier-capi" },
                    secret
                )
            );

        public Task<SecretToolResult> RunAsync(
            IReadOnlyList<string> arguments,
            string? input,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Dictionary<string, string> attributes = ParseAttributes(arguments);
            SecretToolResult result;
            switch (arguments[0])
            {
                case "search":
                    SearchUsedUnlock = arguments.Contains("--unlock");
                    result = new SecretToolResult(
                        0,
                        string.Join(
                            "\n",
                            items
                                .Where(item => Matches(item, attributes))
                                .Select((item, index) => $"[/{index + 1}]\nsecret = {item.Secret}")
                        ),
                        string.Empty
                    );
                    break;
                case "store" when RejectStores:
                    result = new SecretToolResult(1, string.Empty, "keyring is locked");
                    break;
                case "store":
                    StoreUsedDefaultCollection = arguments.Contains("--collection=default");
                    items.RemoveAll(item => SameAttributes(item.Attributes, attributes));
                    bool empty =
                        StoreEmpty
                        || attributes.TryGetValue("service", out string? service) && service == EmptyStoreService;
                    items.Add(new Item(attributes, empty ? string.Empty : input ?? string.Empty));
                    result = new SecretToolResult(0, string.Empty, string.Empty);
                    break;
                case "clear":
                    int removed = items.RemoveAll(item => Matches(item, attributes));
                    result = new SecretToolResult(removed > 0 ? 0 : 1, string.Empty, string.Empty);
                    break;
                default:
                    throw new InvalidOperationException("Unexpected secret-tool command.");
            }

            return Task.FromResult(result);
        }

        private int CountService(string service) =>
            items.Count(item => item.Attributes.TryGetValue("service", out string? value) && value == service);

        private static bool Matches(Item item, Dictionary<string, string> attributes) =>
            attributes.All(pair =>
                item.Attributes.TryGetValue(pair.Key, out string? value)
                && string.Equals(value, pair.Value, StringComparison.Ordinal)
            );

        private static bool SameAttributes(Dictionary<string, string> left, Dictionary<string, string> right) =>
            left.Count == right.Count && left.All(pair => right.GetValueOrDefault(pair.Key) == pair.Value);

        private static Dictionary<string, string> ParseAttributes(IReadOnlyList<string> arguments)
        {
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            int index = 1;
            while (index < arguments.Count)
            {
                if (arguments[index].StartsWith("--", StringComparison.Ordinal))
                {
                    index++;
                    continue;
                }

                attributes.Add(arguments[index], arguments[index + 1]);
                index += 2;
            }

            return attributes;
        }
    }
}
