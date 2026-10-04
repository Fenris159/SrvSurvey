# SrvSurvey-XP 2.1.3.0-rc.58.22

This update adds marker editing to the Surface Mining Survey map, prevents old
mining commands from replaying when journal paths are rediscovered, and fixes
Frontier account linking on Linux when the desktop retains an outdated handler.
Keyboard shortcuts support Gamescope, separate X11 displays, and native Wayland
input, with controls to inspect, select, and reset the input source.

## Surface Mining Survey map

- Click a visible deposit marker to select it with the same segmented target
  reticle used by the Guardian survey map. Click empty map space to deselect it.
- A **SELECTED MARKER** panel appears below **Marker visibility** while a marker
  is selected. Edit its mineral or metal, mineral amount, density, rig capacity,
  latitude, and longitude, then choose **Save marker**.
- **Remove marker** deletes the selected deposit and its associated boundary and
  rig data. Editing or removing a marker does not require a running game.
- Saved edits preserve the deposit's recorded boundary and ongoing trace.
  Changing maps or hiding the selected marker clears its selection.

## Frontier account linking on Linux

- Fixed **No Apps Available** when returning from Frontier authorization after
  an older SrvSurvey callback registration had been removed. Registration now
  refreshes the desktop's application cache before opening the authorization
  page, keeping one current SrvSurvey callback handler available.
- If the desktop registration utility is unavailable, SrvSurvey reports that
  `update-desktop-database` from `desktop-file-utils` is required before opening
  the authorization page.

## Keyboard shortcuts on Linux

- SrvSurvey can discover Elite's separate local X11 display and listen for
  shortcuts there without requiring a Gamescope bridge. The game display must
  permit the connection and provide the X11 RECORD extension.
- Native Wayland shortcuts use the desktop's Global Shortcuts portal when
  available. Previously approved shortcuts are restored silently when supported
  by the desktop; startup, reconnects, and binding changes never open its menu
  automatically.
- **Desktop shortcut settings** under **Settings > Input** opens the desktop
  menu when you want to approve or configure bindings, without an app restart.
  On older GNOME desktops it opens SrvSurvey's Applications settings, where
  **Global Shortcuts** remains available after the initial approval.
  Its separate status shows approval needs and the actual desktop shortcut keys.
  Application key codes supply requested keys; desktop overrides apply to portal
  input and do not rewrite the application's key-code fields. Existing keyboard
  listeners apply changes to application bindings immediately.
- Automatic detection learns one working source for all keyboard shortcuts
  after a shortcut is received with confirmed Elite focus. Using shortcuts in
  SrvSurvey or before the game starts does not select the game's input source.
- Duplicate reports of the same press from different listeners produce one
  action. Detection resets when the game changes, bindings change, or the
  selected source disconnects. Repeated shortcut presses received by another
  working listener can recover from a selected source that has stopped sending
  input; idle time alone does not switch sources.
- With multiple Elite clients, discovery prefers the focused client, then the
  configured overlay monitor when desktop window geometry or a validated
  Gamescope bridge maps it to a game display. Ambiguous displays remain
  unselected rather than choosing an unrelated client.
- The portal's current shortcut registrations are observed, so changes made in
  desktop settings update availability during the session. A portal with only
  some approved bindings does not replace the shared automatic input source.
- Native Wayland desktops may not expose game-window focus. In that case,
  approved portal actions remain global while Elite is running, and those
  unconfirmed-focus actions do not select an automatic source.

## Input source and recovery controls

- Added **Keyboard input source** under **Settings > Input**, below
  **Enable key chords**: **Automatic (recommended)**, **Desktop keyboard**,
  **Game display**, or **Wayland portal**.
- The manual preference is saved with the commander profile and applies to
  every keyboard shortcut in that instance. Unavailable choices are disabled;
  an existing manual choice stays selected if its provider becomes unavailable
  and does not silently switch to another source.
- The input section shows the selected source, provider availability, focus
  status, and last configured shortcut received, including ignored duplicates.
  Arbitrary keystrokes are not recorded in this status.
- **Reset input detection** returns to Automatic and clears detected-source
  and held-key state without restarting the application. Shortcut bindings and
  desktop approvals are retained; use a shortcut with Elite focused to learn
  the source again.
- Disabling keyboard input cancels pending approval and closes the shortcut
  session. Declining approval leaves existing keyboard listeners available.

## Journal chat-command fix

- Steam journal paths that are aliases of the same physical folder now share
  one journal source. Rediscovering an alias no longer rereads the same events
  as new activity, which could restart a mining survey or repeat API reporting.
- Older mining commands are ignored during initial journal loading, including
  when a commander profile is being created for the first time.
- Rereading a journal during a running session no longer re-executes chat
  commands that SrvSurvey has already processed. This prevents an old command
  such as `.mine splat` from unexpectedly starting tracking again.
- Newly sent repetitions of the same command still run normally. Tracking
  distinguishes the original journal entry from a later identical message.
- Multiple commander instances retain independent journal tracking and match
  their own Frontier ID, including separate Steam and non-Steam installations.

## Update channel and packages

- RC51 remains the permanent `xp-v2.1.3.0-rc.51` compatibility bridge. RC58.22
  uses the schema-2 `xp2-v` release channel.
- Development remains the default in-app update channel, with saved channel
  choices preserved.
- Version: `2.1.3.0-rc.58.22`
- Tag: `xp2-v2.1.3.0-rc.58.22`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.58.22-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.58.22-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.58.22-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.58.22-x86_64.AppImage.zsync`

Packages remain self-contained. The numeric Windows `FileVersion` remains
`2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a preview for testing. Keep a backup of existing SrvSurvey data
> and report unexpected behavior through the project issue tracker.
