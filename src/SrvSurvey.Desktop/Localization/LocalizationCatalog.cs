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
    private static readonly IReadOnlyDictionary<string, TranslationCandidate>
        EmptyNormalizedTranslations =
            new Dictionary<string, TranslationCandidate>();
    private static IReadOnlyDictionary<string, string> translations =
        EmptyTranslations;
    private static IReadOnlyDictionary<string, TranslationCandidate>
        normalizedTranslations = EmptyNormalizedTranslations;

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
            normalizedTranslations = EmptyNormalizedTranslations;
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
            normalizedTranslations = EmptyNormalizedTranslations;
            return;
        }

        translations = languageMap.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.GetString() ?? property.Name,
            StringComparer.Ordinal);
        normalizedTranslations = translations
            .Select(entry => new TranslationCandidate(entry.Key, entry.Value))
            .GroupBy(
                candidate => NormalizeSource(candidate.Source),
                StringComparer.Ordinal)
            .Where(group => group.Key.Length > 1 && group.Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group.Single(),
                StringComparer.Ordinal);
    }

    public static string Translate(string? source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return source ?? string.Empty;
        }

        if (translations.TryGetValue(source, out var exact))
        {
            return exact;
        }

        return normalizedTranslations.TryGetValue(
                NormalizeSource(source),
                out var candidate)
            ? AdaptPresentation(source, candidate)
            : source;
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

    private static string NormalizeSource(string source)
    {
        var value = source
            .Replace("&", string.Empty, StringComparison.Ordinal)
            .Replace("…", "...", StringComparison.Ordinal)
            .Trim();
        if (value.EndsWith("...", StringComparison.Ordinal))
        {
            value = value[..^3].TrimEnd();
        }

        if (value.EndsWith(':'))
        {
            value = value[..^1].TrimEnd();
        }

        return string.Join(
                ' ',
                value.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries
                        | StringSplitOptions.TrimEntries))
            .ToUpperInvariant();
    }

    private static string AdaptPresentation(
        string source,
        TranslationCandidate candidate)
    {
        var translated = source.Contains('&', StringComparison.Ordinal)
            ? candidate.Translation
            : candidate.Translation.Replace(
                "&",
                string.Empty,
                StringComparison.Ordinal);
        translated = AlignTrailingMark(
            source,
            candidate.Source,
            translated,
            ':');
        translated = AlignEllipsis(source, candidate.Source, translated);
        return IsAllUpper(source)
            ? translated.ToUpper(CultureInfo.CurrentUICulture)
            : translated;
    }

    private static string AlignTrailingMark(
        string source,
        string candidateSource,
        string translated,
        char mark)
    {
        var sourceHasMark = source.TrimEnd().EndsWith(mark);
        var candidateHasMark = candidateSource.TrimEnd().EndsWith(mark);
        if (sourceHasMark == candidateHasMark)
        {
            return translated;
        }

        return sourceHasMark
            ? translated.TrimEnd() + mark
            : translated.TrimEnd().TrimEnd(mark);
    }

    private static string AlignEllipsis(
        string source,
        string candidateSource,
        string translated)
    {
        var sourceHasEllipsis = HasEllipsis(source);
        var candidateHasEllipsis = HasEllipsis(candidateSource);
        if (sourceHasEllipsis == candidateHasEllipsis)
        {
            return translated;
        }

        if (sourceHasEllipsis)
        {
            return translated.TrimEnd() + (source.TrimEnd().EndsWith('…')
                ? "…"
                : "...");
        }

        var value = translated.TrimEnd();
        return value.EndsWith("…", StringComparison.Ordinal)
            ? value[..^1].TrimEnd()
            : value.EndsWith("...", StringComparison.Ordinal)
                ? value[..^3].TrimEnd()
                : value;
    }

    private static bool HasEllipsis(string value)
    {
        var trimmed = value.TrimEnd();
        return trimmed.EndsWith('…')
            || trimmed.EndsWith("...", StringComparison.Ordinal);
    }

    private static bool IsAllUpper(string value)
    {
        var letters = value.Where(char.IsLetter).ToArray();
        return letters.Length > 0 && letters.All(char.IsUpper);
    }

    private sealed record TranslationCandidate(
        string Source,
        string Translation);
}

public sealed record LocalizationLanguage(string Code, string DisplayName);
