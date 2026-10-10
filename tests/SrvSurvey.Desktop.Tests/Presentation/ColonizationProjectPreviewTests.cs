using System.Net;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ColonizationProjectPreviewTests
{
    private static readonly string[] CombinedBuildNames = ["First build", "Second build"];

    /// <summary>The combined report uses the same native layout, export toolbar, and refresh lifecycle.</summary>
    [AvaloniaFact]
    public void RendersCombinedReportWithBuildMembershipAndDeduplicatedCarriers()
    {
        using var model = new ColonizationProjectPreviewViewModel(
            new ColonizationProjectPreviewViewModelTests.Reader(
                (id, _) =>
                {
                    ColonizationProjectPreviewData data = ColonizationProjectPreviewViewModelTests.Data();
                    return Task.FromResult<ColonizationProjectPreviewData?>(
                        data with
                        {
                            Project = data.Project with { BuildId = id, BuildName = id },
                        }
                    );
                }
            ),
            () => CombinedBuildNames
        );
        var window = new ColonizationProjectPreviewWindow(model);
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal("Combined Build Report - Raven build preview", window.Title);
            Assert.Equal(50, window.FindControl<ProgressBar>("DeliveredProgress")!.Value);
            Assert.True(window.FindControl<Button>("ExportCsvButton")!.IsEnabled);
            Assert.Single(model.CarrierHeaders);
            Assert.Equal("400", model.RemainingText);
            Assert.Equal(2, model.Details.Count);
            string? directory = Environment.GetEnvironmentVariable("SRVSURVEY_BUILD_PREVIEW_RENDER_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                frame.Save(Path.Combine(directory, "combined-build-report.png"), PngBitmapEncoderOptions.Default);
            }
            ScrollViewer body = Assert.Single(
                window.GetVisualDescendants().OfType<ScrollViewer>(),
                scroll => scroll.Parent is Grid
            );
            body.ScrollToEnd();
            using WriteableBitmap? details = window.CaptureRenderedFrame();
            Assert.NotNull(details);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                details.Save(
                    Path.Combine(directory, "combined-build-report-details.png"),
                    PngBitmapEncoderOptions.Default
                );
            }
        }
        finally
        {
            window.Close();
        }
        Assert.False(model.CanExport);
    }

    /// <summary>Verifies compiled native bindings, refresh interaction, narrow-window scrolling, and readable preview rendering.</summary>
    [AvaloniaTheory]
    [InlineData(1180)]
    [InlineData(700)]
    public async Task RendersIndependentPreviewAndRefreshesFromToolbar(int width)
    {
        int reads = 0;
        using var model = new ColonizationProjectPreviewViewModel(
            new ColonizationProjectPreviewViewModelTests.Reader(
                (_, _) =>
                {
                    reads++;
                    return Task.FromResult<ColonizationProjectPreviewData?>(
                        ColonizationProjectPreviewViewModelTests.Data()
                    );
                }
            ),
            "build"
        );
        var window = new ColonizationProjectPreviewWindow(model) { Width = width, Height = 850 };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal("Example build - Raven build preview", window.Title);
            Assert.True(window.FindControl<Button>("ExportCsvButton")!.IsEnabled);
            Assert.Equal(50, window.FindControl<ProgressBar>("DeliveredProgress")!.Value);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Steel");
            Button refresh = window.FindControl<Button>("RefreshButton")!;
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, reads);
            Assert.NotNull(window.CaptureRenderedFrame());
            string? directory = Environment.GetEnvironmentVariable("SRVSURVEY_BUILD_PREVIEW_RENDER_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                frame.Save(Path.Combine(directory, $"build-preview-{width}.png"), PngBitmapEncoderOptions.Default);
            }
            ScrollViewer body = Assert.Single(
                window.GetVisualDescendants().OfType<ScrollViewer>(),
                scroll => scroll.Parent is Grid
            );
            body.ScrollToEnd();
            using WriteableBitmap? details = window.CaptureRenderedFrame();
            Assert.NotNull(details);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                details.Save(
                    Path.Combine(directory, $"build-preview-details-{width}.png"),
                    PngBitmapEncoderOptions.Default
                );
            }
            await model.ExportAsync(window.SaveCsvAsync);
            Assert.Equal(string.Empty, model.ExportStatus);
        }
        finally
        {
            window.Close();
        }
        Assert.False(model.CanExport);
    }

    /// <summary>Clicking a project opens one popout per identity, restores minimized previews, and never changes Show or primary selection.</summary>
    [AvaloniaFact]
    public async Task OpensMultipleProjectsAndCleansUpOnConsentOrOwnerClose()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SrvSurvey-build-preview-" + Guid.NewGuid().ToString("N"));
        using var http = new HttpClient(new Handler());
        using var model = new ColonizationViewModel(
            new ColonizationSettingsStore(Path.Combine(directory, "ui.json")),
            new RavenColonialClient(http)
        );
        var owner = new Window();
        using var coordinator = new ColonizationProjectPreviewWindowCoordinator(model, owner);
        try
        {
            owner.Show();
            ColonizationProject project = ColonizationProjectPreviewViewModelTests.Data().Project;
            model.OpenCombinedReport();
            model.OpenProjectPreview(project);
            Assert.Empty(coordinator.Windows);
            model.IsEnabled = true;
            await model.SetCommanderAsync("Example Cmdr");
            Assert.Single(model.Projects).IsShown = false;
            model.OpenCombinedReport();
            ColonizationProjectPreviewWindow report = Assert.Single(coordinator.Windows);
            report.WindowState = WindowState.Minimized;
            model.OpenCombinedReport();
            Assert.Equal(WindowState.Normal, report.WindowState);
            Assert.Same(report, Assert.Single(coordinator.Windows));
            Assert.Equal("200", ((ColonizationProjectPreviewViewModel)report.DataContext!).RemainingText);
            report.Close();
            model.OpenProjectPreview(project);
            ColonizationProjectPreviewWindow first = Assert.Single(coordinator.Windows);
            first.WindowState = WindowState.Minimized;
            model.OpenProjectPreview(project);
            Assert.Equal(WindowState.Normal, first.WindowState);
            Assert.Same(first, Assert.Single(coordinator.Windows));
            model.OpenProjectPreview(project with { BuildId = "second" });
            Assert.Equal(2, coordinator.Windows.Count);
            coordinator.ShowOrActivate(project with { BuildId = "" });
            Assert.Equal(2, coordinator.Windows.Count);
            first.Close();
            Assert.Single(coordinator.Windows);
            await model.SetCommanderAsync(null);
            Assert.Empty(coordinator.Windows);
            model.OpenProjectPreview(project);
            Assert.Single(coordinator.Windows);
            model.IsEnabled = false;
            Assert.Empty(coordinator.Windows);
            model.IsEnabled = true;
            model.OpenProjectPreview(project);
            Assert.Single(coordinator.Windows);
            owner.Close();
            Assert.Empty(coordinator.Windows);
            coordinator.Dispose();
            coordinator.ShowOrActivate(project);
            coordinator.ShowCombinedReport();
            Assert.Empty(coordinator.Windows);
            Assert.False(model.HasUnsavedProjectVisibility);
        }
        finally
        {
            owner.Close();
            coordinator.Dispose();
            model.Dispose();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>The parameterless preview can be loaded by Avalonia without performing network reads.</summary>
    [AvaloniaFact]
    public void DesignerPreviewHasNoNetworkAccessOrExport()
    {
        var window = new ColonizationProjectPreviewWindow();
        try
        {
            window.Show();
            Assert.NotNull(window.CaptureRenderedFrame());
            Assert.False(window.FindControl<Button>("ExportCsvButton")!.IsEnabled);
            Assert.Equal(
                "Raven Colonial access is off.",
                ((ColonizationProjectPreviewViewModel)window.DataContext!).Status
            );
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Tests the actual project-name hyperlink without changing its visibility checkbox or primary state.</summary>
    [AvaloniaFact]
    public async Task ProjectNameHyperlinkPreservesProjectSelection()
    {
        using var http = new HttpClient(new Handler());
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Example Cmdr");
        ColonizationProjectRowViewModel row = Assert.Single(main.Colonization.Projects);
        row.IsShown = false;
        int opened = 0;
        int reportsOpened = 0;
        main.Colonization.SetCombinedReportOpener(() => reportsOpened++);
        main.Colonization.SetProjectPreviewOpener(project =>
        {
            Assert.Same(row.Project, project);
            opened++;
        });
        var view = new Views.ColonizationView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = 1400,
            Height = 1000,
        };
        try
        {
            window.Show();
            Assert.NotNull(window.CaptureRenderedFrame());
            Button reportButton = view.FindControl<Button>("CombinedBuildReportButton")!;
            Assert.True(reportButton.IsEnabled);
            reportButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, reportsOpened);
            Assert.Equal("Combined Build Report", reportButton.Content);
            Button link = Assert.Single(
                view.GetVisualDescendants().OfType<Button>(),
                button => button.Content is TextBlock { Text: "Example build" }
            );
            link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, opened);
            Assert.False(row.IsShown);
            Assert.False(row.IsPrimary);
            Assert.True(main.Colonization.HasUnsavedProjectVisibility);
            link.DataContext = null;
            link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, opened);
            view.DataContext = null;
            reportButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, reportsOpened);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Supplies bounded public responses without any live API writes.</summary>
    private sealed class Handler : HttpMessageHandler
    {
        /// <summary>Serializes the same live-data shapes returned by Raven's project endpoints.</summary>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            ColonizationProjectPreviewData data = ColonizationProjectPreviewViewModelTests.Data();
            string path = request.RequestUri!.AbsolutePath;
            string json = path switch
            {
                var value when value.EndsWith("/active", StringComparison.Ordinal) => JsonSerializer.Serialize(
                    new[] { data.Project }
                ),
                var value when value.EndsWith("/primary", StringComparison.Ordinal) => "null",
                var value
                    when value.EndsWith("/hiddenIDs", StringComparison.Ordinal)
                        || value.EndsWith("/fc/all", StringComparison.Ordinal) => "[]",
                var value when value.EndsWith("/fc/", StringComparison.Ordinal) => JsonSerializer.Serialize(
                    data.CarrierCargo
                ),
                var value when value.EndsWith("/stats", StringComparison.Ordinal) => JsonSerializer.Serialize(
                    data.Statistics
                ),
                _ => JsonSerializer.Serialize(data.Project),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
