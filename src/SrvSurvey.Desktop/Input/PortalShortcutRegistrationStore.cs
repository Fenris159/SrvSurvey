using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Input;

/// <summary>Remembers requested bindings so unchanged, partially approved desktop choices are respected on restart.</summary>
internal sealed class PortalShortcutRegistrationStore
{
    private readonly UiSettingsDocumentStore? document;
    private string? fingerprint;

    /// <summary>Loads optional persistent registration metadata without storing or granting portal permissions.</summary>
    public PortalShortcutRegistrationStore(string? path = null)
    {
        if (path is not null)
        {
            document = new UiSettingsDocumentStore(path);
            if (document.Load()["Bindings"] is JsonValue value && value.TryGetValue<string>(out string? saved))
            {
                fingerprint = saved;
            }
        }
    }

    public bool HasRegistration => fingerprint is not null;

    /// <summary>Compares normalized IDs and triggers independently of ordering or translated descriptions.</summary>
    public bool Matches(IReadOnlyList<PortalShortcutBinding> bindings) => fingerprint == GetFingerprint(bindings);

    /// <summary>Records a completed request, preserving desktop refusals without disabling a working live session.</summary>
    public void Save(IReadOnlyList<PortalShortcutBinding> bindings)
    {
        fingerprint = GetFingerprint(bindings);
        try
        {
            document?.Update(root => root["Bindings"] = fingerprint);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.TraceInformation("Shortcut registration could not be remembered: {0}", exception.Message);
        }
    }

    /// <summary>Produces stable metadata from the application's requested shortcut IDs and key triggers.</summary>
    private static string GetFingerprint(IReadOnlyList<PortalShortcutBinding> bindings) =>
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    string.Join(
                        '\n',
                        bindings
                            .OrderBy(binding => binding.Id, StringComparer.Ordinal)
                            .Select(binding => binding.Id + "=" + binding.Trigger)
                    )
                )
            )
        );
}
