using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SrvSurvey.Core.Discord;

/// <summary>Records delivery metadata without message text, image paths, or webhook credentials.</summary>
public sealed class DiscordCarrierDiagnostics(Action<string>? log)
{
    public const string Loaded = "loaded";
    public const string EditorApplied = "editor-applied";
    public const string Saved = "saved";
    public const string SaveFailed = "save-failed";
    public const string Queued = "queued";
    public const string Skipped = "skipped";

    public void Write(string stage, string details, Guid? sendId = null)
    {
        try
        {
            string correlation = sendId is { } id ? $" send={id:N}" : "";
            log?.Invoke($"[Discord carrier]{correlation} {stage}; {details}");
        }
        catch (Exception)
        {
            // Optional diagnostics must never interrupt announcement delivery.
        }
    }

    public static string Fingerprint(string value) => Fingerprint(Encoding.UTF8.GetBytes(value));

    public static string Fingerprint(ReadOnlySpan<byte> value) => Convert.ToHexString(SHA256.HashData(value))[..12];

    public static string TemplateFingerprint(DiscordCarrierMessageTemplate template) =>
        Fingerprint(JsonSerializer.Serialize(template));

    public static string SettingsSummary(DiscordCarrierMessageSettings settings) =>
        $"scheduled-template={TemplateFingerprint(settings.ForEvent(CarrierAnnouncementKind.Scheduled))}; "
        + $"cancelled-template={TemplateFingerprint(settings.ForEvent(CarrierAnnouncementKind.Cancelled))}; "
        + $"arrived-template={TemplateFingerprint(settings.ForEvent(CarrierAnnouncementKind.Arrived))}; "
        + $"personal-image={(!string.IsNullOrWhiteSpace(settings.PersonalImagePath))}; personal-crop={settings.PersonalImageCrop is not null}; "
        + $"squadron-image={(!string.IsNullOrWhiteSpace(settings.SquadronImagePath))}; squadron-crop={settings.SquadronImageCrop is not null}";
}
