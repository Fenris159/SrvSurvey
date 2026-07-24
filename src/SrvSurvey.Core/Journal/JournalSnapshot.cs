namespace SrvSurvey.Core.Journal;

using SrvSurvey.Core.Search;

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
    GalacticCoordinate? StarPosition,
    string? BodyName,
    bool IsShutdown,
    DateTimeOffset? LastEventTimestamp,
    int ValidLineCount,
    int RecognizedEventCount,
    int MalformedLineCount);
