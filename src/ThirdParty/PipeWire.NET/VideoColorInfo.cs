namespace PipeWire.NET;

/// <summary>Color quantization range (mirrors <c>spa_video_color_range</c>).</summary>
public enum VideoColorRange
{
    /// <summary>Unspecified.</summary>
    Unknown = 0,
    /// <summary>Full range, 0-255 ("PC" / JPEG range).</summary>
    Full_0_255 = 1,
    /// <summary>Limited range, 16-235 ("TV" / studio range).</summary>
    Limited_16_235 = 2,
}

/// <summary>YUV-to-RGB color matrix coefficients (mirrors <c>spa_video_color_matrix</c>).</summary>
public enum VideoColorMatrix
{
    /// <summary>Unspecified.</summary>
    Unknown = 0,
    /// <summary>Identity (RGB).</summary>
    Rgb = 1,
    /// <summary>ITU-R BT.709 (HD).</summary>
    Bt709 = 2,
    /// <summary>ITU-R BT.601 (SD).</summary>
    Bt601 = 3,
    /// <summary>ITU-R BT.2020 (UHD / wide gamut).</summary>
    Bt2020 = 4,
}

/// <summary>Opto-electronic transfer characteristic (mirrors <c>spa_video_transfer_function</c>).</summary>
public enum VideoTransferFunction
{
    /// <summary>Unspecified.</summary>
    Unknown = 0,
    /// <summary>Pure gamma 2.2.</summary>
    Gamma22 = 1,
    /// <summary>ITU-R BT.709.</summary>
    Bt709 = 2,
    /// <summary>sRGB.</summary>
    Srgb = 3,
    /// <summary>BT.2020 12-bit.</summary>
    Bt2020_12 = 4,
}

/// <summary>Color primaries / gamut (mirrors <c>spa_video_color_primaries</c>).</summary>
public enum VideoColorPrimaries
{
    /// <summary>Unspecified.</summary>
    Unknown = 0,
    /// <summary>ITU-R BT.709 (Rec.709 / sRGB gamut).</summary>
    Bt709 = 1,
    /// <summary>ITU-R BT.2020 (wide gamut).</summary>
    Bt2020 = 2,
}

/// <summary>
/// Color metadata for a video frame, parsed from the negotiated SPA format.
/// Needed for correct color reproduction and HDR / wide-gamut pipelines.
/// </summary>
/// <param name="Range">Quantization range (full vs limited).</param>
/// <param name="Matrix">YUV-to-RGB matrix coefficients.</param>
/// <param name="Transfer">Transfer characteristic (gamma curve).</param>
/// <param name="Primaries">Color primaries / gamut.</param>
public readonly record struct VideoColorInfo(
    VideoColorRange Range,
    VideoColorMatrix Matrix,
    VideoTransferFunction Transfer,
    VideoColorPrimaries Primaries)
{
    /// <summary>An all-unknown color info (source did not report color metadata).</summary>
    public static VideoColorInfo Unknown => default;
}
