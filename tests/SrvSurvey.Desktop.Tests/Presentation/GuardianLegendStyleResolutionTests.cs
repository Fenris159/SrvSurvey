using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class GuardianLegendStyleResolutionTests
{
    [AvaloniaFact]
    public void LegendTemplateUsesTheRequestedCollapsedAndExpandedGeometry()
    {
        var view = new GuardianView();
        using var viewModel = MainWindowViewModelTestBuilder.Create(
            configuredJournalDirectory: null,
            _ => { });
        view.DataContext = viewModel;
        var expander = view.FindControl<Expander>(
            "GuardianSurveyMapLegendExpander")
            ?? throw new InvalidOperationException("Legend expander is missing.");
        var legend = view.FindControl<Border>("GuardianSurveyMapLegend")
            ?? throw new InvalidOperationException("Legend container is missing.");
        var parent = Assert.IsType<Panel>(legend.Parent, exactMatch: false);
        Assert.True(parent.Children.Remove(legend));
        legend.IsVisible = true;
        view.Content = legend;
        var window = new Window
        {
            Width = 420,
            Height = 420,
            Content = view,
        };

        try
        {
            window.Show();
            Assert.NotNull(window.CaptureRenderedFrame());

            var templateBorders = expander.GetVisualDescendants()
                .OfType<Border>()
                .ToArray();
            var headerBackground = Assert.Single(
                templateBorders,
                border => border.Name == "ToggleButtonBackground");
            var content = Assert.Single(
                templateBorders,
                border => border.Name == "ExpanderContent");

            Assert.Equal(new CornerRadius(12), headerBackground.CornerRadius);
            Assert.Equal(new Thickness(1), headerBackground.BorderThickness);
            Assert.Equal(
                new Thickness(0, 0, 0, 10),
                headerBackground.Margin);
            Assert.Equal(0, headerBackground.BoxShadow.Count);
            Assert.Equal(new CornerRadius(0, 0, 12, 12), content.CornerRadius);
            Assert.Equal(
                new Thickness(1, 0, 1, 1),
                content.BorderThickness);
            Assert.Equal(
                BackgroundSizing.OuterBorderEdge,
                content.BackgroundSizing);
            Assert.Equal(new Thickness(10, 0, 0, 0), content.Padding);
            Assert.Equal(new Thickness(0), content.Margin);
            Assert.Equal(290, content.MinWidth);
            Assert.Equal(270, content.MinHeight);
            Assert.Equal(0, content.BoxShadow.Count);

            expander.IsExpanded = true;
            Assert.NotNull(window.CaptureRenderedFrame());
            Assert.Equal(new CornerRadius(12), content.CornerRadius);
            Assert.Equal(new Thickness(1), content.BorderThickness);

            Assert.DoesNotContain("monochrome", expander.Classes);
            viewModel.ThemeOptions.Single(option =>
                option.Definition.Key == "monochrome-dark")
                .SelectCommand.Execute(null);
            Assert.NotNull(window.CaptureRenderedFrame());
            Assert.Contains("monochrome", expander.Classes);
            Assert.Equal(
                Color.Parse("#1C1C1C"),
                Assert.IsType<ISolidColorBrush>(
                    headerBackground.Background,
                    exactMatch: false).Color);
            Assert.Equal(
                Color.Parse("#33FFFFFF"),
                Assert.IsType<ISolidColorBrush>(
                    headerBackground.BorderBrush,
                    exactMatch: false).Color);
            Assert.Equal(
                Color.Parse("#2B2B2B"),
                Assert.IsType<ISolidColorBrush>(
                    content.Background,
                    exactMatch: false).Color);
            Assert.Equal(
                Color.Parse("#33FFFFFF"),
                Assert.IsType<ISolidColorBrush>(
                    content.BorderBrush,
                    exactMatch: false).Color);

            viewModel.ThemeOptions.Single(option =>
                option.Definition.Key == RavenThemeCatalog.DefaultThemeKey)
                .SelectCommand.Execute(null);
            Assert.NotNull(window.CaptureRenderedFrame());
            Assert.DoesNotContain("monochrome", expander.Classes);
        }
        finally
        {
            window.Close();
        }
    }
}
