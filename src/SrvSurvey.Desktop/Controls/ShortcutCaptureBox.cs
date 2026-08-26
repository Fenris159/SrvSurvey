using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    private readonly ControllerChordTracker controllerTracker = new();
    private Key? heldKey;
    private string originalChord = string.Empty;
    private string candidateChord = string.Empty;
    private TopLevel? captureTopLevel;
    private bool capturing;
    private bool hasPendingCommit;

    public ShortcutCaptureBox()
    {
        IsReadOnly = true;
        GotFocus += (_, _) =>
        {
            AttachOutsidePointerHandler();
            BeginCapture();
        };
        LostFocus += (_, _) => EndFocusInteraction();
        DetachedFromVisualTree += (_, _) => EndFocusInteraction();
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
        hasPendingCommit = false;
        candidateChord = string.Empty;
        heldModifiers.Clear();
        heldKey = null;
        controllerTracker.Clear();
        ShortcutCaptureSession.Begin(this, OnControllerInput);
        AttachOutsidePointerHandler();
        UpdateDisplay();
    }

    internal bool CaptureKeyDown(Key key)
    {
        if (!capturing)
        {
            if (key == Key.Escape)
            {
                if (hasPendingCommit)
                {
                    RevertPendingCommit();
                }
                else
                {
                    ReleaseFocus();
                }

                return true;
            }

            return false;
        }

        if (key == Key.Escape)
        {
            CancelCapture();
            return true;
        }

        if (key is Key.Back or Key.Delete)
        {
            Commit(string.Empty);
            return true;
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
        return true;
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

    internal void CaptureControllerInput(ControllerInputChange change)
    {
        if (!capturing)
        {
            return;
        }

        var chord = controllerTracker.UpdateToken(
            change.Token,
            change.IsPressed);
        if (chord is not null)
        {
            Commit(chord);
            return;
        }

        if (controllerTracker.Pressed.Count > 0
            && InputChord.TryNormalize(
                string.Join(' ', controllerTracker.Pressed),
                out var candidate))
        {
            candidateChord = candidate;
            UpdateDisplay();
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
        eventArgs.Handled = CaptureKeyDown(eventArgs.Key);
    }

    private void OnCaptureKeyUp(object? sender, KeyEventArgs eventArgs)
    {
        var wasCapturing = capturing;
        CaptureKeyUp(eventArgs.Key);
        eventArgs.Handled = wasCapturing;
    }

    private void Commit(string chord)
    {
        EndCapture();
        SetCurrentValue(ChordProperty, chord);
        Text = chord;
        hasPendingCommit = true;
        AttachOutsidePointerHandler();
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
        controllerTracker.Clear();
        ShortcutCaptureSession.End(this);
    }

    private void AttachOutsidePointerHandler()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (ReferenceEquals(captureTopLevel, topLevel))
        {
            return;
        }

        DetachOutsidePointerHandler();
        captureTopLevel = topLevel;
        captureTopLevel?.AddHandler(
            PointerPressedEvent,
            OnTopLevelPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void DetachOutsidePointerHandler()
    {
        captureTopLevel?.RemoveHandler(
            PointerPressedEvent,
            OnTopLevelPointerPressed);
        captureTopLevel = null;
    }

    private void OnTopLevelPointerPressed(
        object? sender,
        PointerPressedEventArgs eventArgs)
    {
        if (eventArgs.Source is Visual source
                && (ReferenceEquals(source, this)
                    || this.IsVisualAncestorOf(source)))
        {
            return;
        }

        if (capturing)
        {
            CancelCapture();
        }
        else
        {
            hasPendingCommit = false;
        }

        ReleaseFocus();
    }

    private void RevertPendingCommit()
    {
        SetCurrentValue(ChordProperty, originalChord);
        Text = originalChord;
        hasPendingCommit = false;
    }

    private void EndFocusInteraction()
    {
        if (capturing)
        {
            CancelCapture();
        }

        hasPendingCommit = false;
        DetachOutsidePointerHandler();
        Text = Chord;
    }

    private void ReleaseFocus()
    {
        TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null);
    }

    private void OnControllerInput(ControllerInputChange change)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            CaptureControllerInput(change);
            return;
        }

        Dispatcher.UIThread.Post(() => CaptureControllerInput(change));
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
            tokens.Add("Press keys or controller buttons");
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
