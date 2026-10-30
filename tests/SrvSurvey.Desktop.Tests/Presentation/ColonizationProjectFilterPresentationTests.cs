using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed partial class ColonizationShoppingPresentationTests
{
    /// <summary>Market choices share the Projects view while filters leave the shopping selection intact.</summary>
    [AvaloniaFact]
    public async Task MarketProjectChoicesFollowProjectFiltersAndRefreshWithoutClearingSelection()
    {
        using var http = new HttpClient(
            new RavenHandler
            {
                ActiveProjects =
                [
                    new()
                    {
                        BuildId = "one",
                        BuildName = "Sol Build",
                        SystemName = "Sol",
                    },
                    new()
                    {
                        BuildId = "two",
                        BuildName = "Achenar Build",
                        SystemName = "Achenar",
                    },
                    new()
                    {
                        BuildId = "complete",
                        BuildName = "Completed Build",
                        SystemName = "Sol",
                        IsComplete = true,
                    },
                ],
                SystemRecord = new()
                {
                    Name = "Sol",
                    SystemAddress = 42,
                    Architect = "Example Cmdr",
                    Sites =
                    [
                        new()
                        {
                            Id = "p",
                            Name = "Future Port",
                            BuildType = "asteroid",
                            Status = ColonizationSystemSiteStatus.Plan,
                        },
                    ],
                },
            }
        );
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Example Cmdr");
        var view = new ColonizationView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = 1180,
            Height = 1000,
        };
        try
        {
            window.Show();
            TabControl tabs = view.FindControl<TabControl>("ColonizationTabs")!;
            ComboBox systems = view.FindControl<ComboBox>("ProjectSystemFilter")!;
            ComboBox stages = view.FindControl<ComboBox>("ProjectBuildFilter")!;
            stages.SelectedItem = main.Colonization.ProjectBuildOptions.Single(option =>
                option.Value == ColonizationProjectBuildFilter.Planning
            );
            tabs.SelectedIndex = 1;
            Assert.Single(RenderMarketProjectChoices(view, window));
            string[] selected = main.Colonization.MarketSearchWorkspace!.ProjectIds.Order().ToArray();

            tabs.SelectedIndex = 0;
            stages.SelectedItem = main.Colonization.ProjectBuildOptions.Single(option =>
                option.Value == ColonizationProjectBuildFilter.Building
            );
            tabs.SelectedIndex = 1;
            Assert.Equal(2, RenderMarketProjectChoices(view, window).Length);

            tabs.SelectedIndex = 0;
            systems.SelectedItem = main.Colonization.ProjectSystemOptions.Single(option => option.SystemName == "Sol");
            tabs.SelectedIndex = 1;
            Assert.Equal("Sol Build", Assert.Single(RenderMarketProjectChoices(view, window)).Content);

            tabs.SelectedIndex = 0;
            systems.SelectedItem = main.Colonization.ProjectSystemOptions.Single(option =>
                option.SystemName == "Achenar"
            );
            stages.SelectedItem = main.Colonization.ProjectBuildOptions.Single(option =>
                option.Value == ColonizationProjectBuildFilter.Planning
            );
            tabs.SelectedIndex = 1;
            Assert.Empty(RenderMarketProjectChoices(view, window));
            Assert.Contains(
                view.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && text.Text == "No projects match these filters."
            );

            main.Colonization.ProjectSystemFilter = main.Colonization.ProjectSystemOptions[0];
            Assert.Single(RenderMarketProjectChoices(view, window));
            main.Colonization.ProjectBuildFilter = main.Colonization.ProjectBuildOptions[0];
            Assert.Equal(4, RenderMarketProjectChoices(view, window).Length);
            main.Colonization.SortProjectsCommand.Execute(nameof(ColonizationProjectRowViewModel.BuildName));
            await main.Colonization.RefreshAsync();
            ToggleButton[] refreshed = RenderMarketProjectChoices(view, window);
            Assert.Equal(
                main.Colonization.VisibleProjects.Select(row => row.BuildName),
                refreshed.Select(tag => tag.Content)
            );
            Assert.Equal(selected, main.Colonization.MarketSearchWorkspace.ProjectIds.Order());
            Assert.All(main.Colonization.Projects, row => Assert.True(row.IsShown));
        }
        finally
        {
            window.Close();
        }
    }

    private static ToggleButton[] RenderMarketProjectChoices(ColonizationView view, Window window)
    {
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        return view.FindControl<ItemsControl>("MarketSearchProjects")!
            .GetVisualDescendants()
            .OfType<ToggleButton>()
            .ToArray();
    }

    [AvaloniaTheory]
    [InlineData(820)]
    [InlineData(1180)]
    public async Task ProjectToolbarFiltersBindAndKeepTheRequestedOrder(int width)
    {
        using var http = new HttpClient(
            new RavenHandler
            {
                AdditionalSystems = new Dictionary<string, ColonizationSystemRecord>
                {
                    ["/api/v2/system/Achenar"] = new() { Name = "Achenar", SystemAddress = 43 },
                },
                ActiveProjects =
                [
                    new()
                    {
                        BuildId = "one",
                        BuildName = "Sol Build",
                        SystemName = "Sol",
                    },
                    new()
                    {
                        BuildId = "two",
                        BuildName = "Achenar Build",
                        SystemName = "Achenar",
                    },
                ],
                SystemRecord = new()
                {
                    Name = "Sol",
                    SystemAddress = 42,
                    Architect = "Example Cmdr",
                    Sites =
                    [
                        new()
                        {
                            Id = "p",
                            Name = "Future Port",
                            BuildType = "asteroid",
                            Status = ColonizationSystemSiteStatus.Plan,
                        },
                    ],
                },
            }
        );
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Example Cmdr");
        var view = new ColonizationView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 1000,
        };
        try
        {
            window.Show();
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            ComboBox systems = view.FindControl<ComboBox>("ProjectSystemFilter")!;
            ComboBox stages = view.FindControl<ComboBox>("ProjectBuildFilter")!;
            Button uncheck = view.FindControl<Button>("UncheckProjectSelectionButton")!;
            Button manifest = view.FindControl<Button>("EditCarrierManifestButton")!;
            systems.BringIntoView();
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            Point systemPosition = systems.TranslatePoint(default, view)!.Value;
            Point stagePosition = stages.TranslatePoint(default, view)!.Value;
            Point uncheckPosition = uncheck.TranslatePoint(default, view)!.Value;
            Point manifestPosition = manifest.TranslatePoint(default, view)!.Value;
            Assert.True(systemPosition.X < stagePosition.X && stagePosition.X < uncheckPosition.X);
            Assert.InRange(Math.Abs(systemPosition.Y - uncheckPosition.Y), 0, 3);
            Assert.True(manifestPosition.Y > uncheckPosition.Y || manifestPosition.X > uncheckPosition.X);
            Assert.Equal("<--Uncheck Selection", uncheck.Content);
            CheckBox browse = view.FindControl<CheckBox>("ShowUnaffiliatedProjectsCheckBox")!;
            Assert.False(browse.IsChecked);
            WrapPanel toolbar = Assert.IsType<WrapPanel>(browse.Parent);
            Assert.Equal(
                "Open Raven builds",
                Assert.IsType<Button>(toolbar.Children[toolbar.Children.IndexOf(browse) - 1]).Content
            );
            main.Colonization.UpdateSystemContext("Achenar", null, 43);
            browse.IsChecked = true;
            await main.Colonization.CurrentSystemPlanningTask;
            Assert.True(main.Colonization.ShowUnaffiliatedProjectsInCurrentSystem);
            Assert.Equal("Achenar", main.Colonization.ProjectSystemFilter!.SystemName);
            Assert.Equal(ColonizationProjectBuildFilter.All, main.Colonization.ProjectBuildFilter!.Value);
            browse.IsChecked = false;
            systems.SelectedItem = main.Colonization.ProjectSystemOptions[0];
            stages.SelectedItem = main.Colonization.ProjectBuildOptions.Single(option =>
                option.Value == ColonizationProjectBuildFilter.Planning
            );
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            Assert.True(Assert.Single(main.Colonization.VisibleProjects).Project.IsPlanned);
            uncheck.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(main.Colonization.Projects.Single(row => row.Project.IsPlanned).IsShown);
            Assert.All(
                main.Colonization.Projects.Where(row => !row.Project.IsPlanned),
                row => Assert.True(row.IsShown)
            );
            systems.SelectedItem = main.Colonization.ProjectSystemOptions.Single(option =>
                option.SystemName == "Achenar"
            );
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            Assert.True(main.Colonization.HasNoMatchingProjects);
            uncheck.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(main.Colonization.Projects.Single(row => row.Project.IsPlanned).IsShown);
            Assert.All(
                main.Colonization.Projects.Where(row => !row.Project.IsPlanned),
                row => Assert.True(row.IsShown)
            );
            stages.SelectedItem = main.Colonization.ProjectBuildOptions[0];
            Assert.Equal("Achenar Build", Assert.Single(main.Colonization.VisibleProjects).BuildName);
            systems.SelectedItem = main.Colonization.ProjectSystemOptions[0];
            using WriteableBitmap? final = window.CaptureRenderedFrame();
            Assert.NotNull(final);
            Assert.Equal(3, main.Colonization.VisibleProjects.Count);
            final.Save(
                Path.Combine(Path.GetTempPath(), $"srvsurvey-project-filters-{width}.png"),
                PngBitmapEncoderOptions.Default
            );
        }
        finally
        {
            window.Close();
        }
    }
}
