using System.Runtime.CompilerServices;
using System.Text;

namespace SrvSurvey.Core.Journeys;

public sealed partial class JourneyJournalHistoryReader
{
    /// <summary>Shares the live monitor's physical byte identities across UTF-8 and mixed line endings.</summary>
    private static async IAsyncEnumerable<(string Line, long Offset)> ReadSourceRecordsAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        byte[] buffer = new byte[65536];
        using var line = new MemoryStream();
        long consumed = 0;
        long lineOffset = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            int start = 0;
            for (int index = 0; index < read; index++)
            {
                if (buffer[index] != (byte)'\n')
                {
                    continue;
                }
                await line.WriteAsync(buffer.AsMemory(start, index - start), cancellationToken).ConfigureAwait(false);
                yield return (DecodeSourceLine(line, lineOffset), lineOffset);
                line.SetLength(0);
                line.Position = 0;
                lineOffset = consumed + index + 1;
                start = index + 1;
            }
            await line.WriteAsync(buffer.AsMemory(start, read - start), cancellationToken).ConfigureAwait(false);
            consumed += read;
        }
        if (line.Length > 0)
        {
            yield return (DecodeSourceLine(line, lineOffset), lineOffset);
        }
    }

    private static string DecodeSourceLine(MemoryStream line, long offset)
    {
        ReadOnlySpan<byte> bytes = line.GetBuffer().AsSpan(0, checked((int)line.Length));
        if (!bytes.IsEmpty && bytes[^1] == (byte)'\r')
        {
            bytes = bytes[..^1];
        }
        if (offset == 0 && bytes.StartsWith(Encoding.UTF8.Preamble))
        {
            bytes = bytes[Encoding.UTF8.Preamble.Length..];
        }
        return Encoding.UTF8.GetString(bytes);
    }
}
