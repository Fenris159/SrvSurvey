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
        Assert.Contains("GuardianZoomOverlayPresentation", guardianSite);
        Assert.Contains("ShowEmbeddedZoomPreview", guardianSite);
    }

    [Fact]
    public void GuardianMapShowsLandedShipChevronAtBottomLeft()
    {
        var desktop = Path.Combine(FindRepositoryRoot(), "src", "SrvSurvey.Desktop");
        var presentation = XDocument.Load(Path.Combine(
            desktop,
            "GuardianSiteOverlayPresentation.axaml"));
        var shipIndicator = presentation.Descendants()
            .Single(element => element.Name.LocalName == "Border"
                && element.Attribute("IsVisible")?.Value
                    == "{Binding Guardian.IsShipNavigationVisible}");
        var chevron = shipIndicator.Descendants().Single(element =>
            element.Name.LocalName == "DirectionalChevronControl");

        Assert.Equal("Left", shipIndicator.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("Bottom", shipIndicator.Attribute("VerticalAlignment")?.Value);
        Assert.Equal("8,0,0,8", shipIndicator.Attribute("Margin")?.Value);
        Assert.Equal(
            "{Binding Guardian.ShipRelativeBearingDegrees}",
            chevron.Attribute("BearingDegrees")?.Value);
        Assert.Equal(
            "{Binding Guardian.IsShipNavigationFar}",
            chevron.Attribute("IsFar")?.Value);
        Assert.Contains(shipIndicator.Descendants(), element =>
            element.Attribute("Text")?.Value
                == "{Binding Guardian.ShipNavigationDistanceText}");
    }

    [Fact]
    public void GuardianFiregroupChoicesUseHighContrastSelectedState()
    {
        var desktop = Path.Combine(FindRepositoryRoot(), "src", "SrvSurvey.Desktop");
        var presentation = XDocument.Load(Path.Combine(
            desktop,
            "GuardianStatusOverlayPresentation.axaml"));
        var styles = presentation.Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .ToArray();
        var selectedChoice = styles.Single(style =>
            style.Attribute("Selector")?.Value
                == "Border.guardian-legacy-choice.selected");
        var selectedText = styles.Single(style =>
            style.Attribute("Selector")?.Value
                == "Border.guardian-legacy-choice.selected TextBlock");
        var selectedBindings = presentation.Descendants()
            .SelectMany(element => element.Attributes())
            .Count(attribute => attribute.Name.LocalName == "Classes.selected"
                && attribute.Value.StartsWith(
                    "{Binding Guardian.IsGuardianChoice",
                    StringComparison.Ordinal));

        Assert.Equal(6, selectedBindings);
        Assert.Contains(selectedChoice.Elements(), setter =>
            setter.Attribute("Property")?.Value == "BorderThickness"
            && setter.Attribute("Value")?.Value == "2");
        Assert.Contains(selectedChoice.Elements(), setter =>
            setter.Attribute("Property")?.Value == "Background"
            && setter.Attribute("Value")?.Value
                == "{DynamicResource RavenGuardianSecondaryBrush}");
        Assert.Contains(selectedText.Elements(), setter =>
            setter.Attribute("Property")?.Value == "Foreground"
            && setter.Attribute("Value")?.Value
                == "{DynamicResource RavenGuardianBackgroundBrush}");
        Assert.Contains(selectedText.Elements(), setter =>
            setter.Attribute("Property")?.Value == "FontWeight"
            && setter.Attribute("Value")?.Value == "Bold");
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
