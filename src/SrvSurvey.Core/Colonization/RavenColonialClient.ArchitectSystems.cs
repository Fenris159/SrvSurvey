using System.Text.Json;

namespace SrvSurvey.Core.Colonization;

public interface IRavenColonialArchitectSystemReader
{
    Task<IReadOnlyList<ColonizationArchitectSystem>> GetArchitectSystemsAsync(
        string commander,
        CancellationToken cancellationToken = default
    );
}

public sealed record ColonizationArchitectSystem(string Name, long SystemAddress = 0);

public sealed partial class RavenColonialClient : IRavenColonialArchitectSystemReader
{
    /// <summary>Discovers architect systems, excluding systems where the commander only helped.</summary>
    public async Task<IReadOnlyList<ColonizationArchitectSystem>> GetArchitectSystemsAsync(
        string commander,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commander);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            CreateUri($"api/Cmdr/{Uri.EscapeDataString(commander.Trim())}/map/architect")
        );
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        JsonElement map = await ReadRequiredAsync<JsonElement>(response, "load architect systems", cancellationToken)
            .ConfigureAwait(false);
        if (
            map.ValueKind != JsonValueKind.Object
            || !map.TryGetProperty("systems", out JsonElement systems)
            || systems.ValueKind != JsonValueKind.Array
        )
        {
            throw new InvalidDataException("Raven Colonial returned an invalid architect system catalog.");
        }
        return systems
            .EnumerateArray()
            .Select(node => ReadArchitectSystem(node, commander.Trim()))
            .OfType<ColonizationArchitectSystem>()
            .DistinctBy(system => system.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(system => system.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ColonizationArchitectSystem? ReadArchitectSystem(JsonElement node, string commander)
    {
        if (
            node.ValueKind != JsonValueKind.Object
            || !node.TryGetProperty("cat", out JsonElement categories)
            || categories.ValueKind != JsonValueKind.Array
            || !categories
                .EnumerateArray()
                .Any(category =>
                    category.ValueKind == JsonValueKind.Number && category.TryGetInt32(out int value) && value == 0
                )
        )
        {
            return null;
        }
        string? name = ReadArchitectString(node, "name");
        long address = 0;
        string? info = ReadArchitectString(node, "infos");
        if (!string.IsNullOrWhiteSpace(info) && info.TrimStart().StartsWith('{'))
        {
            try
            {
                using var document = JsonDocument.Parse(info);
                JsonElement snapshot = document.RootElement;
                string? architect = ReadArchitectString(snapshot, "architect");
                if (
                    !string.IsNullOrWhiteSpace(architect)
                    && !string.Equals(architect.Trim(), commander, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return null;
                }
                name = ReadArchitectString(snapshot, "name") ?? name;
                if (
                    snapshot.TryGetProperty("id64", out JsonElement id)
                    && id.ValueKind == JsonValueKind.Number
                    && id.TryGetInt64(out long value)
                    && value > 0
                )
                {
                    address = value;
                }
            }
            catch (JsonException)
            {
                // Category membership remains usable when optional display metadata is malformed.
            }
        }
        return string.IsNullOrWhiteSpace(name) ? null : new ColonizationArchitectSystem(name.Trim(), address);
    }

    private static string? ReadArchitectString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
