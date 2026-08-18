using SDL3;
using Avalonia.Threading;

namespace SrvSurvey.Desktop.Input;

public interface IControllerInputBackend
{
    Task RunAsync(
        string deviceId,
        Action<ControllerInputChange> onInputChanged,
        Action<ControllerBackendStatus> onStatusChanged,
        CancellationToken cancellationToken);
}

public sealed record ControllerInputChange(string Token, bool IsPressed);

public sealed record ControllerBackendStatus(
    bool IsConnected,
    string Message);

public sealed class SdlControllerInputBackend : IControllerInputBackend
{
    private const int TriggerThreshold = 30_000;
    private const int PollDelayMilliseconds = 12;
    private const int ReconnectDelayMilliseconds = 1_000;
    private const SDL.InitFlags InputSubsystems =
        SDL.InitFlags.Joystick | SDL.InitFlags.Gamepad;
    private static readonly SemaphoreSlim SdlLifecycleGate = new(1, 1);

    private static readonly (SDL.GamepadButton Button, string Token)[]
        StandardButtons =
        [
            (SDL.GamepadButton.South, "B1"),
            (SDL.GamepadButton.East, "B2"),
            (SDL.GamepadButton.West, "B3"),
            (SDL.GamepadButton.North, "B4"),
            (SDL.GamepadButton.LeftShoulder, "B5"),
            (SDL.GamepadButton.RightShoulder, "B6"),
            (SDL.GamepadButton.Back, "B7"),
            (SDL.GamepadButton.Start, "B8"),
            (SDL.GamepadButton.LeftStick, "B9"),
            (SDL.GamepadButton.RightStick, "B10"),
            (SDL.GamepadButton.Misc1, "B11"),
            (SDL.GamepadButton.RightPaddle1, "B12"),
            (SDL.GamepadButton.LeftPaddle1, "B13"),
            (SDL.GamepadButton.RightPaddle2, "B14"),
            (SDL.GamepadButton.LeftPaddle2, "B15"),
            (SDL.GamepadButton.Touchpad, "B16"),
            (SDL.GamepadButton.Misc2, "B17"),
            (SDL.GamepadButton.Misc3, "B18"),
            (SDL.GamepadButton.Misc4, "B19"),
            (SDL.GamepadButton.Misc5, "B20"),
            (SDL.GamepadButton.Misc6, "B21"),
        ];

    public async Task RunAsync(
        string deviceId,
        Action<ControllerInputChange> onInputChanged,
        Action<ControllerBackendStatus> onStatusChanged,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentNullException.ThrowIfNull(onInputChanged);
        ArgumentNullException.ThrowIfNull(onStatusChanged);

        var initialized = false;
        var lifecycleEntered = false;
        try
        {
            await SdlLifecycleGate.WaitAsync(cancellationToken);
            lifecycleEntered = true;
            initialized = await Dispatcher.UIThread.InvokeAsync(
                () => SDL.InitSubSystem(InputSubsystems));
            if (!initialized)
            {
                onStatusChanged(new ControllerBackendStatus(
                    IsConnected: false,
                    $"SDL controller input could not start: {GetError()}"));
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                SDL.UpdateJoysticks();
                var device = FindDevice(deviceId);
                if (device is null)
                {
                    onStatusChanged(new ControllerBackendStatus(
                        IsConnected: false,
                        "Waiting for the selected controller..."));
                    await Task.Delay(
                        ReconnectDelayMilliseconds,
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                using var controller = Open(device);
                if (!controller.IsOpen)
                {
                    onStatusChanged(new ControllerBackendStatus(
                        IsConnected: false,
                        $"Could not open {device.Name}: {GetError()}"));
                    await Task.Delay(
                        ReconnectDelayMilliseconds,
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                onStatusChanged(new ControllerBackendStatus(
                    IsConnected: true,
                    $"Controller input is active: {device.Name}."));
                await PollAsync(
                    controller,
                    onInputChanged,
                    cancellationToken).ConfigureAwait(false);
                onStatusChanged(new ControllerBackendStatus(
                    IsConnected: false,
                    $"{device.Name} disconnected; waiting for it to return..."));
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Stopping controller monitoring is an expected cancellation path.
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException
                or TypeInitializationException)
        {
            onStatusChanged(new ControllerBackendStatus(
                IsConnected: false,
                $"SDL controller input could not start: {exception.Message}"));
        }
        finally
        {
            try
            {
                if (initialized)
                {
                    await Dispatcher.UIThread.InvokeAsync(
                        () => SDL.QuitSubSystem(InputSubsystems));
                }
            }
            finally
            {
                if (lifecycleEntered)
                {
                    SdlLifecycleGate.Release();
                }
            }
        }
    }

    private static ControllerDeviceInfo? FindDevice(string deviceId)
    {
        var instanceIds = SDL.GetJoysticks(out _) ?? [];
        foreach (var instanceId in instanceIds)
        {
            var device = SdlControllerDeviceProvider.CreateDevice(instanceId);
            if (string.Equals(
                    device.Id,
                    deviceId,
                    StringComparison.Ordinal))
            {
                return device;
            }
        }

        return null;
    }

    private static OpenController Open(ControllerDeviceInfo device)
    {
        if (SDL.IsGamepad(device.InstanceId))
        {
            var gamepad = SDL.OpenGamepad(device.InstanceId);
            return new OpenController(
                device,
                gamepad == IntPtr.Zero
                    ? IntPtr.Zero
                    : SDL.GetGamepadJoystick(gamepad),
                gamepad);
        }

        return new OpenController(
            device,
            SDL.OpenJoystick(device.InstanceId),
            gamepad: IntPtr.Zero);
    }

    private static async Task PollAsync(
        OpenController controller,
        Action<ControllerInputChange> onInputChanged,
        CancellationToken cancellationToken)
    {
        HashSet<string> previous = [];
        while (!cancellationToken.IsCancellationRequested
            && SDL.JoystickConnected(controller.Joystick))
        {
            SDL.UpdateJoysticks();
            var current = controller.IsGamepad
                ? ReadGamepad(controller.Gamepad)
                : ReadJoystick(controller);
            foreach (var token in current.Except(previous))
            {
                onInputChanged(new ControllerInputChange(
                    token,
                    IsPressed: true));
            }

            foreach (var token in previous.Except(current))
            {
                onInputChanged(new ControllerInputChange(
                    token,
                    IsPressed: false));
            }

            previous = current;
            await Task.Delay(
                PollDelayMilliseconds,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static HashSet<string> ReadGamepad(IntPtr gamepad)
    {
        HashSet<string> pressed = [];
        foreach (var (button, token) in StandardButtons)
        {
            if (SDL.GetGamepadButton(gamepad, button))
            {
                pressed.Add(token);
            }
        }

        AddHat(
            pressed,
            SDL.GetGamepadButton(gamepad, SDL.GamepadButton.DPadUp),
            SDL.GetGamepadButton(gamepad, SDL.GamepadButton.DPadRight),
            SDL.GetGamepadButton(gamepad, SDL.GamepadButton.DPadDown),
            SDL.GetGamepadButton(gamepad, SDL.GamepadButton.DPadLeft));
        if (SDL.GetGamepadAxis(
                gamepad,
                SDL.GamepadAxis.LeftTrigger) >= TriggerThreshold)
        {
            pressed.Add("LT");
        }

        if (SDL.GetGamepadAxis(
                gamepad,
                SDL.GamepadAxis.RightTrigger) >= TriggerThreshold)
        {
            pressed.Add("RT");
        }

        return pressed;
    }

    private static HashSet<string> ReadJoystick(OpenController controller)
    {
        HashSet<string> pressed = [];
        var buttonCount = Math.Min(
            SDL.GetNumJoystickButtons(controller.Joystick),
            128);
        for (var index = 0; index < buttonCount; index++)
        {
            if (SDL.GetJoystickButton(controller.Joystick, index))
            {
                pressed.Add($"B{index + 1}");
            }
        }

        if (SDL.GetNumJoystickHats(controller.Joystick) > 0)
        {
            AddHat(pressed, SDL.GetJoystickHat(controller.Joystick, 0));
        }

        if (SDL.GetJoystickTypeForID(controller.Device.InstanceId)
                == SDL.JoystickType.Gamepad
            && SDL.GetNumJoystickAxes(controller.Joystick) > 2)
        {
            var triggerAxis = SDL.GetJoystickAxis(controller.Joystick, 2);
            if (triggerAxis <= -TriggerThreshold)
            {
                pressed.Add("LT");
            }
            else if (triggerAxis >= TriggerThreshold)
            {
                pressed.Add("RT");
            }
        }

        return pressed;
    }

    private static void AddHat(
        HashSet<string> pressed,
        bool up,
        bool right,
        bool down,
        bool left)
    {
        var token = (up, right, down, left) switch
        {
            (true, false, false, false) => "PovU",
            (true, true, false, false) => "PovUR",
            (false, true, false, false) => "PovR",
            (false, true, true, false) => "PovDR",
            (false, false, true, false) => "PovD",
            (false, false, true, true) => "PovDL",
            (false, false, false, true) => "PovL",
            (true, false, false, true) => "PovUL",
            _ => null,
        };
        if (token is not null)
        {
            pressed.Add(token);
        }
    }

    private static void AddHat(
        HashSet<string> pressed,
        SDL.JoystickHat direction)
    {
        var token = direction switch
        {
            SDL.JoystickHat.Up => "PovU",
            SDL.JoystickHat.RightUp => "PovUR",
            SDL.JoystickHat.Right => "PovR",
            SDL.JoystickHat.RightDown => "PovDR",
            SDL.JoystickHat.Down => "PovD",
            SDL.JoystickHat.LeftDown => "PovDL",
            SDL.JoystickHat.Left => "PovL",
            SDL.JoystickHat.LeftUp => "PovUL",
            _ => null,
        };
        if (token is not null)
        {
            pressed.Add(token);
        }
    }

    private static string GetError()
    {
        var error = SDL.GetError();
        return string.IsNullOrWhiteSpace(error)
            ? "No native error was reported."
            : error;
    }

    private sealed class OpenController(
        ControllerDeviceInfo device,
        IntPtr joystick,
        IntPtr gamepad) : IDisposable
    {
        public ControllerDeviceInfo Device { get; } = device;

        public IntPtr Joystick { get; } = joystick;

        public IntPtr Gamepad { get; } = gamepad;

        public bool IsGamepad => Gamepad != IntPtr.Zero;

        public bool IsOpen => Joystick != IntPtr.Zero;

        public void Dispose()
        {
            if (Gamepad != IntPtr.Zero)
            {
                SDL.CloseGamepad(Gamepad);
            }
            else if (Joystick != IntPtr.Zero)
            {
                SDL.CloseJoystick(Joystick);
            }
        }
    }
}
