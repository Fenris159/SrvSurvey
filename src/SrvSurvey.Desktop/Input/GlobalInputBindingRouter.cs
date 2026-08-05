namespace SrvSurvey.Desktop.Input;

public sealed class GlobalInputBindingRouter
{
    private IReadOnlyDictionary<string, GlobalInputAction> actionsByChord =
        new Dictionary<string, GlobalInputAction>(
            StringComparer.OrdinalIgnoreCase);

    public GlobalInputBindingRouter(GlobalInputSettings settings)
    {
        Update(settings);
    }

    public void Update(GlobalInputSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var routes = new Dictionary<string, GlobalInputAction>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var action in GlobalInputActionCatalog.All.Select(
            definition => definition.Action))
        {
            var configured = settings.Bindings.GetValueOrDefault(
                action);
            if (!InputChord.TryNormalize(configured, out var chord))
            {
                continue;
            }

            routes.TryAdd(chord, action);
        }

        actionsByChord = routes;
    }

    public bool TryResolve(string chord, out GlobalInputAction action)
    {
        action = default;
        return InputChord.TryNormalize(chord, out var normalized)
            && actionsByChord.TryGetValue(normalized, out action);
    }
}
