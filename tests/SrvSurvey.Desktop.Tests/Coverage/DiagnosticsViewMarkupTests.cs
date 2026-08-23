using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class DiagnosticsViewMarkupTests
{
    [Fact]
    public void JournalHistoryIsSeparateFromTheQuestInspector()
    {
        var document = LoadDiagnosticsView();
        var tabs = document.Descendants()
            .Where(element => element.Name.LocalName == "TabItem")
            .Select(element => element.Attribute("Header")?.Value)
            .ToArray();

        Assert.Contains("History", tabs);
        Assert.Contains("Inspector", tabs);
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "ListBox"
            && element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name"
                && attribute.Value == "JournalHistoryEventList"));
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "Button"
            && element.Attribute("Content")?.Value == "Export replay package");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "TextBox"
            && element.Attribute("Text")?.Value
                == "{Binding JournalHistory.SearchText, Mode=TwoWay}");
        Assert.Contains(document.Descendants(), element =>
            element.Attribute("Text")?.Value
                == "{Binding DiagnosticReplayStatus}");
        var historyList = FindNamedElement(
            document,
            "JournalHistoryEventList");
        Assert.DoesNotContain(
            historyList.Descendants(),
            element => element.Name.LocalName == "ItemsPanelTemplate");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "CheckBox"
            && element.Attribute("Content")?.Value
                == "Redact commander identities, chat, locations, coordinates, and screenshot paths");
    }

    [Fact]
    public void LiveLogUsesAnIndependentNonCaretScrollSurface()
    {
        var document = LoadDiagnosticsView();
        var pageScroller = FindNamedElement(
            document,
            "DiagnosticsPageScroller");
        var logScroller = FindNamedElement(
            document,
            "ApplicationLogScroller");
        var logBinding = "{Binding DiagnosticsLog.LogText, Mode=OneWay}";

        Assert.Equal("ScrollViewer", pageScroller.Name.LocalName);
        Assert.Equal("ScrollViewer", logScroller.Name.LocalName);
        Assert.Contains(logScroller.Descendants(), element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value == logBinding);
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "TextBox"
            && element.Attribute("Text")?.Value == logBinding);
    }

    [Fact]
    public void JournalInspectorUsesStableVerticalRowsAndOwnsWheelScrolling()
    {
        var document = LoadDiagnosticsView();
        var eventList = FindNamedElement(
            document,
            "JournalInspectorEventList");

        Assert.Equal(
            "False",
            FindAttribute(
                eventList,
                "ScrollViewer.IsScrollChainingEnabled"));
        Assert.Contains(
            eventList.Descendants(),
            element => element.Name.LocalName == "ItemsPanelTemplate"
                && element.Descendants().Any(child =>
                    child.Name.LocalName == "StackPanel"));

        var inspector = eventList.Ancestors().First(element =>
            element.Name.LocalName == "StackPanel"
            && element.Descendants().Any(descendant =>
                descendant.Name.LocalName == "TextBlock"
                && descendant.Attribute("Text")?.Value
                    == "Journal inspector"));
        var nestedScrollers = inspector.Descendants().Where(element =>
            element.Name.LocalName is "ScrollViewer" or "TextBox");
        Assert.All(
            nestedScrollers,
            element => Assert.Equal(
                "False",
                FindAttribute(
                    element,
                    "IsScrollChainingEnabled",
                    "ScrollViewer.IsScrollChainingEnabled")));
    }

    private static XDocument LoadDiagnosticsView() => XDocument.Load(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "SrvSurvey.Desktop",
        "Views",
        "DiagnosticsView.axaml"));

    private static string? FindAttribute(
        XElement element,
        params string[] names) => element.Attributes()
        .FirstOrDefault(attribute => names.Contains(
            attribute.Name.LocalName,
            StringComparer.Ordinal))
        ?.Value;

    private static XElement FindNamedElement(
        XDocument document,
        string name) => document.Descendants().Single(element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name"
                && attribute.Value == name));

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
