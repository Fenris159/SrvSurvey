# SrvSurvey-XP 2.1.3.0-rc.58.21

This update improves keyboard shortcuts when Elite Dangerous runs through
Gamescope, on a separate X11 display, or with native Wayland input. It adds
controls to inspect, select, and reset the keyboard input source, and prevents
journal rereads from repeating chat commands that have already been processed.

## Keyboard shortcuts on Linux

- SrvSurvey can discover Elite's separate local X11 display and listen for
  shortcuts there without requiring a Gamescope bridge. The game display must
  permit the connection and provide the X11 RECORD extension.
- Native Wayland shortcuts use the desktop's Global Shortcuts portal when
  available. Any needed approval is requested at SrvSurvey startup, before
  transferring focus to Elite. Previously approved shortcuts are restored when
  supported by the desktop.
- Reconnecting or changing bindings during gameplay does not open a new
  permission dialog. If a changed binding needs new approval, the input status
  explains that restarting SrvSurvey will request it at startup. Existing
  keyboard listeners apply changed bindings immediately.
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
- Startup shortcut approval does not pause journal processing, and disabling
  keyboard input cancels pending approval and closes the shortcut session.

## Journal chat-command fix

- Rereading a journal during a running session no longer re-executes chat
  commands that SrvSurvey has already processed. This prevents an old command
  such as `.mine splat` from unexpectedly starting tracking again.
- Newly sent repetitions of the same command still run normally. Tracking
  distinguishes the original journal entry from a later identical message.

## Update channel and packages

- RC51 remains the permanent `xp-v2.1.3.0-rc.51` compatibility bridge. RC58.21
  uses the schema-2 `xp2-v` release channel.
- Development remains the default in-app update channel, with saved channel
  choices preserved.
- Version: `2.1.3.0-rc.58.21`
- Tag: `xp2-v2.1.3.0-rc.58.21`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.58.21-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.58.21-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.58.21-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.58.21-x86_64.AppImage.zsync`

Packages remain self-contained. The numeric Windows `FileVersion` remains
`2.1.3.0`.

## Testing notice

> [!IMPORTANT]
> This remains a preview for testing. Keep a backup of existing SrvSurvey data
> and report unexpected behavior through the project issue tracker.
