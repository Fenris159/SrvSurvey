namespace SrvSurvey.Desktop.Input;

public sealed class GlobalInputBindingRouter
{
    private Dictionary<string, GlobalInputAction> actionsByChord =
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

        Volatile.Write(ref actionsByChord, routes);
    }

    public bool TryResolve(string chord, out GlobalInputAction action)
    {
        action = default;
        var routes = Volatile.Read(ref actionsByChord);
        return InputChord.TryNormalize(chord, out var normalized)
            && routes.TryGetValue(normalized, out action);
    }
}
