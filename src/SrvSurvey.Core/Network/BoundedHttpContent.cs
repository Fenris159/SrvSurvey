using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SrvSurvey.Core.Network;

internal static class BoundedHttpContent
{
    private const int BufferSize = 64 * 1024;

    public static async Task<byte[]> ReadBytesAsync(
        HttpContent content,
        long maximumBytes,
        string description,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (content.Headers.ContentLength is > 0
            && content.Headers.ContentLength > maximumBytes)
        {
            throw TooLarge(description, maximumBytes);
        }

        await using var input = await content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var output = content.Headers.ContentLength is > 0
            && content.Headers.ContentLength <= int.MaxValue
                ? new MemoryStream((int)content.Headers.ContentLength.Value)
                : new MemoryStream();
        var buffer = new byte[BufferSize];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return output.ToArray();
            }

            if (output.Length + read > maximumBytes)
            {
                throw TooLarge(description, maximumBytes);
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public static async Task<JsonDocument> ReadJsonDocumentAsync(
        HttpContent content,
        long maximumBytes,
        string description,
        CancellationToken cancellationToken = default)
    {
        var bytes = await ReadBytesAsync(
                content,
                maximumBytes,
                description,
                cancellationToken)
            .ConfigureAwait(false);
        return JsonDocument.Parse(bytes);
    }

    public static async Task<JsonNode?> ReadJsonNodeAsync(
        HttpContent content,
        long maximumBytes,
        string description,
        CancellationToken cancellationToken = default)
    {
        var bytes = await ReadBytesAsync(
                content,
                maximumBytes,
                description,
                cancellationToken)
            .ConfigureAwait(false);
        return JsonNode.Parse(bytes);
    }

    public static async Task<T?> ReadFromJsonAsync<T>(
        HttpContent content,
        long maximumBytes,
        string description,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var bytes = await ReadBytesAsync(
                content,
                maximumBytes,
                description,
                cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(
            bytes,
            options ?? JsonSerializerOptions.Web);
    }

    public static async Task<string> ReadStringAsync(
        HttpContent content,
        long maximumBytes,
        string description,
        CancellationToken cancellationToken = default)
    {
        var bytes = await ReadBytesAsync(
                content,
                maximumBytes,
                description,
                cancellationToken)
            .ConfigureAwait(false);
        var encoding = ResolveEncoding(content.Headers.ContentType?.CharSet);
        return encoding.GetString(bytes);
    }

    public static async Task<string> ReadStringPrefixAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);

        await using var input = await content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var output = new MemoryStream(maximumBytes);
        var buffer = new byte[Math.Min(BufferSize, maximumBytes)];
        var truncated = content.Headers.ContentLength > maximumBytes;
        while (output.Length < maximumBytes)
        {
            var remaining = maximumBytes - (int)output.Length;
            var read = await input.ReadAsync(
                    buffer.AsMemory(0, Math.Min(buffer.Length, remaining)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }

        if (!truncated && output.Length == maximumBytes)
        {
            truncated = await input.ReadAsync(
                    buffer.AsMemory(0, 1),
                    cancellationToken)
                .ConfigureAwait(false) > 0;
        }

        var encoding = ResolveEncoding(content.Headers.ContentType?.CharSet);
        return encoding.GetString(output.ToArray()) + (truncated ? "..." : string.Empty);
    }

    private static Encoding ResolveEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(charset.Trim(' ', '\"'));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    private static InvalidDataException TooLarge(
        string description,
        long maximumBytes)
    {
        return new InvalidDataException(
            $"{description} exceeded the {maximumBytes:N0}-byte safety limit.");
    }
}
