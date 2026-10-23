using System.Security.Cryptography;
using System.Text;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Network;

/// <summary>Identifies journal evidence independently of folder aliases while distinguishing different physical records.</summary>
internal static class JournalUploadIdentity
{
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
}
