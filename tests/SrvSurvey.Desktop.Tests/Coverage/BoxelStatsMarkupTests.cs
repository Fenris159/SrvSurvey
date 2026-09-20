using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class BoxelStatsMarkupTests
{
    [Fact]
    public void BrowserRowsUseTheSameWrappingStackAsRecentRows()
    {
        var document = XDocument.Load(
            Path.Combine(FindRepositoryRoot(), "src", "SrvSurvey.Desktop", "BoxelStatsWindow.axaml")
        );
        XElement row = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "StackPanel"
                && element.Attribute("Classes")?.Value == "boxel-stats-browser-row"
            );
        XElement prefix = row.Elements()
            .Single(element =>
                element.Name.LocalName == "TextBlock" && element.Attribute("Text")?.Value == "{Binding Prefix}"
            );

        Assert.Equal("Wrap", prefix.Attribute("TextWrapping")?.Value);
        Assert.Contains(
            row.Elements(),
            element => element.Name.LocalName == "Grid" && element.Attribute("ColumnDefinitions")?.Value == "*,Auto"
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
