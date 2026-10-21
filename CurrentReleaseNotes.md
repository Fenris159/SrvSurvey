# SrvSurvey-XP 2.1.3.0-rc.69.6

RC69.6 protects fleet-carrier cargo updates when SrvSurvey or the game closes
and fixes joined stream overlay capture. Pending Raven Colonial updates can
recover next session, and the stream copy now matches the live panels without
painting another copy over the game.

## What's changed

- **Keep carrier cargo updates across interruptions.** Observed personal and
  squadron carrier cargo changes are saved locally before network requests.
  Closing the game or returning to its main menu retains the carrier context
  needed to finish those updates without waiting for undocking.
- **Finish cargo updates when SrvSurvey closes.** Shutdown reads any final
  journal events and allows up to eight seconds to send pending carrier cargo
  updates. Unsent work remains saved for the next session, including after an
  unexpected SrvSurvey exit once the changes have been captured.
- **Recover interrupted requests safely.** If an earlier cargo request may
  have reached Raven Colonial, recovery checks the carrier state before retrying.
  A failed local save prevents sending an update that could not be recovered.
- **Capture joined overlays on a transparent canvas.** Fixes a joined stream
  window that could appear blank or black in OBS. Place its source above a
  separate Elite window source to show the game behind the panels.
- **Keep the stream copy behind the game.** The capture window stays available
  to OBS without drawing a second set of panels over the normal live overlays.
- **Match live panel size in the stream.** Corrects high-DPI and mixed-DPI
  scaling and uses the full captured image, preventing enlarged text and cropped
  columns in the joined overlay.
- **Identify the panels being streamed.** Streaming settings list panel names
  alongside the count and exclude fully invisible or empty captures. The small
  Journal activity and SCO status indicator counts when enabled; it can be
  disabled in Global overlay settings.
- **Reconnect OBS after a Linux app restart.** After restarting or replacing
  SrvSurvey, reopen the overlay's PipeWire source properties and reselect
  **SrvSurveyWindowOne overlay** in the window picker. OBS can otherwise retain
  a paused image from the previous process.

## Update channel and packages

RC69.6 uses the schema-2 `xp2-v` development channel. RC51 remains the legacy
compatibility bridge. Windows and Linux packages are self-contained.

- Version: `2.1.3.0-rc.69.6`
- Tag: `xp2-v2.1.3.0-rc.69.6`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.69.6-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.69.6-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.69.6-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.69.6-x86_64.AppImage.zsync`

The numeric Windows `FileVersion` remains `2.1.3.0`.

## Testing notice

This remains a development preview. Joined overlay capture and scaling were
checked with a live Linux game and OBS session. Sending pending cargo updates
still requires network access; saved work is retained when sending cannot
complete.
