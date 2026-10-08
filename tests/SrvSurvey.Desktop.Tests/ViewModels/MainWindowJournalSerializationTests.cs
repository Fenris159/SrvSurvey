using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class MainWindowJournalSerializationTests
{
    /// <summary>Refresh waits for an earlier poll to finish using its dock, including when another waiting monitor is canceled.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RefreshKeepsEventTimeDockUntilEarlierMonitorFinishes(
        bool cancelWaitingMonitor,
        bool closeDuringManualRefresh
    )
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-journal-serialization-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        using var handler = new CargoRecoveryHandler();
        using var network = new HttpClient(handler);
        using var monitorCancellation = new CancellationTokenSource();
        Task monitor = Task.CompletedTask;
        Task refresh = Task.CompletedTask;
        MainWindowViewModel? viewModel = null;
        try
        {
            string journals = Path.Combine(root, "journals");
            Directory.CreateDirectory(journals);
            string journal = Path.Combine(journals, "Journal.2026-10-08T120000.01.log");
            await File.WriteAllTextAsync(
                journal,
                """
                {"timestamp":"2026-10-08T12:00:00Z","event":"Fileheader","Odyssey":true}
                {"timestamp":"2026-10-08T12:00:01Z","event":"Commander","Name":"Drew","FID":"F123"}
                {"timestamp":"2026-10-08T12:00:02Z","event":"LoadGame","Commander":"Drew","FID":"F123","Ship":"CobraMkIII"}
                {"timestamp":"2026-10-08T12:00:03Z","event":"Docked","MarketID":42,"SystemAddress":20,"StarSystem":"Test","StationName":"ABC-123","StationType":"FleetCarrier","StationServices":["commodities"]}
                """ + "\n"
            );
            await File.WriteAllTextAsync(
                Path.Combine(journals, "Status.json"),
                "{\"event\":\"Status\",\"Flags\":16777216}"
            );
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                Path.Combine(root, "profile"),
                Path.Combine(root, "cache"),
                []
            )
            {
                SettingsFrontierId = "F123",
            };
            var settings = new ColonizationSettingsStore(paths.UiSettingsPath);
            settings.SaveEnabled(true);
            settings.SaveFleetCarrierCargoSyncEnabled(true);
            settings.SavePendingCargoAdjustments([
                new ColonizationPendingCargoAdjustment(
                    "Drew|F123|True",
                    42,
                    new() { ["steel"] = 1 },
                    null,
                    new() { ["steel"] = 100 },
                    Attempted: false,
                    OutcomeUnknown: false
                ),
            ]);
            await new CommanderProfileStore(paths.DataDirectory).SaveRavenColonialApiKeyAsync(
                "F123",
                "Drew",
                true,
                "test-key"
            );
            viewModel = MainWindowViewModelTestBuilder.Create(
                journals,
                builder => builder.WithAppDataPaths(paths).WithExternalNetworkClient(network)
            );
            await viewModel.RefreshAsync();
            Assert.Equal("ABC-123", viewModel.Colonization.ConstructionTitle);
            await File.AppendAllTextAsync(
                journal,
                "{\"timestamp\":\"2026-10-08T12:00:04Z\",\"event\":\"CargoTransfer\",\"Transfers\":[{\"Type\":\"Steel\",\"Count\":4,\"Direction\":\"tocarrier\"}]}\n"
            );
            handler.BlockRecovery = true;
            monitor = closeDuringManualRefresh
                ? viewModel.RefreshAsync()
                : viewModel.MonitorAsync(TimeSpan.FromSeconds(10), monitorCancellation.Token);
            await handler.EnteredRecovery.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (closeDuringManualRefresh)
            {
                Task closing = viewModel.DisposeAsync().AsTask();
                Assert.False(closing.IsCompleted);
                handler.ReleaseRecovery.TrySetResult(true);
                await Task.WhenAll(monitor, closing).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Contains(handler.Adjustments, call => call.MarketId == 42 && call.Cargo["steel"] == 4);
                return;
            }
            if (cancelWaitingMonitor)
            {
                using var waiterCancellation = new CancellationTokenSource();
                Task waiter = viewModel.MonitorAsync(TimeSpan.FromSeconds(10), waiterCancellation.Token);
                await waiterCancellation.CancelAsync();
                await waiter.WaitAsync(TimeSpan.FromSeconds(5));
            }
            await File.AppendAllTextAsync(
                journal,
                """
                {"timestamp":"2026-10-08T12:00:05Z","event":"Docked","MarketID":43,"SystemAddress":30,"StarSystem":"Later","StationName":"XYZ-456","StationType":"FleetCarrier","StationServices":["commodities"]}
                {"timestamp":"2026-10-08T12:00:06Z","event":"CargoTransfer","Transfers":[{"Type":"Steel","Count":2,"Direction":"tocarrier"}]}
                """ + "\n"
            );
            var refreshing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.IsBusy) && viewModel.IsBusy)
                {
                    refreshing.TrySetResult(true);
                }
            };
            refresh = viewModel.RefreshAsync();
            await refreshing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAny(refresh, Task.Delay(TimeSpan.FromMilliseconds(200)));
            Assert.False(refresh.IsCompleted);
            Assert.Equal("ABC-123", viewModel.Colonization.ConstructionTitle);
            handler.ReleaseRecovery.TrySetResult(true);
            await refresh.WaitAsync(TimeSpan.FromSeconds(10));
            await monitorCancellation.CancelAsync();
            await monitor.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Contains(handler.Adjustments, call => call.MarketId == 42 && call.Cargo["steel"] == 4);
            Assert.Contains(handler.Adjustments, call => call.MarketId == 43 && call.Cargo["steel"] == 2);
            Assert.DoesNotContain(handler.Adjustments, call => call.MarketId == 43 && call.Cargo["steel"] == 4);
        }
        finally
        {
            handler.ReleaseRecovery.TrySetResult(true);
            await monitorCancellation.CancelAsync();
            await Task.WhenAll(monitor, refresh);
            if (viewModel is not null)
            {
                await viewModel.DisposeAsync();
            }
            Directory.Delete(root, true);
        }
    }

    private sealed class CargoRecoveryHandler : HttpMessageHandler
    {
        public bool BlockRecovery { get; set; }
        public TaskCompletionSource<bool> EnteredRecovery { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseRecovery { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<(long MarketId, Dictionary<string, int> Cargo)> Adjustments { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/api/fc/42" && BlockRecovery)
            {
                EnteredRecovery.TrySetResult(true);
                await ReleaseRecovery.Task.WaitAsync(cancellationToken);
            }
            if (request.Method == HttpMethod.Patch && path.EndsWith("/cargo", StringComparison.Ordinal))
            {
                Dictionary<string, int> cargo = (
                    await request.Content!.ReadFromJsonAsync<Dictionary<string, int>>(cancellationToken)
                )!;
                long market = long.Parse(path.Split('/')[3], System.Globalization.CultureInfo.InvariantCulture);
                Adjustments.Add((market, cargo));
                return Json(new Dictionary<string, int> { ["steel"] = 100 + cargo["steel"] });
            }
            if (path is "/api/fc/42" or "/api/fc/43")
            {
                return Json(Carrier(path.EndsWith("42", StringComparison.Ordinal) ? 42 : 43));
            }
            if (path.EndsWith("/fc/all", StringComparison.Ordinal))
            {
                return Json(new[] { Carrier(42), Carrier(43) });
            }
            if (
                path.EndsWith("/active", StringComparison.Ordinal)
                || path.EndsWith("/hiddenIDs", StringComparison.Ordinal)
            )
            {
                return Json(Array.Empty<string>());
            }
            if (path.EndsWith("/primary", StringComparison.Ordinal))
            {
                return Json<string?>(null);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static ColonizationFleetCarrier Carrier(long market) =>
            new()
            {
                MarketId = market,
                Name = market == 42 ? "ABC-123" : "XYZ-456",
                Cargo = new() { ["steel"] = 100 },
            };

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(value, options: new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            };
    }
}
