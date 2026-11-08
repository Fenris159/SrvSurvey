using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Journal;

public sealed class JournalFilePartTests
{
    [Theory]
    [InlineData("Journal.2026-10-01T120000.02.log", 2, "Journal.2026-10-01T120000.01.log")]
    [InlineData("Journal.2026-10-01T120000.002.log", 2, "Journal.2026-10-01T120000.001.log")]
    [InlineData("Journal.2026-10-01T120000.100.log", 100, "Journal.2026-10-01T120000.99.log")]
    public void ContinuationsResolveThePreviousFilenameWithoutChangingItsPadding(
        string filename,
        int number,
        string previous
    )
    {
        Assert.True(
            JournalEventEnvelope.TryParse(
                $$"""{"event":"Fileheader","part":{{number}}}""",
                out JournalEventEnvelope? header,
                out _
            )
        );
        var part = JournalFilePart.Read(Path.Combine(Path.GetTempPath(), filename), header);
        Assert.NotNull(part);
        Assert.Equal(previous, Path.GetFileName(part.PreviousPath));
        Assert.Equal(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(part.PreviousPath));
    }
}
