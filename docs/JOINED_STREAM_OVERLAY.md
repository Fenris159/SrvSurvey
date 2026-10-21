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

## Transparent capture and the game image

The joined window contains the live overlay panels on a transparent canvas. Add
Elite as one OBS source and place the joined-window source **above** it in the OBS
Sources list. Capturing a transparent window captures that window's pixels; the
game image comes from the separate source underneath it.

The capture window stays behind application windows, so it does not paint a
second set of panels over Elite. It remains mapped for window capture instead
of being minimized or made fully transparent. Panel positions and dimensions
use the capture window's actual rendering DPI to retain the live panel's
physical pixel size across monitor scaling changes.
High-DPI frame images are sampled across their full pixel raster so their text
and rows retain the same scale as the live panels.

On Linux/Wayland, add **Screen Capture (PipeWire)**, choose a window in the portal
picker, and select **SrvSurveyWindowOne overlay** for the panel source. Keep a
separate PipeWire source for the Elite window below it. A display/monitor source
captures the composited desktop instead of an isolated transparent panel layer.
OBS's [window-capture guide](https://obsproject.com/kb/window-capture-sources)
explains this distinction, and its
[PipeWire renderer](https://github.com/obsproject/obs-studio/blob/master/plugins/linux-pipewire/pipewire.c)
blends the captured alpha channel into the scene.

After restarting or replacing SrvSurvey on Linux, reopen the overlay's PipeWire
source properties and reselect **SrvSurveyWindowOne overlay** in the portal
window picker. OBS can retain a paused image from the previous process, including
its old size, until the source is selected again.

The streaming settings list the names of the composited panels below the count.
The small **Journal activity and SCO status** indicator is a live panel too, even
when its journal activity bar is empty. To hide it, open **Settings → Overlays → Journal activity and SCO status**
and enable **Hide PlotPulse**. Its switch under **Panel availability and shortcuts**
also controls whether it appears. Fully invisible or empty frames do not count.
