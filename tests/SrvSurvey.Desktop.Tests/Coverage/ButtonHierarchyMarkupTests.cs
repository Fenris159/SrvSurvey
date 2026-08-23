using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class ButtonHierarchyMarkupTests
{
    [Fact]
    public void UtilityActionsAreNeverRenderedAsPrimaryButtons()
    {
        var violations = LoadButtons()
            .Where(button => HasClass(button.Element, "primary"))
            .Where(button => IsUtilityAction(button.Element))
            .Select(button => $"{button.File}: {Describe(button.Element)}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Utility buttons must use the neutral secondary hierarchy:\n"
            + string.Join("\n", violations));
    }

    [Fact]
    public void SharedButtonStylesKeepEmphasisAndControlFillsSeparate()
    {
        var root = FindRepositoryRoot();
        var styles = File.ReadAllText(Path.Combine(
                root,
                "src",
                "SrvSurvey.Desktop",
                "Styles",
                "RavenStyles.axaml"))
            .ReplaceLineEndings("\n");

        Assert.Contains(
            "Button.primary\">\n    <Setter Property=\"Background\" Value=\"{DynamicResource RavenControlAccentBrush}\"",
            styles);
        Assert.Contains(
            "Button.link\">\n    <Setter Property=\"Background\" Value=\"Transparent\"",
            styles);
        Assert.Contains(
            "<Setter Property=\"Foreground\" Value=\"{DynamicResource RavenAccentBrush}\" />",
            styles);
    }

    private static IEnumerable<(string File, XElement Element)> LoadButtons()
    {
        var root = FindRepositoryRoot();
        var desktop = Path.Combine(root, "src", "SrvSurvey.Desktop");
        foreach (var file in Directory.EnumerateFiles(
                     desktop,
                     "*.axaml",
                     SearchOption.AllDirectories))
        {
            var document = XDocument.Load(file, LoadOptions.PreserveWhitespace);
            foreach (var button in document.Descendants().Where(element =>
                         element.Name.LocalName == "Button"))
            {
                yield return (Path.GetRelativePath(root, file), button);
            }
        }
    }

    private static bool IsUtilityAction(XElement button)
    {
        var command = button.Attribute("Command")?.Value ?? string.Empty;
        var content = button.Attribute("Content")?.Value ?? string.Empty;
        return command.Contains("Refresh", StringComparison.OrdinalIgnoreCase)
            || content.Equals("Close", StringComparison.OrdinalIgnoreCase)
            || content.StartsWith("Open ", StringComparison.OrdinalIgnoreCase)
            || content.StartsWith("Export ", StringComparison.OrdinalIgnoreCase)
            || content is "Copy handler" or "Copy logs";
    }

    private static bool HasClass(XElement element, string className) =>
        (element.Attribute("Classes")?.Value ?? string.Empty)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Contains(className, StringComparer.Ordinal);

    private static string Describe(XElement button) =>
        button.Attribute("Content")?.Value
        ?? button.Attribute("Command")?.Value
        ?? "unnamed button";

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "SrvSurvey.slnx")))
        {
            directory = directory.Parent;
        }

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
