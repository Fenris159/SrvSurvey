using System.Net;
using System.Net.Sockets;
using System.Text;
using SrvSurvey.Desktop.ProfileSync;
using Xunit;

namespace SrvSurvey.Desktop.Tests.ProfileSync;

public sealed class LoopbackOAuthReceiverTests
{
    [Theory]
    [InlineData("POST /?code=code&state=state HTTP/1.1")]
    [InlineData("GET /another?code=code&state=state HTTP/1.1")]
    [InlineData("GET /?code=code&state=state&state=state HTTP/1.1")]
    [InlineData("GET /?code=code&state HTTP/1.1")]
    [InlineData("GET /?state=state HTTP/1.1")]
    [InlineData("GET /?code=&state=state HTTP/1.1")]
    [InlineData("invalid")]
    public async Task InvalidHttpCallbacksDoNotReturnAnAuthorizationCode(string request)
    {
        using var receiver = new LoopbackOAuthReceiver();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task<string> response = receiver.ReceiveAsync("state", deadline.Token);
        using var browser = new TcpClient();
        await browser.ConnectAsync(IPAddress.Loopback, receiver.RedirectUri.Port, deadline.Token);
        await browser
            .GetStream()
            .WriteAsync(Encoding.ASCII.GetBytes(request + "\r\nHost: localhost\r\n\r\n"), deadline.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => response);
        using var reader = new StreamReader(browser.GetStream());
        Assert.Contains("400 Bad Request", await reader.ReadToEndAsync(deadline.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteAndOversizedHeadersAreBounded(bool oversized)
    {
        using var receiver = new LoopbackOAuthReceiver();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task<string> response = receiver.ReceiveAsync("state", deadline.Token);
        using var browser = new TcpClient();
        await browser.ConnectAsync(IPAddress.Loopback, receiver.RedirectUri.Port, deadline.Token);
        byte[] request = Encoding.ASCII.GetBytes(
            oversized ? new string('x', 64 * 1024) : "GET /?code=code&state=state HTTP/1.1\r\n"
        );
        await browser.GetStream().WriteAsync(request, deadline.Token);
        browser.Client.Shutdown(SocketShutdown.Send);
        await Assert.ThrowsAsync<InvalidDataException>(() => response);
    }

    [Fact]
    public async Task CancellingWhileHeadersAreIncompleteReleasesTheConnection()
    {
        using var receiver = new LoopbackOAuthReceiver();
        using var cancellation = new CancellationTokenSource();
        Task<string> response = receiver.ReceiveAsync("state", cancellation.Token);
        using var browser = new TcpClient();
        await browser.ConnectAsync(IPAddress.Loopback, receiver.RedirectUri.Port);
        await browser.GetStream().WriteAsync(Encoding.ASCII.GetBytes("GET "));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => response);
    }
}
