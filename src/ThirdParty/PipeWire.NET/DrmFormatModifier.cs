namespace PipeWire.NET;

/// <summary>
/// Well-known DRM format modifier values (from <c>drm_fourcc.h</c>). A modifier describes the
/// vendor-specific tiling/compression layout of a dmabuf so a consumer can import it correctly into
/// a GPU (e.g. via Vulkan's <c>VK_EXT_image_drm_format_modifier</c>). The full 64-bit value is what
/// PipeWire negotiates through <c>SPA_FORMAT_VIDEO_modifier</c>.
/// </summary>
public static class DrmFormatModifier
{
    /// <summary>
    /// <c>DRM_FORMAT_MOD_INVALID</c> - "no modifier specified". Used as the sentinel for a frame that
    /// carries no negotiated modifier (host-memory path), and offered by a consumer that accepts any
    /// implicit-modifier layout the producer chooses.
    /// </summary>
    public const ulong Invalid = 0x00ff_ffff_ffff_ffffUL;

    /// <summary><c>DRM_FORMAT_MOD_LINEAR</c> - plain, untiled, uncompressed memory layout.</summary>
    public const ulong Linear = 0UL;
}
