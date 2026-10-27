using System.Runtime.InteropServices;
using System.Windows.Input;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ReleaseUpdateViewModel
{
    private static readonly Lazy<string> BundledReleaseNotes = new(ReadInstalledReleaseNotes);
    private readonly AsyncCommand copySupportDetailsCommand;
    private Func<string, Task>? clipboardWriter;
    private string supportDetailsStatusMessage = string.Empty;

    public ICommand CopySupportDetailsCommand => copySupportDetailsCommand;

    public static string InstalledReleaseNotes => BundledReleaseNotes.Value;

    public string SupportDetails =>
        string.Join(
            Environment.NewLine,
            $"SrvSurvey-XP: {CurrentVersion}",
            $"Release channel: {SelectedChannelName}",
            $"OS: {RuntimeInformation.OSDescription}",
            $"OS architecture: {RuntimeInformation.OSArchitecture}",
            $"App architecture: {RuntimeInformation.ProcessArchitecture}",
            $"Runtime: {RuntimeInformation.FrameworkDescription}"
        );

    public string SupportDetailsStatusMessage
    {
        get => supportDetailsStatusMessage;
        private set
        {
            if (SetField(ref supportDetailsStatusMessage, value))
            {
                OnPropertyChanged(nameof(HasSupportDetailsStatusMessage));
            }
        }
    }

    public bool HasSupportDetailsStatusMessage => !string.IsNullOrWhiteSpace(SupportDetailsStatusMessage);

    public void SetClipboardWriter(Func<string, Task>? writer)
    {
        clipboardWriter = writer;
        copySupportDetailsCommand.RaiseCanExecuteChanged();
    }

    public async Task CopySupportDetailsAsync()
    {
        if (clipboardWriter is not { } writeClipboard)
        {
            SupportDetailsStatusMessage = "The desktop clipboard is unavailable.";
            return;
        }

        try
        {
            await writeClipboard(SupportDetails);
            SupportDetailsStatusMessage = "App version and system details copied to the clipboard.";
        }
        catch (Exception exception)
            when (exception
                    is InvalidOperationException
                        or IOException
                        or NotSupportedException
                        or ExternalException
                        or UnauthorizedAccessException
            )
        {
            SupportDetailsStatusMessage = "App version and system details could not be copied: " + exception.Message;
        }
    }

    private static string ReadInstalledReleaseNotes()
    {
        using Stream stream =
            typeof(ReleaseUpdateViewModel).Assembly.GetManifestResourceStream(
                "SrvSurvey.Desktop.Resources.CurrentReleaseNotes.md"
            ) ?? throw new InvalidOperationException("The installed release notes are missing from this build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
