using SharpHook.Data;

namespace SrvSurvey.Desktop.Input;

/// <summary>Names keys with the standard XKB keysyms shared by X11 servers and desktop shortcut portals.</summary>
internal static class KeysymNames
{
    /// <summary>Translates SharpHook key names into standard keysym names.</summary>
    public static string Get(KeyCode key) =>
        key switch
        {
            KeyCode.VcLeftAlt => "Alt_L",
            KeyCode.VcRightAlt => "Alt_R",
            KeyCode.VcLeftControl => "Control_L",
            KeyCode.VcRightControl => "Control_R",
            KeyCode.VcLeftShift => "Shift_L",
            KeyCode.VcRightShift => "Shift_R",
            KeyCode.VcLeftMeta => "Super_L",
            KeyCode.VcRightMeta => "Super_R",
            KeyCode.VcEnter => "Return",
            KeyCode.VcBackspace => "BackSpace",
            KeyCode.VcCapsLock => "Caps_Lock",
            KeyCode.VcNumLock => "Num_Lock",
            KeyCode.VcScrollLock => "Scroll_Lock",
            KeyCode.VcPageUp => "Prior",
            KeyCode.VcPageDown => "Next",
            KeyCode.VcPrintScreen => "Print",
            KeyCode.VcSpace => "space",
            KeyCode.VcMinus => "minus",
            KeyCode.VcEquals => "equal",
            KeyCode.VcBackQuote => "grave",
            KeyCode.VcOpenBracket => "bracketleft",
            KeyCode.VcCloseBracket => "bracketright",
            KeyCode.VcBackslash => "backslash",
            KeyCode.VcSemicolon => "semicolon",
            KeyCode.VcQuote => "apostrophe",
            KeyCode.VcComma => "comma",
            KeyCode.VcPeriod => "period",
            KeyCode.VcSlash => "slash",
            KeyCode.VcNumPadEnter => "KP_Enter",
            KeyCode.VcNumPadDivide => "KP_Divide",
            KeyCode.VcNumPadMultiply => "KP_Multiply",
            KeyCode.VcNumPadSubtract => "KP_Subtract",
            KeyCode.VcNumPadAdd => "KP_Add",
            KeyCode.VcNumPadDecimal => "KP_Decimal",
            KeyCode.VcNumPadSeparator => "KP_Separator",
            _ => DefaultName(key),
        };

    /// <summary>Handles letters, digits, function keys, navigation keys and numeric keypad digits.</summary>
    private static string DefaultName(KeyCode key)
    {
        string name = key.ToString()[2..];
        if (name.Length == 1)
        {
            return name.ToLowerInvariant();
        }
        return name.StartsWith("NumPad", StringComparison.Ordinal) ? "KP_" + name[6..] : name;
    }
}
