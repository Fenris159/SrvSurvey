namespace SrvSurvey.Desktop.Input;

public sealed class ControllerChordTracker
{
    private readonly HashSet<string> pressed = new(
        StringComparer.Ordinal);
    private string? activeHat;
    private bool releasePending;

    public IReadOnlyCollection<string> Pressed => pressed;

    public string? UpdateButton(int zeroBasedIndex, bool isPressed)
    {
        if (zeroBasedIndex is < 0 or >= 128)
        {
            throw new ArgumentOutOfRangeException(nameof(zeroBasedIndex));
        }

        return UpdateToken($"B{zeroBasedIndex + 1}", isPressed);
    }

    public string? UpdateTrigger(string trigger, bool isPressed)
    {
        if (trigger is not "LT" and not "RT")
        {
            throw new ArgumentOutOfRangeException(nameof(trigger));
        }

        return UpdateToken(trigger, isPressed);
    }

    public string? UpdateHat(ControllerHatDirection direction)
    {
        var nextHat = direction == ControllerHatDirection.Centered
            ? null
            : $"Pov{GetHatSuffix(direction)}";
        if (string.Equals(activeHat, nextHat, StringComparison.Ordinal))
        {
            return null;
        }

        string? chord = null;
        if (activeHat is not null)
        {
            chord = UpdateToken(activeHat, isPressed: false);
        }

        activeHat = nextHat;
        if (nextHat is not null)
        {
            UpdateToken(nextHat, isPressed: true);
        }

        return chord;
    }

    public string? UpdateToken(string token, bool isPressed)
    {
        if (!InputChord.IsControllerToken(token))
        {
            throw new ArgumentException(
                "The token is not a controller input.",
                nameof(token));
        }

        if (isPressed)
        {
            pressed.Add(token);
            return null;
        }

        if (!pressed.Contains(token))
        {
            return null;
        }

        string? chord = null;
        if (!releasePending
            && InputChord.TryNormalize(
                string.Join(' ', pressed),
                out var normalized))
        {
            releasePending = true;
            chord = normalized;
        }

        pressed.Remove(token);
        if (pressed.Count == 0)
        {
            releasePending = false;
        }

        return chord;
    }

    public void Clear()
    {
        pressed.Clear();
        activeHat = null;
        releasePending = false;
    }

    private static string GetHatSuffix(ControllerHatDirection direction)
    {
        return direction switch
        {
            ControllerHatDirection.Up => "U",
            ControllerHatDirection.UpRight => "UR",
            ControllerHatDirection.Right => "R",
            ControllerHatDirection.DownRight => "DR",
            ControllerHatDirection.Down => "D",
            ControllerHatDirection.DownLeft => "DL",
            ControllerHatDirection.Left => "L",
            ControllerHatDirection.UpLeft => "UL",
            _ => throw new ArgumentOutOfRangeException(nameof(direction)),
        };
    }
}

public enum ControllerHatDirection
{
    Centered,
    Up,
    UpRight,
    Right,
    DownRight,
    Down,
    DownLeft,
    Left,
    UpLeft,
}
