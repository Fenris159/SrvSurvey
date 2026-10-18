using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed partial class ColonizationShoppingPresentationTests
{
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
            Button manifest = view.FindControl<Button>("EditCarrierManifestButton")!;
            systems.BringIntoView();
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            Point systemPosition = systems.TranslatePoint(default, view)!.Value;
            Point stagePosition = stages.TranslatePoint(default, view)!.Value;
            Point manifestPosition = manifest.TranslatePoint(default, view)!.Value;
            Assert.True(systemPosition.X < stagePosition.X && stagePosition.X < manifestPosition.X);
            Assert.InRange(Math.Abs(systemPosition.Y - manifestPosition.Y), 0, 3);
            stages.SelectedItem = main.Colonization.ProjectBuildOptions.Single(option =>
                option.Value == ColonizationProjectBuildFilter.Planning
            );
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            Assert.True(Assert.Single(main.Colonization.VisibleProjects).Project.IsPlanned);
            systems.SelectedItem = main.Colonization.ProjectSystemOptions.Single(option =>
                option.SystemName == "Achenar"
            );
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            Assert.True(main.Colonization.HasNoMatchingProjects);
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
