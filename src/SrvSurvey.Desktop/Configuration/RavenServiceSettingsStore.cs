using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class RavenServiceSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public RavenServiceSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public Uri? LoadServiceUri()
    {
        var settings = documentStore.Load()["RavenService"] as JsonObject;
        var value = settings?["ServiceUri"] is JsonValue serviceUri
            && serviceUri.TryGetValue<string>(out var text)
                ? text
                : null;
        return NormalizeServiceUri(value);
    }

    public static Uri? NormalizeServiceUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Host.Length == 0
            || uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty,
        };
        if (!builder.Path.EndsWith("/", StringComparison.Ordinal))
        {
            builder.Path += "/";
        }

        return builder.Uri;
    }
}
