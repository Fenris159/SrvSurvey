using System.Net;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class CodexSearchPresentationTests
{
    /// <summary>The actual view offers subtype-only searches and renders NSP results without biological warnings.</summary>
    [AvaloniaTheory]
    [InlineData(1000, "monochrome-dark", false)]
    [InlineData(1400, "blue-dark", true)]
    [InlineData(1000, "monochrome-dark", true)]
    public async Task CodexAndLandmarkFormsFitAndShowTheMatchingFeature(int width, string palette, bool advanced)
    {
        string directory = Path.Combine(Path.GetTempPath(), "SrvSurvey-codex-ui-" + Guid.NewGuid().ToString("N"));
        var theme = new RavenThemeService(
            Application.Current!,
            new ThemePreferenceStore(Path.Combine(directory, "theme.json"))
        );
        string previous = theme.Current.Key;
        using var http = new HttpClient(new Handler());
        using var model = new NearestSystemsViewModel(new NearestSystemsClient(http), new Resolver());
        model.UpdateContext("Sol", new GalacticCoordinate(0, 0, 0), "Test");
        await model.SearchCodexAsync(new CodexSearchCatalog().Resolve("Bell Mollusc"));
        if (advanced)
        {
            model.SelectedMode = model.Modes[1];
            model.LandmarkSubtypes = "Albens Bell Mollusc";
            Assert.True(model.SearchCommand.CanExecute(null));
            await model.SearchAsync();
        }
        var view = new CodexSearchView { DataContext = model, Margin = new Thickness(18) };
        var scroll = new ScrollViewer { Content = view };
        var window = new Window
        {
            Width = width,
            Height = 850,
            Content = scroll,
        };
        try
        {
            theme.Select(palette);
            theme.ApplyCurrent();
            window.Show();
            Capture(window, $"{palette}-{width}-{(advanced ? "landmarks" : "codex")}", "criteria");
            AutoCompleteBox input = view.FindControl<AutoCompleteBox>(
                advanced ? "LandmarkSubtypeInput" : "CodexNameInput"
            )!;
            Assert.True(input.IsEffectivelyVisible);
            Assert.True(input.Bounds.Width >= 180);
            Assert.Single(model.Results);
            Assert.DoesNotContain("bio", model.Results[0].Notes, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Bell Mollusc", model.Results[0].Notes);
            scroll.ScrollToEnd();
            Capture(window, $"{palette}-{width}-{(advanced ? "landmarks" : "codex")}", "results");
            Assert.Contains(
                view.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text?.Contains("Bell Mollusc", StringComparison.Ordinal) == true
            );
        }
        finally
        {
            window.Close();
            theme.Select(previous);
            theme.ApplyCurrent();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    /// <summary>A Bingo search opens below the sphere tools, so its criteria must be brought into view.</summary>
    [AvaloniaFact]
    public async Task SearchStartsBringCodexCriteriaIntoViewBelowOtherTools()
    {
        using var http = new HttpClient(new Handler());
        using var model = new NearestSystemsViewModel(new NearestSystemsClient(http), new Resolver());
        model.UpdateContext("Sol", new(0, 0, 0), "Cmdr");
        var view = new CodexSearchView { DataContext = model };
        var content = new StackPanel();
        content.Children.Add(new Border { Height = 1200 });
        content.Children.Add(view);
        var scroll = new ScrollViewer { Content = content };
        var window = new Window
        {
            Width = 1000,
            Height = 850,
            Content = scroll,
        };
        try
        {
            window.Show();
            window.CaptureRenderedFrame()?.Dispose();
            Assert.Equal(0, scroll.Offset.Y);
            await model.SearchCodexAsync(new CodexSearchCatalog().Resolve("Bell Mollusc"));
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                static () => { },
                Avalonia.Threading.DispatcherPriority.Background
            );
            window.CaptureRenderedFrame()?.Dispose();
            Assert.True(scroll.Offset.Y >= 1100);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Capture(Window window, string theme, string stage)
    {
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        string? output = Environment.GetEnvironmentVariable("SRVSURVEY_CODEX_UI_OUTPUT");
        if (output is not null)
        {
            Directory.CreateDirectory(output);
            using FileStream stream = File.Create(Path.Combine(output, $"codex-{theme}-{stage}.png"));
            frame.Save(stream, PngBitmapEncoderOptions.Default);
        }
    }

    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {"count":1,"search_reference":"example","results":[{"distance":9030.2,"distance_to_arrival":0,"name":"Distant System","system_name":"Distant System","system_id64":42,"system_x":1,"system_y":2,"system_z":3,"landmarks":[{"type":"Mollusc","subtype":"Albens Bell Mollusc"}]}]}
                        """,
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
    }

    private sealed class Resolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
    }
}
