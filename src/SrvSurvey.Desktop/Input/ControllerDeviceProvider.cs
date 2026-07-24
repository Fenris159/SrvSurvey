using SDL3;

namespace SrvSurvey.Desktop.Input;

public interface IControllerDeviceProvider
{
    ControllerDeviceDiscoveryResult Discover();
}

public sealed record ControllerDeviceInfo(
    string Id,
    string Name,
    string Description,
    uint InstanceId);

public sealed record ControllerDeviceDiscoveryResult(
    IReadOnlyList<ControllerDeviceInfo> Devices,
    string? ErrorMessage)
{
    public bool IsAvailable => ErrorMessage is null;
}

public sealed class SdlControllerDeviceProvider : IControllerDeviceProvider
{
    private static readonly object SdlLifecycleLock = new();
    private const SDL.InitFlags InputSubsystems =
        SDL.InitFlags.Joystick | SDL.InitFlags.Gamepad;

    public ControllerDeviceDiscoveryResult Discover()
    {
        lock (SdlLifecycleLock)
        {
            var initialized = false;
            try
            {
                initialized = SDL.InitSubSystem(InputSubsystems);
                if (!initialized)
                {
                    return Failure(SDL.GetError());
                }

                var instanceIds = SDL.GetJoysticks(out _) ?? [];
                var devices = instanceIds
                    .Select(CreateDevice)
                    .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(device => device.Id, StringComparer.Ordinal)
                    .ToArray();
                return new ControllerDeviceDiscoveryResult(
                    MakeDuplicateNamesDistinct(devices),
                    ErrorMessage: null);
            }
            catch (Exception exception) when (
                exception is DllNotFoundException
                    or EntryPointNotFoundException
                    or BadImageFormatException
                    or TypeInitializationException)
            {
                return Failure(exception.Message);
            }
            finally
            {
                if (initialized)
                {
                    SDL.QuitSubSystem(InputSubsystems);
                }
            }
        }
    }

    public static string CreateStableId(
        string? path,
        ushort vendor,
        ushort product,
        ushort version,
        string name)
    {
        return !string.IsNullOrWhiteSpace(path)
            ? $"path:{path}"
            : $"usb:{vendor:x4}:{product:x4}:{version:x4}:{name}";
    }

    internal static ControllerDeviceInfo CreateDevice(uint instanceId)
    {
        var name = SDL.GetJoystickNameForID(instanceId)
            ?? $"Controller {instanceId}";
        var path = SDL.GetJoystickPathForID(instanceId);
        var vendor = SDL.GetJoystickVendorForID(instanceId);
        var product = SDL.GetJoystickProductForID(instanceId);
        var version = SDL.GetJoystickProductVersionForID(instanceId);
        var type = SDL.GetJoystickTypeForID(instanceId);
        var description = vendor == 0 && product == 0
            ? type.ToString()
            : $"{type} - USB {vendor:X4}:{product:X4}";
        return new ControllerDeviceInfo(
            CreateStableId(path, vendor, product, version, name),
            name,
            description,
            instanceId);
    }

    private static IReadOnlyList<ControllerDeviceInfo>
        MakeDuplicateNamesDistinct(IReadOnlyList<ControllerDeviceInfo> devices)
    {
        var totals = devices
            .GroupBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase);
        var ordinals = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);
        return devices.Select(device =>
        {
            if (totals[device.Name] == 1)
            {
                return device;
            }

            var ordinal = ordinals.GetValueOrDefault(device.Name) + 1;
            ordinals[device.Name] = ordinal;
            return device with { Name = $"{device.Name} ({ordinal})" };
        }).ToArray();
    }

    private static ControllerDeviceDiscoveryResult Failure(string message)
    {
        return new ControllerDeviceDiscoveryResult(
            [],
            string.IsNullOrWhiteSpace(message)
                ? "SDL controller discovery failed."
                : $"SDL controller discovery failed: {message}");
    }
}
