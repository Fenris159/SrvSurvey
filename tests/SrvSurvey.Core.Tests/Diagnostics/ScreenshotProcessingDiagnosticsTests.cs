using SrvSurvey.Core.Diagnostics;

namespace SrvSurvey.Core.Tests.Diagnostics;

public sealed class ScreenshotProcessingDiagnosticsTests
{
    [Fact]
    public void FormatsLoadedSettingsChangesSkipsScansConversionsAndWarnings()
    {
        var lines = new List<string>();
        var diagnostics = new ScreenshotProcessingDiagnostics(lines.Add);
        var settings = new ScreenshotSettingsDiagnostic
        {
            Enabled = true,
            ProcessSteamScreenshots = false,
            AddBanner = true,
            DeleteOriginal = false,
            AutomaticSource = false,
            SourceFolder = @"C:\Pictures\Elite",
            SteamSourceFolder = null,
            TargetFolder = @"C:\Pictures\Elite PNG",
        };

        diagnostics.WriteLoaded(@"C:\AppData\commanders\F123\cross-platform-ui.json", settings, 1, 2);
        diagnostics.WriteSettingsChanged("ProcessSteamScreenshots", settings with { ProcessSteamScreenshots = true });
        diagnostics.WriteSaveFailed(ScreenshotProcessingDiagnostics.SavePreferencesOperation, "Access denied");
        diagnostics.WriteSkippedNative(ScreenshotProcessingDiagnostics.DisabledReason, @"\ED_Pictures\shot.bmp", false);
        diagnostics.WriteSkippedNative(ScreenshotProcessingDiagnostics.DisabledReason, null, false);
        diagnostics.WriteSteamScan(@"C:\Steam\359320\screenshots", "F123", 3, true, true, true);
        diagnostics.WriteSteamScanSkipped(ScreenshotProcessingDiagnostics.DisabledDuringScanReason, 3, true, false);
        diagnostics.WriteConversion(
            new ScreenshotConversionDiagnostic
            {
                IsSteam = false,
                SourcePath = @"C:\Pictures\Elite\shot.bmp",
                OutputPath = @"C:\Pictures\Elite PNG\Sol\Earth.png",
                IncludedBanner = true,
                SourceDeleted = false,
                AerialOutputPath = @"C:\Pictures\Elite PNG\Aerial Alpha\Earth.png",
                Warning = "Aerial note",
                PendingNative = 0,
                PendingSteam = 2,
            }
        );
        diagnostics.WriteConversion(
            new ScreenshotConversionDiagnostic
            {
                IsSteam = true,
                SourcePath = @"C:\Steam\359320\screenshots\20261123_1.jpg",
                OutputPath = @"C:\Pictures\Elite PNG\Sol\Moon.png",
                IncludedBanner = false,
                SourceDeleted = true,
                PendingNative = 0,
                PendingSteam = 1,
            }
        );
        diagnostics.WriteWarning("   ");
        diagnostics.WriteWarning("Source file was locked.");

        Assert.Equal(10, lines.Count);
        Assert.Contains(
            "[Screenshots] loaded; settings=C:\\AppData\\commanders\\F123\\cross-platform-ui.json; enabled=True; processSteam=False; addBanner=True; deleteOriginal=False; autoSource=False; source=C:\\Pictures\\Elite; steamSource=(none); target=C:\\Pictures\\Elite PNG; pendingNative=1; pendingSteam=2",
            lines[0],
            StringComparison.Ordinal
        );
        Assert.Contains(
            "[Screenshots] settings-changed; property=ProcessSteamScreenshots; enabled=True; processSteam=True;",
            lines[1],
            StringComparison.Ordinal
        );
        Assert.Equal("[Screenshots] save-failed; operation=save-preferences; error=Access denied", lines[2]);
        Assert.Equal(
            "[Screenshots] skipped; sourceKind=native; reason=disabled; filename=\\ED_Pictures\\shot.bmp; enabled=False",
            lines[3]
        );
        Assert.Equal(
            "[Screenshots] skipped; sourceKind=native; reason=disabled; filename=(none); enabled=False",
            lines[4]
        );
        Assert.Equal(
            "[Screenshots] steam-scan; folder=C:\\Steam\\359320\\screenshots; frontierId=F123; matched=3; liveSession=True; enabled=True; processSteam=True",
            lines[5]
        );
        Assert.Equal(
            "[Screenshots] steam-scan-skipped; reason=disabled-during-scan; matched=3; enabled=True; processSteam=False",
            lines[6]
        );
        Assert.Equal(
            "[Screenshots] converted; sourceKind=native; source=C:\\Pictures\\Elite\\shot.bmp; output=C:\\Pictures\\Elite PNG\\Sol\\Earth.png; banner=True; sourceDeleted=False; pendingNative=0; pendingSteam=2; aerial=C:\\Pictures\\Elite PNG\\Aerial Alpha\\Earth.png; warning=Aerial note",
            lines[7]
        );
        Assert.Equal(
            "[Screenshots] converted; sourceKind=steam; source=C:\\Steam\\359320\\screenshots\\20261123_1.jpg; output=C:\\Pictures\\Elite PNG\\Sol\\Moon.png; banner=False; sourceDeleted=True; pendingNative=0; pendingSteam=1",
            lines[8]
        );
        Assert.Equal("[Screenshots] warning; Source file was locked.", lines[9]);
    }

    [Fact]
    public void NullAndThrowingLogCallbacksDoNotThrow()
    {
        var silent = new ScreenshotProcessingDiagnostics(null);
        int attempts = 0;
        var failing = new ScreenshotProcessingDiagnostics(_ =>
        {
            attempts++;
            throw new InvalidOperationException("Disk full");
        });
        var settings = new ScreenshotSettingsDiagnostic();
        var conversion = new ScreenshotConversionDiagnostic();

        Exception? exception = Record.Exception(() =>
        {
            silent.WriteLoaded("settings.json", settings, 0, 0);
            failing.WriteLoaded("settings.json", settings, 0, 0);
            failing.WriteSettingsChanged(null, settings);
            failing.WriteConversion(conversion);
            failing.WriteWarning("Still safe");
        });

        Assert.Null(exception);
        Assert.Equal(4, attempts);
    }
}
