using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class GuardianZoomAndPanelVisibilityContractTests
{
    [Fact]
    public void GuardianZoomUsesTwoCircularOrbsInAnInteractiveChildWindow()
    {
        var desktop = Path.Combine(FindRepositoryRoot(), "src", "SrvSurvey.Desktop");
        var presentation = XDocument.Load(Path.Combine(
            desktop,
            "GuardianZoomOverlayPresentation.axaml"));
        var guardianSite = File.ReadAllText(Path.Combine(
            desktop,
            "GuardianSiteOverlayPresentation.axaml"));
        var coordinator = File.ReadAllText(Path.Combine(
            desktop,
            "Platform",
            "Overlay",
            "GuardianOverlayCoordinator.cs"));
        var buttons = presentation.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .ToArray();

        Assert.Equal(2, buttons.Length);
        Assert.All(buttons, button =>
        {
            Assert.Equal("20", button.Attribute("Width")?.Value);
            Assert.Equal("20", button.Attribute("Height")?.Value);
            Assert.Equal("10", button.Attribute("CornerRadius")?.Value);
        });
        Assert.Contains(buttons, button =>
            button.Attribute("Command")?.Value == "{Binding ZoomInCommand}");
        Assert.Contains(buttons, button =>
            button.Attribute("Command")?.Value == "{Binding ZoomOutCommand}");
        Assert.Contains("platform.PreparePassiveWindow(overlay)", coordinator);
        Assert.Contains("platform.SetInteractive(overlay, interactive: true)", coordinator);
        Assert.Contains("OverlayWindowRegistry.Shared.Register(overlay, \"PlotGuardians\")", coordinator);
        Assert.Contains("GuardianZoomOverlayPresentation", guardianSite);
        Assert.Contains("ShowEmbeddedZoomPreview", guardianSite);
    }

    [Fact]
    public void OverlaySettingsOfferMasterAvailabilityAndOptionalShortcuts()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.Desktop",
            "Views",
            "OverlaySettingsView.axaml");
        var settings = XDocument.Load(path);
        var card = settings.Descendants().Single(element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name"
                && attribute.Value == "PanelVisibilityCard"));
        var values = card.Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();

        Assert.Contains("{Binding IsEnabled, Mode=TwoWay}", values);
        Assert.Contains("{Binding Shortcut.Chord, Mode=TwoWay}", values);
        Assert.Contains("Click, then hold shortcut keys", values);
        Assert.Contains(values, value => value.Contains(
            "no default binding",
            StringComparison.Ordinal));

        var outerStack = settings.Root!.Elements().Single(element =>
            element.Name.LocalName == "StackPanel");
        var lastCard = outerStack.Elements().Last(element =>
            element.Name.LocalName == "Border");
        Assert.Contains(lastCard.Attributes(), attribute =>
            attribute.Name.LocalName == "Name"
            && attribute.Value == "PanelVisibilityCard");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "SrvSurvey.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root not found.");
    }
}
