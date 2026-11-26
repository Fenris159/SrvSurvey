namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Formats provider lookup warnings into concise, deduplicated status notices for overlays.</summary>
internal static class SystemSummaryWarningFormatter
{
    public static string Format(IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
        {
            return string.Empty;
        }

        ProviderIssue edsm = ProviderIssue.None;
        ProviderIssue spansh = ProviderIssue.None;
        bool hasOtherProvider = false;
        foreach (string warning in warnings)
        {
            if (warning.StartsWith("EDSM ", StringComparison.Ordinal))
            {
                edsm = MergeIssue(edsm, Classify(warning));
            }
            else if (warning.StartsWith("Spansh ", StringComparison.Ordinal))
            {
                spansh = MergeIssue(spansh, Classify(warning));
            }
            else
            {
                hasOtherProvider = true;
            }
        }

        if (hasOtherProvider)
        {
            return "System data unavailable.";
        }

        return FormatProviders(edsm, spansh);
    }

    private static ProviderIssue Classify(string warning) =>
        warning.Contains("rate-limited", StringComparison.OrdinalIgnoreCase)
        || warning.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
            ? ProviderIssue.RateLimited
            : ProviderIssue.Unavailable;

    private static ProviderIssue MergeIssue(ProviderIssue current, ProviderIssue next) =>
        current == ProviderIssue.RateLimited || next == ProviderIssue.RateLimited ? ProviderIssue.RateLimited : next;

    private static string FormatProviders(ProviderIssue edsm, ProviderIssue spansh) =>
        (edsm, spansh) switch
        {
            (ProviderIssue.RateLimited, ProviderIssue.RateLimited) => "EDSM and Spansh temporarily rate-limited.",
            (ProviderIssue.Unavailable, ProviderIssue.Unavailable) => "EDSM and Spansh data unavailable.",
            (ProviderIssue.RateLimited, ProviderIssue.None) => "EDSM temporarily rate-limited.",
            (ProviderIssue.None, ProviderIssue.RateLimited) => "Spansh temporarily rate-limited.",
            (ProviderIssue.Unavailable, ProviderIssue.None) => "EDSM data unavailable.",
            (ProviderIssue.None, ProviderIssue.Unavailable) => "Spansh data unavailable.",
            (ProviderIssue.RateLimited, ProviderIssue.Unavailable) =>
                "EDSM temporarily rate-limited · Spansh data unavailable.",
            (ProviderIssue.Unavailable, ProviderIssue.RateLimited) =>
                "EDSM data unavailable · Spansh temporarily rate-limited.",
            _ => "System data unavailable.",
        };

    private enum ProviderIssue
    {
        None,
        Unavailable,
        RateLimited,
    }
}
