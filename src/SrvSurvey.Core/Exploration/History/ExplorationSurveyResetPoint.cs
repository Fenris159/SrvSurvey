using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Exploration.History;

public sealed class ExplorationSurveyResetRecoveryException(string? journalName = null)
    : InvalidOperationException(
        journalName is null
            ? "The exploration journal index needs recovery."
            : $"The journal position for {journalName} could not be verified."
    )
{
    public string? JournalName { get; } = journalName;
}

public sealed record ExplorationSurveySourceCheckpoint(string SourceName, long Offset, string Hash)
{
    /// <summary>The catalog coordinate generation in which this particular cursor has been proven.</summary>
    public string? CatalogGeneration { get; init; }

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(SourceName)
            && SourceName is not "." and not ".."
            && !SourceName.Contains('/')
            && !SourceName.Contains('\\')
            && !SourceName.Contains(':')
            && Offset >= -1
            && (CatalogGeneration is null || Guid.TryParseExact(CatalogGeneration, "N", out _))
            && Hash is not null
            && (Offset == -1 || (Hash is { Length: 64 } && Hash.All(Uri.IsHexDigit)));
    }

    public static ExplorationSurveySourceCheckpoint? FromEntry(JournalEventEnvelope entry)
    {
        string? identity = entry.SourceRecordIdentity;
        int separator = identity?.LastIndexOf(':') ?? -1;
        if (
            separator <= 0
            || !long.TryParse(
                identity.AsSpan(separator + 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long offset
            )
        )
        {
            return null;
        }
        return new(
            identity![..separator],
            offset,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.RawJson)))
        );
    }
}

/// <summary>The profile saves this intent atomically with cleared totals; its original boundary can be reapplied after interrupted catalog I/O.</summary>
public sealed record ExplorationSurveyResetPoint(
    string Id,
    string FrontierId,
    bool IsLive,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ExplorationSurveySourceCheckpoint> Sources
)
{
    public string? CatalogGeneration { get; init; }

    public bool IsValidForScope(string owner, bool live)
    {
        return !string.IsNullOrWhiteSpace(Id)
            && CreatedAt != default
            && (CatalogGeneration is null || Guid.TryParseExact(CatalogGeneration, "N", out _))
            && string.Equals(FrontierId, owner, StringComparison.OrdinalIgnoreCase)
            && IsLive == live
            && Sources is not null
            && Sources.All(source => source is not null && source.IsValid())
            && Sources.Select(source => source.SourceName).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                == Sources.Count;
    }
}
