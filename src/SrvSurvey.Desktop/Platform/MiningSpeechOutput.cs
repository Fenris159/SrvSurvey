using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SrvSurvey.Desktop.Platform;

public interface IMiningSpeechOutput : IDisposable
{
    bool IsSupported { get; }
    string ProviderName { get; }
    Task<IReadOnlyList<string>> GetVoicesAsync();
    void Speak(string text, string voice, int volume, int rate);
}

/// <summary>Optional local Windows SAPI output. One background STA owns the speaker and a bounded speech queue.</summary>
public sealed class MiningSpeechOutput : IMiningSpeechOutput
{
    private const string SpeechDispatcherExecutable = "spd-say";
    private readonly BlockingCollection<Action<object>> queue = new(10);
    private readonly Lock sync = new();
    private Thread? worker;
    private volatile bool disposed;
    private static string? LinuxBackend =>
        FindOnPath(SpeechDispatcherExecutable) ?? FindOnPath("espeak-ng") ?? FindOnPath("espeak");
    public static bool IsSupported => OperatingSystem.IsWindows() || LinuxBackend is not null;
    bool IMiningSpeechOutput.IsSupported => IsSupported;
    public string ProviderName =>
        OperatingSystem.IsWindows()
            ? "Windows Speech API"
            : Path.GetFileName(LinuxBackend) switch
            {
                SpeechDispatcherExecutable => "Speech Dispatcher",
                "espeak-ng" => "eSpeak NG",
                "espeak" => "eSpeak",
                _ => "local speech",
            };

    public Task<IReadOnlyList<string>> GetVoicesAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return GetLinuxVoicesAsync();
        }

        var result = new TaskCompletionSource<IReadOnlyList<string>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        if (
            !Enqueue(speaker =>
            {
                try
                {
                    dynamic voices = ((dynamic)speaker).GetVoices();
                    var names = new List<string>();
                    for (int index = 0; index < voices.Count; index++)
                    {
                        names.Add((string)voices.Item(index).GetDescription());
                    }

                    result.TrySetResult(names);
                }
                catch (Exception ex)
                {
                    result.TrySetException(ex);
                }
            })
        )
        {
            result.TrySetResult([]);
        }

        return result.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    public void Speak(string text, string voice, int volume, int rate)
    {
        if (!OperatingSystem.IsWindows())
        {
            SpeakOnLinux(text, voice, volume, rate);
            return;
        }

        Enqueue(speaker =>
        {
            dynamic sapi = speaker;
            sapi.Volume = Math.Clamp(volume, 0, 100);
            sapi.Rate = Math.Clamp(rate, -10, 10);
            if (!string.IsNullOrEmpty(voice))
            {
                dynamic voices = sapi.GetVoices();
                for (int index = 0; index < voices.Count; index++)
                {
                    if ((string)voices.Item(index).GetDescription() == voice)
                    {
                        sapi.Voice = voices.Item(index);
                        break;
                    }
                }
            }
            sapi.Speak(text, 0);
        });
    }

    private Task<IReadOnlyList<string>> GetLinuxVoicesAsync()
    {
        if (disposed || LinuxBackend is not { } backend)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        if (Path.GetFileName(backend) == SpeechDispatcherExecutable)
        {
            return Task.FromResult<IReadOnlyList<string>>([
                "System default",
                "Female 1",
                "Female 2",
                "Female 3",
                "Male 1",
                "Male 2",
                "Male 3",
                "Child",
            ]);
        }

        return Task.Run<IReadOnlyList<string>>(() =>
        {
            using var process = Process.Start(
                new ProcessStartInfo(backend, "--voices")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            );
            if (process is null)
            {
                return [];
            }

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return ParseEspeakVoices(output);
        });
    }

    internal static IReadOnlyList<string> ParseEspeakVoices(string output) =>
        output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Skip(1)
            .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length >= 4)
            .Select(parts => parts[3])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Prepend("System default")
            .ToArray();

    private void SpeakOnLinux(string text, string voice, int volume, int rate)
    {
        if (disposed || string.IsNullOrWhiteSpace(text) || LinuxBackend is not { } backend)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                ProcessStartInfo start = CreateLinuxStartInfo(backend, text, voice, volume, rate);
                using var process = Process.Start(start);
                if (process is null)
                {
                    return;
                }
                if (start.RedirectStandardInput)
                {
                    process.StandardInput.WriteLine(text);
                    process.StandardInput.Close();
                }
                process.WaitForExit(10000);
            }
            catch (Exception ex)
                when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
            { /* Speech failure must not interrupt journal processing. */
            }
        });
    }

    internal static ProcessStartInfo CreateLinuxStartInfo(
        string backend,
        string text,
        string voice,
        int volume,
        int rate
    )
    {
        var start = new ProcessStartInfo(backend)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (Path.GetFileName(backend) == SpeechDispatcherExecutable)
        {
            ConfigureSpeechDispatcher(start, text, voice, volume, rate);
        }
        else
        {
            ConfigureEspeak(start, voice, volume, rate);
        }

        return start;
    }

    private static void ConfigureSpeechDispatcher(
        ProcessStartInfo start,
        string text,
        string voice,
        int volume,
        int rate
    )
    {
        start.ArgumentList.Add("--wait");
        AddNumericArgument(start, "--rate", Math.Clamp(rate, -10, 10) * 10);
        AddNumericArgument(start, "--volume", Math.Clamp(volume, 0, 100) * 2 - 100);
        if (SpeechDispatcherVoiceType(voice) is { } voiceType)
        {
            start.ArgumentList.Add("--voice-type");
            start.ArgumentList.Add(voiceType);
        }
        start.ArgumentList.Add(text);
    }

    private static void ConfigureEspeak(ProcessStartInfo start, string voice, int volume, int rate)
    {
        start.RedirectStandardInput = true;
        start.ArgumentList.Add("--stdin");
        AddNumericArgument(start, "-a", Math.Clamp(volume, 0, 100) * 2);
        AddNumericArgument(start, "-s", 175 + Math.Clamp(rate, -10, 10) * 12);
        if (!string.IsNullOrWhiteSpace(voice) && voice != "System default")
        {
            start.ArgumentList.Add("-v");
            start.ArgumentList.Add(voice);
        }
    }

    private static void AddNumericArgument(ProcessStartInfo start, string name, int value)
    {
        start.ArgumentList.Add(name);
        start.ArgumentList.Add(value.ToString(global::System.Globalization.CultureInfo.InvariantCulture));
    }

    internal static string? SpeechDispatcherVoiceType(string voice) =>
        voice.ToLowerInvariant() switch
        {
            "female 1" => "female1",
            "female 2" => "female2",
            "female 3" => "female3",
            "male 1" => "male1",
            "male 2" => "male2",
            "male 3" => "male3",
            "child" => "child_male",
            _ => null,
        };

    internal static string? FindOnPath(string executable, string? searchPath = null)
    {
        foreach (
            string directory in (searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? "").Split(
                Path.PathSeparator
            )
        )
        {
            if (directory.Length == 0)
            {
                continue;
            }

            string candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private bool Enqueue(Action<object> action)
    {
        lock (sync)
        {
            if (disposed || !OperatingSystem.IsWindows())
            {
                return false;
            }

            if (worker is null)
            {
                worker = new Thread(() =>
                {
                    if (OperatingSystem.IsWindows())
                    {
                        Run();
                    }
                })
                {
                    IsBackground = true,
                    Name = "Mining announcements",
                };
                worker.SetApartmentState(ApartmentState.STA);
                worker.Start();
            }
            return queue.TryAdd(action);
        }
    }

    [SupportedOSPlatform("windows")]
    private void Run()
    {
        object? speaker = null;
        try
        {
            var type = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (type is null)
            {
                return;
            }

            speaker = Activator.CreateInstance(type);
            if (speaker is null)
            {
                return;
            }

            foreach (Action<object> action in queue.GetConsumingEnumerable())
            {
                if (disposed)
                {
                    break;
                }

                try
                {
                    action(speaker);
                }
                catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                { /* A failed voice must not interrupt journal processing. */
                }
            }
        }
        catch (Exception ex) when (ex is COMException or System.Reflection.TargetInvocationException)
        { /* Voice enumeration reports unavailability through its bounded timeout. */
        }
        finally
        {
            try
            {
                if (speaker is not null && Marshal.IsComObject(speaker))
                {
                    Marshal.FinalReleaseComObject(speaker);
                }
            }
            finally
            {
                lock (sync)
                {
                    disposed = true;
                    queue.Dispose();
                }
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            queue.CompleteAdding();
            if (worker is null)
            {
                queue.Dispose();
            }
        }
    }
}
