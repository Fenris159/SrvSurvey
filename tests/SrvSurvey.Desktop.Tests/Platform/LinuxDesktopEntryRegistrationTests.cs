using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

/// <summary>Verifies host application identity without modifying the real desktop's launchers.</summary>
public sealed class LinuxDesktopEntryRegistrationTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("SrvSurvey-desktop-identity-").FullName;

    /// <summary>Honors absolute XDG data directories and falls back when an override is absent or relative.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative/data")]
    public void MissingOrRelativeDataHomeUsesTheUserDefault(string? dataHome)
    {
        Assert.Equal(
            Path.Combine(root, ".local", "share", "applications"),
            LinuxDesktopEntryRegistration.ResolveApplicationsDirectory(root, dataHome)
        );
        string custom = Path.Combine(root, "custom-data");
        Assert.Equal(
            Path.Combine(custom, "applications"),
            LinuxDesktopEntryRegistration.ResolveApplicationsDirectory(root, custom)
        );
    }

    /// <summary>Keeps launchers usable after an AppImage mount disappears, with a process-path fallback.</summary>
    [Fact]
    public void LauncherUsesTheStableAppImageInsteadOfItsTemporaryMount()
    {
        Assert.Equal(
            "/Applications/SrvSurvey.AppImage",
            LinuxDesktopEntryRegistration.ResolveExecutable("/Applications/SrvSurvey.AppImage", "/tmp/.mount/srvsurvey")
        );
        Assert.Equal(
            "/Applications/SrvSurvey",
            LinuxDesktopEntryRegistration.ResolveExecutable(null, "/Applications/SrvSurvey")
        );
        Assert.Equal(
            "/Applications/SrvSurvey",
            LinuxDesktopEntryRegistration.ResolveExecutable(" ", "/Applications/SrvSurvey")
        );
    }

    /// <summary>Rejects missing paths and line breaks that could inject desktop-entry fields.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("/path\nNoDisplay=false")]
    [InlineData("/path\rType=Link")]
    public void MissingOrMultilineLauncherPathIsRejected(string? executable)
    {
        Assert.Throws<InvalidDataException>(() => LinuxDesktopEntryRegistration.ResolveExecutable(null, executable));
    }

    /// <summary>Reuses an installed system launcher without hiding it behind a user-level entry.</summary>
    [Fact]
    public async Task ExistingSystemLauncherIsNotHiddenByAUserOverride()
    {
        string system = Directory.CreateDirectory(Path.Combine(root, "system-applications")).FullName;
        string systemFile = Path.Combine(system, LinuxDesktopEntryRegistration.DesktopFileName);
        const string metadata = "[Desktop Entry]\nName=SrvSurvey\nExec=/bin/true\n";
        await File.WriteAllTextAsync(systemFile, metadata);
        string user = Path.Combine(root, "user-applications");
        await LinuxDesktopEntryRegistration.EnsureAsync(user, "/bin/true", [system], CancellationToken.None);
        Assert.False(Directory.Exists(user));
        Assert.Equal(metadata, await File.ReadAllTextAsync(systemFile));
    }

    /// <summary>Preserves unowned user launchers, including wrapper commands and custom metadata.</summary>
    [Fact]
    public async Task ExistingUserLauncherKeepsItsCustomLaunchCommand()
    {
        string user = Directory.CreateDirectory(Path.Combine(root, "applications")).FullName;
        string launcher = Path.Combine(user, LinuxDesktopEntryRegistration.DesktopFileName);
        const string metadata =
            "[Desktop Entry]\nType=Application\nName=SrvSurvey\nExec=/custom/launch-wrapper %u\nTryExec=/custom/launch-wrapper\nIcon=custom-icon\n";
        await File.WriteAllTextAsync(launcher, metadata);
        await LinuxDesktopEntryRegistration.EnsureAsync(
            user,
            "/Applications/SrvSurvey.AppImage",
            [],
            CancellationToken.None
        );
        Assert.Equal(metadata, await File.ReadAllTextAsync(launcher));
    }

    /// <summary>Refreshes managed launch paths while retaining metadata and avoiding unchanged rewrites.</summary>
    [Fact]
    public async Task MovedAppImageKeepsExistingLauncherMetadataAndAvoidsRedundantWrites()
    {
        string user = Directory.CreateDirectory(Path.Combine(root, "applications")).FullName;
        string launcher = Path.Combine(user, LinuxDesktopEntryRegistration.DesktopFileName);
        await File.WriteAllTextAsync(
            launcher,
            "[Desktop Entry]\nX-SrvSurvey-Managed=true\nType=Application\nName=SrvSurvey\nExec=/old/AppImage %u\nTryExec=/old/AppImage\nIcon=custom-icon\nMimeType=application/json;\n\n[Desktop Action Debug]\nExec=/debug\n"
        );
        await LinuxDesktopEntryRegistration.EnsureAsync(user, "/new/AppImage", [], CancellationToken.None);
        string updated = await File.ReadAllTextAsync(launcher);
        Assert.Contains("Exec=\"/new/AppImage\" %u", updated, StringComparison.Ordinal);
        Assert.Contains("TryExec=/new/AppImage", updated, StringComparison.Ordinal);
        Assert.Contains("Icon=custom-icon", updated, StringComparison.Ordinal);
        Assert.Contains("MimeType=application/json;", updated, StringComparison.Ordinal);
        Assert.Contains($"[Desktop Action Debug]{Environment.NewLine}Exec=/debug", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("NoDisplay=true", updated, StringComparison.Ordinal);
        DateTime saved = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(launcher, saved);
        await LinuxDesktopEntryRegistration.EnsureAsync(user, "/new/AppImage", [], CancellationToken.None);
        Assert.Equal(saved, File.GetLastWriteTimeUtc(launcher));
        Assert.Empty(Directory.GetFiles(user, "*.tmp"));
    }

    /// <summary>Leaves the original entry intact when validation fails or an update is canceled.</summary>
    [Fact]
    public async Task CancellationAndInvalidMetadataPreserveTheExistingEntry()
    {
        string user = Directory.CreateDirectory(Path.Combine(root, "applications")).FullName;
        string launcher = Path.Combine(user, LinuxDesktopEntryRegistration.DesktopFileName);
        const string original = "[Desktop Action Debug]\nExec=/debug\n";
        await File.WriteAllTextAsync(launcher, original);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            LinuxDesktopEntryRegistration.WriteAsync(user, "/bin/true")
        );
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LinuxDesktopEntryRegistration.EnsureAsync(user, "/bin/true", [], canceled.Token)
        );
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LinuxDesktopEntryRegistration.WriteAsync(user, "/bin/true", cancellationToken: canceled.Token)
        );
        Assert.Equal(original, await File.ReadAllTextAsync(launcher));
        Assert.Empty(Directory.GetFiles(user, "*.tmp"));
    }

    /// <summary>Removes the isolated application directory after each test.</summary>
    public void Dispose() => Directory.Delete(root, recursive: true);
}
