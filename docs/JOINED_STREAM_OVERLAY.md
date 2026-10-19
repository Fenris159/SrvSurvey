# Joined stream overlay

Enable **For streaming: use joined overlay** in Settings → Overlays → Joined
stream overlay. **Alt+Ctrl+O** toggles it, and its enabled state is saved.

With Elite running, add a **Window Capture** source in OBS. On Windows, select
**[SrvSurvey.Desktop.exe]: SrvSurveyWindowOne overlay**. The capture surface is
another window belonging to SrvSurvey's existing process.

The capture window remains open when you switch from Elite to OBS, retaining the
same window handle when you return to the game. Its content follows the currently
visible gameplay panels, so it can be empty while those panels are hidden. It
retains its last usable canvas size and position while Elite is minimized, then
follows the restored game bounds. If enabled while Elite is already minimized,
it waits for usable visible bounds before opening. It closes when joined
streaming is disabled or the Elite window is gone.

Earlier builds could omit this source from OBS because it was marked as a utility
window and closed when Elite lost focus. After updating, reopen the Window Capture
properties and select the joined window again.
