using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Input;

/// <summary>Uses native X11 text for GNOME layouts that do not need a desktop input method.</summary>
internal static partial class LinuxTextInput
{
    internal static bool UseNativeTextInput(OverlayHostKind host) =>
        UseNativeTextInput(host, Environment.GetEnvironmentVariable, () => ReadGnomeInputSources());

    internal static bool UseNativeTextInput(
        OverlayHostKind host,
        Func<string, string?> environment,
        Func<string?> readInputSources
    )
    {
        if (
            host != OverlayHostKind.LinuxXWayland
            || !(environment("XDG_CURRENT_DESKTOP") ?? string.Empty)
                .Split(':', StringSplitOptions.TrimEntries)
                .Contains("GNOME", StringComparer.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(environment("AVALONIA_IM_MODULE"))
            || !IsDefaultModule(environment("GTK_IM_MODULE"), "ibus")
            || !IsDefaultModule(environment("QT_IM_MODULE"), "ibus")
            || !IsDefaultModule(environment("XMODIFIERS"), "@im=ibus")
        )
        {
            return false;
        }

        return readInputSources() is { } sources && XkbOnly().IsMatch(sources.Trim());
    }

    private static bool IsDefaultModule(string? value, string desktopDefault) =>
        string.IsNullOrWhiteSpace(value)
        || string.Equals(value.Trim(), desktopDefault, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads GNOME's input source list without letting a failed desktop probe hold up startup.</summary>
    internal static string? ReadGnomeInputSources(string executable = "/usr/bin/gsettings")
    {
        var startInfo = new ProcessStartInfo(executable, "get org.gnome.desktop.input-sources sources")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(300))
            {
                process.Kill();
                return null;
            }

            return process.ExitCode == 0 ? output.GetAwaiter().GetResult() : null;
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    // GSettings prints the array of (source type, layout ID) tuples. An empty list uses the server's XKB layout.
    private const string XkbLayout = @"\('xkb', '[A-Za-z0-9_+.-]+'\)";

    [GeneratedRegex(
        @"\A(?:@a\(ss\) )?\[(?:" + XkbLayout + @"(?:, " + XkbLayout + @")*)?\]\z",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex XkbOnly();
}
