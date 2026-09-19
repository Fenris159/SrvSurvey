# Wayland portal screen-capture failure

Date checked: 2026-09-19

Starting SrvSurvey revision: `93d2a59aae3819c43023f9f3b4fd2bc434c8d603`

## Conclusion

The two supplied RC49.0 logs identify a specific failure after successful portal source selection. In both attempts, SrvSurvey fell back from X11 to the Wayland portal, reported portal version 5, and accepted either a window or monitor. It then failed with:

> `MarshalDirectiveException: Setting SetLastError to 'true' is not supported when runtime marshalling is disabled.`

The failure was not caused by choosing the wrong source in the picker, a denied ScreenCast request, or PipeWire failing to deliver a frame. It occurred while SrvSurvey's vendored PipeWire.NET assembly invoked its local `dup`/`close` declarations with `SetLastError = true`. That assembly has `[DisableRuntimeMarshalling]`, and .NET explicitly disables `SetLastError` support for P/Invokes declared in such an assembly ([.NET disabled runtime marshalling](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/disabled-marshalling); [.NET 10 API reference](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.disableruntimemarshallingattribute?view=net-10.0)).

Commit `6954f25db97fc603e2a2daf3f709c6e21e8f712c` correctly removed `SetLastError = true` from those declarations. The code checks only the native return values and never reads the captured error, so removing the flag does not discard information the current implementation uses. The focused regression test passes on this Ubuntu Wayland host with .NET 10 and reaches `pw_context_connect_fd` without a `MarshalDirectiveException`.

That fix removes the demonstrated RC49.0 blocker, but it does not by itself prove end-to-end capture. This review also corrected descriptor ownership, failed-start cleanup, and stable PipeWire serial targeting for ScreenCast version 6. Portal/PipeWire disconnection recovery and one compositor-approved live frame test remain. Keeping image-based features disabled by default is defensible until that live check succeeds on the target desktops.

## Runtime marshalling and the demonstrated fault

`src/ThirdParty/PipeWire.NET/generated/DisableRuntimeMarshalling.g.cs` applies `[DisableRuntimeMarshalling]` to the vendored PipeWire.NET assembly. Under that mode, the runtime supports only a restricted unmanaged P/Invoke surface and does not support `SetLastError = true`. The restriction applies to declarations in the attributed assembly; it does not disable marshalling for P/Invokes declared in other assemblies ([official .NET behavior](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/disabled-marshalling)). Tmds.DBus can therefore continue to use its own interop declarations independently.

Before `6954f25d`, `PipeWireContext.StartAsync(SafeHandle)` reached `DuplicateFileDescriptor`, whose `DllImport` requested `SetLastError`. That matches both the exception text and its position in the logs: the ScreenCast `Start` request had already returned the selected stream, while the later PipeWire-connected and first-frame messages never appeared. The current declarations omit `SetLastError` and are valid in the runtime-marshalling-disabled assembly.

The generated `pw_context_connect_fd` binding itself uses only unmanaged pointers, integers, and `nuint`, which are supported under disabled runtime marshalling. No evidence points to that generated signature as the reported fault.

## Portal and descriptor lifecycle

SrvSurvey uses the required ScreenCast order: `CreateSession`, `SelectSources`, `Start`, and then `OpenPipeWireRemote`. The portal defines that sequence and returns the selected PipeWire streams from `Start` ([ScreenCast interface](https://github.com/flatpak/xdg-desktop-portal/blob/a68391a7eaaada041bed30157ed391534b5b462a/data/org.freedesktop.portal.ScreenCast.xml#L16-L53)). `OpenPipeWireRemote` returns a permission-scoped connection that can see only the nodes exported by that ScreenCast session ([portal implementation](https://github.com/flatpak/xdg-desktop-portal/blob/a68391a7eaaada041bed30157ed391534b5b462a/desktop-portal/screen-cast.c#L977-L1064)). The portal descriptor must therefore be used to connect the PipeWire context; opening the user's ordinary PipeWire remote would bypass the permission-scoped graph.

The current Tmds.DBus mapping is appropriate. Tmds.DBus 0.95.1 maps `SafeHandle` to the Unix-FD D-Bus type ([signature mapping](https://github.com/tmds/Tmds.DBus/blob/491bde2c16d65a7904397933cffa24e15e45eb2a/src/Tmds.DBus/Signature.cs#L769-L772)), constructs an owning `CloseSafeHandle` for a received descriptor ([message reader](https://github.com/tmds/Tmds.DBus/blob/491bde2c16d65a7904397933cffa24e15e45eb2a/src/Tmds.DBus/Protocol/MessageReader.cs#L348-L358)), and closes it on disposal ([handle implementation](https://github.com/tmds/Tmds.DBus/blob/491bde2c16d65a7904397933cffa24e15e45eb2a/src/Tmds.DBus/CloseSafeHandle.cs#L13-L43)). SrvSurvey correctly keeps that handle alive with `using` and passes the safe handle, rather than an unprotected raw integer, to PipeWire.NET.

PipeWire takes ownership of the descriptor accepted by `pw_context_connect_fd` and closes it when the core disconnects or errors ([official declaration and ownership contract](https://github.com/PipeWire/pipewire/blob/decc0d2efa2db7476faaa70043774b722317da78/src/pipewire/core.h#L617-L631)). Duplicating the portal-owned descriptor before that ownership transfer is therefore correct. PipeWire's implementation does not install the descriptor into its I/O source until partway through connection setup ([core connection](https://github.com/PipeWire/pipewire/blob/decc0d2efa2db7476faaa70043774b722317da78/src/pipewire/core.c#L434-L456); [native-protocol handoff](https://github.com/PipeWire/pipewire/blob/decc0d2efa2db7476faaa70043774b722317da78/src/modules/module-protocol-native.c#L1205-L1244)). Consequently, the current catch-path close is needed when connection fails synchronously before PipeWire takes ownership; removing it would leak the duplicate.

The review found and corrected two descriptor-lifecycle problems:

- Plain `dup()` cleared close-on-exec, so the permission-scoped socket could be inherited by a child process started later ([Linux `dup(2)`](https://man7.org/linux/man-pages/man2/dup.2.html)). The code now duplicates with `fcntl(F_DUPFD_CLOEXEC)` and represents the duplicate with an owning `SafeFileHandle` until the ownership transfer. The newer upstream PipeWire.NET work follows the same pattern ([descriptor helper](https://github.com/Agash/PipeWire.NET/blob/2bf1f58c56786756d615a1e779a51d149a63240b/src/PipeWire.NET/Interop/FdInterop.cs#L39-L86); [context handoff](https://github.com/Agash/PipeWire.NET/blob/2bf1f58c56786756d615a1e779a51d149a63240b/src/PipeWire.NET/Core/PipeWireContext.cs#L624-L703)).
- `StartNative(int)` started the thread loop before calling `pw_context_connect_fd`, but `_started` was set only after `StartNative` returned. If connection returned null, disposal did not stop the already-started loop before destroying its objects. PipeWire requires the thread loop to be stopped before destruction ([thread-loop lifecycle](https://docs.pipewire.org/page_thread_loop.html)). The code now tracks loop startup separately and stops it on connection failure.

## Session and stream recovery

A portal session can be closed by the compositor or backend at any time and reports this through `org.freedesktop.portal.Session.Closed` ([Session interface](https://github.com/flatpak/xdg-desktop-portal/blob/a68391a7eaaada041bed30157ed391534b5b462a/data/org.freedesktop.portal.Session.xml#L12-L59)). SrvSurvey's local `ISession` exposes only `CloseAsync`; it never subscribes to `Closed`. After initialization succeeds, the cached initialization task also remains successful indefinitely. A later closed session therefore produces frame timeouts while retries continue to reuse dead resources.

PipeWire exposes explicit `Error` and `Unconnected` stream states and a state-change callback ([PipeWire stream API](https://docs.pipewire.org/group__pw__stream.html)). The vendored `PipeWireVideoCapture` already surfaces `StateChanged`, but `WaylandPortalGameScreenCapture` subscribes only to `FrameReady`. It should treat `Error` or unexpected `Unconnected` as immediate capture failure, complete any pending request with the native error, dispose the failed session/context, and let the next request create a new portal session. The portal `Closed` signal should take the same invalidation path. This converts the present five-second generic no-frame timeout into an actionable error and makes retries useful.

The first-frame timeout starts only after synchronous `EnsureInitialized()` completes. Portal method calls themselves are awaited without cancellation, and `Dispose()` waits synchronously for initialization. A stalled D-Bus method can therefore delay shutdown or feature disablement. Apply shutdown cancellation to each method call as well as to the later response wait, and avoid synchronously waiting forever during disposal.

Request handles are correctly subscribed before each portal method is called, which avoids missing a fast response. Two protocol details can still be improved. The portal requires `handle_token` and `session_handle_token` values to be unique and not guessable, while SrvSurvey currently derives them from the process ID and a counter. It also permits a request to be reattached through the object path returned by the method ([Request interface](https://github.com/flatpak/xdg-desktop-portal/blob/a68391a7eaaada041bed30157ed391534b5b462a/data/org.freedesktop.portal.Request.xml#L24-L43)). Generate random valid tokens and handle a different returned request path instead of treating it as an unconditional protocol error.

## Portal version 6 stream identity

ScreenCast version 6 adds `pipewire-serial` to each stream and deprecates relying only on the numeric node ID because node IDs can be reused. Clients should connect with `PW_ID_ANY` and set `PW_KEY_TARGET_OBJECT` to the decimal serial ([ScreenCast stream metadata](https://github.com/flatpak/xdg-desktop-portal/blob/a68391a7eaaada041bed30157ed391534b5b462a/data/org.freedesktop.portal.ScreenCast.xml#L213-L286); [PipeWire stream target guidance](https://github.com/PipeWire/pipewire/blob/decc0d2efa2db7476faaa70043774b722317da78/src/pipewire/stream.h#L540-L562); [`target.object` key](https://github.com/PipeWire/pipewire/blob/decc0d2efa2db7476faaa70043774b722317da78/src/pipewire/keys.h#L391-L398)).

The vendored `PipeWireVideoCapture.Connect` already accepts `targetObjectName`. The application now parses the unsigned 64-bit `pipewire-serial`, formats it with the invariant culture, and connects with `PW_ID_ANY` plus that target. It retains the existing numeric-node path when the serial is absent. Both supplied logs report version 5, so missing serial support did not cause the reported RC49.0 exception.

## Verification performed and tests still needed

The current Ubuntu host runs a Wayland session with PipeWire 1.6.2, WirePlumber, and `xdg-desktop-portal`; the live ScreenCast interface reports version 5 with monitor and window sources. The user-facing picker was not opened during this investigation because it would interrupt the active desktop session.

The focused .NET 10 tests pass. They cover the original marshalling exception, close-on-exec duplication, failed-start loop cleanup, and portal v6 serial targeting with a version 5 fallback:

```console
dotnet test tests/SrvSurvey.Desktop.Tests/SrvSurvey.Desktop.Tests.csproj \
  --no-restore \
  --filter FullyQualifiedName~PipeWirePortalMarshallingTests
```

The portal descriptor test uses an owned Linux safe handle and reaches the native PipeWire connection attempt. Its `/dev/null` descriptor intentionally cannot produce a PipeWire stream, so it verifies the handoff rather than compositor capture.

A separate noninteractive smoke test used the live PipeWire 1.6.2 daemon to link the bundled 64×32 BGRA producer to the bundled capture client. The client received the expected pixel data and both streams shut down cleanly. That verifies format negotiation, CPU-readable frame delivery, and cleanup without opening the portal chooser. It still does not substitute for a compositor-approved portal stream.

The remaining boundaries are:

1. Simulate `Session.Closed` and PipeWire `Error`/`Unconnected`; assert that a pending capture fails immediately and the next capture creates a fresh session.
2. Bound portal method calls and disposal with cancellation tests.
3. Complete one live integration check on Wayland: select the Elite Dangerous window (and separately the display fallback), observe `PipeWire connected`, receive a first frame, and verify crop coordinates. That final check requires a compositor-approved source and cannot be replaced by a headless unit test.

The recurring Tmds.DBus input-method messages in the logs are unrelated to the ScreenCast failure: the decisive capture exception names the unsupported `SetLastError` marshalling option, and it appears only after portal selection succeeds.
