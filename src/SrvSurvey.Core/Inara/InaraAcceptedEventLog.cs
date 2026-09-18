using System.Globalization;

namespace SrvSurvey.Core.Inara;

/// <summary>
/// Rolling accepted-event detail log for Inara uploads. Lines never include API keys
/// and are pruned after <see cref="Retention"/>.
/// </summary>
public sealed class InaraAcceptedEventLog
{
    public static readonly TimeSpan Retention = TimeSpan.FromHours(12);

    private readonly Lock sync = new();
    private readonly TimeProvider timeProvider;
    private readonly string filePath;

    public InaraAcceptedEventLog(string filePath, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        this.filePath = filePath;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void Record(string eventName, string eventTimestamp, string? summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventTimestamp);
        DateTimeOffset now = timeProvider.GetUtcNow();
        string line = string.IsNullOrWhiteSpace(summary)
            ? string.Create(CultureInfo.InvariantCulture, $"{now:O} {eventName} {eventTimestamp}")
            : string.Create(CultureInfo.InvariantCulture, $"{now:O} {eventName} {eventTimestamp} {summary}");
        lock (sync)
        {
            try
            {
                string? directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                List<string> lines = File.Exists(filePath) ? [.. File.ReadAllLines(filePath)] : [];
                lines.Add(line);
                DateTimeOffset cutoff = now - Retention;
                File.WriteAllLines(
                    filePath,
                    lines.Where(item => !TryGetWrittenAt(item, out DateTimeOffset written) || written >= cutoff)
                );
            }
            catch
            {
                // Detail logging must never interrupt journal publication.
            }
        }
    }

    public IReadOnlyList<string> ReadLines()
    {
        lock (sync)
        {
            try
            {
                return File.Exists(filePath) ? File.ReadAllLines(filePath) : [];
            }
            catch
            {
                return [];
            }
        }
    }

    private static bool TryGetWrittenAt(string line, out DateTimeOffset written)
    {
        int separator = line.IndexOf(' ');
        string token = separator < 0 ? line : line[..separator];
        return DateTimeOffset.TryParse(token, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out written);
    }
}
