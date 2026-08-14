using System.Reflection;

namespace SrvSurvey.Desktop.Platform;

internal static class VoxStellarSharedKeyProvider
{
    internal const string EnvironmentVariable =
        "SRVSURVEY_VOXSTELLAR_SHARED_KEY";
    internal const string MetadataName = "SrvSurvey.VoxStellar.SharedKey";

    public static string? GetSharedKey()
    {
        var environmentValue = Environment.GetEnvironmentVariable(
            EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue.Trim();
        }

        return typeof(VoxStellarSharedKeyProvider).Assembly
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
