using System.Text.RegularExpressions;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed partial class TestIsolationContractTests
{
    [Fact]
    public void DesktopTestsDoNotConstructMainWindowViewModelWithProductionAppData()
    {
        var testRoot = Path.Combine(FindRepositoryRoot(), "tests", "SrvSurvey.Desktop.Tests");
        var offenders = Directory
            .EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => ProductionConstructorPattern().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(testRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
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

    [GeneratedRegex(@"new\s+MainWindowViewModel\s*\(")]
    private static partial Regex ProductionConstructorPattern();
}
