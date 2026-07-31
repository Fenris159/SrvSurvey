using System.Reflection;

namespace SrvSurvey.Desktop.Platform.Inara;

internal static class InaraApplicationKeyProvider
{
    internal const string EnvironmentVariable =
        "SRVSURVEY_INARA_APPLICATION_API_KEY";
    internal const string MetadataName = "SrvSurvey.Inara.ReadKey";

    public static string? GetApplicationKey()
    {
        var environmentValue = Environment.GetEnvironmentVariable(
            EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue.Trim();
        }

        return typeof(InaraApplicationKeyProvider).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(
                attribute.Key,
                MetadataName,
                StringComparison.Ordinal))
            ?.Value
            ?.Trim() is { Length: > 0 } embedded
                ? embedded
                : null;
    }
}
