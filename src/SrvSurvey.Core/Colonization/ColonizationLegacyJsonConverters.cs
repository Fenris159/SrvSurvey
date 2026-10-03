using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Colonization;

/// <summary>Accepts legacy string market IDs while keeping malformed values eligible for conservative name-based repair.</summary>
public sealed class ColonizationLegacyMarketIdConverter : JsonConverter<long?>
{
    /// <summary>Reads numeric or string IDs; unsupported legacy strings represent an unresolved ID.</summary>
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.Number when reader.TryGetInt64(out long number) => number,
            JsonTokenType.String
                when long.TryParse(
                    reader.GetString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long number
                ) => number,
            JsonTokenType.String => null,
            _ => throw new JsonException("A colonisation market ID must be a number or string."),
        };
    }

    /// <summary>Writes normalized IDs as numbers, preserving missing values as null.</summary>
    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value is { } number)
        {
            writer.WriteNumberValue(number);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

/// <summary>Distinguishes absent legacy status from explicit planned, building, completed, or demolished states.</summary>
public sealed class ColonizationLegacySiteStatusConverter : JsonConverter<ColonizationSystemSiteStatus?>
{
    /// <summary>Accepts absent status and rejects unknown explicit values rather than broadening repair eligibility.</summary>
    public override ColonizationSystemSiteStatus? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("A colonisation site status must be a string.");
        }
        string? text = reader.GetString()?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        if (Enum.TryParse(text, ignoreCase: true, out ColonizationSystemSiteStatus status) && Enum.IsDefined(status))
        {
            return status;
        }
        throw new JsonException("Unknown colonisation site status.");
    }

    /// <summary>Writes supported statuses using the API's lowercase names.</summary>
    public override void Write(
        Utf8JsonWriter writer,
        ColonizationSystemSiteStatus? value,
        JsonSerializerOptions options
    )
    {
        if (value is { } status)
        {
            writer.WriteStringValue(status.ToString().ToLowerInvariant());
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
