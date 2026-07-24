using System.Text.RegularExpressions;

namespace SrvSurvey.Desktop.Input;

public static partial class InputChord
{
    private static readonly string[] ModifierOrder = ["ALT", "CTRL", "SHIFT"];

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var tokens = value.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0
            || tokens.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != tokens.Length)
        {
            return false;
        }

        if (tokens.All(IsControllerToken))
        {
            normalized = string.Join(
                ' ',
                tokens.Select(NormalizeControllerToken)
                    .Order(StringComparer.Ordinal));
            return true;
        }

        var keyTokens = tokens
            .Where(token => !IsModifier(token))
            .ToArray();
        if (keyTokens.Length != 1
            || IsControllerToken(keyTokens[0])
            || ControllerLikePattern().IsMatch(keyTokens[0]))
        {
            return false;
        }

        var modifiers = ModifierOrder.Where(modifier => tokens.Contains(
            modifier,
            StringComparer.OrdinalIgnoreCase));
        normalized = string.Join(
            ' ',
            modifiers.Append(NormalizeKeyboardToken(keyTokens[0])));
        return true;
    }

    public static bool IsControllerToken(string token)
    {
        return ControllerButtonPattern().IsMatch(token)
            || ControllerPovPattern().IsMatch(token)
            || token.Equals("LT", StringComparison.OrdinalIgnoreCase)
            || token.Equals("RT", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsModifier(string token)
    {
        return ModifierOrder.Contains(token, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeControllerToken(string token)
    {
        if (token.StartsWith("B", StringComparison.OrdinalIgnoreCase))
        {
            return $"B{int.Parse(token[1..])}";
        }

        if (token.StartsWith("Pov", StringComparison.OrdinalIgnoreCase))
        {
            return "Pov" + token[3..].ToUpperInvariant();
        }

        return token.ToUpperInvariant();
    }

    private static string NormalizeKeyboardToken(string token)
    {
        if (token.Length == 1)
        {
            return token.ToUpperInvariant();
        }

        if (FunctionKeyPattern().IsMatch(token)
            || DigitKeyPattern().IsMatch(token))
        {
            return token.ToUpperInvariant();
        }

        return token.Equals("Backspace", StringComparison.OrdinalIgnoreCase)
            ? "Backspace"
            : token;
    }

    [GeneratedRegex("^B(?:[1-9]|[1-9][0-9]|1[01][0-9]|12[0-8])$", RegexOptions.IgnoreCase)]
    private static partial Regex ControllerButtonPattern();

    [GeneratedRegex("^Pov(?:U|UR|R|DR|D|DL|L|UL|UP)$", RegexOptions.IgnoreCase)]
    private static partial Regex ControllerPovPattern();

    [GeneratedRegex("^F(?:[1-9]|1[0-9]|2[0-4])$", RegexOptions.IgnoreCase)]
    private static partial Regex FunctionKeyPattern();

    [GeneratedRegex("^D[0-9]$", RegexOptions.IgnoreCase)]
    private static partial Regex DigitKeyPattern();

    [GeneratedRegex("^(?:B[0-9]+|Pov.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ControllerLikePattern();
}
