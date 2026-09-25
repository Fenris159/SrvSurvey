using System.Diagnostics;
using System.Runtime.Versioning;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class MiningReliabilityTests
{
    [Fact]
    public void CalibrationIdentityPreservesEveryExplicitAdjustment()
    {
        MiningDetectionSettings settings = new MiningDetectionSettings().Normalize();
        Assert.True(settings.HasSameCalibration(settings.Normalize()));
        Assert.False(settings.HasSameCalibration(settings with { X = double.BitIncrement(settings.X) }));
        Assert.False(
            settings.HasSameCalibration(
                settings with
                {
                    CircleAspectRatio = double.BitIncrement(settings.CircleAspectRatio),
                }
            )
        );
        Assert.False(
            settings.HasSameCalibration(
                settings with
                {
                    RotationDegrees = double.BitIncrement(settings.RotationDegrees),
                }
            )
        );
    }

    [Theory]
    [InlineData(5, 10, 4, 100, true)]
    [InlineData(4, 100, 5, 10, false)]
    [InlineData(4, 20, 4, 10, true)]
    [InlineData(4, 10, 4, 20, false)]
    [InlineData(4, 20, 4, 20, false)]
    public void CircleGridPrioritizesCorroboratingCirclesThenConfidence(
        int count,
        double score,
        int bestCount,
        double bestScore,
        bool expected
    )
    {
        Assert.Equal(expected, MiningCircleMask.IsBetterMatch(count, score, bestCount, bestScore));
    }

    [Fact]
    public void DisposingUnusedSpeechQueueDoesNotStartSpeech()
    {
        var speech = new MiningSpeechOutput();
        Exception? exception = Record.Exception(speech.Dispose);
        Assert.Null(exception);
    }

    [Fact]
    public void LinuxSpeechCommandsClampSettingsAndKeepTextOutOfTheShell()
    {
        ProcessStartInfo dispatcher = MiningSpeechOutput.CreateLinuxStartInfo(
            "/usr/bin/spd-say",
            "Platinum; do not execute this",
            "Female 2",
            150,
            -20
        );
        Assert.False(dispatcher.UseShellExecute);
        Assert.False(dispatcher.RedirectStandardInput);
        Assert.False(dispatcher.RedirectStandardError);
        Assert.Equal(
            ["--wait", "--rate", "-100", "--volume", "100", "--voice-type", "female2", "Platinum; do not execute this"],
            dispatcher.ArgumentList
        );

        ProcessStartInfo espeak = MiningSpeechOutput.CreateLinuxStartInfo(
            "/usr/bin/espeak-ng",
            "Osmium",
            "en-gb",
            -5,
            20
        );
        Assert.True(espeak.RedirectStandardInput);
        Assert.Equal(["--stdin", "-a", "0", "-s", "295", "-v", "en-gb"], espeak.ArgumentList);

        ProcessStartInfo defaultEspeak = MiningSpeechOutput.CreateLinuxStartInfo(
            "/usr/bin/espeak",
            "Osmium",
            "System default",
            50,
            0
        );
        Assert.DoesNotContain("-v", defaultEspeak.ArgumentList);
    }

    [Theory]
    [InlineData("Female 1", "female1")]
    [InlineData("Female 2", "female2")]
    [InlineData("Female 3", "female3")]
    [InlineData("Male 1", "male1")]
    [InlineData("Male 2", "male2")]
    [InlineData("Male 3", "male3")]
    [InlineData("Child", "child_male")]
    [InlineData("System default", null)]
    public void SpeechDispatcherVoiceTypesMapToPortableNames(string voice, string? expected)
    {
        Assert.Equal(expected, MiningSpeechOutput.SpeechDispatcherVoiceType(voice));
    }

    [Fact]
    public void EspeakVoiceParsingAndPathLookupAreDeterministic()
    {
        const string output =
            "Pty Language Age/Gender VoiceName File Other Languages\n"
            + " 5  en-gb          M  english-gb en\n"
            + " 5  en-us          M  english-us en-us\n"
            + " malformed\n"
            + " 5  en-gb          M  english-gb en\n";
        Assert.Equal(["System default", "english-gb", "english-us"], MiningSpeechOutput.ParseEspeakVoices(output));

        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string first = Path.Combine(root, "first");
        string second = Path.Combine(root, "second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        try
        {
            string executable = Path.Combine(second, "speech-test");
            File.WriteAllText(executable, "test");
            string path = string.Join(Path.PathSeparator, "", first, second);
            Assert.Equal(executable, MiningSpeechOutput.FindOnPath("speech-test", path));
            Assert.Null(MiningSpeechOutput.FindOnPath("missing", path));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LinuxVoiceLoadingHandlesStartFailuresAndStopsTimedOutProcesses()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string invalid = Path.Combine(root, "not-executable");
            await File.WriteAllTextAsync(invalid, "not an executable");
            Assert.Empty(await MiningSpeechOutput.ReadEspeakVoicesAsync(invalid, TimeSpan.FromMilliseconds(200)));

            string listing = WriteLinuxExecutable(
                root,
                "voice-list",
                "printf 'header\\n'\ni=0\nwhile [ \"$i\" -lt 3000 ]; do printf ' 5 en-us M english-us en-us\\n'; i=$((i+1)); done\n"
            );
            Assert.Equal(
                ["System default", "english-us"],
                await MiningSpeechOutput.ReadEspeakVoicesAsync(listing, TimeSpan.FromSeconds(2))
            );

            string backend = WriteLinuxExecutable(
                root,
                "espeak-ng",
                "printf '%s' $$ > \"$(dirname \"$0\")/pid\"\nwhile :; do sleep 1; done\n"
            );
            var timer = Stopwatch.StartNew();
            Assert.Empty(await MiningSpeechOutput.ReadEspeakVoicesAsync(backend, TimeSpan.FromMilliseconds(300)));
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2));
            int processId = int.Parse(
                await File.ReadAllTextAsync(Path.Combine(root, "pid")),
                global::System.Globalization.CultureInfo.InvariantCulture
            );
            Assert.True(
                await WaitUntilAsync(() => Task.FromResult(!IsProcessRunning(processId)), TimeSpan.FromSeconds(2))
            );
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LinuxSpeechUsesOneWorkerAndBoundsPendingUtterances()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string backend = WriteLinuxExecutable(
                root,
                "spd-say",
                "log=\"$(dirname \"$0\")/speech.log\"\nprintf 'start\\n' >> \"$log\"\nsleep 0.3\nprintf 'end\\n' >> \"$log\"\n"
            );
            string log = Path.Combine(root, "speech.log");
            using var speech = new MiningSpeechOutput(backend);
            speech.Speak("first", "", 50, 0);
            Assert.True(await WaitUntilAsync(() => Task.FromResult(File.Exists(log)), TimeSpan.FromSeconds(2)));
            for (int index = 0; index < 30; index++)
            {
                speech.Speak($"notice {index}", "", 50, 0);
            }

            Assert.Equal(10, speech.PendingLinuxUtterances);
            Assert.True(
                await WaitUntilAsync(
                    async () => (await File.ReadAllLinesAsync(log)).Length == 22,
                    TimeSpan.FromSeconds(6)
                )
            );
            string[] lines = await File.ReadAllLinesAsync(log);
            for (int index = 0; index < lines.Length; index += 2)
            {
                Assert.Equal("start", lines[index]);
                Assert.Equal("end", lines[index + 1]);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [SupportedOSPlatform("linux")]
    private static string WriteLinuxExecutable(string root, string name, string script)
    {
        string path = Path.Combine(root, name);
        File.WriteAllText(path, "#!/bin/sh\n" + script);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return await condition();
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [Fact]
    public void MiningChimeSamplesHonorVolumeAndOfferDistinctPortableCues()
    {
        byte[] muted = MiningChimeOutput.CreateSamples(MiningChimeOutput.DefaultChime, 0);
        byte[] twoTone = MiningChimeOutput.CreateSamples(MiningChimeOutput.DefaultChime, 100);
        byte[] highLow = MiningChimeOutput.CreateSamples("High-low", 100);
        byte[] crystal = MiningChimeOutput.CreateSamples("Crystal", 100);
        byte[] fallback = MiningChimeOutput.CreateSamples("Unknown", 100);

        Assert.Equal(["Two-tone", "High-low", "Crystal"], MiningChimeOutput.Chimes);
        Assert.Equal(muted.Length, twoTone.Length);
        for (int index = 0; index < muted.Length; index += sizeof(float))
        {
            Assert.Equal(0, BitConverter.ToSingle(muted, index));
        }
        Assert.Contains(twoTone, value => value != 0);
        Assert.NotEqual(twoTone, highLow);
        Assert.NotEqual(twoTone, crystal);
        Assert.Equal(twoTone, fallback);

        var output = new MiningChimeOutput();
        output.Play("Crystal", 40);
        output.Dispose();
        output.Play("High-low", 40);
        output.Dispose();
    }
}
