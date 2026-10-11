using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.ProfileSync;
using SrvSurvey.Desktop.Tests;
using SrvSurvey.Desktop.Tests.ProfileSync;
using Xunit;

namespace SrvSurvey.Desktop.ViewModels;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ActiveUsersViewModelTests
{
    /// <summary>Loads only once after a commander is known, publishing the seven-day count without waiting on HTTP.</summary>
    [Fact]
    public async Task FirstCommanderLoadStartsOneNonblockingRefresh()
    {
        using var root = new ProfileSyncTestRoot();
        using var handler = new WorkerHandler();
        using var network = new HttpClient(handler);
        await using var viewModel = new ActiveUsersViewModel(
            new DailyActiveUsersClient(root.Paths.DataDirectory, "test", "Linux", network)
        );
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        Assert.Equal("—", viewModel.CountText);
        viewModel.SessionLoaded(false);
        Assert.True(viewModel.PendingRefresh.IsCompleted);
        Assert.Equal(0, handler.RequestCount);

        viewModel.SessionLoaded(true);
        viewModel.SessionLoaded(true);
        await viewModel.PendingRefresh;
        Assert.Equal("42", viewModel.CountText);
        Assert.Equal([nameof(ActiveUsersViewModel.CountText)], changes);
        Assert.Equal(2, handler.RequestCount);
        await viewModel.DisposeAsync();
        await viewModel.DisposeAsync();
        viewModel.SessionLoaded(true);
        Assert.Equal(2, handler.RequestCount);
    }

    /// <summary>Keeps activity current when the next day's session loads without restarting the application.</summary>
    [Fact]
    public async Task LaterDayCanRefreshWithoutRestarting()
    {
        using var root = new ProfileSyncTestRoot();
        using var handler = new WorkerHandler();
        using var network = new HttpClient(handler);
        var clock = new UsageClock();
        await using var viewModel = new ActiveUsersViewModel(
            new DailyActiveUsersClient(root.Paths.DataDirectory, "test", "Linux", network, clock: clock),
            clock: clock
        );
        viewModel.SessionLoaded(true);
        await viewModel.PendingRefresh;
        Assert.Equal(2, handler.RequestCount);
        clock.UtcNow = clock.UtcNow.AddDays(1);
        viewModel.SessionLoaded(true);
        await viewModel.PendingRefresh;
        Assert.Equal(4, handler.RequestCount);
        viewModel.SessionLoaded(true);
        Assert.Equal(4, handler.RequestCount);
    }

    /// <summary>Disables all activity requests during diagnostic replay.</summary>
    [Fact]
    public async Task DisabledReportingDoesNotReadOrWriteIdentity()
    {
        using var root = new ProfileSyncTestRoot();
        using var handler = new WorkerHandler();
        using var network = new HttpClient(handler);
        await using var viewModel = new ActiveUsersViewModel(
            new DailyActiveUsersClient(root.Paths.DataDirectory, "test", "Linux", network),
            enabled: false
        );
        viewModel.SessionLoaded(true);
        await viewModel.PendingRefresh;
        Assert.Equal("—", viewModel.CountText);
        Assert.Equal(0, handler.RequestCount);
        Assert.False(Directory.Exists(Path.Combine(root.Paths.DataDirectory, "usage")));
    }

    /// <summary>Cancellation observes pending work without publishing state after disposal.</summary>
    [Fact]
    public async Task ShutdownCancelsBackgroundRequests()
    {
        using var root = new ProfileSyncTestRoot();
        using var handler = new WorkerHandler { WaitForCancellation = true };
        using var network = new HttpClient(handler);
        var viewModel = new ActiveUsersViewModel(
            new DailyActiveUsersClient(root.Paths.DataDirectory, "test", "Linux", network)
        );
        viewModel.SessionLoaded(true);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(viewModel.PendingRefresh.IsCompleted);
        viewModel.SessionLoaded(true);
        Assert.Equal(1, handler.RequestCount);
        await viewModel.DisposeAsync();
        Assert.True(viewModel.PendingRefresh.IsCompletedSuccessfully);
        Assert.Equal("—", viewModel.CountText);
    }

    /// <summary>Wires the first journal session to the count and excludes its identity and checkpoint from cloud backup.</summary>
    [Fact]
    public async Task MainWindowSessionRefreshesCountAndBackupExcludesUsage()
    {
        using var root = new ProfileSyncTestRoot();
        string journals = root.Path("data", "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllTextAsync(
            Path.Combine(journals, "Journal.2026-10-11T120000.01.log"),
            """
            {"timestamp":"2026-10-11T12:00:00Z","event":"Fileheader","Odyssey":true,"gameversion":"4.0"}
            {"timestamp":"2026-10-11T12:00:01Z","event":"Commander","Name":"Probe","FID":"F123"}
            {"timestamp":"2026-10-11T12:00:02Z","event":"LoadGame","Commander":"Probe","FID":"F123","GameMode":"Solo"}
            """
        );
        using var handler = new WorkerHandler();
        using var network = new HttpClient(handler);
        await using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            journals,
            builder => builder.WithAppDataPaths(root.Paths).WithExternalNetworkClient(network)
        );

        await main.RefreshAsync();
        await main.ActiveUsers.PendingRefresh;
        Assert.Equal("42", main.ActiveUsers.CountText);
        Assert.Equal(2, handler.RequestCount);
        Assert.True(File.Exists(root.Path("data", "usage/local-user.json")));
        ProfileSnapshot snapshot = root.Store.Capture();
        Assert.DoesNotContain(
            snapshot.Portable.Keys.Concat(snapshot.Machine.Keys),
            key => key.Contains("usage", StringComparison.Ordinal)
        );
        await main.RefreshAsync();
        Assert.Equal(2, handler.RequestCount);
    }

    /// <summary>Places the localized count above the sidebar commander card, visible across workspaces.</summary>
    [AvaloniaFact]
    public async Task SidebarShowsDailyCountAboveCommander()
    {
        using var root = new ProfileSyncTestRoot();
        using var handler = new WorkerHandler();
        using var network = new HttpClient(handler);
        await using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithAppDataPaths(root.Paths).WithExternalNetworkClient(network)
        );
        var window = new MainWindow(main) { Width = 1000, Height = 800 };
        try
        {
            window.Show();
            TextBlock count = window.FindControl<TextBlock>("ActiveUsersCount")!;
            Button commander = window.FindControl<Button>("ActiveCommanderCard")!;
            Assert.Equal("—", count.Text);
            main.ActiveUsers.SessionLoaded(true);
            await main.ActiveUsers.PendingRefresh;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal("42", count.Text);
            Assert.True(count.IsEffectivelyVisible);
            Assert.True(
                count.TranslatePoint(default, window)!.Value.Y < commander.TranslatePoint(default, window)!.Value.Y
            );
            StackPanel display = window.FindControl<StackPanel>("ActiveUsersDisplay")!;
            Assert.Equal("Active Users 7d:", Assert.IsType<TextBlock>(display.Children[0]).Text);
            Assert.Contains("7 days", ToolTip.GetTip(display)?.ToString());
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class UsageClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 11, 23, 59, 0, TimeSpan.Zero);

        /// <summary>Returns a controllable UTC day for long-running-session tests.</summary>
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class WorkerHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public bool WaitForCancellation { get; init; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Responds only to the usage Worker while keeping unrelated integrations offline.</summary>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.RequestUri!.Host != WellKnownUris.ActiveUsersPublic.Host)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }
            RequestCount++;
            if (WaitForCancellation)
            {
                Entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.Method == HttpMethod.Post ? "ok" : """{"active7d":42}"""),
            };
        }
    }
}
