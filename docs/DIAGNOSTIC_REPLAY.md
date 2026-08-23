# Diagnostic journal replay

SrvSurvey's diagnostic replay tools reproduce a journal-driven application
session without using the local Elite Dangerous profile or live journal folder.
They are intended for issue reports that depend on event order, timing, or the
visual state of an overlay.

## Capture and export

Open **Diagnostics > History** in a normal SrvSurvey instance. The History tab
reads the durable Elite journal files independently from the Inspector, supports
event-name and raw-payload search, and shows the complete JSON for the selected
event.

Choose an exact inclusive UTC timestamp range, preview it, and export a
`.srvreplay` package. The exporter includes the earlier Commander and LoadGame
bootstrap events needed to establish the replay identity. Redacted export is the
default and pseudonymizes every Commander/FID identity and location relationship,
removes sent and received chat, zeroes coordinates, and removes screenshot file
paths. Raw export still removes credential-like properties; it preserves journal
fields that the redacted mode intentionally masks.

The package also carries a non-sensitive overlay presentation snapshot: panel
enablement, placement, per-panel and global scale, opacity, and the observed game
viewport. Diagnostic mode applies that snapshot to its session-local settings;
it never imports the reporter's full profile or credentials.

## Replay controller

`SrvSurvey.ReplayController` is a separate, utilitarian application distributed
beside `SrvSurvey.Desktop`. It accepts a raw journal or `.srvreplay` package from
any location, validates it, and copies the evidence into an application-managed
replay directory. The original evidence is never used as a writable playback
file.

On Linux, launch the controller from the portable archive directly, or dispatch
to it through the AppImage:

```console
./SrvSurvey-XP-<version>-x86_64.AppImage --replay-controller
```

After selecting the SrvSurvey executable, use **Launch diagnostic SrvSurvey**.
The controller starts it with:

```console
SrvSurvey.Desktop --diagnostic-replay <managed-session-manifest>
```

The controller provides step, play, pause, speed, previous, and restart actions.
Play preserves source order and uses source timestamp deltas scaled by the
selected speed. Equal, missing, or regressing timestamps produce no artificial
delay. Previous reconstructs state deterministically by restarting the child and
replaying to the preceding event; it does not attempt to reverse mutable state.

## Isolation contract

Diagnostic replay is an explicit application runtime mode, not an operating
system sandbox. Before normal composition begins, SrvSurvey replaces its live
inputs and writable roots with the selected replay session:

- journal monitoring reads only the controller's progressive playback journal;
- configuration, profile, data, and caches use session-local directories;
- Commander name and Frontier ID come only from the imported journal stream;
- external HTTP traffic is rejected by a deny-all transport;
- Frontier, update, publication, inference, and screenshot integrations are
  unavailable;
- clipboard writes, URI and folder launchers, restart helpers, game-window
  switching, global input, and other external desktop effects are disabled;
- a synthetic visible/foreground game host keeps the normal overlay subsystem
  eligible even when Elite is not running.

The synthetic host does not force an overlay visible. Normal journal reducers,
overlay eligibility checks, settings, and presentation lifetimes remain
authoritative, so passive overlays appear and hide at the same event-driven
points they would during a live session. The main window uses an orange border
to identify the altered runtime mode; it does not add a replay bar or watermark.

## Managed session contents

Each import creates a retained `diagnostic-replays/replay-*` directory containing
an immutable source journal, a progressive playback journal, a versioned
manifest, isolated configuration/data/cache directories, and retained diagnostic
logs. Restart and Previous clear the derived playback/configuration/data/cache
state while preserving source evidence and logs. The controller exposes buttons
to open the session and log directories for manual inspection or removal.

## Fidelity and trust boundary

Import treats packages as untrusted input. It applies file, event, line, and JSON
depth limits; rejects unsafe archive paths and links; requires a Commander name
and Frontier ID; verifies package checksums and manifest counts; and resolves all
session paths beneath the managed session root.

Format version 1 is journal-first. It does not yet capture timestamped Status,
Cargo, ShipLocker, NavRoute, or Market snapshots. The controller states this
limitation for every imported session, and the manifest records its format and
source version so later revisions can add those timelines without silently
changing version 1 behavior.
