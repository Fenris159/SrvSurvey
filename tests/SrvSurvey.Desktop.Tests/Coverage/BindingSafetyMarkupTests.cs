namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class BindingSafetyMarkupTests
{
    [Fact]
    public void OverlayPreviewBindsProgressBarsToANonNullableValue()
    {
        var markup = ReadDesktopMarkup("OverlayPositionPreviewWindow.axaml");

        Assert.Contains(
            "Value=\"{Binding ProgressValue}\"",
            markup,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Value=\"{Binding Progress}\"",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void JourneyDetailsDoNotDereferenceANullSelection()
    {
        var markup = ReadDesktopMarkup("JourneyWindow.axaml");

        Assert.Contains("{Binding SelectedSystemName}", markup);
        Assert.Contains("{Binding SelectedSystemAddressText}", markup);
        Assert.DoesNotContain("{Binding SelectedSystem.Name}", markup);
        Assert.DoesNotContain("{Binding SelectedSystem.Address", markup);
    }

    private static string ReadDesktopMarkup(string fileName) => File.ReadAllText(
        Path.Combine(FindRepositoryRoot(), "src", "SrvSurvey.Desktop", fileName));

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
