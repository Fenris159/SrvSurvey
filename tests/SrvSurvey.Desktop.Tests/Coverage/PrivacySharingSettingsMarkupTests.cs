using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class PrivacySharingSettingsMarkupTests
{
    [Fact]
    public void PrivacySharingStartsWithDuplicatePublicationWarning()
    {
        var document = XDocument.Load(
            Path.Combine(FindRepositoryRoot(), "src", "SrvSurvey.Desktop", "Views", "SettingsView.axaml")
        );
        var nameAttribute = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
        var warning = Assert.Single(
            document.Descendants(),
            element => (string?)element.Attribute(nameAttribute) == "SharingConflictWarning"
        );
        var firstSharingCard = Assert.Single(
            document.Descendants(),
            element => (string?)element.Attribute(nameAttribute) == "NetworkPrivacyCard"
        );
        var values = warning
            .DescendantsAndSelf()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();

        Assert.Contains("{Binding SettingsWorkspace.IsPrivacySelected}", values);
        Assert.Contains(
            values,
            value =>
                value.Contains("only one Elite Dangerous third-party application", StringComparison.Ordinal)
                && value.Contains("duplicate entries", StringComparison.Ordinal)
                && value.Contains("conflicting updates", StringComparison.Ordinal)
        );
        Assert.Contains(firstSharingCard, warning.ElementsAfterSelf());
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
