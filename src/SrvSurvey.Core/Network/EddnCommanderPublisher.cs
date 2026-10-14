using System.Security.Cryptography;
using System.Text;

namespace SrvSurvey.Core.Network;

/// <summary>Gives concurrent commanders independent durable EDDN queues while preserving legacy pending reports.</summary>
public sealed class EddnCommanderPublisher : IEddnPublisher, IDisposable
{
    private readonly Lock sync = new();
    private readonly Dictionary<string, EddnPublisher> publishers = new(StringComparer.Ordinal);
    private readonly Func<string, string?, EddnPublisher> create;
    private readonly EddnPublisher legacy;
    private readonly string directory;
    private bool enabled;
    private bool suspended;
    private bool disposed;

    /// <summary>Restores account queues with shared consent scoped to this settings profile and a legacy drain-only queue.</summary>
    public EddnCommanderPublisher(
        string softwareVersion,
        string directory,
        string legacyOutboxPath,
        HttpClient? client = null,
        Action<string>? log = null,
        Func<DateTimeOffset>? utcNow = null,
        bool automaticProcessing = true
    )
    {
        this.directory = directory;
        create = (path, consent) =>
            new EddnPublisher(
                softwareVersion,
                new EddnOutboxStorage(path, consent),
                client,
                log: log,
                utcNow: utcNow,
                automaticProcessing: automaticProcessing
            );
        legacy = create(legacyOutboxPath, Path.Combine(directory, "consent"));
    }

    /// <summary>Routes an immutable commander session to its own queue rather than competing for another client's lease.</summary>
    public Task<EddnPublicationResult> ApplyAsync(
        EddnApplyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (disposed || string.IsNullOrWhiteSpace(request.FrontierId))
            {
                return Task.FromResult(EddnPublicationResult.Empty);
            }
            SetEnabled(request.Enabled);
            EddnPublisher publisher = GetOrCreatePublisher(AccountKey(request.FrontierId));
            return publisher.ApplyAsync(request, cancellationToken);
        }
    }

    /// <summary>Restores only the selected commander's persisted queue before journal activity, without leasing another instance's account.</summary>
    public void RestorePendingAccount(string? frontierId)
    {
        lock (sync)
        {
            if (disposed || string.IsNullOrWhiteSpace(frontierId))
            {
                return;
            }
            string account = AccountKey(frontierId);
            string path = Path.Combine(directory, account, "outbox.json");
            if (File.Exists(path) || Directory.Exists(path + ".d"))
            {
                GetOrCreatePublisher(account);
            }
        }
    }

    /// <summary>Uses the same bounded, normalized account path for both startup recovery and live capture.</summary>
    private static string AccountKey(string frontierId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(frontierId.Trim().ToUpperInvariant())));

    /// <summary>Opens a selected account with the current consent and suspension state while the routing lock is held.</summary>
    private EddnPublisher GetOrCreatePublisher(string account)
    {
        if (!publishers.TryGetValue(account, out EddnPublisher? publisher))
        {
            publisher = create(Path.Combine(directory, account, "outbox.json"), Path.Combine(directory, "consent"));
            publisher.SetSuspended(suspended);
            publisher.SetEnabled(enabled);
            publishers.Add(account, publisher);
        }
        return publisher;
    }

    /// <summary>Propagates consent to loaded queues; the shared marker also protects queues owned by other instances.</summary>
    public void SetEnabled(bool enabled)
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }
            this.enabled = enabled;
            legacy.SetEnabled(enabled);
            foreach (EddnPublisher publisher in publishers.Values)
            {
                publisher.SetEnabled(enabled);
            }
        }
    }

    /// <summary>Pauses uncertain live attribution without discarding already captured payloads.</summary>
    public void SetSuspended(bool suspended)
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }
            this.suspended = suspended;
            legacy.SetSuspended(suspended);
            foreach (EddnPublisher publisher in publishers.Values)
            {
                publisher.SetSuspended(suspended);
            }
        }
    }

    /// <summary>Waits for captured companion evidence and attempts due reports without bypassing retry deadlines.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        EddnPublisher[] captured;
        lock (sync)
        {
            captured = [legacy, .. publishers.Values];
        }
        foreach (EddnPublisher publisher in captured)
        {
            await publisher.WaitForCompanionReadsAsync(cancellationToken).ConfigureAwait(false);
            await publisher.ProcessPendingAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Releases every owned queue without deleting pending reports or receipts.</summary>
    public void Dispose()
    {
        EddnPublisher[] captured;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            captured = [legacy, .. publishers.Values];
            publishers.Clear();
        }
        foreach (EddnPublisher publisher in captured)
        {
            publisher.Dispose();
        }
    }
}
