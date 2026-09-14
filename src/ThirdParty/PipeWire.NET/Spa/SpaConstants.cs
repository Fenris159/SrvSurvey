using PipeWire.NET.Generated;

namespace PipeWire.NET.Spa;

// These static aliases let SpaPodBuilder/SpaPodReader and PipeWireStream refer to SPA
// constants by short stable names while the actual integer values come from the
// generated enums in PipeWire.NET.Generated. Re-generating the bindings (against a
// future PipeWire that renumbers an enum) automatically updates these.
//
// If you need a constant that isn't aliased here, reach directly into
// PipeWire.NET.Generated.spa_* - those are also public.

/// <summary>SPA pod type / object type constants - pulled from generated <see cref="Native"/>.</summary>
internal static class SpaType
{
    internal const uint None       = Native.SPA_TYPE_None;
    internal const uint Bool       = Native.SPA_TYPE_Bool;
    internal const uint Id         = Native.SPA_TYPE_Id;
    internal const uint Int        = Native.SPA_TYPE_Int;
    internal const uint Long       = Native.SPA_TYPE_Long;
    internal const uint Float      = Native.SPA_TYPE_Float;
    internal const uint Double     = Native.SPA_TYPE_Double;
    internal const uint String     = Native.SPA_TYPE_String;
    internal const uint Bytes      = Native.SPA_TYPE_Bytes;
    internal const uint Rectangle  = Native.SPA_TYPE_Rectangle;
    internal const uint Fraction   = Native.SPA_TYPE_Fraction;
    internal const uint Bitmap     = Native.SPA_TYPE_Bitmap;
    internal const uint Array      = Native.SPA_TYPE_Array;
    internal const uint Struct     = Native.SPA_TYPE_Struct;
    internal const uint Object     = Native.SPA_TYPE_Object;
    internal const uint Sequence   = Native.SPA_TYPE_Sequence;
    internal const uint Pointer    = Native.SPA_TYPE_Pointer;
    internal const uint Fd         = Native.SPA_TYPE_Fd;
    internal const uint Choice     = Native.SPA_TYPE_Choice;
    internal const uint Pod        = Native.SPA_TYPE_Pod;

    internal const uint ObjectFormat           = Native.SPA_TYPE_OBJECT_Format;
    internal const uint ObjectParamBuffers     = Native.SPA_TYPE_OBJECT_ParamBuffers;
    internal const uint ObjectParamMeta        = Native.SPA_TYPE_OBJECT_ParamMeta;
    internal const uint ObjectParamIo          = Native.SPA_TYPE_OBJECT_ParamIO;
    internal const uint ObjectParamProfile     = Native.SPA_TYPE_OBJECT_ParamProfile;
    internal const uint ObjectParamPortConfig  = Native.SPA_TYPE_OBJECT_ParamPortConfig;
    internal const uint ObjectParamRoute       = Native.SPA_TYPE_OBJECT_ParamRoute;
    internal const uint ObjectProfiler         = Native.SPA_TYPE_OBJECT_Profiler;
    internal const uint ObjectParamLatency     = Native.SPA_TYPE_OBJECT_ParamLatency;

    // SPA data plane types (spa_data.type) - pulled from spa_data_type enum
    internal const uint DataMemPtr  = (uint)spa_data_type.SPA_DATA_MemPtr;
    internal const uint DataMemFd   = (uint)spa_data_type.SPA_DATA_MemFd;
    internal const uint DataDmaBuf  = (uint)spa_data_type.SPA_DATA_DmaBuf;
}

/// <summary>
/// Flags for <c>spa_data.flags</c> (spa/buffer/buffer.h). A dmabuf a producer hands over for a
/// consumer to read is marked <see cref="Readable"/>.
/// </summary>
internal static class SpaDataFlag
{
    internal const uint Readable    = 1u << 0;
    internal const uint Writable    = 1u << 1;
    internal const uint Dynamic     = 1u << 2;
    internal const uint ReadWrite   = Readable | Writable;
}

/// <summary>Short aliases for the generated <see cref="spa_param_type"/> enum.</summary>
internal static class SpaParam
{
    internal const uint Invalid    = (uint)spa_param_type.SPA_PARAM_Invalid;
    internal const uint PropInfo   = (uint)spa_param_type.SPA_PARAM_PropInfo;
    internal const uint Props      = (uint)spa_param_type.SPA_PARAM_Props;
    internal const uint EnumFormat = (uint)spa_param_type.SPA_PARAM_EnumFormat;
    internal const uint Format     = (uint)spa_param_type.SPA_PARAM_Format;
    internal const uint Buffers    = (uint)spa_param_type.SPA_PARAM_Buffers;
    internal const uint Meta       = (uint)spa_param_type.SPA_PARAM_Meta;
    internal const uint IO         = (uint)spa_param_type.SPA_PARAM_IO;
    internal const uint Latency    = (uint)spa_param_type.SPA_PARAM_Latency;
}

/// <summary>Short aliases for the generated <see cref="spa_format"/> enum (object-property keys).</summary>
internal static class SpaFormatVideo
{
    internal const uint MediaType    = (uint)spa_format.SPA_FORMAT_mediaType;
    internal const uint MediaSubtype = (uint)spa_format.SPA_FORMAT_mediaSubtype;
    internal const uint Format       = (uint)spa_format.SPA_FORMAT_VIDEO_format;
    internal const uint Size         = (uint)spa_format.SPA_FORMAT_VIDEO_size;
    internal const uint Framerate    = (uint)spa_format.SPA_FORMAT_VIDEO_framerate;
    internal const uint Modifier     = (uint)spa_format.SPA_FORMAT_VIDEO_modifier;
    internal const uint ColorRange       = (uint)spa_format.SPA_FORMAT_VIDEO_colorRange;
    internal const uint ColorMatrix      = (uint)spa_format.SPA_FORMAT_VIDEO_colorMatrix;
    internal const uint TransferFunction = (uint)spa_format.SPA_FORMAT_VIDEO_transferFunction;
    internal const uint ColorPrimaries   = (uint)spa_format.SPA_FORMAT_VIDEO_colorPrimaries;
}

/// <summary>
/// Flags carried in a <c>spa_pod_prop</c> header (spa/pod/pod.h). Used when offering DRM format
/// modifiers: the first negotiation pass advertises the modifier choice with
/// <see cref="Mandatory"/> | <see cref="DontFixate"/> so the producer returns the filtered set of
/// modifiers it supports without collapsing it to one, letting the consumer fixate on a modifier
/// its GPU can actually import (the canonical two-step dmabuf modifier handshake).
/// </summary>
internal static class SpaPodPropFlag
{
    internal const uint Readonly   = 1u << 0;
    internal const uint Hardware   = 1u << 1;
    internal const uint HintDict   = 1u << 2;
    internal const uint Mandatory  = 1u << 3;
    internal const uint DontFixate = 1u << 4;
}

/// <summary>Short aliases for the generated <see cref="spa_media_type"/> enum.</summary>
internal static class SpaMediaType
{
    internal const uint Unknown = (uint)spa_media_type.SPA_MEDIA_TYPE_unknown;
    internal const uint Audio   = (uint)spa_media_type.SPA_MEDIA_TYPE_audio;
    internal const uint Video   = (uint)spa_media_type.SPA_MEDIA_TYPE_video;
    internal const uint Image   = (uint)spa_media_type.SPA_MEDIA_TYPE_image;
    internal const uint Binary  = (uint)spa_media_type.SPA_MEDIA_TYPE_binary;
    internal const uint Stream  = (uint)spa_media_type.SPA_MEDIA_TYPE_stream;
    internal const uint Application = (uint)spa_media_type.SPA_MEDIA_TYPE_application;
}

/// <summary>Short aliases for the generated <see cref="spa_media_subtype"/> enum.</summary>
internal static class SpaMediaSubtype
{
    internal const uint Unknown = (uint)spa_media_subtype.SPA_MEDIA_SUBTYPE_unknown;
    internal const uint Raw     = (uint)spa_media_subtype.SPA_MEDIA_SUBTYPE_raw;
    internal const uint Dsp     = (uint)spa_media_subtype.SPA_MEDIA_SUBTYPE_dsp;
}

/// <summary>Short aliases for the generated <see cref="spa_video_format"/> enum.</summary>
internal static class SpaVideoFormat
{
    internal const uint Unknown = (uint)spa_video_format.SPA_VIDEO_FORMAT_UNKNOWN;
    internal const uint I420    = (uint)spa_video_format.SPA_VIDEO_FORMAT_I420;
    internal const uint YUY2    = (uint)spa_video_format.SPA_VIDEO_FORMAT_YUY2;
    internal const uint RGBA    = (uint)spa_video_format.SPA_VIDEO_FORMAT_RGBA;
    internal const uint BGRA    = (uint)spa_video_format.SPA_VIDEO_FORMAT_BGRA;
    internal const uint RGBx    = (uint)spa_video_format.SPA_VIDEO_FORMAT_RGBx;
    internal const uint BGRx    = (uint)spa_video_format.SPA_VIDEO_FORMAT_BGRx;
    internal const uint NV12    = (uint)spa_video_format.SPA_VIDEO_FORMAT_NV12;
}

/// <summary>Short aliases for the audio side of <see cref="spa_format"/>.</summary>
internal static class SpaFormatAudio
{
    internal const uint Format   = (uint)spa_format.SPA_FORMAT_AUDIO_format;
    internal const uint Rate     = (uint)spa_format.SPA_FORMAT_AUDIO_rate;
    internal const uint Channels = (uint)spa_format.SPA_FORMAT_AUDIO_channels;
}

/// <summary>Short aliases for the generated <see cref="spa_audio_format"/> enum.</summary>
internal static class SpaAudioFormat
{
    internal const uint Unknown = (uint)spa_audio_format.SPA_AUDIO_FORMAT_UNKNOWN;
    internal const uint U8      = (uint)spa_audio_format.SPA_AUDIO_FORMAT_U8;
    internal const uint S16Le   = (uint)spa_audio_format.SPA_AUDIO_FORMAT_S16_LE;
    internal const uint S24Le   = (uint)spa_audio_format.SPA_AUDIO_FORMAT_S24_LE;
    internal const uint S32Le   = (uint)spa_audio_format.SPA_AUDIO_FORMAT_S32_LE;
    internal const uint F32Le   = (uint)spa_audio_format.SPA_AUDIO_FORMAT_F32_LE;
}

/// <summary>Property keys of a ParamMeta object (spa_param_meta).</summary>
internal static class SpaParamMeta
{
    internal const uint Type = (uint)spa_param_meta.SPA_PARAM_META_type;
    internal const uint Size = (uint)spa_param_meta.SPA_PARAM_META_size;
}

/// <summary>SPA metadata type IDs (spa_meta_type).</summary>
internal static class SpaMetaType
{
    internal const uint Header = (uint)spa_meta_type.SPA_META_Header;
}

/// <summary>Property keys of a ParamBuffers object (spa_param_buffers).</summary>
internal static class SpaParamBuffers
{
    internal const uint Buffers  = (uint)spa_param_buffers.SPA_PARAM_BUFFERS_buffers;
    internal const uint Blocks   = (uint)spa_param_buffers.SPA_PARAM_BUFFERS_blocks;
    internal const uint Size     = (uint)spa_param_buffers.SPA_PARAM_BUFFERS_size;
    internal const uint Stride   = (uint)spa_param_buffers.SPA_PARAM_BUFFERS_stride;
    internal const uint Align    = (uint)spa_param_buffers.SPA_PARAM_BUFFERS_align;
    internal const uint DataType = (uint)spa_param_buffers.SPA_PARAM_BUFFERS_dataType;
}

/// <summary>SPA choice type IDs (spa_choice_type).</summary>
internal static class SpaChoiceType
{
    internal const uint None  = 0;
    internal const uint Range = 1;
    internal const uint Step  = 2;
    internal const uint Enum  = 3;
    internal const uint Flags = 4;
}
