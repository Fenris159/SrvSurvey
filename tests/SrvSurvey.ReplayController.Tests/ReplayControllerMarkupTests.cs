using System.Xml.Linq;

namespace SrvSurvey.ReplayController.Tests;

public sealed class ReplayControllerMarkupTests
{
    [Fact]
    public void SpeedPickerFollowsPlaybackChangeEligibility()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.ReplayController",
            "MainWindow.axaml"));
        var speedPicker = document.Descendants().Single(element =>
            element.Name.LocalName == "ComboBox"
            && element.Attribute("SelectedItem")?.Value
                == "{Binding SpeedMultiplier, Mode=TwoWay}");

        Assert.Equal(
            "{Binding CanChangeSpeed}",
            speedPicker.Attribute("IsEnabled")?.Value);
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
