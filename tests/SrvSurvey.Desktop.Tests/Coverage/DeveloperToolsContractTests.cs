using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class DeveloperToolsContractTests
{
    [Fact]
    public void DebugBuildsIncludeAvaloniaDeveloperToolsSupport()
    {
        var repositoryRoot = FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "SrvSurvey.Desktop",
            "SrvSurvey.Desktop.csproj"));
        var diagnosticsPackage = project.Descendants().Single(element =>
            element.Name.LocalName == "PackageReference"
            && element.Attribute("Include")?.Value
                == "AvaloniaUI.DiagnosticsSupport");
        var itemGroup = diagnosticsPackage.Parent
            ?? throw new InvalidDataException(
                "Developer Tools package group is missing.");
        var app = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "SrvSurvey.Desktop",
            "App.axaml.cs"));

        Assert.Equal("2.2.3", diagnosticsPackage.Attribute("Version")?.Value);
        Assert.Equal(
            "'$(Configuration)' == 'Debug'",
            itemGroup.Attribute("Condition")?.Value);
        Assert.Contains(
            "this.AttachDeveloperTools();",
            app.Replace("\r\n", "\n"),
            StringComparison.Ordinal);
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
