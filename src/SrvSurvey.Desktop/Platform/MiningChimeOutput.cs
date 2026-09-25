using SDL3;

namespace SrvSurvey.Desktop.Platform;

public interface IMiningChimeOutput : IDisposable
{
    void Play(string chime, int volume);
}

/// <summary>Short synthesized cues played through the app's cross-platform SDL runtime.</summary>
public sealed class MiningChimeOutput : IMiningChimeOutput
{
    private const int SampleRate = 24000;
    public const string DefaultChime = "Two-tone";
    public static IReadOnlyList<string> Chimes { get; } = [DefaultChime, "High-low", "Crystal"];
    private readonly Lock sync = new();
    private nint stream;
    private bool disposed;

    public void Play(string chime, int volume)
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            try
            {
                EnsureStream();
                if (stream == 0)
                {
                    return;
                }

                byte[] samples = CreateSamples(chime, volume);
                SDL.PutAudioStreamData(stream, samples, samples.Length);
                SDL.ResumeAudioStreamDevice(stream);
            }
            catch (Exception ex)
                when (ex is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException)
            { /* An unavailable audio backend must not interrupt journal processing. */
            }
        }
    }

    internal static byte[] CreateSamples(string chime, int volume)
    {
        const double durationSeconds = 0.28;
        int sampleCount = (int)(SampleRate * durationSeconds);
        byte[] bytes = new byte[sampleCount * sizeof(float)];
        float gain = Math.Clamp(volume, 0, 100) / 100f * 0.28f;
        for (int index = 0; index < sampleCount; index++)
        {
            double time = index / (double)SampleRate;
            double frequency = Frequency(chime, time);
            double envelope = Math.Min(1, time / 0.012) * Math.Min(1, (durationSeconds - time) / 0.055);
            float sample = (float)(Math.Sin(2 * Math.PI * frequency * time) * gain * envelope);
            BitConverter.TryWriteBytes(bytes.AsSpan(index * sizeof(float), sizeof(float)), sample);
        }

        return bytes;
    }

    private static double Frequency(string chime, double time) =>
        chime switch
        {
            "High-low" => time < 0.14 ? 880 : 660,
            "Crystal" => time switch
            {
                < 0.09 => 1046.5,
                < 0.18 => 1318.5,
                _ => 1568,
            },
            _ => time < 0.14 ? 660 : 880,
        };

    private void EnsureStream()
    {
        if (stream != 0 || !SDL.InitSubSystem(SDL.InitFlags.Audio))
        {
            return;
        }

        var specification = new SDL.AudioSpec
        {
            Format = SDL.AudioFormat.AudioF32LE,
            Channels = 1,
            Freq = SampleRate,
        };
        stream = SDL.OpenAudioDeviceStream(SDL.AudioDeviceDefaultPlayback, in specification, null, 0);
    }

    public void Dispose()
    {
        lock (sync)
        {
            disposed = true;
            if (stream == 0)
            {
                return;
            }

            SDL.DestroyAudioStream(stream);
            stream = 0;
        }
    }
}
