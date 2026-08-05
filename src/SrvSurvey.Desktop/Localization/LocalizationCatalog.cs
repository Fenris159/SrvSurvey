using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SrvSurvey.Desktop.Localization;

public static class LocalizationCatalog
{
    private const string LegacyResourceName =
        "SrvSurvey.Desktop.Resources.baseline-localization.json";
    private const string ApplicationResourceName =
        "SrvSurvey.Desktop.Resources.avalonia-localization.json";
    private const string SourceResourceName =
        "SrvSurvey.Desktop.Resources.avalonia-localization-source.json";

    private static readonly IReadOnlyDictionary<string, string>
        EmptyTranslations = new Dictionary<string, string>();
    private static readonly IReadOnlyDictionary<string, TranslationCandidate>
        EmptyNormalizedTranslations =
            new Dictionary<string, TranslationCandidate>();
    private static IReadOnlyDictionary<string, string> translations =
        EmptyTranslations;
    private static IReadOnlyDictionary<string, string> applicationTranslations =
        EmptyTranslations;
    private static IReadOnlyDictionary<string, TranslationCandidate>
        normalizedTranslations = EmptyNormalizedTranslations;
    private static IReadOnlyList<FormatTranslationPattern> formatPatterns = [];

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

    internal static int LegacyTranslationCount => translations.Count;

    internal static int ApplicationTranslationCount =>
        applicationTranslations.Count;

    internal static int SourceCount { get; } = LoadSourceCount();

    public static void Initialize(string? language)
    {
        var normalized = NormalizeLanguage(language);
        CurrentLanguage = normalized;
        if (normalized == "en")
        {
            translations = EmptyTranslations;
            applicationTranslations = EmptyTranslations;
            normalizedTranslations = EmptyNormalizedTranslations;
            formatPatterns = [];
            return;
        }

        translations = LoadLegacyTranslations(normalized);
        applicationTranslations = LoadApplicationTranslations(normalized);
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
        formatPatterns = applicationTranslations
            .Where(entry => FormatTranslationPattern.IsTemplate(entry.Key))
            .Select(entry => FormatTranslationPattern.Create(
                entry.Key,
                entry.Value))
            .OrderByDescending(pattern => pattern.Anchor.Length)
            .ThenByDescending(pattern => pattern.Source.Length)
            .ToArray();
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

        if (normalizedTranslations.TryGetValue(
                NormalizeSource(source),
                out var candidate))
        {
            return AdaptPresentation(source, candidate);
        }

        if (applicationTranslations.TryGetValue(source, out exact))
        {
            return exact;
        }

        foreach (var pattern in formatPatterns)
        {
            if (pattern.TryTranslate(source, out var formatted))
            {
                return formatted;
            }
        }

        return source;
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
        return value.EndsWith('…')
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

    private static IReadOnlyDictionary<string, string> LoadLegacyTranslations(
        string language)
    {
        using var document = OpenResource(LegacyResourceName);
        if (!document.RootElement.TryGetProperty(language, out var languageMap)
            || languageMap.ValueKind != JsonValueKind.Object)
        {
            return EmptyTranslations;
        }

        return languageMap.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.GetString() ?? property.Name,
            StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> LoadApplicationTranslations(
        string language)
    {
        using var document = OpenResource(ApplicationResourceName);
        if (!document.RootElement.TryGetProperty(language, out var languageMap)
            || languageMap.ValueKind != JsonValueKind.Array)
        {
            return EmptyTranslations;
        }

        return languageMap.EnumerateArray().ToDictionary(
            element => element.GetProperty("source").GetString()
                ?? string.Empty,
            element => element.GetProperty("translation").GetString()
                ?? element.GetProperty("source").GetString()
                ?? string.Empty,
            StringComparer.Ordinal);
    }

    private static int LoadSourceCount()
    {
        using var document = OpenResource(SourceResourceName);
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.GetArrayLength()
            : 0;
    }

    private static JsonDocument OpenResource(string resourceName)
    {
        var stream = typeof(LocalizationCatalog).Assembly
            .GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"The embedded localization catalog {resourceName} is missing.");
        try
        {
            return JsonDocument.Parse(stream);
        }
        finally
        {
            stream.Dispose();
        }
    }

    private sealed class FormatTranslationPattern
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

        private static readonly Regex Placeholder = new(
            @"\{(\d+)\}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            RegexTimeout);

        private readonly Regex matcher;
        private readonly string translation;

        private FormatTranslationPattern(
            string source,
            string translation,
            Regex matcher,
            string anchor)
        {
            Source = source;
            this.translation = translation;
            this.matcher = matcher;
            Anchor = anchor;
        }

        public string Source { get; }

        public string Anchor { get; }

        public static bool IsTemplate(string source)
        {
            return Placeholder.IsMatch(source);
        }

        public static FormatTranslationPattern Create(
            string source,
            string translation)
        {
            var expression = new StringBuilder("^");
            var literals = new List<string>();
            var offset = 0;
            foreach (Match placeholder in Placeholder.Matches(source))
            {
                var literal = source[offset..placeholder.Index];
                literals.Add(literal);
                expression.Append(Regex.Escape(literal));
                expression.Append("(?<arg")
                    .Append(placeholder.Groups[1].Value)
                    .Append(">.*?)");
                offset = placeholder.Index + placeholder.Length;
            }

            var tail = source[offset..];
            literals.Add(tail);
            expression.Append(Regex.Escape(tail)).Append('$');
            return new FormatTranslationPattern(
                source,
                translation,
                new Regex(
                    expression.ToString(),
                    RegexOptions.Compiled
                        | RegexOptions.CultureInvariant
                        | RegexOptions.Singleline,
                    RegexTimeout),
                literals.MaxBy(value => value.Length) ?? string.Empty);
        }

        public bool TryTranslate(string source, out string result)
        {
            if (Anchor.Length > 0
                && !source.Contains(Anchor, StringComparison.Ordinal))
            {
                result = string.Empty;
                return false;
            }

            var match = matcher.Match(source);
            if (!match.Success)
            {
                result = string.Empty;
                return false;
            }

            result = Placeholder.Replace(
                translation,
                placeholder => match.Groups[
                    $"arg{placeholder.Groups[1].Value}"].Value);
            return true;
        }
    }
}

public sealed record LocalizationLanguage(string Code, string DisplayName);
