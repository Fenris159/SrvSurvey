namespace PipeWire.NET;

/// <summary>Property names returned for streams by the XDG Desktop Portal ScreenCast API.</summary>
public static class PipeWirePortalMetadata
{
    /// <summary>
    /// Stable PipeWire object serial introduced by ScreenCast version 6. Use its decimal value as
    /// <c>target.object</c> with a wildcard stream node id.
    /// </summary>
    public const string SerialPropertyName = "pipewire-serial";
}
