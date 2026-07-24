using SharpHook.Data;

namespace SrvSurvey.Desktop.Input;

public static class KeyboardChordFormatter
{
    public static string? Format(KeyCode keyCode, EventMask eventMask)
    {
        var key = GetKeyName(keyCode);
        if (key is null)
        {
            return null;
        }

        var tokens = new List<string>(4);
        if (eventMask.HasAlt())
        {
            tokens.Add("ALT");
        }

        if (eventMask.HasCtrl())
        {
            tokens.Add("CTRL");
        }

        if (eventMask.HasShift())
        {
            tokens.Add("SHIFT");
        }

        tokens.Add(key);
        return string.Join(' ', tokens);
    }

    public static string? GetKeyName(KeyCode keyCode)
    {
        if (keyCode is KeyCode.VcLeftAlt
            or KeyCode.VcRightAlt
            or KeyCode.VcLeftControl
            or KeyCode.VcRightControl
            or KeyCode.VcLeftShift
            or KeyCode.VcRightShift
            or KeyCode.VcLeftMeta
            or KeyCode.VcRightMeta
            or KeyCode.VcUndefined)
        {
            return null;
        }

        var name = keyCode.ToString();
        if (!name.StartsWith("Vc", StringComparison.Ordinal))
        {
            return null;
        }

        var token = name[2..];
        if (token.Length == 1 && char.IsDigit(token[0]))
        {
            return $"D{token}";
        }

        return keyCode switch
        {
            KeyCode.VcMinus => "-",
            KeyCode.VcEquals => "+",
            KeyCode.VcBackspace => "Backspace",
            KeyCode.VcBackQuote => "Oemtilde",
            KeyCode.VcOpenBracket => "OemOpenBrackets",
            KeyCode.VcCloseBracket => "OemCloseBrackets",
            KeyCode.VcBackslash => "OemPipe",
            KeyCode.VcSemicolon => "OemSemicolon",
            KeyCode.VcQuote => "OemQuotes",
            KeyCode.VcComma => "Oemcomma",
            KeyCode.VcPeriod => "OemPeriod",
            KeyCode.VcSlash => "OemQuestion",
            KeyCode.VcNumPadDivide => "Divide",
            KeyCode.VcNumPadMultiply => "Multiply",
            KeyCode.VcNumPadSubtract => "Subtract",
            KeyCode.VcNumPadAdd => "Add",
            KeyCode.VcNumPadDecimal => "Decimal",
            KeyCode.VcNumPadSeparator => "Separator",
            _ when token.StartsWith("NumPad", StringComparison.Ordinal) => token,
            _ => token,
        };
    }
}
