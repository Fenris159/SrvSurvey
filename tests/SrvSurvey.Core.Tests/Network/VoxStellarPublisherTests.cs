using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Tests.Network;

public sealed class VoxStellarPublisherTests
{
    [Fact]
    public async Task SendsOnlySupportedLiveEventsWithExpectedEnvelopeAndSignature()
    {
        const string sharedKey = "test-shared-key";
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        using var publisher = new VoxStellarPublisher(
            "2.1.3.0-rc.23",
            sharedKey,
            client);
        publisher.SetEnabled(true);

        var result = await publisher.ApplyAsync(new VoxStellarApplyRequest
        {
            JournalEvents =
            [
                Parse("""{"timestamp":"2026-08-13T22:00:00Z","event":"Scan","BodyName":"Test A 1","BodyID":1}"""),
                Parse("""{"timestamp":"2026-08-13T22:00:01Z","event":"Docked","StationName":"Test Port"}"""),
            ],
            CommanderName = "Test Cmdr",
            Enabled = true,
            AllowPublishing = true,
        });

        var request = await handler.Request.Task.WaitAsync(
            TimeSpan.FromSeconds(2));
        Assert.Equal(["Scan"], result.QueuedEventNames);
        Assert.Empty(result.Warnings);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(WellKnownUris.VoxStellarWebhook, request.Uri);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("SrvSurvey-XP/2.1.3.0-rc.23", request.UserAgent);

        using var document = JsonDocument.Parse(request.Body);
        Assert.Equal(
            "Test Cmdr",
            document.RootElement.GetProperty("commander").GetString());
        Assert.Equal(
            "Scan",
            document.RootElement
                .GetProperty("data")
                .GetProperty("event")
                .GetString());
        Assert.Equal(
            ExpectedSignature(sharedKey, request.Body),
            request.Signature);
        Assert.Equal(request.Signature.ToLowerInvariant(), request.Signature);
    }

    [Fact]
    public async Task BootstrapAndDisabledUpdatesNeverReachTheWebhook()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        using var publisher = new VoxStellarPublisher(
            "1.0.0",
            "test-key",
            client);
        var scan = Parse("""{"event":"Scan","BodyName":"Test A 1"}""");

        var bootstrap = await publisher.ApplyAsync(new VoxStellarApplyRequest
        {
            JournalEvents = [scan],
            CommanderName = "Test Cmdr",
            Enabled = true,
            AllowPublishing = false,
        });
        var disabled = await publisher.ApplyAsync(new VoxStellarApplyRequest
        {
            JournalEvents = [scan],
            CommanderName = "Test Cmdr",
            Enabled = false,
            AllowPublishing = true,
        });

        await Task.Delay(50);
        Assert.Empty(bootstrap.QueuedEventNames);
        Assert.Empty(disabled.QueuedEventNames);
        Assert.False(handler.Request.Task.IsCompleted);
    }

    [Fact]
    public async Task DisablingConsentDropsQueuedWorkThatHasNotStarted()
    {
        var handler = new BlockingHandler();
        using var client = new HttpClient(handler);
        using var publisher = new VoxStellarPublisher(
            "1.0.0",
            "test-key",
            client);
        publisher.SetEnabled(true);
        await publisher.ApplyAsync(new VoxStellarApplyRequest
        {
            JournalEvents =
            [
                Parse("""{"event":"Scan","BodyName":"Test A 1"}"""),
                Parse("""{"event":"ScanOrganic","Body":"Test A 1"}"""),
            ],
            CommanderName = "Test Cmdr",
            Enabled = true,
            AllowPublishing = true,
        });
        await handler.FirstRequestStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2));

        publisher.SetEnabled(false);
        handler.ReleaseFirstRequest.TrySetResult();
        await Task.Delay(100);

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task MissingBuildKeyReportsConfigurationWithoutSending()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        using var publisher = new VoxStellarPublisher(
            "1.0.0",
            sharedKey: null,
            client);

        var result = await publisher.ApplyAsync(new VoxStellarApplyRequest
        {
            JournalEvents = [Parse("""{"event":"FSDJump","StarSystem":"Test A"}""")],
            CommanderName = "Test Cmdr",
            Enabled = true,
            AllowPublishing = true,
        });

        Assert.False(publisher.IsConfigured);
        Assert.Empty(result.QueuedEventNames);
        Assert.Contains(result.Warnings, warning => warning.Contains(
            "signing key",
            StringComparison.Ordinal));
        Assert.False(handler.Request.Task.IsCompleted);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(
            json,
            out var journalEvent,
            out var error), error);
        return journalEvent!;
    }

    private static string ExpectedSignature(string key, byte[] body)
    {
        return Convert.ToHexString(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(key),
                body))
            .ToLowerInvariant();
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        string ContentType,
        string UserAgent,
        string Signature,
        byte[] Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public TaskCompletionSource<RecordedRequest> Request { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request.TrySetResult(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Content!.Headers.ContentType!.MediaType!,
                request.Headers.UserAgent.ToString(),
                request.Headers.GetValues("Signature").Single(),
                await request.Content.ReadAsByteArrayAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        public TaskCompletionSource FirstRequestStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstRequest { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            FirstRequestStarted.TrySetResult();
            await ReleaseFirstRequest.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
