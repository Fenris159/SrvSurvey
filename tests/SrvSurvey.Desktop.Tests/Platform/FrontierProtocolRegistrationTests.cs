using SrvSurvey.Desktop.Platform.Frontier;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class FrontierProtocolRegistrationTests
{
    /// <summary>Verifies a failed cache refresh is reported before the user can open an unusable authorization link.</summary>
    [Theory]
    [InlineData("/bin/false", "could not refresh")]
    [InlineData("/nonexistent/srvsurvey/update-desktop-database", "desktop-file-utils")]
    public async Task RegistrationReportsDesktopDatabaseFailure(string updaterPath, string message)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            FrontierProtocolRegistration.RefreshLinuxDesktopDatabaseAsync("/tmp", updaterPath)
        );

        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies replacing the legacy callback entry also refreshes the desktop's MIME lookup cache.</summary>
    [Fact]
    public async Task RegistrationRefreshesStaleCallbackCache()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string directory = Directory.CreateTempSubdirectory("SrvSurvey protocol cache ").FullName;
        try
        {
            string cache = Path.Combine(directory, "mimeinfo.cache");
            await File.WriteAllTextAsync(
                cache,
                "[MIME Cache]\nx-scheme-handler/srvsurvey=io.github.fenris159.SrvSurvey.frontier-auth.desktop;\n"
            );
            await File.WriteAllTextAsync(
                Path.Combine(directory, "io.github.fenris159.SrvSurvey.frontier-auth.desktop"),
                "[Desktop Entry]\nType=Application\nName=SrvSurvey\nExec=/bin/true %u\nMimeType=x-scheme-handler/srvsurvey;\n"
            );
            await File.WriteAllTextAsync(
                Path.Combine(directory, "other.desktop"),
                "[Desktop Entry]\nType=Application\nName=Other\nExec=/bin/true %u\nMimeType=application/json;\n"
            );

            await FrontierProtocolRegistration.WriteLinuxDesktopFileAsync(directory, "/bin/true");

            string updated = await File.ReadAllTextAsync(cache);
            Assert.Contains(
                "x-scheme-handler/srvsurvey=io.github.fenris159.SrvSurvey.desktop;",
                updated,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain("frontier-auth.desktop", updated, StringComparison.Ordinal);
            Assert.Contains("application/json=other.desktop;", updated, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies an installed launcher and old OAuth entry are consolidated into one current handler.</summary>
    [Fact]
    public async Task RegistrationReusesLauncherAndRemovesDuplicateHandler()
    {
        string directory = Directory.CreateTempSubdirectory("SrvSurvey-protocol-").FullName;
        try
        {
            string launcher = Path.Combine(directory, "io.github.fenris159.SrvSurvey.desktop");
            string legacy = Path.Combine(directory, "io.github.fenris159.SrvSurvey.frontier-auth.desktop");
            const string previous =
                "[Desktop Entry]\nType=Application\nName=SrvSurvey\nExec=/old/SrvSurvey %u\nTryExec=/old/SrvSurvey\nIcon=custom-icon\nCategories=Game;Utility;\nMimeType=x-scheme-handler/srvsurvey;\n\n[Desktop Action Debug]\nExec=/old/debug\n";
            await File.WriteAllTextAsync(launcher, previous);
            await File.WriteAllTextAsync(legacy, "[Desktop Entry]\nMimeType=x-scheme-handler/srvsurvey;\n");

            string registered = await FrontierProtocolRegistration.WriteLinuxDesktopFileAsync(
                directory,
                "/Applications/SrvSurvey.AppImage"
            );

            string[] handlers = Directory
                .GetFiles(directory, "*.desktop")
                .Where(path => File.ReadAllText(path).Contains("x-scheme-handler/srvsurvey", StringComparison.Ordinal))
                .ToArray();
            Assert.Single(handlers);
            Assert.Equal(launcher, registered);
            Assert.False(File.Exists(legacy));
            string updated = await File.ReadAllTextAsync(launcher);
            Assert.Contains("Exec=\"/Applications/SrvSurvey.AppImage\" %u", updated, StringComparison.Ordinal);
            Assert.Contains("TryExec=/Applications/SrvSurvey.AppImage", updated, StringComparison.Ordinal);
            Assert.Contains("Icon=custom-icon", updated, StringComparison.Ordinal);
            Assert.Contains("Categories=Game;Utility;", updated, StringComparison.Ordinal);
            Assert.Contains(
                $"[Desktop Action Debug]{Environment.NewLine}Exec=/old/debug",
                updated,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain("NoDisplay=true", updated, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies unintegrated AppImages use one hidden entry and refresh it when the executable moves.</summary>
    [Fact]
    public async Task RepeatedRegistrationUsesOneHiddenCanonicalEntry()
    {
        string directory = Directory.CreateTempSubdirectory("SrvSurvey-protocol-").FullName;
        try
        {
            await FrontierProtocolRegistration.WriteLinuxDesktopFileAsync(directory, "/old/SrvSurvey.AppImage");
            string registered = await FrontierProtocolRegistration.WriteLinuxDesktopFileAsync(
                directory,
                "/Applications/My Survey/SrvSurvey.AppImage"
            );

            Assert.Single(Directory.GetFiles(directory, "*.desktop"));
            Assert.Equal("io.github.fenris159.SrvSurvey.desktop", Path.GetFileName(registered));
            string content = await File.ReadAllTextAsync(registered);
            Assert.Contains("NoDisplay=true", content, StringComparison.Ordinal);
            Assert.Contains(
                "Exec=\"/Applications/My Survey/SrvSurvey.AppImage\" %u",
                content,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain("/old/", content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies callback registration adds its MIME type without duplicating or dropping other types.</summary>
    [Theory]
    [InlineData("", "MimeType=x-scheme-handler/srvsurvey;")]
    [InlineData("MimeType=application/json;", "MimeType=application/json;x-scheme-handler/srvsurvey;")]
    [InlineData(
        "MimeType=x-scheme-handler/srvsurvey;application/json;",
        "MimeType=x-scheme-handler/srvsurvey;application/json;"
    )]
    public async Task RegistrationPreservesOtherMimeTypes(string existingMimeType, string expectedMimeType)
    {
        string directory = Directory.CreateTempSubdirectory("SrvSurvey-protocol-").FullName;
        try
        {
            string launcher = Path.Combine(directory, "io.github.fenris159.SrvSurvey.desktop");
            await File.WriteAllTextAsync(launcher, $"[Desktop Entry]\nName=SrvSurvey\n{existingMimeType}\n");

            await FrontierProtocolRegistration.WriteLinuxDesktopFileAsync(
                directory,
                "/Applications/SrvSurvey.AppImage"
            );
            string content = await File.ReadAllTextAsync(launcher);

            Assert.Contains(expectedMimeType, content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies spaced declarations are replaced once and other MIME types and action keys survive.</summary>
    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData(" \t ")]
    public async Task RegistrationUpdatesDeclarationsWithWhitespace(string whitespace)
    {
        string directory = Directory.CreateTempSubdirectory("SrvSurvey-protocol-").FullName;
        try
        {
            string launcher = Path.Combine(directory, "io.github.fenris159.SrvSurvey.desktop");
            await File.WriteAllTextAsync(
                launcher,
                $"[Desktop Entry]\nName=SrvSurvey\n# Exec = keep this comment\nX-Example=Exec = keep this value\nNo delimiter\nExec{whitespace}={whitespace}/old/SrvSurvey %u\nTryExec{whitespace}={whitespace}/old/SrvSurvey\nMimeType{whitespace}={whitespace}application/json;\n\n[Desktop Action Debug]\nExec = /old/debug\n"
            );

            await FrontierProtocolRegistration.WriteLinuxDesktopFileAsync(
                directory,
                "/Applications/SrvSurvey.AppImage"
            );

            string[] lines = await File.ReadAllLinesAsync(launcher);
            string[] mainEntry = lines.TakeWhile(line => line != "[Desktop Action Debug]").ToArray();
            foreach (string key in new[] { "Exec", "TryExec", "MimeType" })
            {
                Assert.Single(mainEntry, line => line.Split('=', 2)[0].Trim() == key);
            }
            Assert.Contains("Exec=\"/Applications/SrvSurvey.AppImage\" %u", mainEntry);
            Assert.Contains("TryExec=/Applications/SrvSurvey.AppImage", mainEntry);
            Assert.Contains("MimeType=application/json;x-scheme-handler/srvsurvey;", mainEntry);
            Assert.Contains("# Exec = keep this comment", mainEntry);
            Assert.Contains("X-Example=Exec = keep this value", mainEntry);
            Assert.Contains("No delimiter", mainEntry);
            Assert.Contains("Exec = /old/debug", lines);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies executable characters use desktop-string and command quoting without changing the URL code.</summary>
    [Theory]
    [InlineData("/Applications/$Survey/SrvSurvey.AppImage", @"/Applications/\\$Survey/SrvSurvey.AppImage", "")]
    [InlineData("/Applications/`Survey`/SrvSurvey.AppImage", @"/Applications/\\`Survey\\`/SrvSurvey.AppImage", "")]
    [InlineData(
        "/Applications/100%/%u-%F-SrvSurvey.AppImage",
        "/Applications/100%%/%%u-%%F-SrvSurvey.AppImage",
        "/usr/bin/env "
    )]
    [InlineData(@"/Applications/Back\Slash/SrvSurvey.AppImage", @"/Applications/Back\\\\Slash/SrvSurvey.AppImage", "")]
    [InlineData("/Applications/\"Survey\"/SrvSurvey.AppImage", """/Applications/\\"Survey\\"/SrvSurvey.AppImage""", "")]
    public async Task RegistrationEscapesExecutableAndKeepsUrlFieldCode(
        string executable,
        string encodedExecutable,
        string commandPrefix
    )
    {
        string directory = Directory.CreateTempSubdirectory("SrvSurvey-protocol-").FullName;
        try
        {
            string launcher = await FrontierProtocolRegistration.WriteLinuxDesktopFileAsync(directory, executable);
            string[] lines = await File.ReadAllLinesAsync(launcher);

            Assert.Contains($"Exec={commandPrefix}\"{encodedExecutable}\" %u", lines);
            Assert.Contains("TryExec=" + executable.Replace("\\", "\\\\", StringComparison.Ordinal), lines);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies a malformed launcher is reported without replacing its contents.</summary>
    [Fact]
    public async Task RegistrationRejectsMissingDesktopEntrySection()
    {
        string directory = Directory.CreateTempSubdirectory("SrvSurvey-protocol-").FullName;
        try
        {
            string launcher = Path.Combine(directory, "io.github.fenris159.SrvSurvey.desktop");
            const string original = "[Desktop Action Debug]\nExec=/debug\n";
            await File.WriteAllTextAsync(launcher, original);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                FrontierProtocolRegistration.WriteLinuxDesktopFileAsync(directory, "/Applications/SrvSurvey.AppImage")
            );

            Assert.Equal(original, await File.ReadAllTextAsync(launcher));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
