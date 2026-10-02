using SrvSurvey.Desktop.Platform.Frontier;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class FrontierProtocolRegistrationTests
{
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
            Assert.Contains("[Desktop Action Debug]\nExec=/old/debug", updated, StringComparison.Ordinal);
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
