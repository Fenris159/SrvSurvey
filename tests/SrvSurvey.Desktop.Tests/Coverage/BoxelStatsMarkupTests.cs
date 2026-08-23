using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class BoxelStatsMarkupTests
{
    [Fact]
    public void BrowserRowsKeepPrefixAndMetricsOnSeparateReadableLines()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.Desktop",
            "BoxelStatsWindow.axaml"));
        var row = document.Descendants().Single(element =>
            element.Name.LocalName == "Grid"
            && element.Attribute("Classes")?.Value == "boxel-stats-browser-row");
        var prefix = row.Elements().Single(element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value == "{Binding Prefix}");

        Assert.Equal("Auto,Auto", row.Attribute("RowDefinitions")?.Value);
        Assert.Null(row.Attribute("ColumnDefinitions"));
        Assert.Equal("NoWrap", prefix.Attribute("TextWrapping")?.Value);
        Assert.Equal("CharacterEllipsis", prefix.Attribute("TextTrimming")?.Value);
        Assert.Contains(row.Elements(), element =>
            element.Name.LocalName == "Grid"
            && element.Attribute("Grid.Row")?.Value == "1");
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
