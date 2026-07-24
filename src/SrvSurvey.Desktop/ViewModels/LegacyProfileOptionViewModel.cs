using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class LegacyProfileOptionViewModel(LegacyProfileDiscovery discovery)
{
    public LegacyProfileDiscovery Discovery { get; } = discovery;

    public string DisplayName => Discovery.Kind switch
    {
        LegacyProfileLocationKind.Desktop =>
            $"Desktop profile ({Discovery.FileCount:N0} files)",
        LegacyProfileLocationKind.MicrosoftStore =>
            $"Microsoft Store profile ({Discovery.FileCount:N0} files)",
        LegacyProfileLocationKind.MicrosoftStoreBackup =>
            $"Microsoft Store recovery profile ({Discovery.FileCount:N0} files)",
        _ => $"Legacy profile ({Discovery.FileCount:N0} files)",
    };

    public string Path => Discovery.Path;
}
