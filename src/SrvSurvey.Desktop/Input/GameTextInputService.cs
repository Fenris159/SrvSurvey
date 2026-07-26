using SharpHook;
using SharpHook.Data;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Input;

public interface IGameTextInputService
{
    bool IsAvailable { get; }

    string? UnavailableReason { get; }

    GameTextInputResult EnterText(string text);
}

public sealed record GameTextInputResult(bool Succeeded, string Status);

public static class GameTextInputService
{
    public static IGameTextInputService CreateCurrent()
    {
        var host = OverlayPlatformCapabilities.DetectCurrent().Host;
        if (host is OverlayHostKind.Windows or OverlayHostKind.LinuxX11)
        {
            return new SharpHookGameTextInputService();
        }

        return new UnavailableGameTextInputService(
            host == OverlayHostKind.LinuxWayland
                ? "Galaxy Map text entry requires X11; Wayland does not "
                    + "permit the required synthetic keyboard input."
                : "Galaxy Map text entry is unavailable on this platform.");
    }
}

public sealed class UnavailableGameTextInputService : IGameTextInputService
{
    public UnavailableGameTextInputService(string reason)
    {
        UnavailableReason = string.IsNullOrWhiteSpace(reason)
            ? "Game text entry is unavailable."
            : reason;
    }

    public bool IsAvailable => false;

    public string UnavailableReason { get; }

    public GameTextInputResult EnterText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new GameTextInputResult(false, UnavailableReason);
    }
}

internal sealed class SharpHookGameTextInputService : IGameTextInputService
{
    private readonly Func<string, UioHookResult> simulateText;

    public SharpHookGameTextInputService()
        : this(new EventSimulator().SimulateTextEntry)
    {
    }

    internal SharpHookGameTextInputService(
        Func<string, UioHookResult> simulateText)
    {
        this.simulateText = simulateText
            ?? throw new ArgumentNullException(nameof(simulateText));
    }

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public GameTextInputResult EnterText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        try
        {
            var result = simulateText(text);
            return result == UioHookResult.Success
                ? new GameTextInputResult(
                    true,
                    $"Entered {text} in the Galaxy Map.")
                : new GameTextInputResult(
                    false,
                    "Galaxy Map text entry failed with SharpHook result "
                        + result
                        + ".");
        }
        catch (Exception exception) when (
            exception is HookException
                or InvalidOperationException
                or NotSupportedException
                or DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException)
        {
            return new GameTextInputResult(
                false,
                "Galaxy Map text entry failed: " + exception.Message);
        }
    }
}
