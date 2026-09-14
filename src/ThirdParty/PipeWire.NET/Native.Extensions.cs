// Hand-maintained extension of the generated `Native` partial class.
//
// Why this file lives outside generated/: generate/generate.sh wipes the
// generated/ directory on each regeneration. Anything we hand-write must live
// elsewhere. We keep the same `PipeWire.NET.Generated` namespace and the same
// `static partial class Native` so consumers see these symbols seamlessly
// alongside generated declarations (e.g. `Native.PW_VERSION_STREAM_EVENTS`).
//
// Contents:
//   - PW_VERSION_* and PW_ID_* macro constants. ClangSharp 21 cannot translate
//     function-like macros + CompoundLiteralExpr macros like PW_MAP_RANGE_INIT
//     prevent generate-macro-bindings from running at all.
//   - SPA interface VTBL dispatch helpers (pw_core_get_registry,
//     pw_registry_add_listener) - these are C macros, not exported symbols.
//
// Verify against /usr/include/pipewire-0.3/pipewire/*.h when bumping PipeWire.

#pragma warning disable CS1591 // Missing XML comment - matches the suppression in generated/*.g.cs
#pragma warning disable CA1707 // Identifiers should not contain underscores (matches generated style)
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix

namespace PipeWire.NET.Generated;

public static unsafe partial class Native
{
    // - Property keys (pipewire/keys.h) -
    // These are #define string macros; ClangSharp can't emit them without
    // generate-macro-bindings (fatal on this header set - see pipewire.rsp).
    // Hand-declared here; values are stable across PipeWire 0.3.x.

    /// <summary><c>media.class</c> - node media class (e.g. "Video/Source").</summary>
    public const string PW_KEY_MEDIA_CLASS    = "media.class";
    /// <summary><c>media.type</c> - "Video" / "Audio".</summary>
    public const string PW_KEY_MEDIA_TYPE     = "media.type";
    /// <summary><c>media.category</c> - "Capture" / "Playback" / "Duplex".</summary>
    public const string PW_KEY_MEDIA_CATEGORY = "media.category";
    /// <summary><c>media.role</c> - "Camera" / "Music" / "Screen" / ...</summary>
    public const string PW_KEY_MEDIA_ROLE     = "media.role";
    /// <summary><c>node.name</c> - stable node name.</summary>
    public const string PW_KEY_NODE_NAME      = "node.name";
    /// <summary><c>node.description</c> - human-readable node name.</summary>
    public const string PW_KEY_NODE_DESCRIPTION = "node.description";
    /// <summary><c>node.nick</c> - short display name.</summary>
    public const string PW_KEY_NODE_NICK      = "node.nick";
    /// <summary><c>target.object</c> - bind a stream to a specific node by serial/name.</summary>
    public const string PW_KEY_TARGET_OBJECT  = "target.object";

    // - Sentinel ids -

    /// <summary>Wildcard node id - passed to pw_stream_connect to let the daemon auto-select.</summary>
    public const uint PW_ID_ANY  = 0xFFFFFFFFu;

    /// <summary>The well-known id of the PipeWire core object.</summary>
    public const uint PW_ID_CORE = 0u;

    // - Interface versions (struct pw_*_methods.version / pw_*_events.version) -

    public const uint PW_VERSION_CLIENT          = 3;
    public const uint PW_VERSION_CLIENT_EVENTS   = 0;
    public const uint PW_VERSION_CLIENT_METHODS  = 0;
    public const uint PW_VERSION_CONTEXT_EVENTS  = 1;
    public const uint PW_VERSION_CONTROL_EVENTS  = 0;
    public const uint PW_VERSION_CORE            = 4;
    public const uint PW_VERSION_CORE_EVENTS     = 1;
    public const uint PW_VERSION_CORE_METHODS    = 0;
    public const uint PW_VERSION_REGISTRY         = 3;
    public const uint PW_VERSION_REGISTRY_EVENTS  = 0;
    public const uint PW_VERSION_REGISTRY_METHODS = 0;
    public const uint PW_VERSION_DEVICE           = 3;
    public const uint PW_VERSION_DEVICE_EVENTS    = 0;
    public const uint PW_VERSION_FACTORY          = 3;
    public const uint PW_VERSION_FACTORY_EVENTS   = 0;
    public const uint PW_VERSION_GLOBAL_EVENTS    = 0;
    public const uint PW_VERSION_LINK             = 3;
    public const uint PW_VERSION_MODULE           = 3;
    public const uint PW_VERSION_NODE             = 3;
    public const uint PW_VERSION_PORT             = 3;
    public const uint PW_VERSION_PROXY_EVENTS     = 0;
    public const uint PW_VERSION_STREAM_EVENTS    = 2;
    public const uint PW_VERSION_FILTER_EVENTS    = 1;
    public const uint PW_VERSION_DATA_LOOP_EVENTS = 0;
    public const uint PW_VERSION_MAIN_LOOP_EVENTS = 0;

    // - SPA interface dispatch -
    // The PipeWire C API exposes many methods as macros that dispatch through
    // an SPA interface VTBL (struct spa_interface { spa_callbacks { funcs, data } }).
    // Each pw_* object (pw_core, pw_registry, ...) begins with a spa_interface,
    // so we cast object* -> spa_interface* and walk the callback table.

    /// <summary>Reads the SPA interface VTBL from a PipeWire object.</summary>
    /// <typeparam name="TMethods">The methods VTBL struct (e.g. <c>pw_core_methods</c>).</typeparam>
    /// <param name="obj">The PipeWire object (pw_core*, pw_registry*, etc.).</param>
    /// <param name="methods">[out] The typed methods VTBL.</param>
    /// <param name="userData">[out] User-data pointer to pass as the first arg of each method call.</param>
    public static void GetInterface<TMethods>(
        void* obj,
        out TMethods* methods,
        out void* userData) where TMethods : unmanaged
    {
        ArgumentNullException.ThrowIfNull(obj);
        var iface = (spa_interface*)obj;
        methods  = (TMethods*)iface->cb.funcs;
        userData = iface->cb.data;
    }

    /// <summary>
    /// Calls <c>pw_core_methods.get_registry</c> via SPA interface dispatch.
    /// Equivalent to the C macro <c>pw_core_get_registry()</c>.
    /// </summary>
    public static pw_registry* pw_core_get_registry(pw_core* core, uint version, nuint userDataSize)
    {
        GetInterface(core, out pw_core_methods* methods, out void* data);
        if (methods is null || methods->get_registry is null)
            throw new InvalidOperationException("pw_core has no get_registry method (interface VTBL is null).");
        return methods->get_registry(data, version, userDataSize);
    }

    /// <summary>
    /// Calls <c>pw_registry_methods.add_listener</c> via SPA interface dispatch.
    /// Equivalent to the C macro <c>pw_registry_add_listener()</c>.
    /// </summary>
    public static int pw_registry_add_listener(
        pw_registry* registry,
        spa_hook* listener,
        pw_registry_events* events,
        void* data)
    {
        GetInterface(registry, out pw_registry_methods* methods, out void* userData);
        if (methods is null || methods->add_listener is null)
            return -1;
        return methods->add_listener(userData, listener, events, data);
    }

    /// <summary>
    /// Calls <c>pw_proxy_methods.destroy</c> equivalent - pw_proxy_destroy IS exported,
    /// so this just forwards. Kept here for symmetry with the dispatch helpers above.
    /// </summary>
    public static void pw_registry_destroy(pw_registry* registry) =>
        pw_proxy_destroy((pw_proxy*)registry);
}
