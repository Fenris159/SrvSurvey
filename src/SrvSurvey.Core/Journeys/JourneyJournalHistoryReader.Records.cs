using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Journeys;

public sealed partial class JourneyJournalHistoryReader
{
    private static IAsyncEnumerable<(string Line, long Offset)> ReadSourceRecordsAsync(
        Stream stream,
        CancellationToken cancellationToken
    ) => JournalSourceRecordReader.ReadAsync(stream, cancellationToken);
}
