using System.Security.Cryptography;
using System.Text;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Desktop.Platform.Frontier;

public sealed record FrontierOAuthCallback(
    string Code,
    string State,
    string Error,
    string ErrorDescription)
{
    public const string Scheme = "srvsurvey";
    public const string Host = "frontier-auth";
    public static string RedirectUri => WellKnownUris.FrontierOAuthRedirect.OriginalString;

    public static FrontierOAuthCallback? Find(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments
            .Select(Parse)
            .FirstOrDefault(callback => callback is not null);
    }

    public static FrontierOAuthCallback? Parse(string? value)
    {
        var candidate = value?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(candidate)
            || !Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Scheme, StringComparison.Ordinal)
            || !string.Equals(uri.Host, Host, StringComparison.Ordinal)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || (uri.AbsolutePath.Length > 0 && uri.AbsolutePath != "/"))
        {
            return null;
        }

        var query = ParseQuery(uri.Query);
        return new FrontierOAuthCallback(
            query.GetValueOrDefault("code") ?? string.Empty,
            query.GetValueOrDefault("state") ?? string.Empty,
            query.GetValueOrDefault("error") ?? string.Empty,
            query.GetValueOrDefault("error_description") ?? string.Empty);
    }

    public static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
        var rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split(
            '&',
            StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var rawName = separator < 0 ? pair : pair[..separator];
            var rawValue = separator < 0 ? string.Empty : pair[(separator + 1)..];
            values[Uri.UnescapeDataString(rawName.Replace('+', ' '))] =
                Uri.UnescapeDataString(rawValue.Replace('+', ' '));
        }

        return values;
    }
}
