using System.Diagnostics;
using SharpHook.Data;

namespace SrvSurvey.Desktop.Input;

/// <summary>Turns one display's raw key events into chord activations, repairing X11 repeats and omitted modifiers.</summary>
internal sealed class KeyPressTracker(KeyboardInputSource source, bool usesX11Events)
{
    private static readonly long PressStateMaxAgeTicks = 2 * Stopwatch.Frequency;
    private static readonly long ModifierStateMaxAgeTicks = 10 * Stopwatch.Frequency;
    private const ulong X11AutoRepeatEventGapMilliseconds = 5;
    private readonly Lock gate = new();
    private readonly Dictionary<KeyCode, KeyPressState> pressedKeys = [];

    private readonly record struct KeyPressState(long LastPressTimestamp, ulong? LastReleaseEventTime);

    /// <summary>Reports a non-repeated press, tracking only modifiers and keys pressed while SrvSurvey or Elite has focus.</summary>
    public void Press(
        IKeyboardActivationSink sink,
        KeyCode keyCode,
        EventMask mask,
        ulong eventTime,
        KeyboardFocusEvidence evidence
    )
    {
        KeyboardFocus focus = sink.SampleFocus(evidence);
        if (!IsModifierKey(keyCode) && !focus.AllowsShortcuts)
        {
            return;
        }

        string? chord;
        lock (gate)
        {
            bool repeated =
                pressedKeys.TryGetValue(keyCode, out KeyPressState previousPress)
                && IsRepeatedPress(previousPress, focus.Timestamp, eventTime);
            pressedKeys[keyCode] = new KeyPressState(focus.Timestamp, null);
            if (repeated)
            {
                return;
            }

            if (usesX11Events)
            {
                // XRecord can omit held modifiers from a non-modifier key's mask.
                mask |= GetObservedModifierMask(focus.Timestamp);
            }
            chord = KeyboardChordFormatter.Format(keyCode, mask);
        }

        if (chord is not null)
        {
            sink.Activate(new KeyboardActivation(source, chord, null, focus));
        }
    }

    /// <summary>Releases a key on this display without clearing another display's state.</summary>
    public void Release(KeyCode keyCode, ulong eventTime)
    {
        lock (gate)
        {
            if (usesX11Events && pressedKeys.TryGetValue(keyCode, out KeyPressState previousPress))
            {
                pressedKeys[keyCode] = previousPress with { LastReleaseEventTime = eventTime };
            }
            else
            {
                pressedKeys.Remove(keyCode);
            }
        }
    }

    /// <summary>Forgets held keys after a reset, listener restart, or game change.</summary>
    public void Clear()
    {
        lock (gate)
        {
            pressedKeys.Clear();
        }
    }

    /// <summary>Recognizes X11 release/press repeats and recently held keys.</summary>
    private bool IsRepeatedPress(KeyPressState previousPress, long timestamp, ulong eventTime)
    {
        // X11 reports a held key's repeat as a release and press with the same native event time.
        if (
            usesX11Events
            && previousPress.LastReleaseEventTime is ulong releaseEventTime
            && eventTime >= releaseEventTime
            && eventTime - releaseEventTime <= X11AutoRepeatEventGapMilliseconds
        )
        {
            return true;
        }

        return previousPress.LastReleaseEventTime is null
            && timestamp >= previousPress.LastPressTimestamp
            && timestamp - previousPress.LastPressTimestamp < PressStateMaxAgeTicks;
    }

    /// <summary>Reconstructs held modifiers when XRecord omits them from the event mask.</summary>
    private EventMask GetObservedModifierMask(long timestamp)
    {
        EventMask mask = EventMask.None;
        if (IsModifierHeld(KeyCode.VcLeftAlt, timestamp))
        {
            mask |= EventMask.LeftAlt;
        }

        if (IsModifierHeld(KeyCode.VcRightAlt, timestamp))
        {
            mask |= EventMask.RightAlt;
        }

        if (IsModifierHeld(KeyCode.VcLeftControl, timestamp))
        {
            mask |= EventMask.LeftCtrl;
        }

        if (IsModifierHeld(KeyCode.VcRightControl, timestamp))
        {
            mask |= EventMask.RightCtrl;
        }

        if (IsModifierHeld(KeyCode.VcLeftShift, timestamp))
        {
            mask |= EventMask.LeftShift;
        }

        if (IsModifierHeld(KeyCode.VcRightShift, timestamp))
        {
            mask |= EventMask.RightShift;
        }

        return mask;
    }

    /// <summary>Checks modifier state within this display's bounded observation window.</summary>
    private bool IsModifierHeld(KeyCode keyCode, long timestamp)
    {
        return pressedKeys.TryGetValue(keyCode, out KeyPressState state)
            && state.LastReleaseEventTime is null
            && timestamp >= state.LastPressTimestamp
            && timestamp - state.LastPressTimestamp < ModifierStateMaxAgeTicks;
    }

    private static bool IsModifierKey(KeyCode keyCode)
    {
        return keyCode
            is KeyCode.VcLeftAlt
                or KeyCode.VcRightAlt
                or KeyCode.VcLeftControl
                or KeyCode.VcRightControl
                or KeyCode.VcLeftShift
                or KeyCode.VcRightShift;
    }
}
