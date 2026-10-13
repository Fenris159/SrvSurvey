namespace SrvSurvey.Core.Network;

/// <summary>Configures external dependencies and machine-local retention for the signed journal sender.</summary>
public sealed record VoxStellarPublisherOptions
{
    /// <summary>Supplies a reusable HTTP client; the sender owns its client only when this is omitted.</summary>
    public HttpClient? Client { get; init; }

    /// <summary>Overrides the provider URL for controlled transport tests.</summary>
    public Uri? Endpoint { get; init; }

    /// <summary>Receives consolidated delivery diagnostics without event bodies or signing keys.</summary>
    public Action<string>? Log { get; init; }

    /// <summary>Controls batch timers and retry deadlines.</summary>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>Overrides the normal batch interval without changing per-event wire payloads.</summary>
    public TimeSpan? BatchInterval { get; init; }

    /// <summary>Retains reports privately under the originating commander; null selects memory-only storage.</summary>
    public string? OutboxDirectory { get; init; }
}
