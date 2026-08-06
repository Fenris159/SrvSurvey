using System.Reflection;
using System.Text.Json;

namespace SrvSurvey.Core.Network;

/// <summary>
/// Loads well-known external and application URIs from embedded configuration
/// so call sites do not hard-code absolute paths or URI strings.
/// </summary>
public static class WellKnownUris
{
    private const string ResourceName = "SrvSurvey.Core.Resources.well-known-uris.json";

    private static readonly IReadOnlyDictionary<string, string> Values = Load();

    public static Uri PublishedCodexReference => RequireUri("PublishedCodexReference");
    public static Uri PublishedRegionalCodexCandidatesCsv => RequireUri("PublishedRegionalCodexCandidatesCsv");
    public static Uri PublishedKnownSystemAddresses => RequireUri("PublishedKnownSystemAddresses");
    public static Uri PublishedBiologyCriteriaArchive => RequireUri("PublishedBiologyCriteriaArchive");
    public static Uri PublishedGuardianTemplates => RequireUri("PublishedGuardianTemplates");
    public static Uri PublishedGuardianRuins => RequireUri("PublishedGuardianRuins");
    public static Uri PublishedGuardianStructures => RequireUri("PublishedGuardianStructures");
    public static Uri PublishedGuardianSurveyArchive => RequireUri("PublishedGuardianSurveyArchive");
    public static Uri PublishedHumanSettlementsArchive => RequireUri("PublishedHumanSettlementsArchive");
    public static Uri PublishedGreenGasGiants => RequireUri("PublishedGreenGasGiants");
    public static Uri PublishedRavenNicknames => RequireUri("PublishedRavenNicknames");
    public static Uri ExampleInvalidPackage => RequireUri("ExampleInvalidPackage");
    public static Uri InaraCommanderApiSettings => RequireUri("InaraCommanderApiSettings");
    public static Uri CanonnChallenge => RequireUri("CanonnChallenge");
    public static Uri EdastroCodexMap => RequireUri("EdastroCodexMap");
    public static Uri CodexMissingForm => RequireUri("CodexMissingForm");
    public static Uri ColonisationWiki => RequireUri("ColonisationWiki");
    public static Uri EdGalaxyVisitedStars => RequireUri("EdGalaxyVisitedStars");
    public static Uri FrontierOAuthRedirect => RequireUri("FrontierOAuthRedirect");
    public static Uri DesktopLogoAsset => RequireUri("DesktopLogoAsset");

    public static string CanonnSignalsSystemPrefix => Require("CanonnSignalsSystem");
    public static string CanonnCodexRegionsEntryPrefix => Require("CanonnCodexRegionsEntry");
    public static string CanonnBioforgeEntryPrefix => Require("CanonnBioforgeEntry");
    public static string CanonnUndiscoveredCodexCommanderPrefix => Require("CanonnUndiscoveredCodexCommander");
    public static string EdastroOrganicMapPrefix => Require("EdastroOrganicMapPrefix");
    public static Uri SpanshApiBase => RequireUri("SpanshApiBase");
    public static string SpanshBodyPrefix => Require("SpanshBody");
    public static string SpanshSystemPrefix => Require("SpanshSystem");
    public static string EdsmSystemById64Prefix => Require("EdsmSystemById64");

    public static string Require(string key)
    {
        if (!Values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Well-known URI key '{key}' is missing from embedded configuration.");
        }

        return value;
    }

    public static Uri RequireUri(string key) => new(Require(key), UriKind.Absolute);

    private static IReadOnlyDictionary<string, string> Load()
    {
        var assembly = typeof(WellKnownUris).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' was not found.");
        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' did not contain a URI map.");
        return parsed;
    }
}
