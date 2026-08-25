using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class EdsmSettingsMarkupTests
{
    [Fact]
    public void EdsmCardFollowsInaraAndUsesCommanderScopedCredentialOptIn()
    {
        var document = LoadSettingsView();
        var nameAttribute = XName.Get(
            "Name",
            "http://schemas.microsoft.com/winfx/2006/xaml");
        var inara = Assert.Single(
            document.Descendants(),
            element => (string?)element.Attribute(nameAttribute) == "InaraCard");
        var edsm = Assert.Single(
            document.Descendants(),
            element => (string?)element.Attribute(nameAttribute) == "EdsmCard");

        Assert.Equal(
            edsm,
            inara.ElementsAfterSelf().First(element => element.Name.LocalName == "Border"));

        var values = edsm.DescendantsAndSelf()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();
        Assert.Contains("{Binding Edsm.EdsmCommanderName, Mode=TwoWay}", values);
        Assert.Contains("{Binding Edsm.ApiKey, Mode=TwoWay}", values);
        Assert.Contains("{Binding Edsm.SaveCredentialsCommand}", values);
        Assert.Contains("{Binding Edsm.RequestClearCredentialsCommand}", values);
        Assert.Contains("{Binding Edsm.ConfirmClearCredentialsCommand}", values);
        Assert.Contains(
            "Enable direct EDSM synchronization in only one application at a time to avoid duplicate requests.",
            values);
        Assert.Contains(
            values,
            value => value.Contains("Startup history", StringComparison.Ordinal)
                && value.Contains("diagnostic replay", StringComparison.Ordinal)
                && value.Contains("multiple Elite windows", StringComparison.Ordinal));
    }

    private static XDocument LoadSettingsView() => XDocument.Load(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "SrvSurvey.Desktop",
        "Views",
        "SettingsView.axaml"));

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
