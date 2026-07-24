using System.Text.Json;
using System.Security.Cryptography;

namespace SrvSurvey.Core.Journal;

public static class StatusFileReader
{
    public const string FileName = "Status.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<StatusReadResult> ReadAsync(
        string path,
        int maximumAttempts = 3,
        TimeSpan? retryDelay = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumAttempts, 1);
        var delay = retryDelay ?? TimeSpan.FromMilliseconds(25);
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    16 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var content = new MemoryStream();
                await stream.CopyToAsync(content, cancellationToken).ConfigureAwait(false);
                var bytes = content.ToArray();
                var status = JsonSerializer.Deserialize<EliteStatus>(
                    bytes,
                    SerializerOptions);
                if (status is null)
                {
                    throw new JsonException("Status.json contained no JSON value.");
                }

                var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                return new StatusReadResult(status, hash, null, attempt);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or JsonException)
            {
                lastException = exception;
                if (attempt < maximumAttempts)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return new StatusReadResult(
            null,
            null,
            $"Could not read {path} after {maximumAttempts} attempts: "
                + lastException?.Message,
            maximumAttempts);
    }
}

public sealed record StatusReadResult(
    EliteStatus? Status,
    string? ContentHash,
    string? Error,
    int Attempts)
{
    public bool IsSuccess => Status is not null;
}
