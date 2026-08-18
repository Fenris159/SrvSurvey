using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.Input;

namespace SrvSurvey.Desktop.Controls;

public sealed class ShortcutCaptureBox : TextBox
{
    public static readonly StyledProperty<string> ChordProperty =
        AvaloniaProperty.Register<ShortcutCaptureBox, string>(
            nameof(Chord),
            string.Empty,
            defaultBindingMode: BindingMode.TwoWay);

    private readonly HashSet<Key> heldModifiers = [];
    private Key? heldKey;
    private string originalChord = string.Empty;
    private string candidateChord = string.Empty;
    private bool capturing;

    public ShortcutCaptureBox()
    {
        IsReadOnly = true;
        GotFocus += (_, _) => BeginCapture();
        LostFocus += (_, _) => CancelCapture();
        DetachedFromVisualTree += (_, _) => CancelCapture();
        AddHandler(
            PointerPressedEvent,
            (_, _) => BeginCapture(),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            KeyDownEvent,
            OnCaptureKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            KeyUpEvent,
            OnCaptureKeyUp,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    public string Chord
    {
        get => GetValue(ChordProperty);
        set => SetValue(ChordProperty, value ?? string.Empty);
    }

    protected override Type StyleKeyOverride => typeof(TextBox);

    internal bool IsCapturing => capturing;

    internal void BeginCapture()
    {
        if (capturing)
        {
            return;
        }

        capturing = true;
        originalChord = Chord;
        candidateChord = string.Empty;
        heldModifiers.Clear();
        heldKey = null;
        ShortcutCaptureSession.Begin();
        UpdateDisplay();
    }

    internal void CaptureKeyDown(Key key)
    {
        if (!capturing)
        {
            BeginCapture();
        }

        if (key == Key.Escape)
        {
            CancelCapture();
            return;
        }

        if (key is Key.Back or Key.Delete)
        {
            Commit(string.Empty);
            return;
        }

        if (IsModifier(key))
        {
            heldModifiers.Add(key);
        }
        else if (GetKeyName(key) is not null)
        {
            heldKey = key;
        }

        candidateChord = FormatCandidate(includePrompt: false);
        UpdateDisplay();
    }

    internal void CaptureKeyUp(Key key)
    {
        if (!capturing)
        {
            return;
        }

        if (IsModifier(key))
        {
            heldModifiers.Remove(key);
        }
        else if (heldKey == key)
        {
            heldKey = null;
        }

        if (heldModifiers.Count == 0 && heldKey is null)
        {
            if (InputChord.TryNormalize(candidateChord, out var normalized))
            {
                Commit(normalized);
            }
            else
            {
                CancelCapture();
            }
        }
    }

    internal void CancelCapture()
    {
        if (!capturing)
        {
            Text = Chord;
            return;
        }

        EndCapture();
        Text = originalChord;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ChordProperty && !capturing)
        {
            Text = Chord;
        }
    }

    private void OnCaptureKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        CaptureKeyDown(eventArgs.Key);
        eventArgs.Handled = true;
    }

    private void OnCaptureKeyUp(object? sender, KeyEventArgs eventArgs)
    {
        CaptureKeyUp(eventArgs.Key);
        eventArgs.Handled = true;
    }

    private void Commit(string chord)
    {
        EndCapture();
        SetCurrentValue(ChordProperty, chord);
        Text = chord;
    }

    private void EndCapture()
    {
        if (!capturing)
        {
            return;
        }

        capturing = false;
        heldModifiers.Clear();
        heldKey = null;
        ShortcutCaptureSession.End();
    }

    private void UpdateDisplay()
    {
        Text = candidateChord.Length > 0
            ? candidateChord
            : FormatCandidate(includePrompt: true);
    }

    private string FormatCandidate(bool includePrompt)
    {
        var tokens = new List<string>(4);
        if (heldModifiers.Any(key => key is Key.LeftAlt or Key.RightAlt))
        {
            tokens.Add("ALT");
        }

        if (heldModifiers.Any(key => key is Key.LeftCtrl or Key.RightCtrl))
        {
            tokens.Add("CTRL");
        }

        if (heldModifiers.Any(key => key is Key.LeftShift or Key.RightShift))
        {
            tokens.Add("SHIFT");
        }

        if (heldKey is { } key && GetKeyName(key) is { } keyName)
        {
            tokens.Add(keyName);
        }
        else if (includePrompt)
        {
            tokens.Add("Press shortcut keys");
        }

        return string.Join(' ', tokens);
    }

    private static bool IsModifier(Key key)
    {
        return key is Key.LeftAlt or Key.RightAlt
            or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift;
    }

    private static string? GetKeyName(Key key)
    {
        var name = key.ToString();
        return key switch
        {
            Key.None or Key.Escape or Key.Back or Key.Delete => null,
            Key.OemMinus => "-",
            Key.OemPlus => "+",
            Key.OemComma => "Oemcomma",
            Key.OemPeriod => "OemPeriod",
            Key.OemQuestion => "OemQuestion",
            Key.Return => "Enter",
            _ when name.Length == 2
                && name[0] == 'D'
                && char.IsDigit(name[1]) => name,
            _ => name,
        };
    }
}
