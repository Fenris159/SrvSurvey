using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlayEditorFolderTabTests
{
    [AvaloniaFact]
    public void PreviewRendersAVisibleFolderTabAttachedAboveTheBody()
    {
        var definition = OverlayLayoutCatalog.GetRequired("PlotFSSInfo");
        var preview = new OverlayPositionPreviewWindow(definition);
        try
        {
            OverlayThemeResources.Apply(preview);
            preview.ApplyRuntimePresentationTheme();
            preview.Show();

            Assert.Equal(
                new Thickness(12, 4, 12, 5),
                preview.EditorFolderTabControl.Padding);
            Assert.Equal(
                new Thickness(2, 2, 2, 0),
                preview.EditorFolderTabControl.BorderThickness);
            Assert.Equal(
                new CornerRadius(7, 7, 0, 0),
                preview.EditorFolderTabControl.CornerRadius);

            Assert.True(preview.EditorFolderTabControl.IsVisible);
            Assert.Equal(
                definition.DisplayName,
                preview.EditorFolderTabLabelControl.Text);
            Assert.True(preview.EditorFolderTabControl.MinHeight >= 24);
            Assert.True(preview.EditorFolderTabControl.Bounds.Width >= 72);
            Assert.True(preview.EditorFolderTabControl.Bounds.Height >= 24);
            Assert.Equal(0, preview.EditorFolderTabControl.Bounds.Top);
            Assert.True(
                preview.PreviewBodyControl.Bounds.Top
                    >= preview.EditorFolderTabControl.Bounds.Bottom - 2);

            AssertFolderTabBrush(preview.EditorFolderTabControl.Background);
            AssertFolderTabBrush(preview.EditorFolderTabControl.BorderBrush);
            Assert.False(preview.EditorFolderTabStateButtonControl.IsVisible);
            Assert.Equal(1, preview.EditorPreviewStateCount);
            Assert.False(preview.CycleEditorPreviewState());

            var frame = preview.CaptureRenderedFrame();
            Assert.NotNull(frame);
        }
        finally
        {
            preview.Close();
        }
    }

    [AvaloniaFact]
    public void StatefulFolderTabCyclesRealSharedPresentationData()
    {
        var definition = OverlayLayoutCatalog.GetRequired("PlotBioSystem");
        var preview = new OverlayPositionPreviewWindow(definition);
        try
        {
            OverlayThemeResources.Apply(preview);
            preview.ApplyRuntimePresentationTheme();
            preview.Show();

            var presentation = Assert.IsType<BiologySurveyOverlayPresentation>(
                preview.RuntimePresentation);
            Assert.True(preview.EditorFolderTabStateButtonControl.IsVisible);
            Assert.Equal(3, preview.EditorPreviewStateCount);
            Assert.Equal("System overview", preview.CurrentEditorPreviewStateName);
            Assert.Contains("1/3", preview.EditorFolderTabStateLabelControl.Text);
            var stateLabelBrush = Assert.IsType<ISolidColorBrush>(
                preview.EditorFolderTabStateLabelControl.Foreground,
                exactMatch: false);
            Assert.Equal(Color.Parse("#5C130D"), stateLabelBrush.Color);

            var overview = Assert.IsType<SystemSurveyOverlayViewModel>(
                presentation.DataContext);
            Assert.True(overview.Survey.BiologySurveyDisplay.IsSystemOverview);
            Assert.Equal(
                "System biology",
                overview.Survey.BiologySurveyDisplay.Title);
            Assert.All(
                overview.Survey.BiologySurveyDisplay.Bodies,
                body => Assert.StartsWith(
                    "avares://SrvSurvey.Desktop/Assets/Bodies/",
                    body.BodyIconAssetPath,
                    StringComparison.Ordinal));
            Assert.Equal(
                overview.Survey.BiologySurveyDisplay.Bodies.Count,
                overview.Survey.BiologySurveyDisplay.Bodies
                    .Select(body => body.BodyIconAssetPath)
                    .Distinct(StringComparer.Ordinal)
                    .Count());

            preview.EditorFolderTabStateButtonControl.RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(presentation, preview.RuntimePresentation);
            Assert.Equal("Body predictions", preview.CurrentEditorPreviewStateName);
            Assert.Contains("2/3", preview.EditorFolderTabStateLabelControl.Text);
            var predictions = Assert.IsType<SystemSurveyOverlayViewModel>(
                presentation.DataContext);
            Assert.True(predictions.Survey.BiologySurveyDisplay.IsBodyDetail);
            Assert.Equal(
                "Body Predictions",
                predictions.Survey.BiologySurveyDisplay.Title);
            Assert.True(predictions.Survey.BiologySurveyDisplay.RequiresDss);
            Assert.All(
                predictions.Survey.BiologySurveyDisplay.Organisms,
                organism => Assert.True(organism.IsPrediction));
            Assert.Equal(
                4,
                predictions.Survey.BiologySurveyDisplay.OrganismGroups.Count);
            Assert.Equal(
                3,
                predictions.Survey.BiologySurveyDisplay.OrganismGroups
                    .Single(group => group.GenusName == "Tussock")
                    .Species.Count);
            Assert.Contains(
                predictions.Survey.BiologySurveyDisplay.OrganismGroups,
                group => group.IsCommanderFirst);
            Assert.Contains(
                predictions.Survey.BiologySurveyDisplay.OrganismGroups,
                group => group.IsGlobalRegionalFirst);

            Assert.True(preview.CycleEditorPreviewState());
            Assert.Same(presentation, preview.RuntimePresentation);
            Assert.Equal("Body identified", preview.CurrentEditorPreviewStateName);
            Assert.Contains("3/3", preview.EditorFolderTabStateLabelControl.Text);
            var identified = Assert.IsType<SystemSurveyOverlayViewModel>(
                presentation.DataContext);
            Assert.True(identified.Survey.BiologySurveyDisplay.IsBodyDetail);
            Assert.Equal(
                "Identified Bio",
                identified.Survey.BiologySurveyDisplay.Title);
            Assert.False(identified.Survey.BiologySurveyDisplay.RequiresDss);
            Assert.Contains(
                identified.Survey.BiologySurveyDisplay.Organisms,
                organism => organism.IsCurrentSample);
            Assert.Contains(
                identified.Survey.BiologySurveyDisplay.Organisms,
                organism => organism.ShouldDim);
            Assert.All(
                identified.Survey.BiologySurveyDisplay.Organisms,
                organism => Assert.False(organism.IsPrediction));
            Assert.All(
                identified.Survey.BiologySurveyDisplay.OrganismGroups,
                group => Assert.False(group.IsPrediction));
            Assert.DoesNotContain(
                identified.Survey.BiologySurveyDisplay.OrganismGroups,
                group => group.IsGlobalRegionalFirst);
            Assert.Contains(
                identified.Survey.BiologySurveyDisplay.OrganismGroups,
                group => group.IsAnalyzed);

            Assert.True(preview.CycleEditorPreviewState());
            Assert.Equal("System overview", preview.CurrentEditorPreviewStateName);
            Assert.Contains("1/3", preview.EditorFolderTabStateLabelControl.Text);
            Assert.NotNull(preview.CaptureRenderedFrame());
        }
        finally
        {
            preview.Close();
        }
    }

    [AvaloniaFact]
    public void RuntimePreviewDoesNotRetainASecondCatalogSizedBackingLayer()
    {
        var definition = OverlayLayoutCatalog.GetRequired(
            "PlotGuardianSystem");
        var preview = new OverlayPositionPreviewWindow(definition);
        try
        {
            OverlayThemeResources.Apply(preview);
            preview.ApplyRuntimePresentationTheme();
            preview.ConfigureOpacity(0.35, null);
            preview.Show();

            Assert.Equal(1, preview.MinWidth);
            Assert.Equal(0.35, preview.PreviewBodyControl.Opacity);
            Assert.Equal(
                new Thickness(0),
                preview.PreviewBodyControl.Padding);
            Assert.Same(
                Brushes.Transparent,
                preview.PreviewBodyControl.Background);
            var measured = preview.GetExpectedPixelSize(1);
            Assert.True(
                measured.Width < definition.PreviewSize.Width,
                $"Content measured {measured.Width} against the old "
                    + $"{definition.PreviewSize.Width}px catalog floor.");
        }
        finally
        {
            preview.Close();
        }
    }

    [AvaloniaFact]
    public void BiologyStatusProgressMatchesItsLabelAndStaysInsideTheHeader()
    {
        var preview = new OverlayPositionPreviewWindow(
            OverlayLayoutCatalog.GetRequired("PlotBioStatus"));
        try
        {
            OverlayThemeResources.Apply(preview);
            preview.ApplyRuntimePresentationTheme();
            preview.Show();
            Assert.NotNull(preview.CaptureRenderedFrame());

            var presentation = Assert.IsType<BiologyStatusOverlayPresentation>(
                preview.RuntimePresentation);
            var progress = Assert.Single(
                presentation.GetVisualDescendants().OfType<ProgressBar>());
            var header = Assert.IsType<Grid>(progress.Parent);
            var state = Assert.IsType<SystemSurveyOverlayViewModel>(
                presentation.DataContext).Survey.BiologyStatus!;

            Assert.Equal(state.CompletionPercent, progress.Value);
            Assert.True(header.ClipToBounds);
            Assert.Equal(0, progress.MinWidth);
            Assert.True(progress.Bounds.Left >= 0);
            Assert.True(progress.Bounds.Right <= header.Bounds.Width + 0.01);

            Assert.True(preview.CycleEditorPreviewState());
            Assert.True(preview.CycleEditorPreviewState());
            Assert.Equal(
                "DSS required",
                preview.CurrentEditorPreviewStateName);
            Assert.NotNull(preview.CaptureRenderedFrame());
            state = Assert.IsType<SystemSurveyOverlayViewModel>(
                presentation.DataContext).Survey.BiologyStatus!;
            Assert.Equal(0, state.CompletionPercent);
            Assert.Equal(0, progress.Value);
        }
        finally
        {
            preview.Close();
        }
    }

    private static void AssertFolderTabBrush(IBrush? candidate)
    {
        var brush = Assert.IsType<ISolidColorBrush>(
            candidate,
            exactMatch: false);
        Assert.Equal(Color.Parse("#FFCC33"), brush.Color);
    }
}
