using System.Globalization;
using System.Text;

namespace SrvSurvey.Core.Diagnostics;

/// <summary>Records structured screenshot configuration, scan, conversion, and warning events.</summary>
public sealed class ScreenshotProcessingDiagnostics(Action<string>? log)
{
    public const string SavePreferencesOperation = "save-preferences";
    public const string PinOutputFolderOperation = "pin-output-folder";
    public const string DisabledReason = "disabled";
    public const string DisabledDuringScanReason = "disabled-during-scan";

    public void WriteLoaded(
        string settingsPath,
        ScreenshotSettingsDiagnostic settings,
        int pendingNative,
        int pendingSteam
    )
    {
        ArgumentNullException.ThrowIfNull(settings);
        Write(
            "loaded",
            string.Create(
                CultureInfo.InvariantCulture,
                $"settings={FormatOptional(settingsPath)}; {FormatSettings(settings)}; "
                    + $"pendingNative={pendingNative}; pendingSteam={pendingSteam}"
            )
        );
    }

    public void WriteSettingsChanged(string? propertyName, ScreenshotSettingsDiagnostic settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Write("settings-changed", $"property={FormatOptional(propertyName)}; {FormatSettings(settings)}");
    }

    public void WriteSaveFailed(string operation, string error)
    {
        Write("save-failed", $"operation={FormatOptional(operation)}; error={FormatOptional(error)}");
    }

    public void WriteSkippedNative(string reason, string? filename, bool enabled)
    {
        Write(
            "skipped",
            string.Create(
                CultureInfo.InvariantCulture,
                $"sourceKind=native; reason={FormatOptional(reason)}; "
                    + $"filename={FormatOptional(filename)}; enabled={enabled}"
            )
        );
    }

    public void WriteSteamScan(
        string folder,
        string frontierId,
        int matchedCount,
        bool hasLiveSession,
        bool enabled,
        bool processSteam
    )
    {
        Write(
            "steam-scan",
            string.Create(
                CultureInfo.InvariantCulture,
                $"folder={FormatOptional(folder)}; frontierId={FormatOptional(frontierId)}; "
                    + $"matched={matchedCount}; liveSession={hasLiveSession}; "
                    + $"enabled={enabled}; processSteam={processSteam}"
            )
        );
    }

    public void WriteSteamScanSkipped(string reason, int matchedCount, bool enabled, bool processSteam)
    {
        Write(
            "steam-scan-skipped",
            string.Create(
                CultureInfo.InvariantCulture,
                $"reason={FormatOptional(reason)}; matched={matchedCount}; "
                    + $"enabled={enabled}; processSteam={processSteam}"
            )
        );
    }

    public void WriteConversion(ScreenshotConversionDiagnostic conversion)
    {
        ArgumentNullException.ThrowIfNull(conversion);
        var details = new StringBuilder();
        details.Append(
            string.Create(
                CultureInfo.InvariantCulture,
                $"sourceKind={(conversion.IsSteam ? "steam" : "native")}; "
                    + $"source={FormatOptional(conversion.SourcePath)}; "
                    + $"output={FormatOptional(conversion.OutputPath)}; "
                    + $"banner={conversion.IncludedBanner}; "
                    + $"sourceDeleted={conversion.SourceDeleted}; "
                    + $"pendingNative={conversion.PendingNative}; "
                    + $"pendingSteam={conversion.PendingSteam}"
            )
        );
        if (!string.IsNullOrWhiteSpace(conversion.AerialOutputPath))
        {
            details.Append("; aerial=").Append(conversion.AerialOutputPath);
        }
        if (!string.IsNullOrWhiteSpace(conversion.Warning))
        {
            details.Append("; warning=").Append(conversion.Warning);
        }

        Write("converted", details.ToString());
    }

    public void WriteWarning(string warning)
    {
        if (string.IsNullOrWhiteSpace(warning))
        {
            return;
        }

        Write("warning", warning);
    }

    private void Write(string stage, string details)
    {
        if (log is null)
        {
            return;
        }

        try
        {
            log($"[Screenshots] {stage}; {details}");
        }
        catch (Exception)
        {
            // Optional diagnostics must never interrupt screenshot processing.
        }
    }

    private static string FormatSettings(ScreenshotSettingsDiagnostic settings) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"enabled={settings.Enabled}; processSteam={settings.ProcessSteamScreenshots}; "
                + $"addBanner={settings.AddBanner}; deleteOriginal={settings.DeleteOriginal}; "
                + $"autoSource={settings.AutomaticSource}; source={FormatOptional(settings.SourceFolder)}; "
                + $"steamSource={FormatOptional(settings.SteamSourceFolder)}; "
                + $"target={FormatOptional(settings.TargetFolder)}"
        );

    private static string FormatOptional(string? value) => string.IsNullOrWhiteSpace(value) ? "(none)" : value;
}

public sealed record ScreenshotSettingsDiagnostic
{
    public bool Enabled { get; init; }
    public bool ProcessSteamScreenshots { get; init; }
    public bool AddBanner { get; init; }
    public bool DeleteOriginal { get; init; }
    public bool AutomaticSource { get; init; }
    public string? SourceFolder { get; init; }
    public string? SteamSourceFolder { get; init; }
    public string? TargetFolder { get; init; }
}

public sealed record ScreenshotConversionDiagnostic
{
    public bool IsSteam { get; init; }
    public string? SourcePath { get; init; }
    public string? OutputPath { get; init; }
    public bool IncludedBanner { get; init; }
    public bool SourceDeleted { get; init; }
    public string? AerialOutputPath { get; init; }
    public string? Warning { get; init; }
    public int PendingNative { get; init; }
    public int PendingSteam { get; init; }
}
