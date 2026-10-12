using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class EddnSettingsMarkupTests
{
    [Fact]
    public void ConfigureButtonIsImmediatelyBeforeInaraAndSchemaModeIsNotAChoice()
    {
        XDocument document = LoadMarkup("Views", "SettingsView.axaml");
        string[] values = document
            .Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();

        Assert.Contains("Configure EDDN Sharing", values);
        Assert.DoesNotContain(values, value => value.Contains("EddnUseTestSchemas", StringComparison.Ordinal));

        string[] cardTitles = document
            .Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Select(element => element.Attribute("Text")?.Value)
            .OfType<string>()
            .Where(value => value is "EDDN" or "Inara")
            .ToArray();
        Assert.Equal(["EDDN", "Inara"], cardTitles);
    }

    [Fact]
    public void DialogExplainsIdentityStorageLiveSchemasAndDuplicateUploaders()
    {
        string[] values = LoadMarkup("EddnIntegrationDialog.axaml")
            .Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();
        string text = string.Join('\n', values);

        Assert.Contains("Commander name", text, StringComparison.Ordinal);
        Assert.Contains("no account, personal API key", text, StringComparison.Ordinal);
        Assert.Contains("durable local retry queue", text, StringComparison.Ordinal);
        Assert.Contains("production schema references", text, StringComparison.Ordinal);
        Assert.Contains("SrvSurvey or EDMC", text, StringComparison.Ordinal);
        Assert.Contains("duplicate submissions", text, StringComparison.Ordinal);
        Assert.Contains("Enable EDDN sharing for live Commander sessions", values);
    }

    /// <summary>Verifies ed astro has dedicated configure disclosure and new poi precedes refresh.</summary>
    [Fact]
    public void EdAstroHasDedicatedConfigureDisclosureAndNewPoiPrecedesRefresh()
    {
        XDocument settings = LoadMarkup("Views", "SettingsView.axaml");
        Assert.Contains(
            settings.Descendants(),
            element => element.Attribute("Content")?.Value == "Configure EDAstro Sharing"
        );
        string disclosure = string.Join(
            "\n",
            LoadMarkup("EdAstroIntegrationDialog.axaml").Descendants().Attributes().Select(attribute => attribute.Value)
        );
        Assert.Contains("SrvSurvey-XP", disclosure, StringComparison.Ordinal);
        Assert.Contains("CarrierStats", disclosure, StringComparison.Ordinal);
        Assert.Contains("ScanOrganic", disclosure, StringComparison.Ordinal);
        Assert.Contains("off by default", disclosure, StringComparison.Ordinal);
        Assert.Contains("ten seconds", disclosure, StringComparison.Ordinal);
        Assert.Contains("duplicate submissions", disclosure, StringComparison.Ordinal);
        XElement[] buttons = LoadMarkup("Views", "ExplorationPoiView.axaml")
            .Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Where(element => element.Attribute("Content")?.Value is "New POI" or "Refresh catalogue")
            .ToArray();
        Assert.Equal(["New POI", "Refresh catalogue"], buttons.Select(element => element.Attribute("Content")!.Value));
    }

    private static XDocument LoadMarkup(params string[] relativePath)
    {
        return XDocument.Load(
            Path.Combine(FindRepositoryRoot(), "src", "SrvSurvey.Desktop", Path.Combine(relativePath))
        );
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

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
