using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Network;

/// <summary>Identifies journal evidence independently of folder aliases while distinguishing different physical records.</summary>
internal static class JournalUploadIdentity
{
    private static readonly HashSet<string> ObservationEvents = new(StringComparer.Ordinal)
    {
        "Scan",
        "ScanOrganic",
        "CodexEntry",
        "FSDJump",
        "CarrierJump",
        "Location",
        "Docked",
        "FSSDiscoveryScan",
        "FSSBodySignals",
        "FSSAllBodiesFound",
        "FSSSignalDiscovered",
        "SAASignalsFound",
        "ScanBaryCentre",
        "NavBeaconScan",
        "NavRoute",
        "ApproachSettlement",
    };

    /// <summary>Hashes original source evidence and a mapped-event discriminator without placing raw journals in the receipt ledger.</summary>
    public static string Capture(JournalEventEnvelope entry, string discriminator = "") =>
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    (entry.SourceRecordIdentity ?? Guid.NewGuid().ToString("N"))
                        + "\n"
                        + entry.RawJson
                        + "\n"
                        + discriminator
                )
            )
        );

    /// <summary>Identifies immutable observations without changing their values, array order, or event timestamps.</summary>
    public static string CaptureContent(JToken payload, string scope = "") =>
        "content:" + Convert.ToHexString(ContentDigest(payload, scope));

    public static Guid CaptureContentId(JToken payload, string scope) =>
        new(ContentDigest(payload, scope).AsSpan(0, 16));

    private static byte[] ContentDigest(JToken payload, string scope)
    {
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        using (var writer = new JsonTextWriter(text))
        {
            WriteCanonical(writer, payload);
        }
        return SHA256.HashData(Encoding.UTF8.GetBytes(scope + "\n" + text.ToString()));
    }

    /// <summary>Leaves personal transaction records distinct even when the game timestamps them in the same second.</summary>
    public static string? CaptureObservation(string eventName, string json, string scope = "") =>
        ObservationEvents.Contains(eventName) ? CaptureContent(JToken.Parse(json), scope) : null;

    private static void WriteCanonical(JsonWriter writer, JToken value)
    {
        if (value is JObject obj)
        {
            writer.WriteStartObject();
            foreach (JProperty property in obj.Properties().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (value is JArray array)
        {
            writer.WriteStartArray();
            foreach (JToken item in array)
            {
                WriteCanonical(writer, item);
            }
            writer.WriteEndArray();
        }
        else
        {
            value.WriteTo(writer);
        }
    }
}
