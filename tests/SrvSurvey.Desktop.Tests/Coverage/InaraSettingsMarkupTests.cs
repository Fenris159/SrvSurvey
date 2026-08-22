using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class InaraSettingsMarkupTests
{
    [Fact]
    public void InaraCardUsesCommanderKeyOptInAndWarnsAboutDuplicateUploads()
    {
        var values = LoadSettingsView().Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();

        Assert.Contains(
            "Enable Inara uploads in only one application at a time to avoid duplicate commander events.",
            values);
        Assert.Contains("{Binding Inara.SaveApiKeyCommand}", values);
        Assert.Contains("{Binding Inara.RequestClearApiKeyCommand}", values);
        Assert.Contains("{Binding Inara.ConfirmClearApiKeyCommand}", values);
        Assert.DoesNotContain(
            values,
            value => value.Contains(
                "Developer test mode",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            values,
            value => value.Contains(
                "Inara.UploadEnabled",
                StringComparison.Ordinal));
    }

    private static XDocument LoadSettingsView() => XDocument.Load(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "SrvSurvey.Desktop",
        "Views",
        "SettingsView.axaml"));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SrvSurvey.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root.");
    }
}
