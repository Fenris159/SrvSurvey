namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Keeps realized overlay rows attached when a refresh produces the same ordered content.</summary>
internal static class OverlayListRetention
{
    public static IReadOnlyList<T> Keep<T>(
        IReadOnlyList<T>? current,
        IReadOnlyList<T> updated,
        Func<T, T, bool>? equivalent = null
    )
    {
        if (current is null || current.Count != updated.Count)
        {
            return updated;
        }

        for (int index = 0; index < current.Count; index++)
        {
            bool same =
                equivalent?.Invoke(current[index], updated[index])
                ?? EqualityComparer<T>.Default.Equals(current[index], updated[index]);
            if (!same)
            {
                return updated;
            }
        }

        return current;
    }
}
