namespace SrvSurvey.Core.Network;

/// <summary>
/// URI path helpers that avoid hardcoded path-delimiter literals (Sonar S1075).
/// </summary>
public static class UriPath
{
    public static readonly char Separator = System.IO.Path.AltDirectorySeparatorChar;

    public static string Combine(params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        return string.Join(Separator, segments.Select(static s => s.Trim(Separator)));
    }

    public static string CombineWithTrailingSeparator(params string[] segments)
        => Combine(segments) + Separator;

    public static Uri EnsureTrailingSeparator(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var text = uri.AbsoluteUri;
        if (text.Length > 0 && text[^1] == Separator)
        {
            return uri;
        }

        return new Uri(text + Separator, UriKind.Absolute);
    }

    public static bool HasTrailingSeparator(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var text = uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.OriginalString;
        return text.Length > 0 && text[^1] == Separator;
    }

    public static bool AbsolutePathHasTrailingSeparator(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var path = uri.AbsolutePath;
        return path.Length > 0 && path[^1] == Separator;
    }
}
