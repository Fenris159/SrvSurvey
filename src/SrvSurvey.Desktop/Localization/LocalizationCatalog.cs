using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace SrvSurvey.Desktop.Localization;

public static class LocalizationCatalog
{
    private const string ResourceName =
        "SrvSurvey.Desktop.Resources.legacy-localization.json";

    private static readonly IReadOnlyDictionary<string, string>
        EmptyTranslations = new Dictionary<string, string>();
    private static IReadOnlyDictionary<string, string> translations =
        EmptyTranslations;

    public static IReadOnlyList<LocalizationLanguage> Languages { get; } =
    [
        new("en", "English"),
        new("de", "Deutsch"),
        new("es", "Español"),
        new("fr", "Français"),
        new("pt-BR", "Português (Brasil)"),
        new("ru", "Русский"),
        new("zh-Hans", "简体中文"),
        new("ps", "Pseudo"),
    ];

    public static string CurrentLanguage { get; private set; } = "en";

    internal static int TranslationCount => translations.Count;

    public static void Initialize(string? language)
    {
        var normalized = NormalizeLanguage(language);
        CurrentLanguage = normalized;
        if (normalized == "en")
        {
            translations = EmptyTranslations;
            return;
        }

        var assembly = typeof(LocalizationCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded localization catalog {ResourceName} is missing.");
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty(normalized, out var languageMap)
            || languageMap.ValueKind != JsonValueKind.Object)
        {
            translations = EmptyTranslations;
            return;
        }

        translations = languageMap.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.GetString() ?? property.Name,
            StringComparer.Ordinal);
    }

    public static string Translate(string? source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return source ?? string.Empty;
        }

        return translations.GetValueOrDefault(source) ?? source;
    }

    public static string NormalizeLanguage(string? language)
    {
        var value = language?.Trim();
        return Languages.FirstOrDefault(option => string.Equals(
                option.Code,
                value,
                StringComparison.OrdinalIgnoreCase))
            ?.Code ?? "en";
    }

    public static void ApplyCulture(string? language)
    {
        var normalized = NormalizeLanguage(language);
        var culture = CultureInfo.CreateSpecificCulture(normalized);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}

public sealed record LocalizationLanguage(string Code, string DisplayName);
