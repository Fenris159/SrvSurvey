using System.Globalization;
using System.Text.Json;

namespace SrvSurvey.Core.Journal;

/// <summary>A filename and header that agree on one part of a journal session.</summary>
public sealed record JournalFilePart(string Series, int Number, string? PreviousPath)
{
    public static JournalFilePart? Read(string path, JournalEventEnvelope? header)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string[] pieces = Path.GetFileName(path).Split('.');
        if (
            pieces.Length != 4
            || header?.EventName != "Fileheader"
            || !int.TryParse(pieces[2], NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            || number < 1
        )
        {
            return null;
        }
        if (
            (
                header.Payload.TryGetProperty("part", out JsonElement declared)
                || header.Payload.TryGetProperty("Part", out declared)
            ) && (declared.ValueKind != JsonValueKind.Number || !declared.TryGetInt32(out int part) || part != number)
        )
        {
            return null;
        }
        string previousFormat = pieces[2].StartsWith('0')
            ? "D" + pieces[2].Length.ToString(CultureInfo.InvariantCulture)
            : "D2";
        string? previous =
            number == 1
                ? null
                : Path.Combine(
                    Path.GetDirectoryName(Path.GetFullPath(path))!,
                    $"{pieces[0]}.{pieces[1]}.{(number - 1).ToString(previousFormat, CultureInfo.InvariantCulture)}.{pieces[3]}"
                );
        return new(pieces[1], number, previous);
    }
}
