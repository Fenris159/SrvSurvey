namespace SrvSurvey.Desktop.Input;

public static class ShortcutCaptureSession
{
    private static int activeCaptures;
    private static long suppressUntilUtcTicks;

    public static bool IsActive => Volatile.Read(ref activeCaptures) > 0
        || DateTime.UtcNow.Ticks < Volatile.Read(ref suppressUntilUtcTicks);

    internal static void Begin()
    {
        Interlocked.Increment(ref activeCaptures);
    }

    internal static void End()
    {
        Interlocked.Decrement(ref activeCaptures);
        Volatile.Write(
            ref suppressUntilUtcTicks,
            DateTime.UtcNow.AddMilliseconds(250).Ticks);
    }
}
