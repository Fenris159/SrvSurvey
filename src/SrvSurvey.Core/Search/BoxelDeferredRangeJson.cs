using System.Text.Json.Nodes;

namespace SrvSurvey.Core.Search;

internal static class BoxelDeferredRangeJson
{
    public static BoxelDeferredRangeSnapshot[] Read(
        JsonObject root,
        string propertyName)
    {
        if (root[propertyName] is not JsonArray ranges)
        {
            return [];
        }

        return ranges
            .OfType<JsonObject>()
            .Select(range => new BoxelDeferredRangeSnapshot
            {
                Prefix = GetString(range, "prefix") ?? string.Empty,
                StartSystemNumber = GetInt32(range, "startSystemNumber") ?? -1,
                SortDescending = GetBoolean(range, "sortDescending") ?? false,
                Exceptions = ReadIntArray(range, "exceptions"),
            })
            .ToArray();
    }

    public static JsonArray Write(IEnumerable<BoxelDeferredRangeSnapshot> ranges)
    {
        var result = new JsonArray();
        foreach (var range in ranges.OrderBy(
                     range => range.Prefix,
                     StringComparer.Ordinal))
        {
            result.Add(new JsonObject
            {
                ["prefix"] = range.Prefix,
                ["startSystemNumber"] = range.StartSystemNumber,
                ["sortDescending"] = range.SortDescending,
                ["exceptions"] = new JsonArray(
                    range.Exceptions
                        .Distinct()
                        .Order()
                        .Select(number => JsonValue.Create(number))
                        .ToArray()),
            });
        }
        return result;
    }

    private static int[] ReadIntArray(
        JsonObject root,
        string propertyName)
    {
        return root[propertyName] is JsonArray values
            ? values
                .OfType<JsonValue>()
                .Select(value => value.TryGetValue<int>(out var number)
                    ? number
                    : -1)
                .Where(number => number >= 0)
                .Distinct()
                .Order()
                .ToArray()
            : [];
    }

    private static string? GetString(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<string>(out var text)
                ? text
                : null;
    }

    private static int? GetInt32(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<int>(out var number)
                ? number
                : null;
    }

    private static bool? GetBoolean(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<bool>(out var boolean)
                ? boolean
                : null;
    }
}
