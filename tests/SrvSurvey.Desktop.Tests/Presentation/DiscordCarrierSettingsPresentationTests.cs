using System.Collections.Concurrent;
using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SrvSurvey.Core.Discord;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.ViewModels;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class DiscordCarrierSettingsPresentationTests
{
    [AvaloniaTheory]
    [InlineData("blue-dark")]
    [InlineData("blue-light")]
    [InlineData("orange-dark")]
    [InlineData("green-dark")]
    [InlineData("green-light")]
    [InlineData("monochrome-dark")]
    public async Task ApplicationSettingsExposeMaskedWebhookAndBothCarrierChoices(string palette)
    {
        string root = Path.Combine(Path.GetTempPath(), "discord-render-" + Guid.NewGuid().ToString("N"));
        var paths = new AppDataPaths(
            Path.Combine(root, "config"),
            Path.Combine(root, "data"),
            Path.Combine(root, "cache"),
            []
        );
        var theme = new RavenThemeService(Application.Current!, new ThemePreferenceStore(paths.UiSettingsPath));
        string previous = theme.Current.Key;
        await using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithAppDataPaths(paths)
        );
        var view = new SettingsView { DataContext = main };
        var window = new Window
        {
            Width = 1100,
            Height = 1100,
            Content = view,
            Classes = { "app-window" },
        };
        try
        {
            theme.Select(palette);
            theme.ApplyCurrent();
            window.Show();
            main.SettingsWorkspace.SelectCategory("application");
            window.UpdateLayout();
            Border section = view.FindControl<Border>("DiscordCarrierAnnouncementsCard")!;
            section.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(section.IsVisible);
            Assert.False(view.FindControl<CheckBox>("DiscordCarrierEnabledCheckBox")!.IsChecked);
            TextBox url = view.FindControl<TextBox>("DiscordWebhookUrlTextBox")!;
            Assert.Equal('*', url.PasswordChar);
            url.Text = "not a Discord URL";
            Assert.True(view.FindControl<TextBlock>("DiscordWebhookUrlValidation")!.IsVisible);
            url.Text = "https://discord.com/api/webhooks/123/sample-token";
            Assert.False(view.FindControl<TextBlock>("DiscordWebhookUrlValidation")!.IsVisible);
            Assert.Equal(url.Text, main.DiscordCarrierAnnouncements.WebhookUrl);
            Assert.True(view.FindControl<Button>("TestDiscordWebhookButton")!.IsEnabled);
            Assert.True(section.Bounds.Width > 500);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            string? output = Environment.GetEnvironmentVariable("SRVSURVEY_DISCORD_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                frame.Save(Path.Combine(output, palette + ".png"), PngBitmapEncoderOptions.Default);
            }
            main.SettingsWorkspace.SelectCategory("desktop");
            Assert.False(section.IsVisible);
            main.SettingsWorkspace.SearchQuery = "discord squadron";
            Assert.Single(Assert.Single(main.SettingsWorkspace.GroupedSearchResults).Results);
        }
        finally
        {
            window.Close();
            theme.Select(previous);
            theme.ApplyCurrent();
            await main.DisposeAsync();
            Directory.Delete(root, true);
        }
    }

    [AvaloniaFact]
    public async Task LiveJournalMonitorPostsBothCarriersWithoutReplayingStartupHistory()
    {
        string root = Path.Combine(Path.GetTempPath(), "discord-monitor-" + Guid.NewGuid().ToString("N"));
        string journals = Path.Combine(root, "journals");
        Directory.CreateDirectory(journals);
        string journal = Path.Combine(journals, "Journal.2026-10-28T120000.01.log");
        await File.WriteAllTextAsync(
            journal,
            """
            {"timestamp":"2026-10-28T12:00:00Z","event":"Fileheader","Odyssey":true,"gameversion":"4.0.0.1"}
            {"timestamp":"2026-10-28T12:00:01Z","event":"Commander","Name":"Drew","FID":"F123"}
            {"timestamp":"2026-10-28T12:00:02Z","event":"LoadGame","Commander":"Drew","FID":"F123","Ship":"CobraMkIII","GameMode":"Solo"}
            {"timestamp":"2026-10-28T12:00:03Z","event":"CarrierStats","CarrierID":1,"Name":"Explorer","Callsign":"ABC-123"}
            {"timestamp":"2026-10-28T12:00:03Z","event":"CarrierStats","CarrierID":2,"CarrierType":"SquadronCarrier","Name":"Squad","Callsign":"SQU-123"}
            {"timestamp":"2026-10-28T12:00:04Z","event":"CarrierLocation","CarrierID":1,"StarSystem":"Sol"}
            {"timestamp":"2026-10-28T12:00:04Z","event":"CarrierLocation","CarrierID":2,"CarrierType":"SquadronCarrier","StarSystem":"Achenar"}
            """ + "\n"
        );
        var paths = new AppDataPaths(
            Path.Combine(root, "config"),
            Path.Combine(root, "data"),
            Path.Combine(root, "cache"),
            []
        )
        {
            SettingsFrontierId = "F123",
        };
        new DiscordCarrierSettingsStore(paths.UiSettingsPath).Save(
            new(true, "https://discord.com/api/webhooks/123/sample-token")
        );
        var payloads = new ConcurrentQueue<string>();
        using var http = new HttpClient(new Handler(payloads));
        await using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            journals,
            builder => builder.WithAppDataPaths(paths).WithExternalNetworkClient(http)
        );
        try
        {
            await main.RefreshAsync();
            Assert.Empty(payloads);
            await File.AppendAllTextAsync(
                journal,
                """
                {"timestamp":"2026-10-28T12:00:10Z","event":"CarrierJumpRequest","CarrierID":1,"CarrierType":"FleetCarrier","SystemName":"Lave","Body":"Lave 1","DepartureTime":"2026-10-28T12:01:00Z"}
                {"timestamp":"2026-10-28T12:00:11Z","event":"CarrierJumpRequest","CarrierID":2,"CarrierType":"SquadronCarrier","SystemName":"Colonia","DepartureTime":"2026-10-28T12:01:00Z"}
                """ + "\n"
            );
            await main.RefreshAsync();
            await main.DiscordCarrierAnnouncements.PendingDelivery.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, payloads.Count);
            Assert.Contains(
                payloads,
                text =>
                    text.Contains("Explorer", StringComparison.Ordinal)
                    && text.Contains("Sol", StringComparison.Ordinal)
            );
            Assert.Contains(
                payloads,
                text =>
                    text.Contains("Squadron carrier", StringComparison.Ordinal)
                    && text.Contains("Achenar", StringComparison.Ordinal)
            );
            await File.AppendAllTextAsync(
                journal,
                """
                {"timestamp":"2026-10-28T12:00:20Z","event":"CarrierJumpCancelled","CarrierID":1}
                {"timestamp":"2026-10-28T12:01:10Z","event":"CarrierLocation","CarrierID":2,"CarrierType":"SquadronCarrier","StarSystem":"Colonia"}
                """ + "\n"
            );
            await main.RefreshAsync();
            await main.DiscordCarrierAnnouncements.PendingDelivery.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(4, payloads.Count);
            Assert.Contains(payloads, text => text.Contains("jump cancelled", StringComparison.Ordinal));
            Assert.Contains(payloads, text => text.Contains("jump completed", StringComparison.Ordinal));
            await File.AppendAllTextAsync(
                journal,
                """
                {"timestamp":"2026-10-28T13:00:00Z","event":"Commander","Name":"Other","FID":"F999"}
                {"timestamp":"2026-10-28T13:00:01Z","event":"CarrierJumpRequest","CarrierID":99,"SystemName":"Foreign itinerary"}
                {"timestamp":"2026-10-28T13:00:02Z","event":"Fileheader","Odyssey":true,"gameversion":"4.0.0.1"}
                {"timestamp":"2026-10-28T13:00:03Z","event":"CarrierJumpRequest","CarrierID":100,"SystemName":"Unattributed itinerary"}
                {"timestamp":"2026-10-28T13:00:04Z","event":"Commander","Name":"Drew","FID":"F123"}
                {"timestamp":"2026-10-28T13:00:05Z","event":"LoadGame","Commander":"Drew","FID":"F123","Ship":"CobraMkIII","GameMode":"Solo"}
                {"timestamp":"2026-10-28T13:00:06Z","event":"CarrierJumpRequest","CarrierID":1,"SystemName":"Lave"}
                """ + "\n"
            );
            await main.RefreshAsync();
            await main.DiscordCarrierAnnouncements.PendingDelivery.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(5, payloads.Count);
            Assert.DoesNotContain(payloads, text => text.Contains("Foreign itinerary", StringComparison.Ordinal));
            Assert.DoesNotContain(payloads, text => text.Contains("Unattributed itinerary", StringComparison.Ordinal));
        }
        finally
        {
            await main.DisposeAsync();
            Directory.Delete(root, true);
        }
    }

    private sealed class Handler(ConcurrentQueue<string> payloads) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.RequestUri!.Host == "discord.com")
            {
                payloads.Enqueue(await request.Content!.ReadAsStringAsync(cancellationToken));
            }
            return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }
}
