using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpHook.Data;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Input;

/// <summary>Translates a transient XRecord stream into the application's existing keyboard events.</summary>
internal sealed class X11KeyboardRecord : IX11KeyboardRecord
{
    private readonly List<UioHookEvent> events = [];
    private readonly Dictionary<byte, KeyCode> keyCodes = [];
    private readonly IX11KeyboardRecordApi native;
    private readonly X11KeyboardRecordCallback callback;
    private bool callbackFailed;
    private bool disposed;

    /// <summary>Owns the native stream and roots its callback for the full stream lifetime.</summary>
    private X11KeyboardRecord(IX11KeyboardRecordApi native)
    {
        this.native = native;
        callback = OnRecord;
    }

    /// <summary>Reports disconnected native streams or invalid callback packets.</summary>
    public bool HasFailed => callbackFailed || native.HasFailed;

    /// <summary>Opens the platform's keyboard-only XRecord stream.</summary>
    public static IX11KeyboardRecord? TryCreate(string displayName) =>
        TryCreate(displayName, X11Native.CreateKeyboardRecordApi());

    /// <summary>Opens and maps a stream, releasing partial native resources when setup fails.</summary>
    internal static IX11KeyboardRecord? TryCreate(string displayName, IX11KeyboardRecordApi native)
    {
        var record = new X11KeyboardRecord(native);
        try
        {
            if (!native.Open(displayName, record.callback))
            {
                record.Dispose();
                return null;
            }
            record.BuildKeyMap();
            return record;
        }
        catch (Exception exception)
            when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            record.Dispose();
            return null;
        }
    }

    /// <summary>Maps this server's keyboard layout onto existing shortcut key names.</summary>
    private void BuildKeyMap()
    {
        foreach (KeyCode key in Enum.GetValues<KeyCode>())
        {
            byte code = native.ResolveKeyCode(GetKeysymName(key));
            if (code != 0)
            {
                keyCodes.TryAdd(code, key);
            }
        }
    }

    /// <summary>Translates SharpHook key names into X11's standard keysym names.</summary>
    internal static string GetKeysymName(KeyCode key) =>
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
            _ => DefaultKeysymName(key),
        };

    /// <summary>Handles letters, digits, function keys, navigation keys and numeric keypad digits.</summary>
    private static string DefaultKeysymName(KeyCode key)
    {
        string name = key.ToString()[2..];
        if (name.Length == 1)
        {
            return name.ToLowerInvariant();
        }
        return name.StartsWith("NumPad", StringComparison.Ordinal) ? "KP_" + name[6..] : name;
    }

    /// <summary>Copies native keyboard packets into managed events without invoking application callbacks.</summary>
    private void OnRecord(nint closure, nint pointer)
    {
        try
        {
            X11KeyboardRecordData packet = Marshal.PtrToStructure<X11KeyboardRecordData>(pointer);
            if (packet.Category != 0 || packet.ClientSwapped != 0 || packet.Data == nint.Zero)
            {
                return;
            }
            for (int offset = 0; (nuint)(offset + 32) <= packet.DataLength * 4; offset += 32)
            {
                byte type = Marshal.ReadByte(packet.Data, offset);
                if (
                    type is not (2 or 3)
                    || !keyCodes.TryGetValue(Marshal.ReadByte(packet.Data, offset + 1), out KeyCode key)
                )
                {
                    continue;
                }
                events.Add(
                    new UioHookEvent
                    {
                        Type = type == 2 ? EventType.KeyPressed : EventType.KeyReleased,
                        Time = unchecked((uint)Marshal.ReadInt32(packet.Data, offset + 4)),
                        Keyboard = new KeyboardEventData { KeyCode = key },
                    }
                );
            }
        }
        catch (Exception exception)
        {
            callbackFailed = true;
            Trace.TraceWarning("Game keyboard recording failed: {0}", exception.Message);
        }
        finally
        {
            native.FreeData(pointer);
        }
    }

    /// <summary>Drains buffered key events without waiting for a future press.</summary>
    public IReadOnlyList<UioHookEvent> ReadEvents()
    {
        if (disposed || HasFailed)
        {
            return [];
        }
        native.ReadReplies();
        UioHookEvent[] result = events.ToArray();
        events.Clear();
        return result;
    }

    /// <summary>Releases the stream exactly once and drops pending events.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        native.Dispose();
        events.Clear();
    }
}

/// <summary>Provides native stream operations without exposing Xlib resources to shortcut routing.</summary>
internal interface IX11KeyboardRecordApi : IDisposable
{
    /// <summary>Reports a failed transient X server connection.</summary>
    bool HasFailed { get; }

    /// <summary>Opens and enables a keyboard-only recording stream.</summary>
    bool Open(string displayName, X11KeyboardRecordCallback callback);

    /// <summary>Resolves a standard keysym against the current server layout.</summary>
    byte ResolveKeyCode(string name);

    /// <summary>Drains immediately available native recording replies.</summary>
    void ReadReplies();

    /// <summary>Releases one native callback packet.</summary>
    void FreeData(nint pointer);
}

/// <summary>Receives a packet owned by the native recording library.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void X11KeyboardRecordCallback(nint closure, nint data);

/// <summary>Matches XRecordInterceptData's native ABI, whose data length is in four-byte units.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct X11KeyboardRecordData
{
    public nuint IdBase;
    public nuint ServerTime;
    public nuint ClientSequence;
    public int Category;
    public int ClientSwapped;
    public nint Data;
    public nuint DataLength;
}
