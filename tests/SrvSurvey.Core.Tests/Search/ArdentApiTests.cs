using System.Net;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class ArdentApiTests
{
    [Fact]
    public async Task SeparateClientsDoNotShareTheRequestGate()
    {
        using var handler = new GatedHandler();
        using var http = new HttpClient(handler);
        using var first = new ArdentApi(http);
        using var second = new ArdentApi(http);

        Task<System.Text.Json.JsonDocument> held = first.GetAsync("commodities", 1024, "test");
        await handler.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            using System.Text.Json.JsonDocument response = await second
                .GetAsync("commodities", 1024, "test")
                .WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(2, handler.Requests);
        }
        finally
        {
            handler.ReleaseFirst.SetResult();
        }

        using System.Text.Json.JsonDocument completed = await held;
    }

    private sealed class GatedHandler : HttpMessageHandler
    {
        private int requests;
        public int Requests => Volatile.Read(ref requests);
        public TaskCompletionSource FirstEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (Interlocked.Increment(ref requests) == 1)
            {
                FirstEntered.SetResult();
                await ReleaseFirst.Task.WaitAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }
}
