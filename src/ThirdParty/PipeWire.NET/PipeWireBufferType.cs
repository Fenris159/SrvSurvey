namespace PipeWire.NET;

/// <summary>
/// How a frame's pixel/sample data is backed in memory (mirrors <c>spa_data.type</c>).
/// </summary>
/// <remarks>
/// For zero-copy GPU pipelines, check for <see cref="DmaBuf"/> and import
/// <see cref="VideoFrame.Fd"/> directly into the GPU rather than reading the CPU span.
/// </remarks>
public enum PipeWireBufferType
{
    /// <summary>Unknown / unmapped.</summary>
    Unknown,
    /// <summary>Plain host memory pointer (<c>SPA_DATA_MemPtr</c>) - read via the data span.</summary>
    MemPtr,
    /// <summary>Memory-mapped file descriptor (<c>SPA_DATA_MemFd</c>).</summary>
    MemFd,
    /// <summary>DMA-BUF file descriptor (<c>SPA_DATA_DmaBuf</c>) - import the fd for zero-copy.</summary>
    DmaBuf,
}
