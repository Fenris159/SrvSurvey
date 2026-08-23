using System.Text;

namespace SrvSurvey.Core.Diagnostics.Replay;

public interface IReplayDelay
{
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal interface IReplayJournalWriter
{
    Task AppendLineAsync(
        string path,
        string line,
        CancellationToken cancellationToken);
}

internal sealed class AtomicReplayJournalWriter(
    Action? emissionStarting = null) : IReplayJournalWriter
{
    public async Task AppendLineAsync(
        string path,
        string line,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = Encoding.UTF8.GetBytes(line + "\n");
        emissionStarting?.Invoke();
        await using var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 16 * 1024,
            useAsync: true);
        await stream.WriteAsync(payload, CancellationToken.None);
        await stream.FlushAsync(CancellationToken.None);
    }
}

public sealed class JournalReplayPlayer
{
    private readonly DiagnosticReplaySession session;
    private readonly IReplayDelay delay;
    private readonly IReplayJournalWriter writer;
    private readonly SemaphoreSlim gate = new(1, 1);
    private int position;

    public JournalReplayPlayer(
        DiagnosticReplaySession session,
        IReplayDelay? delay = null)
        : this(session, delay, new AtomicReplayJournalWriter())
    {
    }

    internal JournalReplayPlayer(
        DiagnosticReplaySession session,
        IReplayDelay? delay,
        IReplayJournalWriter writer)
    {
        this.session = session
            ?? throw new ArgumentNullException(nameof(session));
        this.delay = delay ?? new SystemReplayDelay();
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public event EventHandler<JournalReplayPositionChangedEventArgs>?
        PositionChanged;

    public int Position => Volatile.Read(ref position);

    public bool IsComplete => Position >= session.Events.Count;

    public async Task<bool> StepAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await AppendNextCoreAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task PlayAsync(
        double speed,
        CancellationToken cancellationToken)
    {
        ValidateSpeed(speed, nameof(speed));
        return PlayAsync(() => speed, cancellationToken);
    }

    public async Task PlayAsync(
        Func<double> speedProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(speedProvider);

        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan wait;
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (position >= session.Events.Count)
                {
                    return;
                }

                var speed = speedProvider();
                ValidateSpeed(speed, nameof(speedProvider));
                wait = ResolveDelay(position, speed);
            }
            finally
            {
                gate.Release();
            }

            if (wait > TimeSpan.Zero)
            {
                await delay.WaitAsync(wait, cancellationToken);
            }

            if (!await StepAsync(cancellationToken))
            {
                return;
            }
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await File.WriteAllTextAsync(
                session.PlaybackJournalPath,
                string.Empty,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            position = 0;
            PositionChanged?.Invoke(
                this,
                new JournalReplayPositionChangedEventArgs(
                    Position,
                    session.Events.Count,
                    currentEvent: null));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SeekAsync(
        int position,
        CancellationToken cancellationToken)
    {
        if (position < 0 || position > session.Events.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        await ResetAsync(cancellationToken);
        while (Position < position)
        {
            _ = await StepAsync(cancellationToken);
        }
    }

    private async Task<bool> AppendNextCoreAsync(
        CancellationToken cancellationToken)
    {
        if (position >= session.Events.Count)
        {
            return false;
        }

        var replayEvent = session.Events[position];
        cancellationToken.ThrowIfCancellationRequested();
        await writer.AppendLineAsync(
            session.PlaybackJournalPath,
            replayEvent.RawJson,
            cancellationToken);

        position++;
        PositionChanged?.Invoke(
            this,
            new JournalReplayPositionChangedEventArgs(
                position,
                session.Events.Count,
                replayEvent));
        return true;
    }

    private TimeSpan ResolveDelay(int nextPosition, double speed)
    {
        if (nextPosition == 0
            || session.Events[nextPosition - 1].Timestamp is not { } previous
            || session.Events[nextPosition].Timestamp is not { } next
            || next <= previous)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromTicks((long)((next - previous).Ticks / speed));
    }

    private static void ValidateSpeed(double speed, string parameterName)
    {
        if (!double.IsFinite(speed) || speed <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Replay speed must be a finite positive number.");
        }
    }

    private sealed class SystemReplayDelay : IReplayDelay
    {
        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Task.Delay(delay, cancellationToken);
        }
    }
}

public sealed class JournalReplayPositionChangedEventArgs(
    int position,
    int total,
    JournalReplayEvent? currentEvent) : EventArgs
{
    public int Position { get; } = position;

    public int Total { get; } = total;

    public JournalReplayEvent? CurrentEvent { get; } = currentEvent;
}
