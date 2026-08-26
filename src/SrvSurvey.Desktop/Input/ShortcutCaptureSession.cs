namespace SrvSurvey.Desktop.Input;

public static class ShortcutCaptureSession
{
    private static readonly object Sync = new();
    private static readonly List<CaptureTarget> controllerCaptures = [];
    private static int activeCaptures;
    private static long suppressUntilUtcTicks;

    public static bool IsActive => Volatile.Read(ref activeCaptures) > 0
        || DateTime.UtcNow.Ticks < Volatile.Read(ref suppressUntilUtcTicks);

    internal static void Begin(
        object owner,
        Action<ControllerInputChange> onControllerInput)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(onControllerInput);
        lock (Sync)
        {
            controllerCaptures.RemoveAll(target =>
                ReferenceEquals(target.Owner, owner));
            controllerCaptures.Add(new CaptureTarget(
                owner,
                onControllerInput));
            Volatile.Write(ref activeCaptures, controllerCaptures.Count);
        }
    }

    internal static void End(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (Sync)
        {
            controllerCaptures.RemoveAll(target =>
                ReferenceEquals(target.Owner, owner));
            Volatile.Write(ref activeCaptures, controllerCaptures.Count);
        }

        Volatile.Write(
            ref suppressUntilUtcTicks,
            DateTime.UtcNow.AddMilliseconds(250).Ticks);
    }

    internal static bool TryCapture(ControllerInputChange change)
    {
        Action<ControllerInputChange>? capture;
        lock (Sync)
        {
            capture = controllerCaptures.Count == 0
                ? null
                : controllerCaptures[^1].OnControllerInput;
        }

        if (capture is null)
        {
            return false;
        }

        capture(change);
        return true;
    }

    private sealed record CaptureTarget(
        object Owner,
        Action<ControllerInputChange> OnControllerInput);
}
