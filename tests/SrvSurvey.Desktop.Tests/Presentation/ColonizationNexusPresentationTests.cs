using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Behaviors;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ColonizationNexusPresentationTests
{
    [AvaloniaTheory]
    [InlineData(680)]
    [InlineData(820)]
    [InlineData(900)]
    [InlineData(960)]
    [InlineData(1180)]
    public async Task SingleSystemKeepsOneColumnWidthInsteadOfSpanningBothColumns(int width)
    {
        ColonizationNexus source = NexusFixture.Snapshot();
        var reader = new ColonizationNexusViewModelTests.NexusReader
        {
            Read = (_, _) => Task.FromResult<ColonizationNexus?>(source with { Systems = [source.Systems[0]] }),
        };
        using var model = new ColonizationNexusViewModel(reader, ColonizationBuildCatalog.LoadEmbedded(), _ => { });
        model.SetContext(true, "Test", "key");
        await model.RefreshAsync();
        var view = new ColonizationNexusView { DataContext = model };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 1050,
        };
        try
        {
            window.Show();
            Assert.NotNull(window.CaptureRenderedFrame());
            ListBox route = view.GetVisualDescendants().OfType<ListBox>().Single(list => list.Name == "NexusRoute");
            ListBoxItem item = Assert.IsType<ListBoxItem>(route.ContainerFromIndex(0));
            Assert.InRange(item.Bounds.Width, route.Bounds.Width * .4, route.Bounds.Width * .51);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(1180)]
    [InlineData(680)]
    [InlineData(2200)]
    public async Task RendersRouteScopesAndReadableCargoTableAtWorkspaceSizes(int width)
    {
        ColonizationProject? opened = null;
        using var model = new ColonizationNexusViewModel(
            new ColonizationNexusViewModelTests.NexusReader(),
            ColonizationBuildCatalog.LoadEmbedded(),
            project => opened = project
        );
        model.SetReportOpener((_, _) => { });
        model.SetContext(true, "Test", "test-key");
        await model.RefreshAsync();
        var view = new ColonizationNexusView { DataContext = model };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 1050,
        };
        SetMonochromeResources(window);
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(
                Path.Combine(Path.GetTempPath(), $"srvsurvey-nexus-{width}.png"),
                PngBitmapEncoderOptions.Default
            );
            ComboBox picker = view.FindControl<ComboBox>("NexusPicker")!;
            Assert.Equal(2, picker.Items.Count);
            Assert.Same(model.SelectedNexus, picker.SelectedItem);
            Assert.True(view.FindControl<Button>("RefreshNexus")!.IsEnabled);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Test Nexus");
            ItemsControl route = view.GetVisualDescendants()
                .OfType<ItemsControl>()
                .Single(control => control.Name == "NexusRoute");
            Assert.Equal(3, route.ItemCount);
            AssertSystemStrips(route, 2);
            Assert.DoesNotContain(
                route.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("nexus-project")
            );
            Border hubDetails = view.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Name == "NexusHubDetails");
            Assert.Contains(hubDetails.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Hub Details");
            Assert.Contains(hubDetails.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Route stats");
            ContentControl selectedHub = hubDetails
                .GetVisualDescendants()
                .OfType<ContentControl>()
                .Single(control => control.Name == "NexusSelectedHub");
            Assert.DoesNotContain(
                selectedHub.GetVisualDescendants().OfType<HyperlinkButton>(),
                button => button.Classes.Contains("nexus-system-link")
            );
            Assert.Contains(hubDetails.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Assigned to:");
            Assert.Single(hubDetails.GetVisualDescendants().OfType<ColonizationCargoProgressBar>());
            Assert.Equal(3, route.GetVisualDescendants().OfType<ColonizationCargoProgressBar>().Count());
            Assert.Contains(hubDetails.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Test (owner)");
            Assert.Contains(
                hubDetails.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "Test Carrier (ABC-123)"
            );
            Border project = Assert.Single(
                hubDetails.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("nexus-project")
            );
            Assert.Same(
                model.Detail!.Systems[0],
                view.GetVisualDescendants()
                    .OfType<ContentControl>()
                    .Single(control => control.Name == "NexusSelectedHub")
                    .Content
            );
            Assert.Contains(project.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Test Port");
            Assert.Contains(
                project.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == model.Detail.Systems[0].Builds[0].Type
            );
            Assert.Equal(50, Assert.Single(project.GetVisualDescendants().OfType<ProgressBar>()).Value);
            Button projectName = project
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Classes.Contains("nexus-project-name"));
            projectName.Command!.Execute(null);
            Assert.Same(model.Detail.Systems[0].Builds[0].Project, opened);
            Assert.DoesNotContain(
                view.GetVisualDescendants().OfType<Expander>(),
                expander => Equals(expander.Header, "Facility types")
            );
            Button[] systems = route
                .GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Classes.Contains("nexus-system-link"))
                .ToArray();
            Assert.Equal(3, systems.Length);
            Assert.All(systems, button => Assert.InRange(button.Bounds.Width, 1, width - 20));
            Assert.Equal(new Uri(RavenColonialClient.WebsiteUri, "#sys=1"), systems[0].Tag);
            Assert.All(systems, button => Assert.IsType<HyperlinkButton>(button));
            Assert.Equal("Test System", Assert.IsType<TextBlock>(systems[0].Content).Text);
            route.BringIntoView();
            using WriteableBitmap? systemFrame = window.CaptureRenderedFrame();
            Assert.NotNull(systemFrame);
            systemFrame.Save(
                Path.Combine(Path.GetTempPath(), $"srvsurvey-nexus-systems-{width}.png"),
                PngBitmapEncoderOptions.Default
            );
            hubDetails.BringIntoView();
            using WriteableBitmap? hubFrame = window.CaptureRenderedFrame();
            Assert.NotNull(hubFrame);
            hubFrame.Save(
                Path.Combine(Path.GetTempPath(), $"srvsurvey-nexus-hub-details-{width}.png"),
                PngBitmapEncoderOptions.Default
            );
            Button copy = route
                .GetVisualDescendants()
                .OfType<Button>()
                .First(button => button.Classes.Contains("nexus-copy"));
            Assert.Equal("Test System", ClipboardCopyBehavior.GetText(copy));
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Test System", await window.Clipboard!.TryGetTextAsync());
            ComboBox scope = view.GetVisualDescendants()
                .OfType<ComboBox>()
                .Single(control => control.Name == "NexusScopePicker");
            Assert.Equal(0, scope.SelectedIndex);
            scope.SelectedIndex = 1;
            Assert.Same(model.Detail.Systems[0], scope.SelectedItem);
            scope.SelectedIndex = 0;
            ScrollViewer scroll = view.FindControl<ScrollViewer>("NexusPageScroll")!;
            scroll.ScrollToEnd();
            using WriteableBitmap? details = window.CaptureRenderedFrame();
            Assert.NotNull(details);
            details.Save(
                Path.Combine(Path.GetTempPath(), $"srvsurvey-nexus-details-{width}.png"),
                PngBitmapEncoderOptions.Default
            );
            ItemsControl needs = view.GetVisualDescendants()
                .OfType<ItemsControl>()
                .Single(control => control.Name == "NexusNeeds");
            Assert.Equal(1, needs.ItemCount);
            Assert.Contains(needs.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Steel");
            Button crane = view.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Classes.Contains("nexus-build-link"));
            Assert.Equal(new Uri(RavenColonialClient.WebsiteUri, "#build=build"), crane.Tag);
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
            Assert.All(
                view.GetVisualDescendants().OfType<ProgressBar>().Where(bar => !bar.IsIndeterminate),
                bar =>
                {
                    Assert.InRange(bar.Value, 0, 100);
                    Color background = Assert.IsType<ISolidColorBrush>(bar.Background, exactMatch: false).Color;
                    Assert.True(background == Colors.Transparent || background == Color.Parse("#737373"));
                }
            );
            Assert.Equal(
                Color.Parse("#737373"),
                Assert
                    .IsType<ISolidColorBrush>(
                        Assert.Single(project.GetVisualDescendants().OfType<ProgressBar>()).Background,
                        exactMatch: false
                    )
                    .Color
            );
            AssertHubCardSelection(window, view, model);
            window.Width = width > 1000 ? 480 : 2200;
            using WriteableBitmap? resized = window.CaptureRenderedFrame();
            Assert.NotNull(resized);
            AssertSystemStrips(route, width > 1000 ? 1 : 2);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertHubCardSelection(
        Window window,
        ColonizationNexusView view,
        ColonizationNexusViewModel model
    )
    {
        ListBox route = view.GetVisualDescendants().OfType<ListBox>().Single(list => list.Name == "NexusRoute");
        ListBoxItem first = Assert.IsType<ListBoxItem>(route.ContainerFromIndex(0));
        ListBoxItem second = Assert.IsType<ListBoxItem>(route.ContainerFromIndex(1));
        Assert.True(first.IsSelected);
        view.FindControl<ScrollViewer>("NexusPageScroll")!.ScrollToHome();
        using WriteableBitmap? before = window.CaptureRenderedFrame();
        Assert.NotNull(before);
        Point point = second
            .TranslatePoint(new Point(second.Bounds.Width - 12, second.Bounds.Height - 8), window)!
            .Value;
        var hit = window.InputHitTest(point) as Visual;
        Assert.True(
            hit?.GetVisualAncestors().OfType<ListBoxItem>().Contains(second),
            $"Click {point} missed the item {second.Bounds}; hit {hit?.GetType().Name}."
        );
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        using WriteableBitmap? after = window.CaptureRenderedFrame();
        Assert.NotNull(after);
        Assert.Same(model.Detail!.Systems[1], model.Detail.SelectedSystem);
        Assert.True(second.IsSelected);
        Assert.False(first.IsSelected);
        Assert.Equal(
            Color.Parse("#E6D59A"),
            Assert.IsType<ISolidColorBrush>(second.BorderBrush, exactMatch: false).Color
        );
        Assert.Equal(Colors.Transparent, Assert.IsType<ISolidColorBrush>(first.BorderBrush, exactMatch: false).Color);
        Assert.DoesNotContain(
            view.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("nexus-project")
        );
        Assert.True(second.Focus());
        window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
        Assert.Same(model.Detail.Systems[2], model.Detail.SelectedSystem);
        model.Detail.SelectedSystem = model.Detail.Systems[0];
    }

    private static void AssertSystemStrips(ItemsControl route, int columns)
    {
        Border[] strips = route
            .GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("nexus-system"))
            .ToArray();
        Assert.Equal(3, strips.Length);
        Assert.All(strips, strip => Assert.True(strip.Bounds.Width > strip.Bounds.Height));
        Point[] positions = strips.Select(strip => strip.TranslatePoint(default, route)!.Value).ToArray();
        Assert.Equal(columns, positions.Select(point => Math.Round(point.X)).Distinct().Count());
        Assert.True(positions[2].Y > positions[0].Y);
    }

    [AvaloniaFact]
    public async Task NexusTabUsesActiveCredentialAndOpensExistingBuildPreviewWithoutTrackingIt()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SrvSurvey-nexus-" + Guid.NewGuid().ToString("N"));
        using var http = new HttpClient(new NexusHandler());
        using var model = new ColonizationViewModel(
            new ColonizationSettingsStore(Path.Combine(directory, "ui.json")),
            new RavenColonialClient(http)
        );
        var owner = new Window();
        using var coordinator = new ColonizationProjectPreviewWindowCoordinator(model, owner);
        try
        {
            owner.Show();
            model.IsEnabled = true;
            model.SetCommanderProfile("F1", true, "test-key");
            await model.SetCommanderAsync("Test");
            model.SelectedWorkspaceTabIndex = 3;
            await model.Nexus.ActiveTask;
            Assert.Equal("Test Nexus", model.Nexus.Detail!.Nexus.Name);
            Assert.Empty(model.Projects);
            ColonizationNexusBuildRow build = Assert.Single(model.Nexus.Detail.SelectedScope.Builds);
            build.OpenCommand.Execute(null);
            ColonizationProjectPreviewWindow preview = Assert.Single(coordinator.Windows);
            Assert.IsType<ColonizationProjectPreviewViewModel>(preview.DataContext);
            Assert.Equal("Test Port - Raven build preview", preview.Title);
            Assert.Empty(model.Projects);
            Assert.False(model.HasUnsavedProjectVisibility);
            model.Nexus.Detail.OpenAllReportCommand.Execute(null);
            Assert.Equal(2, coordinator.Windows.Count);
            model.Nexus.Detail.OpenAllReportCommand.Execute(null);
            Assert.Equal(2, coordinator.Windows.Count);
            model.Nexus.Detail.Systems[0].OpenReportCommand.Execute(null);
            Assert.Equal(3, coordinator.Windows.Count);
            ColonizationProjectPreviewViewModel[] reports = coordinator
                .Windows.Select(window => Assert.IsType<ColonizationProjectPreviewViewModel>(window.DataContext))
                .Where(report => report.IsCombinedReport)
                .ToArray();
            Assert.Equal(2, reports.Length);
            Assert.All(reports, report => Assert.Equal(100, report.Snapshot!.Remaining));
            model.SetCommanderProfile("F1", true, "changed-key");
            Assert.Single(coordinator.Windows);
            model.IsEnabled = false;
            Assert.Null(model.Nexus.Detail);
            Assert.Empty(coordinator.Windows);
        }
        finally
        {
            owner.Close();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static void SetMonochromeResources(Window window)
    {
        window.Background = new SolidColorBrush(Color.Parse("#0A0A0A"));
        window.Resources["RavenSurfaceBrush"] = new SolidColorBrush(Color.Parse("#121212"));
        window.Resources["RavenSecondaryFillBrush"] = new SolidColorBrush(Color.Parse("#262626"));
        window.Resources["RavenAccentBrush"] = new SolidColorBrush(Color.Parse("#E6D59A"));
        window.Resources["RavenTextBrush"] = new SolidColorBrush(Color.Parse("#E5E5E5"));
        window.Resources["RavenTertiaryTextBrush"] = new SolidColorBrush(Color.Parse("#737373"));
        window.Resources["RavenBorderBrush"] = new SolidColorBrush(Color.Parse("#333333"));
    }

    [AvaloniaFact]
    public async Task WorkspaceAutoRefreshUpdatesNexusAndSkipsAnInFlightNexusRead()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SrvSurvey-nexus-refresh-" + Guid.NewGuid().ToString("N"));
        var handler = new NexusHandler();
        using var http = new HttpClient(handler);
        var ticks = Channel.CreateUnbounded<TaskCompletionSource>();
        Task Delay(TimeSpan duration, CancellationToken token)
        {
            if (duration == TimeSpan.FromHours(1))
            {
                return Task.Delay(Timeout.Infinite, token);
            }
            var completion = new TaskCompletionSource();
            ticks.Writer.TryWrite(completion);
            return completion.Task.WaitAsync(token);
        }
        async Task<TaskCompletionSource> NextTick() =>
            await ticks.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        using var model = new ColonizationViewModel(
            new ColonizationSettingsStore(Path.Combine(directory, "ui.json")),
            new RavenColonialClient(http),
            delayAsync: Delay
        );
        try
        {
            model.IsEnabled = true;
            model.SetCommanderProfile("F1", true, "test-key");
            await model.SetCommanderAsync("Test");
            model.SelectedWorkspaceTabIndex = 3;
            await model.Nexus.ActiveTask;
            int workspaceReads = handler.WorkspaceReads;
            Assert.Equal(1, handler.NexusReads);
            model.IsAutoRefreshEnabled = true;
            (await NextTick()).SetResult();
            TaskCompletionSource tick = await NextTick();
            Assert.Equal(2, handler.NexusReads);
            Assert.Equal(workspaceReads, handler.WorkspaceReads);
            handler.PendingNexus = new();
            Task manual = model.Nexus.RefreshAsync();
            Assert.True(model.Nexus.IsBusy);
            tick.SetResult();
            await NextTick();
            Assert.Equal(3, handler.NexusReads);
            handler.PendingNexus.SetResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(NexusFixture.Snapshot())),
                }
            );
            await manual;
            model.IsAutoRefreshEnabled = false;
            await model.AutoRefreshTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(model.Nexus.IsBusy);
        }
        finally
        {
            model.Dispose();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private sealed class NexusHandler : HttpMessageHandler
    {
        public int NexusReads { get; private set; }
        public int WorkspaceReads { get; private set; }
        public TaskCompletionSource<HttpResponseMessage>? PendingNexus { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/api/Chain/a")
            {
                NexusReads++;
                if (PendingNexus is not null)
                {
                    return PendingNexus.Task.WaitAsync(cancellationToken);
                }
            }
            if (path.EndsWith("/active", StringComparison.Ordinal))
            {
                WorkspaceReads++;
            }
            string json = path switch
            {
                "/api/Cmdr/nexus" => JsonSerializer.Serialize(
                    new[]
                    {
                        new ColonizationNexusSummary { Id = "a", Name = "Test Nexus" },
                    }
                ),
                "/api/Chain/a" => JsonSerializer.Serialize(NexusFixture.Snapshot()),
                "/api/project/build" => JsonSerializer.Serialize(NexusFixture.Snapshot().Systems[0].Builds[0]),
                "/api/project/build/fc/" or "/api/project/build/stats" => "{}",
                _ when path.EndsWith("/primary", StringComparison.Ordinal) => "null",
                _ => "[]",
            };
            if (path is "/api/Cmdr/nexus" or "/api/Chain/a")
            {
                Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("rcc-key")));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
