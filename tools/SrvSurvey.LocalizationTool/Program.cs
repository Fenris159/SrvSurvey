using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SrvSurvey.LocalizationTool;

if (args.Length == 3
    && string.Equals(args[0], "normalize-catalog", StringComparison.Ordinal))
{
    await NormalizeCatalogAsync(args[1], args[2]);
    return 0;
}

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "Usage: SrvSurvey.LocalizationTool <repository-root> <output-json>\n" +
        "   or: SrvSurvey.LocalizationTool normalize-catalog <input-json> <output-json>");
    return 2;
}

var repositoryRoot = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
var extractor = new LocalizationSourceExtractor(repositoryRoot);
var entries = extractor.Extract();
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
await File.WriteAllTextAsync(
    outputPath,
    JsonSerializer.Serialize(
        entries,
        new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
Console.WriteLine($"Extracted {entries.Count:N0} localizable strings to {outputPath}.");
return 0;

static async Task NormalizeCatalogAsync(string inputPath, string outputPath)
{
    await using var stream = File.OpenRead(Path.GetFullPath(inputPath));
    using var document = await JsonDocument.ParseAsync(stream);
    var result = new Dictionary<string, IReadOnlyList<LocalizationTranslationEntry>>(
        StringComparer.Ordinal);
    foreach (var language in document.RootElement.EnumerateObject())
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        if (language.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in language.Value.EnumerateObject())
            {
                entries[property.Name] = property.Value.GetString() ?? property.Name;
            }
        }
        else if (language.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in language.Value.EnumerateArray())
            {
                var source = element.GetProperty("source").GetString() ?? string.Empty;
                entries[source] = element.GetProperty("translation").GetString() ?? source;
            }
        }

        result[language.Name] = entries
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new LocalizationTranslationEntry(entry.Key, entry.Value))
            .ToArray();
    }

    await File.WriteAllTextAsync(
        Path.GetFullPath(outputPath),
        JsonSerializer.Serialize(
            result,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
        new UTF8Encoding(false));
}

namespace SrvSurvey.LocalizationTool
{
    internal sealed class LocalizationSourceExtractor(string repositoryRoot)
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

        private static readonly HashSet<string> LocalizableAttributes =
            new(StringComparer.Ordinal)
            {
            "Text",
            "Content",
            "Header",
            "Title",
            "PlaceholderText",
            "ToolTip.Tip",
            "AutomationProperties.Name",
            };

        private static readonly Regex Whitespace = new(
            @"\s+",
            RegexOptions.Compiled,
            RegexTimeout);
        private static readonly Regex HexColor = new(
            @"^#[0-9A-Fa-f]{3,8}$",
            RegexOptions.Compiled,
            RegexTimeout);
        private static readonly Regex FileOrUri = new(
            @"^(?:https?://|avares://|[A-Za-z]:\\|[/\\]|.*\.(?:json|png|jpe?g|gif|zip|tar|gz|dll|exe|cs|axaml|xaml|resx|xml|lua|csv|dat))$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase,
            RegexTimeout);
        private static readonly Regex CodeFragment = new(
            "(?:=>|\\b(?:namespace|public|private|internal|class|return|foreach|using)\\s|;\\s*\\}|\\{\\s*\\\")",
            RegexOptions.Compiled,
            RegexTimeout);

        public IReadOnlyList<LocalizationSourceEntry> Extract()
        {
            var entries = new Dictionary<string, LocalizationSourceEntry>(
                StringComparer.Ordinal);
            ExtractXaml(entries);
            ExtractCSharp(entries);
            return entries.Values
                .OrderBy(entry => entry.Text, StringComparer.Ordinal)
                .ToArray();
        }

        private void ExtractXaml(
            IDictionary<string, LocalizationSourceEntry> entries)
        {
            var root = Path.Combine(repositoryRoot, "src", "SrvSurvey.Desktop");
            foreach (var path in Directory.EnumerateFiles(
                         root,
                         "*.axaml",
                         SearchOption.AllDirectories))
            {
                var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
                foreach (var attribute in document.Descendants().Attributes())
                {
                    if (!LocalizableAttributes.Contains(attribute.Name.LocalName)
                        || attribute.Value.StartsWith('{')
                        || !TryNormalize(attribute.Value, out var text))
                    {
                        continue;
                    }

                    Add(entries, text, path, "xaml");
                }
            }
        }

        private void ExtractCSharp(
            IDictionary<string, LocalizationSourceEntry> entries)
        {
            var root = Path.Combine(repositoryRoot, "src", "SrvSurvey.Desktop");
            foreach (var path in Directory.EnumerateFiles(
                         root,
                         "*.cs",
                         SearchOption.AllDirectories)
                         .Where(path => !IsBuildOutput(path)))
            {
                var syntaxTree = CSharpSyntaxTree.ParseText(File.ReadAllText(path));
                var syntaxRoot = syntaxTree.GetRoot();

                foreach (var expression in syntaxRoot.DescendantNodes()
                             .OfType<ExpressionSyntax>())
                {
                    if (IsNestedStringExpression(expression)
                        || !TryCreateTemplate(expression, out var template)
                        || !TryNormalize(template, out var text))
                    {
                        continue;
                    }

                    Add(entries, text, path, "csharp");
                }
            }
        }

        private static bool IsBuildOutput(string path)
        {
            var separator = Path.DirectorySeparatorChar;
            return path.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase)
                || path.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNestedStringExpression(ExpressionSyntax expression)
        {
            return expression.Parent is BinaryExpressionSyntax parent
                       && parent.IsKind(SyntaxKind.AddExpression)
                || expression.Parent is InterpolationSyntax
                || expression.Ancestors().OfType<AttributeSyntax>().Any();
        }

        private static bool TryCreateTemplate(
            ExpressionSyntax expression,
            out string template)
        {
            var placeholderIndex = 0;
            var containsText = false;
            template = BuildTemplate(
                expression,
                ref placeholderIndex,
                ref containsText);
            return containsText;
        }

        private static string BuildTemplate(
            ExpressionSyntax expression,
            ref int placeholderIndex,
            ref bool containsText)
        {
            switch (expression)
            {
                case LiteralExpressionSyntax literal
                    when literal.IsKind(SyntaxKind.StringLiteralExpression)
                        || literal.IsKind(SyntaxKind.Utf8StringLiteralExpression):
                    var literalValue = literal.Token.ValueText;
                    if (literalValue.Contains('\n'))
                    {
                        return string.Empty;
                    }

                    containsText = true;
                    return literalValue;

                case InterpolatedStringExpressionSyntax interpolated:
                    containsText = true;
                    var builder = new StringBuilder();
                    foreach (var content in interpolated.Contents)
                    {
                        if (content is InterpolatedStringTextSyntax text)
                        {
                            builder.Append(text.TextToken.ValueText);
                        }
                        else if (content is InterpolationSyntax)
                        {
                            builder.Append('{').Append(placeholderIndex++).Append('}');
                        }
                    }

                    return builder.ToString();

                case BinaryExpressionSyntax binary
                    when binary.IsKind(SyntaxKind.AddExpression):
                    return BuildTemplate(
                            binary.Left,
                            ref placeholderIndex,
                            ref containsText)
                        + BuildTemplate(
                            binary.Right,
                            ref placeholderIndex,
                            ref containsText);

                case ParenthesizedExpressionSyntax parenthesized:
                    return BuildTemplate(
                        parenthesized.Expression,
                        ref placeholderIndex,
                        ref containsText);

                default:
                    return $"{{{placeholderIndex++}}}";
            }
        }

        private void Add(
            IDictionary<string, LocalizationSourceEntry> entries,
            string text,
            string path,
            string sourceKind)
        {
            if (entries.TryGetValue(text, out var existing))
            {
                if (!existing.SourceKinds.Contains(sourceKind, StringComparer.Ordinal))
                {
                    entries[text] = existing with
                    {
                        SourceKinds = existing.SourceKinds
                            .Append(sourceKind)
                            .OrderBy(value => value, StringComparer.Ordinal)
                            .ToArray(),
                    };
                }

                return;
            }

            entries[text] = new LocalizationSourceEntry(
                text,
                text.Contains("{0}", StringComparison.Ordinal),
                [sourceKind],
                Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'));
        }

        private static bool TryNormalize(string value, out string text)
        {
            text = Whitespace.Replace(value, " ").Trim();
            if (text.Length < 2
                || text.Length > 500
                || !text.Any(char.IsLetter)
                || HexColor.IsMatch(text)
                || FileOrUri.IsMatch(text)
                || CodeFragment.IsMatch(text)
                || text[0] is '$' or '#'
                || text.StartsWith('.')
                || text.StartsWith("--", StringComparison.Ordinal)
                || text.StartsWith('&')
                || text.StartsWith("*.", StringComparison.Ordinal)
                || text.StartsWith('"')
                || text.StartsWith(", \"", StringComparison.Ordinal)
                || Regex.IsMatch(
                    text,
                    @"^-[A-Za-z]",
                    RegexOptions.CultureInvariant,
                    RegexTimeout)
                || text.StartsWith("xmlns", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("x:", StringComparison.Ordinal)
                || (!text.Any(char.IsWhiteSpace) && text.Contains('_'))
                || text.Count(character => character is '{' or '}') % 2 != 0)
            {
                text = string.Empty;
                return false;
            }

            if (!text.Any(char.IsWhiteSpace)
                && text[0] is >= 'a' and <= 'z'
                && text.Any(char.IsUpper))
            {
                text = string.Empty;
                return false;
            }

            return true;
        }
    }

    internal sealed record LocalizationSourceEntry(
        string Text,
        bool IsFormat,
        IReadOnlyList<string> SourceKinds,
        string FirstSource);

    internal sealed record LocalizationTranslationEntry(
        string Source,
        string Translation);
}
