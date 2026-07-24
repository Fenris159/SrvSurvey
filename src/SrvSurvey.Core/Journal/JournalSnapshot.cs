namespace SrvSurvey.Core.Journal;

public sealed record JournalSnapshot(
    string? SourcePath,
    string? GameVersion,
    string? GameBuild,
    bool? IsOdyssey,
    string? CommanderName,
    string? FrontierId,
    string? GameMode,
    string? SystemName,
    long? SystemAddress,
    string? BodyName,
    bool IsShutdown,
    DateTimeOffset? LastEventTimestamp,
    int ValidLineCount,
    int RecognizedEventCount,
    int MalformedLineCount);
