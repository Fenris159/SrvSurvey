# EDVR controller input without game action calls

Date checked: 2026-11-04.

An experimental frame-driven input path is viable for the reviewed EDVR
versions: SrvSurvey can attach its own controller actions to EDVR's existing
OpenXR session, synchronize them from the frame loop, and reuse the existing
pointer geometry and input mailbox. This is an implementation inference from
the source and API contracts below, not a real-headset result.

## EDVR evidence

The version reported in [SrvSurvey #264](https://github.com/Fenris159/SrvSurvey/issues/264),
EDVR `v0.22.0-rc.4`, resolves to commit
`ab1955de6846830690ea69626758127195844da8`. Its production `src/` and `include/`
trees contain no calls or entry-point loads for `xrCreateAction`,
`xrAttachSessionActionSets`, or `xrSyncActions`. The OpenXR host loads the
rendering/session functions and creates its instance with:

- `applicationName = "Elite Dangerous (EDVR)"`
- `engineName = "EDVR"`
- `applicationVersion = 0`, `engineVersion = 0` through zero-initialization.

The versions therefore cannot identify the EDVR package release. These are
source findings for the reviewed commit, not a guarantee about future EDVR.
See the [tagged release](https://github.com/characterecho-sean/edvr-unofficial-patch/releases/tag/v0.22.0-rc.4)
and [pinned instance creation / function loads](https://github.com/characterecho-sean/edvr-unofficial-patch/blob/ab1955de6846830690ea69626758127195844da8/src/openxr/native_runtime_host.h#L4207-L4254).

As a forward check, the production trees at main commit
`1da4ea9ea5cb9a876d625a6a6dcc7ccb864ed3f5` also contain no action creation,
attachment, or synchronization calls, and retain the same names and zero
versions. See the [pinned main instance creation](https://github.com/characterecho-sean/edvr-unofficial-patch/blob/1da4ea9ea5cb9a876d625a6a6dcc7ccb864ed3f5/src/openxr/native_runtime_host.h#L4306-L4315).

## OpenXR and VDXR constraints

Action sets attach once per `XrSession`, become immutable, and cannot be
detached to make room for a later application attachment. A second attachment
must return `XR_ERROR_ACTIONSETS_ALREADY_ATTACHED`. The attach contract has no
session-state restriction requiring attachment before `xrBeginSession`; a
first attachment from a running session's frame loop is therefore legal.
This last conclusion is an inference from the complete validity rules, not a
special layer exemption. See [OpenXR 1.1.63 input specification](https://github.com/KhronosGroup/OpenXR-Docs/blob/release-1.1.63/specification/sources/chapters/input.adoc#L648-L679)
and [attach reference](https://registry.khronos.org/OpenXR/specs/1.1/man/html/xrAttachSessionActionSets.html).

Synchronizing zero active sets is permitted: `countActiveActionSets` can be
zero and `activeActionSets` null. Unfocused synchronization returns
`XR_SESSION_NOT_FOCUSED` and leaves input inactive. Synchronization changes the
active sets for the entire session, so an independent sync must not overwrite
an application's active-set selection. See [sync structure validity](https://registry.khronos.org/OpenXR/specs/1.1/man/html/XrActionsSyncInfo.html)
and [synchronization contract](https://registry.khronos.org/OpenXR/specs/1.1/man/html/xrSyncActions.html).

The reported VDXR build, `6da20fe478e331c05672243fd82a9a1500cd39f5`, reinforces
these conclusions: attachment checks session validity and previous attachment
without a running-state gate; synchronization clears the active-set collection
even for a zero-set call. It separately checks focus. See [VDXR attachment](https://github.com/mbucchia/VirtualDesktop-OpenXR/blob/6da20fe478e331c05672243fd82a9a1500cd39f5/virtualdesktop-openxr/action.cpp#L458-L504)
and [VDXR synchronization](https://github.com/mbucchia/VirtualDesktop-OpenXR/blob/6da20fe478e331c05672243fd82a9a1500cd39f5/virtualdesktop-openxr/action.cpp#L1003-L1091).

## Initial implementation boundary

1. Require an explicit, persisted **Experimental EDVR controller input**
   preference, transported as a fresh, seqlock-protected mailbox flag. Also
   require the exact application and engine names, the existing Elite process
   check, and the existing VDXR / D3D11 route checks. Identity alone is not a
   version guarantee. The opt-in and input-source acknowledgement are tied to
   the current publisher generation so old reserved bytes cannot enable input.
2. Reuse the layer-owned actions and bindings already created during SrvSurvey
   instance initialization. If any application attach/sync calls have already
   been observed, keep the existing merged application-driven path and never
   attempt independent attachment.
3. Attach once while interaction is still off, then publish input readiness.
   This avoids a deadlock where the desktop refuses to enable interaction
   until the native handshake is ready, but native setup waits for enablement.
4. For the independent path only, sync owned sets once per running frame while
   enabled, sample poses/buttons, and publish through the existing mailbox.
   Sync zero active sets when disabled, focus is lost, or the publisher is
   stale. Preserve tracking, release-before-rearming, and capture cancellation
   behavior.
5. Preserve attachment over stop/start of the same session. Destroy action
   spaces with their session; a new session requires a new attachment. Input
   failures must leave the original game frame submission available.

Generic "no game calls observed yet" detection is insufficient. A game can
attach later, and another implicit layer can also own input. Once independent
sets attach, switching that session back to application-owned input is not
possible under core OpenXR. Stopping our pointer cannot undo the attachment;
session recreation or game restart is required. Do not fake a successful late
application attach or suppress its runtime error.

New bindings cannot be added to action sets that have already attached. The
existing immutable-generation handling must remain in force for discovery
session restarts. See [binding replacement and immutability](https://registry.khronos.org/OpenXR/specs/1.1/man/html/xrSuggestInteractionProfileBindings.html).

## Feedback required

Automated fake-runtime checks cover EDVR with no application input
calls, explicit-off zero-set sync, focused controller clicks, held-button
rearming, stop/start without reattachment, session recreation, attach/sync
failures preserving rendering, and an application-owned path that never gains
an extra frame-driven sync. They also verify publisher-generation opt-in and
acknowledgement, plus deactivation after overlay graphics failure. A late game
attach exposes the incompatibility rather than reporting false success.

Validation on 2026-11-04: the combined VR branch passes the repository quality
gate with 92.2% changed-code C# coverage, 5,695 desktop tests and 521 core tests.
The Windows native layer cross-compiles and passes its fake-runtime / software
D3D11 tests under Wine; the Linux packet and pointer tests pass. Deliberate
mutations of persistence, opt-in, synchronization, generation checks, application
ownership, and late-attachment error forwarding were detected and restored.

Physical acceptance still needs the reported Quest 3 / EDVR / VDXR combination,
both controllers, the calibration panel, dashboard focus loss, publisher
reconnection, and the co-installed OpenKneeboard / COVAS layers. Record exact
versions and native error codes; fake-runtime success does not establish that
those layers cooperate in the real session.
