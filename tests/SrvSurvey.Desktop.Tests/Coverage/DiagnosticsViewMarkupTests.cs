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
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "CalendarDatePicker"
            && element.Attribute("SelectedDate")?.Value
                == "{Binding JournalHistory.RangeFromDate, Mode=TwoWay}");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "TimePicker"
            && element.Attribute("SelectedTime")?.Value
                == "{Binding JournalHistory.RangeFromTime, Mode=TwoWay}");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "CalendarDatePicker"
            && element.Attribute("SelectedDate")?.Value
                == "{Binding JournalHistory.RangeToDate, Mode=TwoWay}");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "TimePicker"
            && element.Attribute("SelectedTime")?.Value
                == "{Binding JournalHistory.RangeToTime, Mode=TwoWay}");
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "TextBox"
            && element.Attribute("Text")?.Value is
                "{Binding JournalHistory.RangeFromText, Mode=TwoWay}"
                or "{Binding JournalHistory.RangeToText, Mode=TwoWay}");
    }

    [Fact]
    public void ReplayRangeUsesAlignedDateAndTimeRows()
    {
        var document = LoadDiagnosticsView();
        var rangeFields = FindNamedElement(document, "ReplayRangeFields");

        Assert.Equal("110,360", rangeFields.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal(
            "Auto,Auto,Auto,Auto",
            rangeFields.Attribute("RowDefinitions")?.Value);
        AssertRangeFieldLayout(
            rangeFields,
            "Start Date:",
            "{Binding JournalHistory.RangeFromDate, Mode=TwoWay}",
            "0",
            "{Binding JournalHistory.RangeFromTime, Mode=TwoWay}",
            "1");
        AssertRangeFieldLayout(
            rangeFields,
            "End Date:",
            "{Binding JournalHistory.RangeToDate, Mode=TwoWay}",
            "2",
            "{Binding JournalHistory.RangeToTime, Mode=TwoWay}",
            "3");
    }

    [Fact]
    public void JournalHistoryDetailsDoNotDereferenceAMissingSelection()
    {
        var document = LoadDiagnosticsView();
        var textBindings = document.Descendants()
            .Select(element => element.Attribute("Text")?.Value)
            .OfType<string>()
            .ToArray();

        Assert.DoesNotContain(
            textBindings,
            binding => binding.Contains(
                "JournalHistory.SelectedEvent.",
                StringComparison.Ordinal));
        Assert.Contains(
            "{Binding JournalHistory.SelectedEventFileName}",
            textBindings);
        Assert.Contains(
            "{Binding JournalHistory.SelectedEventCommanderName}",
            textBindings);
        Assert.Contains(
            "{Binding JournalHistory.SelectedEventSystemName}",
            textBindings);
        Assert.Contains(
            "{Binding JournalHistory.SelectedEventTimestamp}",
            textBindings);
        Assert.Contains(
            "{Binding JournalHistory.SelectedEventRawJson}",
            textBindings);
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

    private static void AssertRangeFieldLayout(
        XElement fields,
        string dateLabel,
        string dateBinding,
        string dateRow,
        string timeBinding,
        string timeRow)
    {
        Assert.Contains(fields.Elements(), element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value == dateLabel
            && element.Attribute("Grid.Row")?.Value == dateRow);
        Assert.Contains(fields.Elements(), element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value == "Time H/M/S:"
            && element.Attribute("Grid.Row")?.Value == timeRow);
        Assert.Contains(fields.Elements(), element =>
            element.Name.LocalName == "CalendarDatePicker"
            && element.Attribute("SelectedDate")?.Value == dateBinding
            && element.Attribute("Grid.Row")?.Value == dateRow
            && element.Attribute("Grid.Column")?.Value == "1");
        Assert.Contains(fields.Elements(), element =>
            element.Name.LocalName == "TimePicker"
            && element.Attribute("SelectedTime")?.Value == timeBinding
            && element.Attribute("Grid.Row")?.Value == timeRow
            && element.Attribute("Grid.Column")?.Value == "1");
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
