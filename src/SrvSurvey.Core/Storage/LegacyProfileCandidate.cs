namespace SrvSurvey.Core.Storage;

public sealed record LegacyProfileCandidate(
    LegacyProfileLocationKind Kind,
    string Path);

public enum LegacyProfileLocationKind
{
    Desktop,
    MicrosoftStore,
    MicrosoftStoreBackup,
}
