# Diagnostic journal replay

SrvSurvey's diagnostic replay tools reproduce a journal-driven application
session without using the local Elite Dangerous profile or live journal folder.
They are intended for issue reports that depend on event order, timing, or the
visual state of an overlay.

## Capture and export

Open **Diagnostics > History** in a normal SrvSurvey instance. The History tab
reads the durable Elite journal files independently from the Inspector, supports
event-name and raw-payload search, and shows the complete JSON for the selected
event. To keep long-lived installations responsive, the panel indexes the full
history while retaining the most recent 50,000 events as its searchable display
window. Timestamp export streams the full index, including ranges older than the
display window.

Choose an exact inclusive UTC range with the calendar and 24-hour time controls,
preview it, and export a `.srvreplay` package. The most recent 24 hours are
selected initially, and the controls limit a package to 31 days so an accidental
multi-year selection does not exceed the portable replay bounds. The exporter
includes the earlier Commander and LoadGame bootstrap events needed to establish
the replay identity. Redacted export is the default and pseudonymizes every
Commander/FID identity and location relationship, removes sent and received chat,
zeroes coordinates, and removes screenshot file paths. Raw export still removes
credential-like properties; it preserves journal fields that the redacted mode
intentionally masks.

The package also carries a non-sensitive overlay presentation snapshot: panel
enablement, placement, per-panel and global scale, opacity, and the observed game
viewport. Diagnostic mode applies that snapshot to its session-local settings;
it never imports the reporter's full profile or credentials.

While SrvSurvey is running normally, it retains a rolling 24-hour history of
validated changes from `Status.json`, `Cargo.json`, `ShipLocker.json`,
`NavRoute.json`, and `Market.json`. The history is split into hourly local files,
flushes each accepted snapshot before it is used for export, suppresses snapshots
whose only change is their timestamp, and removes data older than 24 hours. It is
not uploaded. A replay export includes snapshots inside the selected range plus
the latest snapshot of each available type before the range, so journal and
companion-file state share one timestamp-ordered replay timeline. A range longer
than the retained history can therefore have partial companion coverage; the
package records its actual coverage and any missing input types.

Redacted exports also remove companion location names, identifiers, and
coordinates. Cargo and material names and quantities remain because they are the
state the diagnostic replay is intended to reproduce. Raw exports preserve the
validated companion payloads.

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

Before launching SrvSurvey, the controller applies the package's earlier journal
and companion bootstrap inputs. The application therefore starts with commander,
location, status, inventory, route, and market context already established when
those inputs are available. The controller then provides step, play, pause,
speed, previous, and restart actions. Play preserves source order and uses source
timestamp deltas scaled by the selected speed. Equal, missing, or regressing
timestamps produce no artificial delay. Previous reconstructs state
deterministically by restarting the child and replaying to the preceding input;
it does not attempt to reverse mutable state.

## Isolation contract

Diagnostic replay is an explicit application runtime mode, not an operating
system sandbox. Before normal composition begins, SrvSurvey replaces its live
inputs and writable roots with the selected replay session:

- journal monitoring reads only the controller's progressive playback journal;
- Status, Cargo, ShipLocker, NavRoute, and Market readers use only the
  controller's progressive playback companion files;
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
an immutable source journal and companion timeline, progressive journal and
companion playback files, a versioned manifest, isolated
configuration/data/cache directories, and retained diagnostic logs. Restart and
Previous clear the derived playback/configuration/data/cache state while
preserving source evidence and logs. The controller exposes buttons to open the
session and log directories for manual inspection or removal.

## Fidelity and trust boundary

Import treats packages as untrusted input. It applies file, event, line, and JSON
depth limits; rejects unsafe archive paths and links; requires a Commander name
and Frontier ID; verifies package checksums and manifest counts; and resolves all
session paths beneath the managed session root.

Format version 2 requires a journal and a companion timeline, even when the
companion timeline is empty. Earlier package and managed-session formats are
rejected rather than interpreted with reduced replay fidelity.
