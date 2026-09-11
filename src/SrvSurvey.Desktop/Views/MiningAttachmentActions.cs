using System.Diagnostics;
using SrvSurvey.Desktop.Runtime;

namespace SrvSurvey.Desktop.Views;

internal static class MiningAttachmentActions
{
    public static string Open(string path)
    {
        if (!DesktopExternalEffectPolicy.IsAllowed)
        {
            return DesktopExternalEffectPolicy.DisabledMessage;
        }

        try
        {
            if (!File.Exists(path))
            {
                return "The original screenshot is unavailable.";
            }

            if (Path.GetExtension(path).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".webp"))
            {
                return "Unsupported screenshot format.";
            }

            Process.Start(new ProcessStartInfo(Path.GetFullPath(path)) { UseShellExecute = true });
            return "Screenshot opened.";
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidOperationException
                        or System.ComponentModel.Win32Exception
            )
        {
            return "Screenshot could not be opened: " + ex.Message;
        }
    }
}
