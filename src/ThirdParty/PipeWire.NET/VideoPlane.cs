namespace PipeWire.NET;

/// <summary>
/// The dmabuf layout of one plane of a video frame. A packed format (BGRA) has a single plane;
/// a planar format (NV12) has two (Y, then interleaved UV); a modifier with auxiliary/compression
/// data can add more. Together with <see cref="VideoFrame.Modifier"/> this is everything a GPU
/// needs to import the frame zero-copy (e.g. one <c>VkImage</c> per plane fd, or a disjoint
/// multi-plane image via <c>VK_EXT_image_drm_format_modifier</c>).
/// </summary>
/// <param name="Fd">dmabuf file descriptor backing this plane (may be shared across planes).</param>
/// <param name="Offset">Byte offset of the plane within its backing fd.</param>
/// <param name="Stride">Bytes per row of the plane.</param>
/// <param name="Size">Plane size in bytes, or 0 when the producer did not report it.</param>
public readonly record struct VideoPlane(long Fd, uint Offset, int Stride, uint Size);
