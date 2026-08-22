using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class EddnSettingsMarkupTests
{
    [Fact]
    public void ConfigureButtonIsImmediatelyBeforeInaraAndSchemaModeIsNotAChoice()
    {
        var document = LoadMarkup("Views", "SettingsView.axaml");
        var values = document.Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();

        Assert.Contains("Configure EDDN Sharing", values);
        Assert.DoesNotContain(
            values,
            value => value.Contains(
                "EddnUseTestSchemas",
                StringComparison.Ordinal));

        var cardTitles = document.Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Select(element => element.Attribute("Text")?.Value)
            .OfType<string>()
            .Where(value => value is "EDDN" or "Inara")
            .ToArray();
        Assert.Equal(["EDDN", "Inara"], cardTitles);
    }

    [Fact]
    public void DialogExplainsIdentityStorageTestSchemasAndDuplicateUploaders()
    {
        var values = LoadMarkup("EddnIntegrationDialog.axaml")
            .Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();
        var text = string.Join('\n', values);

        Assert.Contains("Commander name", text, StringComparison.Ordinal);
        Assert.Contains("no account, personal API key", text, StringComparison.Ordinal);
        Assert.Contains("durable local retry queue", text, StringComparison.Ordinal);
        Assert.Contains("/test schema references fixed internally", text, StringComparison.Ordinal);
        Assert.Contains("SrvSurvey or EDMC", text, StringComparison.Ordinal);
        Assert.Contains("duplicate submissions", text, StringComparison.Ordinal);
        Assert.Contains("Enable EDDN sharing for live Commander sessions", values);
    }

    private static XDocument LoadMarkup(params string[] relativePath)
    {
        return XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.Desktop",
            Path.Combine(relativePath)));
    }

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
