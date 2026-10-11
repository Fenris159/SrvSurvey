using System.Net.Http.Json;
using System.Text.Json;

namespace SrvSurvey.Core.Network;

/// <summary>Reports anonymous installation activity and caches the seven-day user count once per UTC day.</summary>
public sealed class DailyActiveUsersClient : IDisposable
{
    private const int MaximumStatsBytes = 16 * 1024;
    private const int MaximumPingBytes = 1024;
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly string directory;
    private readonly string version;
    private readonly string os;
    private readonly Action<string>? log;
    private readonly TimeProvider clock;

    /// <summary>Uses machine-local state shared by commander instances, without any commander identifiers.</summary>
    public DailyActiveUsersClient(
        string dataDirectory,
        string version,
        string os,
        HttpClient? client = null,
        Action<string>? log = null,
        TimeProvider? clock = null
    )
    {
        directory = Path.Combine(dataDirectory, "usage");
        this.version = version;
        this.os = os;
        this.client = client ?? new HttpClient();
        ownsClient = client is null;
        this.log = log;
        this.clock = clock ?? TimeProvider.System;
    }

    /// <summary>Serializes daily attempts across processes and returns the last valid count on network failure.</summary>
    public async Task<long?> RefreshAsync(CancellationToken cancellationToken = default)
    {
        long? count = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10), clock);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            Directory.CreateDirectory(directory);
            using FileStream lease = await AcquireLeaseAsync(deadline.Token).ConfigureAwait(false);
            LocalUsageState state = ReadState();
            count = state.Active7d;
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            if (state.LastAttemptDay == today)
            {
                return count;
            }

            deadline.Token.ThrowIfCancellationRequested();
            // Reserve this day's attempt before sending, including uncertain transport outcomes.
            state = state with
            {
                LastAttemptDay = today,
            };
            SaveState(state);
            await PingAsync(state.Id, deadline.Token).ConfigureAwait(false);
            using HttpResponseMessage response = await client
                .GetAsync(WellKnownUris.ActiveUsersPublic, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using JsonDocument stats = await BoundedHttpContent
                .ReadJsonDocumentAsync(response.Content, MaximumStatsBytes, "Active-user statistics", deadline.Token)
                .ConfigureAwait(false);
            if (
                stats.RootElement.ValueKind != JsonValueKind.Object
                || !stats.RootElement.TryGetProperty("active7d", out JsonElement active)
                || !active.TryGetInt64(out long updatedCount)
                || updatedCount < 0
            )
            {
                throw new InvalidDataException("Active-user statistics did not contain a valid seven-day count.");
            }

            SaveState(state with { Active7d = updatedCount });
            return updatedCount;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or InvalidDataException
                        or UnauthorizedAccessException
                        or JsonException
                        or HttpRequestException
                        or OperationCanceledException
                        or InvalidOperationException
            )
        {
            log?.Invoke("Daily active-user refresh skipped: " + exception.Message);
            return count;
        }
    }

    /// <summary>Sends anonymous installation activity with a shorter deadline so statistics can still refresh.</summary>
    private async Task PingAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3), clock);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using var request = new HttpRequestMessage(HttpMethod.Post, WellKnownUris.ActiveUsersPing)
            {
                Content = JsonContent.Create(
                    new
                    {
                        id,
                        version,
                        os,
                    }
                ),
            };
            using HttpResponseMessage response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await BoundedHttpContent
                .ReadBytesAsync(response.Content, MaximumPingBytes, "Active-user check-in", deadline.Token)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or InvalidDataException
                || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            )
        {
            // Public statistics remain useful even when this installation's check-in fails.
            log?.Invoke("Daily active-user check-in skipped: " + exception.Message);
        }
    }

    /// <summary>Waits off the UI thread for another instance's short-lived exclusive reporting lease.</summary>
    private async Task<FileStream> AcquireLeaseAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    Path.Combine(directory, "report.lock"),
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None
                );
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Retains the installation identity; damaged state is skipped rather than registering another user.</summary>
    private LocalUsageState ReadState()
    {
        string path = Path.Combine(directory, "local-user.json");
        if (!File.Exists(path))
        {
            return new LocalUsageState { Id = Guid.NewGuid().ToString("N") };
        }
        LocalUsageState? state = JsonSerializer.Deserialize<LocalUsageState>(File.ReadAllText(path));
        if (state is null || !Guid.TryParseExact(state.Id, "N", out _) || state.Active7d is < 0)
        {
            throw new InvalidDataException("The local active-user state is invalid; its identity was preserved.");
        }
        return state;
    }

    /// <summary>Atomically replaces the daily checkpoint and cached count on the same filesystem.</summary>
    private void SaveState(LocalUsageState state)
    {
        string path = Path.Combine(directory, "local-user.json");
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(state));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>Releases only the HTTP client owned by this service.</summary>
    public void Dispose()
    {
        if (ownsClient)
        {
            client.Dispose();
        }
    }

    /// <summary>Machine-local identity, attempt date, and last valid public count, excluded from profile sync.</summary>
    private sealed record LocalUsageState
    {
        /// <summary>Stores the random installation identity shared by this machine's commander instances.</summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>Reserves the last UTC reporting day before sending a potentially uncertain check-in.</summary>
        public DateOnly? LastAttemptDay { get; init; }

        /// <summary>Retains the last valid public seven-day count for offline sessions.</summary>
        public long? Active7d { get; init; }
    }
}
