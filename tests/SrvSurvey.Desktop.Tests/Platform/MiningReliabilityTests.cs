using System.Diagnostics;
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
