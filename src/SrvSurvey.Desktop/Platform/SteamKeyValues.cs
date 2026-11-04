using System.Text;

namespace SrvSurvey.Desktop.Platform;

/// <summary>Reads the bounded text KeyValues files used by Steam's local account and screenshot indexes.</summary>
internal sealed class SteamKeyValues
{
    internal string? Value { get; private init; }
    internal Dictionary<string, SteamKeyValues> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal string? GetValue(string name) => Children.GetValueOrDefault(name)?.Value;

    internal static SteamKeyValues Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 16 * 1024 * 1024)
        {
            throw new InvalidDataException("The Steam index is too large.");
        }
        using var reader = new StreamReader(stream);
        var parser = new Parser(reader.ReadToEnd());
        return parser.ReadObject(0);
    }

    private sealed class Parser(string text)
    {
        private int position;

        internal SteamKeyValues ReadObject(int depth)
        {
            if (depth > 16)
            {
                throw new InvalidDataException("The Steam index is nested too deeply.");
            }
            var result = new SteamKeyValues();
            while (NextToken() is { } key)
            {
                if (key is { Structural: true, Text: "}" } && depth > 0)
                {
                    return result;
                }
                Token value = NextToken() ?? throw new InvalidDataException("The Steam index is incomplete.");
                if (key.Structural || value is { Structural: true, Text: "}" })
                {
                    throw new InvalidDataException("The Steam index contains an unexpected brace.");
                }
                SteamKeyValues child = value is { Structural: true, Text: "{" }
                    ? ReadObject(depth + 1)
                    : new() { Value = value.Text };
                if (!result.Children.TryAdd(key.Text, child))
                {
                    throw new InvalidDataException("The Steam index contains a duplicate key.");
                }
            }
            return depth == 0 ? result : throw new InvalidDataException("The Steam index is incomplete.");
        }

        private readonly record struct Token(string Text, bool Structural = false);

        private Token? NextToken()
        {
            SkipTrivia();
            if (position == text.Length)
            {
                return null;
            }
            char first = text[position++];
            if (first is '{' or '}')
            {
                return new(first.ToString(), Structural: true);
            }
            return new(first == '"' ? ReadQuotedToken() : ReadUnquotedToken());
        }

        private void SkipTrivia()
        {
            while (position < text.Length)
            {
                if (char.IsWhiteSpace(text[position]))
                {
                    position++;
                }
                else if (text[position] == '/' && position + 1 < text.Length && text[position + 1] == '/')
                {
                    while (position < text.Length && text[position] != '\n')
                    {
                        position++;
                    }
                }
                else
                {
                    break;
                }
            }
        }

        private string ReadUnquotedToken()
        {
            int start = position - 1;
            while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not ('{' or '}'))
            {
                position++;
            }
            return text[start..position];
        }

        private string ReadQuotedToken()
        {
            var token = new StringBuilder();
            while (position < text.Length)
            {
                char next = text[position++];
                if (next == '"')
                {
                    return token.ToString();
                }
                if (next == '\\' && position < text.Length && text[position] is '\\' or '"')
                {
                    next = text[position++];
                }
                token.Append(next);
            }
            throw new InvalidDataException("The Steam index contains an unterminated string.");
        }
    }
}
