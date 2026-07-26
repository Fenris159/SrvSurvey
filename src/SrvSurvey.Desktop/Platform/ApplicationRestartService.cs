using System.Diagnostics;
using System.Reflection;

namespace SrvSurvey.Desktop.Platform;

public sealed class ApplicationRestartService
{
    private readonly string processPath;
    private readonly string entryAssemblyPath;
    private readonly IReadOnlyList<string> arguments;

    public ApplicationRestartService()
        : this(
            Environment.ProcessPath
                ?? throw new InvalidOperationException(
                    "The current application executable could not be resolved."),
            Assembly.GetEntryAssembly()?.Location
                ?? throw new InvalidOperationException(
                    "The current application assembly could not be resolved."),
            Program.StartupArguments)
    {
    }

    internal ApplicationRestartService(
        string processPath,
        string entryAssemblyPath,
        IReadOnlyList<string> arguments)
    {
        this.processPath = Path.GetFullPath(processPath);
        this.entryAssemblyPath = Path.GetFullPath(entryAssemblyPath);
        this.arguments = arguments?.ToArray()
            ?? throw new ArgumentNullException(nameof(arguments));
    }

    public void StartReplacement()
    {
        var startInfo = CreateStartInfo(
            processPath,
            entryAssemblyPath,
            arguments);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "The replacement SrvSurvey process did not start.");
    }

    internal static ProcessStartInfo CreateStartInfo(
        string processPath,
        string entryAssemblyPath,
        IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryAssemblyPath);
        ArgumentNullException.ThrowIfNull(arguments);
        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
        };
        if (IsDotnetHost(processPath))
        {
            startInfo.ArgumentList.Add(entryAssemblyPath);
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static bool IsDotnetHost(string path)
    {
        return string.Equals(
            Path.GetFileNameWithoutExtension(path),
            "dotnet",
            StringComparison.OrdinalIgnoreCase);
    }
}
