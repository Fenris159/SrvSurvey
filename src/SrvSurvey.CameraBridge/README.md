# Cockpit camera bridge prototype

This experimental D3D11 observer supplies SrvSurvey with a live, local **camera projection relative to the cockpit**. It does not enable VR, replace native freelook, or run frame-analysis dumps during normal use.

Three controlled game captures establish the rigid-reference calculation: centered view faces forward; native freelook changes it by approximately 43.63°; turning the ship with centered freelook preserves cockpit anchoring. The current standalone harness uses a different PS source, bindings, offsets and partial buffer view; it produces the expected verified camera packets through real D3D11 staged readbacks, resumes after context reacquisition and stops after lease expiry. The versioned profile loader has separate Windows API checks. Loading the current build through the actual game's EDHM chain still needs confirmation. Other ships, UI states, camera modes and game updates remain unvalidated.

The default profile is inactive. `Profiles/EliteOdyssey-RigidCockpit.ini` explicitly selects the captured experimental source. Observation also requires the app's opt-in setting and a fresh local heartbeat.

## Independent profile refresh

At SrvSurvey launch, an enabled cockpit mode checks the versioned profile in the
`Fenris159/SrvSurvey` GitHub repository's `SrvSurvey-Avalonia` branch. The source is
`src/SrvSurvey.CameraBridge/Profiles/EliteOdyssey-RigidCockpit.ini`; it becomes
available to clients when published to that branch. This is a small configuration
download, independent of an application or DLL release.

Downloads are limited to 8 KiB with a 15-second deadline. The app checks the
packet protocol and a strict data-only envelope. It preserves declarative layout
fields, including fields intended for a newer DLL, without interpreting them.
Paths, commands, extra INI sections and duplicate fields are rejected. The bridge
owns layout compatibility and rejects unsupported fields, formats or reader
versions before capture; a desktop-app rebuild is not needed to forward a new
DLL's declarative settings.

A newer `ProfileVersion` is required for any content change. Equal-version
changes, older versions, malformed content and unsupported packet protocols
retain the current cache. A failed network check retains the cache. One
predecessor is saved as `profile.ini.previous`; the active cache is replaced
atomically under a cross-instance lock. At graphics startup the bridge chooses
the newest usable profile from the installed baseline, retained reader snapshot, predecessor and cache.
It cannot enable an inactive installation.

The machine-local cache is `cockpit-camera/profile.ini` below SrvSurvey's cache
directory: normally `%LOCALAPPDATA%\SrvSurvey\cache` on Windows, or
`$XDG_CACHE_HOME/SrvSurvey` / `~/.cache/SrvSurvey` on Linux. It is downloadable
reference data, separate from player profiles and backups. Disabled cockpit mode
and diagnostic replay do not perform this launch check.

The installer writes a stable `SrvSurveyCameraBridge.paths.ini` locator beside
the DLL, using UTF16 with a BOM so Windows INI APIs preserve Unicode paths. On
Proton the locator uses the standard `Z:` host-filesystem mapping. For a custom
SrvSurvey cache root, pass `-ProfileCachePath '<absolute cache>/cockpit-camera/profile.ini'`
when installing. Automatic refresh never modifies the installed DLL, profile,
locator or installer receipt, so rollback ownership checks remain valid.

The bridge reads the installed and cached profiles once during graphics startup.
It uses a newer compatible cache only when the installed experimental profile is
already enabled. A missing or rejected cache falls back to that installed profile.
The bridge keeps its last decoder-supported source in `profile.ini.reader-<reader version>`, independently of the app's one-step download history. Successive catalogs that need a newer DLL therefore cannot discard the current reader's fallback. Each reader owns its own retained file. Retention serializes the selected value, coordinates writers with a filesystem sharing lock, and rechecks the revision before replacing the snapshot. If retention cannot complete within a bounded startup wait, the reader uses its already retained or installed fallback.

Restart Elite to load a newly downloaded profile; restarting SrvSurvey alone does
not reload the game's bridge. Global overlay settings show the refresh result
separately from live camera/calibration status.

For a routine source update, maintainers validate the source, edit the descriptor,
and increment `ProfileVersion`. Shader identity, stage/slot bindings, buffer sizes,
record headers/strides, field offsets, numeric packing, matrix conventions and
selection thresholds can change without rebuilding either binary. The bundled
revision 2 describes the same captured source as revision 1, using the new
explicit `[Layout]` section.

`RigidLayoutVersion=2` selects the declarative rigid-root decoder; version 1 keeps
the legacy fixed layout. Older DLLs reject version 2 instead of ignoring new
offsets. `SchemaVersion=1` and `RequiredBridgeVersion=2` identify this descriptor
and reader. A future descriptor may require a newer DLL; the app can cache and
forward it unchanged. A new decoding algorithm, unsupported numeric format,
rendering API or hook still needs a DLL update. This catalog never downloads or
executes native code. Packet-protocol changes may also require an app update.

## Build and check

Portable checks on Linux:

```sh
cmake -S src/SrvSurvey.CameraBridge -B /tmp/srvsurvey-camera-tests
cmake --build /tmp/srvsurvey-camera-tests
ctest --test-dir /tmp/srvsurvey-camera-tests --output-on-failure
pwsh -NoProfile -File src/SrvSurvey.CameraBridge/tests/InstallerTests.ps1
```

Cross-build and package the x64 Windows DLL for Windows or Wine/DXVK:

```sh
cmake -S src/SrvSurvey.CameraBridge -B /tmp/srvsurvey-camera-win64 \
  -DCMAKE_SYSTEM_NAME=Windows \
  -DCMAKE_CXX_COMPILER=x86_64-w64-mingw32-g++ \
  -DCMAKE_BUILD_TYPE=Release -DBUILD_TESTING=OFF
cmake --build /tmp/srvsurvey-camera-win64
cmake --install /tmp/srvsurvey-camera-win64 --prefix /tmp/srvsurvey-camera-package
```

On Windows use a normal x64 CMake/MSVC build. Compiler warnings fail the native build. There are no downloaded native dependencies.

## Standalone native integration harness

Build with `-DCAMERA_BRIDGE_INTEGRATION_TESTS=ON`, then run `CameraBridgeWindowsIntegration.exe` with the full path to the built DLL. The harness creates its own temporary DLL/profile, a non-activating window, actual D3D11/DXGI resources and a loopback receiver. It supplies captured fixture data to real staged GPU readbacks; it does not mock COM objects or touch a game installation.

`--transient-reacquire` verifies that an acquisition released before the actual final release does not leak the observation transport. `--overlapping-release` verifies cleanup when two release callbacks overlap around a third transient acquisition. Each race scenario resets its barriers, requires completion without timeout, and must run separately; combining race flags is rejected before creating graphics resources. `--concurrent-reacquire` delays the external COM Release boundary while another thread reacquires the same context. `--omit-device-output` and `--swapchain-only` exercise valid omitted API outputs. These modes are isolated harness tests and never attach to Elite.

`--present-test` exercises the actual Present hook while avoiding monitor-timing work on a headless display. `--catalog-layout` selects PS identity and bindings, relocated record/camera offsets, a changed stride and a partial SRV; the expected camera packet is unchanged. `--mapped-window` is intended only for an isolated test display such as Xvfb; the default window stays hidden. `--without-observer` runs a differential control with native observation disabled.

Controlled runs in a new private Wine prefix passed the live packet, external-reference-zero/reacquisition and lease-expiry checks. A normal Present in that fresh/headless environment encounters a DXGI zero-refresh division; Present(TEST) avoids it. Both observed and unhooked control runs encounter an X11 I/O failure during DXGI teardown, including a bounded retry inside the actual Steam runtime. The harness therefore does **not** currently report a clean end-to-end exit in this environment. This baseline limitation must not be described as a green native integration run or as proof that the actual game chain is tested.

## Reversible installation

Exit Elite first. From the generated bridge package, run PowerShell 7.2 or newer:

```powershell
./Install-CameraBridge.ps1 -GameDirectory '<Elite Odyssey executable directory>' `
  -BridgeDirectory . -CaptureProfile ./Profiles/EliteOdyssey-RigidCockpit.ini
```

For Linux/Proton use the corresponding Linux game directory. The script refuses to operate while an `EliteDangerous*` process exists. It inspects process names only.

The helper preserves the existing EDHM/3DMigoto `d3d11.dll`. It copies `SrvSurveyCameraBridge.dll` and the selected profile as `SrvSurveyCameraBridge.ini`, generates the cache locator, then sets the existing `d3dx.ini` `[System]` options:

```ini
proxy_d3d11=SrvSurveyCameraBridge.dll
load_library_redirect=0
```

A different existing proxy, unowned bridge files, ambiguous sections/options or an installed build with changed files causes refusal. There is no force option. Upgrades require uninstalling the verified earlier installation first. Repeating the same installation is idempotent.

Before changing configuration it saves the exact original `d3dx.ini` and SHA256-backed installation manifest in a timestamped `SrvSurveyCameraBridge.backup-*` directory. UTF8/UTF16 BOMs and existing line endings are preserved; legacy single-byte comments round-trip without changing their bytes. Unrelated options and comments stay intact. Failed installations roll back files they can still identify as their own; backups remain available.

Enable cockpit mode in SrvSurvey, restart SrvSurvey, and let its launch-time catalog check finish before starting Elite. The bridge itself verifies the executing shader's full-bytecode identity before observing a draw. Missing or rejected source data produces no pose packet; the app can keep its normal fixed overlay placement.

To uninstall, exit Elite and run:

```powershell
./Install-CameraBridge.ps1 -GameDirectory '<Elite Odyssey executable directory>' -Uninstall
```

Uninstall first verifies the current configuration, all installed files and the original backup against the manifest. If any changed, it leaves them untouched and reports the backup path. Otherwise it restores the exact original configuration, removes its files and active manifest, and retains the backup directory. Earlier two-file installation receipts can also be uninstalled.

Disabling the app option stops heartbeat renewal; GPU observation stops within two seconds. Removing or replacing the loaded DLL requires exiting the game. Unloading patched code from a running game is unsupported.

## Verified source profile

`SrvSurveyCameraBridge.ini` is read once when the first device is observed:

| Field | Meaning |
|---|---|
| `Validated` | Must be 1 for the explicitly validated experimental profile. |
| `ProfileVersion` | Positive revision counter; cached revisions must be newer than the installed profile. |
| `ProtocolVersion` | Must be 1 for the bridge/app packet format. |
| `VertexShaderHash` | Executing bytecode's 64-bit 3DMigoto hash, hexadecimal. The legacy key name is retained; `ShaderStage` selects VS or PS. The captured VS is `69cbbf4aae46746f`. |
| `RigidLayoutVersion` | 1 for the legacy fixed layout; 2 for the declarative rigid-reference decoder. |
| `DrawKind` | 1=DrawIndexed, 2=Draw, 3=DrawIndexedInstanced, 4=DrawInstanced. Captured source uses 3. |

The `[Layout]` descriptor exposes these data-only settings. The supplied profile
lists every default explicitly. Names are case insensitive; offsets are bytes.

| Settings | Supported values / role |
|---|---|
| `SchemaVersion`, `RequiredBridgeVersion` | Descriptor schema 1; minimum reader 2. Unknown fields or a newer reader fail closed. |
| `ShaderStage` | `vs` or `ps`; actual shader identity must match. |
| `RecordStage`, `RecordSlot`, `CameraStage`, `CameraSlot` | Resource/constant-buffer stage (`vs`, `ps`, `gs`, `hs`, `ds`, `cs`) and binding slot. |
| `BufferKind`, `BufferElementStride`, `RecordStride`, `RecordDataOffset` | Structured or raw resource; hardware element size, logical record size and optional header. Element stride 0 accepts the resource's stride. Raw views use 4-byte elements. Partial views respect their first element and length. |
| `CameraBytes`, `MaximumBufferBytes` | Expected constant-buffer length and resource size cap. Compiled hard limits remain 64 KiB and 8 MiB. |
| `PositionOffset`, `PositionFormat`, `PositionScale` | Root position: `float32` or `float64`, scaled into metres. |
| `RotationOffset`, `RotationFormat`, `RotationOrder`, `RotationConjugate` | Quaternion: `unorm16`, `snorm16`, `float32`, `float64`; `xyzw` or `wxyz`; optional conjugation (0/1). |
| `ProjectionOffset`, `ProjectionFormat`, `ProjectionTranspose`, `ProjectionIncludesOrigin` | Perspective matrix, scalar format and transpose (0/1). Origin flag 0 subtracts the camera before projection; 1 uses a world-to-clip matrix that already includes translation. |
| `CameraPositionOffset`, `CameraPositionFormat`, `CameraPositionScale` | Camera origin and units, also used to find nearby root records. |
| `AxesSource`, `AxesOffset`, `AxesFormat`, `AxesStride`, `AxesTranspose` | Orthonormal camera axes from `buffer`, or derived from `projection`. Row stride and optional transpose describe stored axes. |
| `CanonicalNearDepth` | Output clip-depth normalization; source depth conventions can change without changing the app's projection reader. |
| `NearRadius`, `PositionTolerance`, `AngleToleranceDegrees` | Nearby-candidate radius, rigid-position tolerance and angular tolerance. |
| `QuaternionTolerance`, `CameraTolerance` | Unit-quaternion and orthonormal-camera validation tolerances. |
| `MinimumMembers`, `MaximumNearRecords`, `DominanceRatio` | Required support, bounded candidate count (at most 256), dominance over runner-up (at least 2×). |
| `MinimumViewportWidth`, `MinimumViewportHeight`, `ViewportMatchesTarget` | Main-pass viewport checks and optional render-target size match (0/1). |

The decoder accepts centered perspective projections with orthonormal right-handed
camera axes, normalizes the clip-depth row, and always exports the same packet
format. The descriptor does not express arbitrary transform programs. All reads,
resource ranges, sizes, enum values and thresholds are checked before use.

The configured `shader_hash=3dmigoto` is **zero-seeded FNV-1 over the complete DXBC blob**, multiplying before XOR. FNV-1a, the usual nonzero FNV offset basis and a shader-code-chunk-only hash do not match. Primary implementation: [mode mapping](https://github.com/bo3b/3Dmigoto/blob/master/DirectX11/globals.h), [full-bytecode hashing](https://github.com/bo3b/3Dmigoto/blob/master/DirectX11/HackerDevice.cpp), [FNV routine](https://github.com/bo3b/3Dmigoto/blob/master/util.h). An EDHM replacement shader can have a different executing hash; it will fail this exact identity check rather than silently using another source.

At the selected shader's main-viewport draw, the observer copies these two resources coherently before the draw:

- `VS t33`: the structured view, 336-byte elements, at most 8 MiB. Current captures contain 3,096,576 bytes / 9,216 elements.
- `VS b1`: 5,376 bytes. Float4 rows 270–273 hold rotation/projection, row 275 the camera origin, and rows 277–279 the camera axes.

Each candidate instance has four little-endian UNORM16 quaternion components at byte 8, decoded as `word/32767.5−1` and normalized, and a float3 world position at byte 16. Records must lie within 3 m of the camera, with quaternion norm error below 0.001. The observer finds a joint position/orientation group within 1 mm and 0.25°, requiring at least 8 members and at least twice the runner-up support. More than 256 nearby candidates, ambiguous groups, invalid camera axes or a non-perspective pass fail closed.

The captured dominant groups contain 21, 22 and 23 records; each runner-up contains 6. They follow the ship while their camera-relative eye offset remains constant within approximately 50 microns. Instance indices compact between frames, so no permanently saved index, asset offset, `FirstInstance` or centered world-camera heading is used as the cockpit reference.

The exported column-vector WVP is:

```text
transpose(CB1 float4 rows 270..273) × affine(root quaternion rotation, root position − camera position)
```

This is a full invertible 3D transform, including translation. Its arbitrary local z=0 plane may pass through the camera; the full transform remains valid. The consumer transposes once for System.Numerics row-vector use.

## Live transport and lifetime

The bridge listens only on UDP `127.0.0.1:47765`. The app uses an ephemeral endpoint and a random session nonce. A request renews a two-second observation lease. Without a live lease no GPU copies are made. The nonce binds responses to the current app session; it is not a security boundary against other programs running as the same user.

Request, exactly 40 bytes:

| Offset | Bytes | Value |
|---:|---:|---|
| 0 | 8 | ASCII `SRVCAM01` |
| 8 | 32 | Random nonzero nonce |

Response, exactly 136 bytes; integers and IEEE754 floats use little endian:

| Offset | Bytes | Value |
|---:|---:|---|
| 0 | 8 | ASCII `SRVCAM01` |
| 8 | 4 | Version 1 |
| 12 | 4 | Packet length 136 |
| 16 | 8 | Increasing sequence number |
| 24 | 8 | Capture time as signed Unix UTC milliseconds |
| 32 | 4 | Flags: bit 0 indicates the validated rigid-cockpit source |
| 36 | 4 | Diagnostic root record index; can change between frames |
| 40 | 64 | Sixteen WVP floats, row major, column-vector shader convention |
| 104 | 32 | Current app nonce |

The bridge never silently substitutes a world-camera orientation or simulated pose. Missing profile or source produces no pose packet.

A three-slot staging ring holds paired record-view/constant-buffer copies and one completion query per pair. Queries use `D3D11_ASYNC_GETDATA_DONOTFLUSH`; completed staging buffers use `D3D11_MAP_FLAG_DO_NOT_WAIT`. It does not flush, block waiting for GPU readback, or read game memory mapped for writing. At most three 8 MiB resources and three 64 KiB constant buffers are retained; actual captured source sizes use approximately 9.3 MiB total staging memory.

Draw hooks keep a thread-local copy of the immutable original vtable functions; their steady path needs no global lookup lock. Selected GPU readbacks copy only the bound record-view range.

The bridge observes swap chains supplied with device creation, and the device adapter's DXGI factory for later `CreateSwapChain`/`CreateSwapChainForHwnd`. `Present` establishes the one-submission-per-frame limit. If no supported swap-chain route is observed, an 8 ms interval bounds capture instead.

Readbacks older than 250 ms or belonging to an earlier app session are dropped. Global monotonic submission identities and newest-completion selection prevent delayed ring slots from rewinding an emitted pose. After a pause of two seconds, queued old heartbeats are discarded before accepting a fresh request.

Lease expiry releases staging resources at the next matching draw. Releasing a context's last external reference also releases that observation session. Because devices can retain private context ownership, `GetImmediateContext` reacquisition registers observation, and a live draw recovers it if a concurrent final-release callback retired an earlier session. This also works when device creation omitted the immediate-context output. Process shutdown relies on OS cleanup rather than COM/WinSock calls under the DLL loader lock.

## Verification and remaining limits

- Twenty-six portable C++ behavior checks cover the packet, validation, profile-cache compatibility, lifetime lease, sample order, relocated typed/transposed layouts, quaternion formats, bounded partial views, pairwise rigid-root coherence and actual captured rigid-reference outcomes. Deliberately breaking the protected behaviors makes the checks fail.
- `CameraProfileFilesTests.exe` exercises the production Windows INI loader against a canonical cached profile, Unicode cache path, declarative bindings/offsets, future-layout fallback and missing locator. It requires no graphics device or game installation.
- The sparse fixture header retains only first 32 bytes of relevant captured records and camera rows, with original whole-buffer SHA256 provenance. No multi-megabyte journal or graphics dumps are checked in.
- Installer fixtures cover exact encoding/line-ending round trips, idempotence, unowned proxy/files, modified installation refusal, running-game refusal, failure rollback and explicit experimental profile selection.
- The Windows x64 DLL and real-API harness build with warnings as errors. The harness observed native live UDP output, null-output device creation, external-reference-zero context reacquisition and opt-out lease expiry. Actual EDHM chain loading, normal game Present, clean native teardown, device recreation, latency and frame pacing remain unverified; see the harness limitation above.
- Only immediate-context ordinary/indexed draw methods are observed. Deferred contexts, indirect draws, `GetImmediateContext1`, `Present1`, D3D12, native Vulkan and macOS are outside this prototype.
- The DLL patches selected COM vtable slots and remains loaded for the game process lifetime. It installs no system-wide hook, overwrites no shaders, and does not inject into an already-running game.
