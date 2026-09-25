using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace SrvSurvey.Core.Diagnostics;

/// <summary>
/// Collapses repeated provider failures of the same kind into one diagnostic line.
/// </summary>
public sealed class ProviderFailureLog
{
    private readonly Lock sync = new();
    private readonly Dictionary<string, Failure> pending = new(StringComparer.Ordinal);

    public void Record(string provider, string route, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        if (exception is OperationCanceledException { InnerException: not TimeoutException })
        {
            return;
        }

        string operation = Classify(route);
        string key = provider + "|" + operation;
        string detail = (route.Length == 0 ? operation : route) + " " + Describe(exception);
        lock (sync)
        {
            if (pending.TryGetValue(key, out Failure? existing))
            {
                pending[key] = existing with { Count = existing.Count + 1, Detail = detail };
            }
            else
            {
                pending[key] = new Failure(provider, operation, 1, detail);
            }
        }
    }

    public IReadOnlyList<string> Drain()
    {
        Failure[] ready;
        lock (sync)
        {
            ready = pending.Values.ToArray();
            pending.Clear();
        }

        return ready.Select(Format).ToArray();
    }

    public static string Classify(string route)
    {
        if (route.Contains("commodities/imports", StringComparison.OrdinalIgnoreCase))
        {
            return "system imports";
        }

        if (route.Contains("material-trader", StringComparison.OrdinalIgnoreCase))
        {
            return "material traders";
        }

        if (
            route.Contains("/commodity/", StringComparison.OrdinalIgnoreCase)
            || route.Contains("commodity/name", StringComparison.OrdinalIgnoreCase)
        )
        {
            return "station prices";
        }

        if (
            route.Equals("commodities", StringComparison.OrdinalIgnoreCase)
            || route.EndsWith("/commodities", StringComparison.OrdinalIgnoreCase)
        )
        {
            return "commodity averages";
        }

        if (route.Contains("bodies/search", StringComparison.OrdinalIgnoreCase))
        {
            return "body search";
        }

        if (route.Contains("systems/search", StringComparison.OrdinalIgnoreCase))
        {
            return "system search";
        }

        if (route.Contains("stations/search", StringComparison.OrdinalIgnoreCase))
        {
            return "station search";
        }

        return "request";
    }

    private static string Format(Failure failure)
    {
        if (failure.Count == 1)
        {
            return failure.Provider + " " + failure.Operation + " failed: " + failure.Detail;
        }

        return failure.Provider
            + " "
            + failure.Operation
            + " failed "
            + failure.Count.ToString(CultureInfo.InvariantCulture)
            + " times. Last: "
            + failure.Detail;
    }

    private static string Describe(Exception exception)
    {
        Exception root = exception.GetBaseException();
        string message = root.Message;
        if (message.Length > 400)
        {
            message = message[..400];
        }

        if (exception is HttpRequestException { StatusCode: { } status })
        {
            return "HTTP " + ((int)status).ToString(CultureInfo.InvariantCulture) + " " + message;
        }

        return root.GetType().Name + ": " + message;
    }

    private sealed record Failure(string Provider, string Operation, int Count, string Detail);
}
