# SrvSurvey-XP 2.1.3.0-rc.71.1

RC71.1 fixes original SrvSurvey profile imports, restores discovery credits in
Next Jump, and makes mining commodity labels consistent. It builds on RC71's
SteamOS and Gamescope overlay support.

## What's changed

- **Mining missions:** Mining → Missions now uses themed cards with cargo aboard,
  cumulative deliveries, expiry, rewards, hotspot search, and click-to-copy
  destination systems. Each card has a tracking checkbox; new missions are
  tracked by default and choices are saved per commander. The dedicated
  **Mining Missions** overlay shows selected active missions during collection
  and return trips, including outside a mining session. Its height follows
  the selected missions, with a bounded list for larger mission stacks.

- **Mission overlay sizing:** Mining Missions and Massacre Missions expand and
  contract with their mission rows while retaining the selected left or right
  growth edge. Long lists scroll within the available viewport at the current
  display and overlay scale; saved heights no longer leave empty massacre panels.

- **Combat and colonization overlays:** The combat suppression option now
  applies only while the colonization commodities panel is visible. Loaded
  projects alone no longer hide combat panels, and hiding or clearing the
  colony panel restores eligible combat overlays without interrupting tracking.
- **Massacre mission progress:** Rows show completed kills out of the required
  total, such as **0/6** through **6/6**, and use the commodities panel's green
  checkmark when the objective is complete.
- **On-foot overlay suppression:** Dominator and Maverick suit switches now
  hide non-combat overlays while keeping ground combat and massacre mission
  panels eligible to appear. Both suit choices are grouped together on the left.
- **Overlay editor:** Each text role can select a brush from the overlay
  appearance swatches alongside its size slider. Brush overrides follow theme
  changes, save per panel, and reset with the panel's text settings.
- **Next Jump text roles:** The subtitle uses Caption, destination detail labels
  use Title, and the route legend uses Header.
- **Carrier manifests:** Guardian salvage aliases now match local inventory.
  Local CAPI, carrier-market stock, and journal transfers provide the comparison
  totals, and locally held goods missing from Raven appear in the editor. All
  goods are visible by default; the colony-only filter remains available.
  Partial or delayed market updates no longer restore quantities superseded
  by a newer accepted CAPI inventory.
- **Colonization overlays and market search:** Commodity panels contract as rows
  disappear, including with a saved panel size. Outdated local markets appears
  beside Ardent, while the carrier, surface-port, and full-supply filters stay
  visible above the ShoppingList instructions after a search.
- **Mining overlays:** Surface panels and calibration have a separate
  **Mining (surface)** editor category. The new **Session** panel shows the
  active or paused session's location, yield, duration, activity, limpets, and
  prospecting breakdown (finds, quality hits, average and best yields), with
  normal theme, sizing, and availability controls. It expands and contracts
  with its content, retains the selected left or right growth edge, and scrolls
  long sessions within the available viewport, including at increased scale.
- **Mining session controls:** Start, Pause / Resume, and End Session shortcuts
  share their saved bindings with Settings → Input. The new default-on
  **Stop sessions automatically after FSD jump or docking** option ends sessions
  on live travel events, including paused sessions, without ending them during
  historical journal recovery.


- **Original SrvSurvey profile imports:** Backup, verify, and merge now accepts
  intact Windows profiles whose equivalent folder names use different casing,
  such as `routes` and `Routes`. File contents are still verified before the
  imported profile is activated, and recovery backups remain available.
- **Overlay settings during profile import:** Imports now preserve current
  overlay visibility, opacity, scale, streaming, VR, and shortcut settings as well as
  layouts. Unsaved settings retain XP defaults instead of taking disabled
  visibility flags from an original SrvSurvey profile. XP imports also retain
  mining overlay settings without excluding mining history or missions.
- **Downloaded Codex images:** Imports skip the downloadable image cache,
  reducing unnecessary copying and hashing. Personal flora photos and existing
  XP caches are preserved. Imported image preferences no longer redirect XP to
  an old cache path or enable bulk image downloads.
- **Next Jump overlay discovery credits:** Available commander names and
  discovery dates now appear correctly, including when a name has no date.
  Discovery and update times recognize EDSM's UTC date format, and system
  discovery credits take priority over individual body credits.
- **Next Jump overlay previews:** The position editor now uses the live route
  and destination logic. Choose followed-route, neutron-stop, plotted-route,
  or direct-target examples. Scoopable badges follow the destination's star
  class; refuel and neutron badges follow the selected route stop. Invented
  detail rows and the preview-only success message have been removed.
- **Mining notifications and prospectors:** Commodity names such as Gold,
  Silver, and Palladium now use consistent capitalization. Multiword mineral
  and core names use their readable catalog spelling. Journal-provided
  translations are preserved.
- **Included RC71 features:** Steam Deck Gaming Mode and Gamescope overlays,
  automatic Next Jump panel height, clearer route markers, and
  Commander Powerplay status remain available. See the
  [Steam Deck Gaming Mode guide](https://github.com/Fenris159/SrvSurvey/blob/1dee430ad5f68455582fa3e1fa7be061afed240c/docs/STEAMOS_GAMING_MODE.md)
  for setup and compositor requirements.

## Update channel and packages

RC71.1 uses the schema-2 `xp2-v` development channel. RC51 remains the legacy
compatibility bridge. Windows and Linux packages are self-contained.

- Version: `2.1.3.0-rc.71.1`
- Tag: `xp2-v2.1.3.0-rc.71.1`
- Windows: `SrvSurvey-XP-2.1.3.0-rc.71.1-win-x64.zip`
- Linux: `SrvSurvey-XP-2.1.3.0-rc.71.1-linux-x64.tar.gz`
- AppImage: `SrvSurvey-XP-2.1.3.0-rc.71.1-x86_64.AppImage`
- AppImage delta index: `SrvSurvey-XP-2.1.3.0-rc.71.1-x86_64.AppImage.zsync`

The numeric Windows `FileVersion` remains `2.1.3.0`.

## Testing notice and SteamOS limits

This remains a development preview. Private Gamescope compositor tests pass;
actual Deck touch, trackpad, controller, suspend/resume, docking, and SteamOS
version differences still need device validation.

Turn the Deck performance HUD off because it competes for the single external
overlay slot. Custom maximum/per-window scaling is unsupported. If scaling
becomes unknown, select a scaling mode again in Steam. Automatic Galaxy Map text
entry is unavailable in external Gamescope sessions, and primary-root X11
capture cannot supply the composited game image for image-based features.
