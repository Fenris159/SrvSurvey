using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SrvSurvey.Desktop.ProfileSync;

/// <summary>Receives a single bounded OAuth callback through an unprivileged loopback TCP listener.</summary>
internal sealed class LoopbackOAuthReceiver : IDisposable
{
    private const int MaximumHeaderBytes = 64 * 1024;
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);

    /// <summary>Reserves a loopback port directly, avoiding HTTP.sys URL permissions and a port handoff race.</summary>
    internal LoopbackOAuthReceiver()
    {
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        RedirectUri = new UriBuilder(Uri.UriSchemeHttp, IPAddress.Loopback.ToString(), port).Uri;
    }

    internal Uri RedirectUri { get; }

    /// <summary>Verifies the callback state before returning a code and writes a plain browser response.</summary>
    internal async Task<string> ReceiveAsync(string state, CancellationToken cancellationToken)
    {
        using TcpClient peer = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        using NetworkStream stream = peer.GetStream();
        string header = await ReadHeaderAsync(stream, cancellationToken).ConfigureAwait(false);
        string? code = ReadCode(header, state);
        bool valid = code is not null;
        byte[] body = Encoding.UTF8.GetBytes(
            valid
                ? "SrvSurvey received the Google response. You can close this page and return to SrvSurvey."
                : "Google Drive linking was cancelled or its response could not be verified. Return to SrvSurvey to try again."
        );
        string status = valid ? "200 OK" : "400 Bad Request";
        byte[] response = Encoding.UTF8.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"
        );
        await stream.WriteAsync(response, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        if (code is null)
        {
            throw new InvalidOperationException("Google Drive authorization was cancelled or its state did not match.");
        }
        return code;
    }

    /// <summary>Accepts only a GET for this callback address with one code and one matching state.</summary>
    private string? ReadCode(string header, string state)
    {
        string[] request = header.Split("\r\n", 2)[0].Split(' ');
        if (request.Length != 3 || request[0] != "GET" || !request[1].StartsWith("/?", StringComparison.Ordinal))
        {
            return null;
        }
        if (
            !Uri.TryCreate(RedirectUri, request[1], out Uri? callback)
            || callback.Host != RedirectUri.Host
            || callback.Port != RedirectUri.Port
            || callback.AbsolutePath != "/"
        )
        {
            return null;
        }
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string parameter in callback.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = parameter.Split('=', 2);
            if (pair.Length != 2 || !query.TryAdd(WebUtility.UrlDecode(pair[0]), WebUtility.UrlDecode(pair[1])))
            {
                return null;
            }
        }
        string? code = query.GetValueOrDefault("code");
        return query.GetValueOrDefault("state") == state && !string.IsNullOrWhiteSpace(code) ? code : null;
    }

    /// <summary>Reads a bounded header without accepting an unbounded request line or body.</summary>
    private static async Task<string> ReadHeaderAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var header = new MemoryStream();
        byte[] buffer = new byte[1024];
        while (header.Length < MaximumHeaderBytes)
        {
            int count = await stream
                .ReadAsync(
                    buffer.AsMemory(0, (int)Math.Min(buffer.Length, MaximumHeaderBytes - header.Length)),
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (count == 0)
            {
                throw new InvalidDataException("The Google callback ended before its headers were complete.");
            }
            await header.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            string text = Encoding.ASCII.GetString(header.GetBuffer(), 0, (int)header.Length);
            if (text.Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                return text;
            }
        }
        throw new InvalidDataException("The Google callback exceeds the supported header size.");
    }

    /// <summary>Releases the callback port when authorization completes or is cancelled.</summary>
    public void Dispose() => listener.Stop();
}
