using System.Text.Json;
using SrvSurvey.Desktop.Presentation;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>The player-facing task and symbol catalog, checked against the current workspaces.</summary>
public static class GuideCatalog
{
    private const string AvaloniaResourceScheme = "avares";
    private const string DesktopAssemblyName = "SrvSurvey.Desktop";
    private const string GuardianSiteMap = "Guardian site map";
    private const string GuardianSiteMapLegend = "Guardian site map legend";
    private const string HumanSettlementMap = "Human settlement map";
    private const string HumanSettlementQuestMap = "Human settlement quest map";
    private const string HumanSettlementConflictZoneMap = "Human settlement conflict-zone map";

    private static readonly Lazy<GuideSectionViewModel[]> ChatCommandSections = new(LoadChatCommandSections);

    /// <summary>Returns the complete offline guide, including the shipped chat-command reference.</summary>
    public static IReadOnlyList<GuideCategoryViewModel> Create()
    {
        return
        [
            Category(
                "getting-started",
                "01",
                "Getting started",
                "Connect SrvSurvey to the correct Commander and understand how the journal-driven workspaces and overlays behave.",
                [
                    IntroSection(
                        "First launch",
                        "SrvSurvey reads Elite Dangerous Journal files and companion status files. It does not need to modify the game installation.",
                        [
                            "Open Settings and confirm the journal folder. The default location is discovered automatically on supported platforms.",
                            "Choose a preferred Commander when more than one profile appears in the same journal folder.",
                            "Keep SrvSurvey open while playing. Workspaces and context-sensitive overlays update when new journal events arrive.",
                        ],
                        [
                            "Overview shows the active Commander, game mode, location, exploration totals, and unclaimed exobiology rewards.",
                            "If no Commander appears, use Diagnostics to inspect the selected folder and newest Journal file.",
                        ]
                    ),
                    IntroSection(
                        "How the interface is organized",
                        "The sidebar keeps the active Commander and the major SrvSurvey workflows close at hand. Guides remains available even when Elite is not running.",
                        [],
                        [
                            "Select the Active Commander card to open the current Frontier profile. Overview remains at the top of the sidebar.",
                            "Survey groups Exploration, Exobiology, and Boxel; Navigation groups Travel, Search, and Bookmarks; Activities groups Mining, Surface Mining, Guardian, Quests, and Colonization. Expanding one group collapses the previous group.",
                            "Diagnostics, Settings, Theme, and Guides remain in the utility area. The small overlay-settings button beside supported activities opens only that activity's overlay controls.",
                            "Use the sidebar icon at the top-right of the navigation column to collapse it into a narrow strip and give the current workspace more room. The same icon restores navigation; your selected workspace stays open and the window keeps its size.",
                            "Settings is divided into Application, Desktop, Global overlays, Input, Privacy & sharing, Screenshots, and Data & migration. Search settings to jump directly to a matching control.",
                            "Theme owns both application palettes and in-game overlay appearance, while Guides can search workflows, settings, and map symbols.",
                        ]
                    ),
                    Section(
                        "Find help quickly",
                        "Guides are available without Elite running. Each subject explains one task.",
                        [
                            "Expand a category on the left and select a subject. Opening another category collapses the previous one; the current guide stays open until you select a subject.",
                            "Search at the top using a feature name, setting label, chat command, or symbol. All words must match, in any order. Select a result to read the full instructions.",
                            "Follow the numbered steps. Good to know explains requirements and limitations; illustrated symbols show the same shapes used by the overlays. You can select and copy instruction text.",
                        ],
                        []
                    ),
                    IntroSection(
                        "Automatic overlays",
                        "Most overlays appear only when their information is useful, then hide when that game context ends.",
                        [
                            "Play normally; entering FSS, approaching a body, landing, opening the Galaxy Map, or docking can activate the relevant overlay.",
                            "Use Theme > Overlay Settings to disable individual overlays or change their triggers.",
                            "Use the global overlay visibility shortcut when you need to hide or restore every detached overlay at once.",
                        ],
                        [
                            "Borderless or windowed Elite modes provide the most predictable desktop overlay stacking. Exclusive fullscreen behavior depends on the operating system and compositor.",
                            "A passive overlay is click-through. The live-interaction shortcut temporarily makes existing live overlays draggable without opening the full position editor.",
                        ]
                    ),
                ]
            ),
            Category(
                "overview-journals",
                "02",
                "Overview and journals",
                "Understand the live Commander state, multi-Commander selection, and the difference between current-session and historical data.",
                [
                    Section(
                        "Reading Overview",
                        "Overview is the quickest health check for the active journal session.",
                        [],
                        [
                            "Commander and Frontier ID identify the profile receiving events.",
                            "Location and body follow the newest location, approach, touchdown, and departure events.",
                            "Exploration totals combine jump, scan, mapping, landing, distance, and estimated reward state for the active profile.",
                            "Exobiology totals show samples and rewards that have not yet been sold.",
                        ]
                    ),
                    Section(
                        "Multiple Commanders",
                        "Profiles remain isolated even when their journals share one folder.",
                        [
                            "Choose the preferred Commander in Settings, or select no preference when you want the newest active profile.",
                            "Use the Multiple commanders card on Overview to launch an isolated SrvSurvey process for another saved profile.",
                            "Use Next Elite window when several game clients are open and an overlay needs to follow a different window.",
                        ],
                        [
                            "Ambiguous shared Cargo.json data is suppressed while multiple Elite windows are detected. A fresh unambiguous cargo write is required before cargo can re-enter plans or publishing.",
                        ]
                    ),
                    Section(
                        "Live versus historical processing",
                        "Live journal monitoring never requires rewriting an Elite journal. Historical tools analyze older files separately.",
                        [],
                        [
                            "Diagnostics can analyze older journals by Commander and date without changing profile data.",
                            "Commander Codex merges and Odyssey system/body reconstruction require explicit confirmation, verified backups, and atomic activation.",
                            "Recent active journals are excluded from destructive historical reconstruction paths.",
                        ]
                    ),
                ]
            ),
            Category(
                "exploration",
                "03",
                "Exploration",
                "Use FSS, body, system, route, and screenshot tools without losing the familiar original-overlay cues.",
                [
                    Section(
                        "FSS and system survey",
                        "The FSS overlays summarize what the current system contains and what remains worth investigating.",
                        [
                            "Honk or enter FSS to establish the system body count and signal state.",
                            "Resolve bodies in FSS; undiscovered bodies carry a flag and completed system scans carry a completion check.",
                            "Review body type, estimated scan/DSS value, biological and geological signals, terraformable status, and landability.",
                        ],
                        [
                            "Use Exploration overlay settings to dim or filter low-value bodies while valuable bodies remain highlighted.",
                            "External-data enrichment can add prior discovery, traffic, station, and biological context when its privacy setting is enabled.",
                            "EDSM and Spansh body lookups wait for an active Elite session and a confirmed visit to the current system. If newly discovered body data is not indexed yet, SrvSurvey allows up to three retries after the initial lookup and remembers the current-visit budget across application restarts.",
                            "Leaving and later returning to the system starts a new visit budget. Closing and reopening SrvSurvey during the same visit does not bypass the limit.",
                        ]
                    ),
                    Section(
                        "Body information and values",
                        "Body information combines the scan, orbit, atmosphere, gravity, temperature, volcanism, materials, and reward details known so far.",
                        [],
                        [
                            "A check beside value means the detailed surface scan is complete.",
                            "T identifies a terraformable candidate and L identifies a landable body in compact FSS rows.",
                            "A first-discovery flag means the body was not previously discovered according to the journal data.",
                            "Enable Show flight warnings and choose a gravity threshold in Exploration overlay settings. The warning appears only when SrvSurvey can identify a nearby landable body in a supported ship, fighter, or SRV flight context.",
                            "Flight-warning color and advice escalate at 2 g, 4 g, and 8 g. The panel hides when the body context is stale, while on foot, or while an external game panel has focus.",
                        ]
                    ),
                    Section(
                        "Galaxy Map and next jump",
                        "Route overlays keep destination and next-hop information visible while the Galaxy Map or route is active.",
                        [],
                        [
                            "Next-jump information shows route progress, remaining distance, star class, scoopability, neutron routing, and available system context.",
                            "Galaxy Map preview can show destination discovery, biological, traffic, and port data; some fields require external lookup data.",
                            "Use the configurable jump-information shortcut to show or hide the route overlay manually.",
                        ]
                    ),
                    Section(
                        "Exploration screenshots",
                        "Screenshot processing can convert new images, organize them, and embed useful site data without changing old captures.",
                        [
                            "Choose source and destination folders in Settings.",
                            "Enable the filename, folder, banner, or aerial-alignment options you want.",
                            "Use the screenshot-data shortcut to enable or disable banners for future images.",
                        ],
                        [
                            "Guardian aerial images can be rotated and aligned using per-layout altitude guidance.",
                            "Processing is opt-in and writes converted output to the configured destination.",
                        ]
                    ),
                ]
            ),
            Category(
                "exobiology",
                "04",
                "Exobiology",
                "Interpret biology predictions, reward PIPs, sample spacing, surface radar, prior scans, and Codex progress.",
                [
                    Section(
                        "Predictions and bio signals",
                        "Predictions narrow each unresolved biological signal using the body facts currently known.",
                        [],
                        [
                            "Criteria can include body type, atmosphere and composition, gravity, temperature, pressure, volcanism, materials, parent star, galactic region, nebula proximity, and Guardian proximity.",
                            "A hatched reward PIP is a prediction. A solid PIP is a confirmed organism. A trailing question mark means that organism is still predicted rather than identified; hover it for the exact state.",
                            "The four vertical PIP segments use configurable reward thresholds. More filled segments indicate a higher expected reward band, not sample completion.",
                            "Each PIP has the legacy translucent dotted outer frame plus a solid outline around every filled or possible-reward segment. Both border layers follow the PIP state and are independently configurable in Overlay appearance.",
                            "A body row can contain more prediction PIPs than its reported biological-signal count when several genera remain plausible. Those additional hatched PIPs are alternative candidates, not extra signals or additive rewards.",
                            "A reward range appears when unresolved signals could match organisms with different values.",
                            "A filled flag marks a Commander first, an outline flag marks a Commander regional first, and a sun marks a potential Galactic-region first from external candidate data.",
                        ],
                        [
                            GuideIconKind.BiologyRewardKnown,
                            GuideIconKind.BiologyRewardPredicted,
                            GuideIconKind.BiologyRewardUnknown,
                        ]
                    ),
                    Section(
                        "Sampling an organism",
                        "The bio-status and surface overlays track the active genus, three-sample sequence, colony distance, and estimated reward.",
                        [
                            "Scan the first specimen with the Genetic Sampler.",
                            "Move outside the displayed colony radius before taking the next sample; the radar ring and distance state show whether the spacing is valid.",
                            "Complete all three samples. The confirmed organism and unclaimed reward are retained in the Commander profile until sale events clear them.",
                        ],
                        [
                            "First-footfall status helps estimate the first-logged bonus. Treat screen-inferred rewards as estimates; the game determines the actual biological sale value.",
                            "The first-footfall screen detector is optional; its shortcut can override the current body state when automatic inference is unavailable.",
                        ],
                        [GuideIconKind.RadarSample]
                    ),
                    Section(
                        "Surface radar and bookmarks",
                        "The grounded radar keeps the Commander, ship, SRV, samples, prior scan locations, and numbered bookmarks in one relative view.",
                        [],
                        [
                            "The center ringed arrow is the Commander and current heading. Triangles represent the ship; a dim triangle is a former ship position; a rounded rectangle is the SRV.",
                            "Dots represent biological samples or bookmarks. Their surrounding circles show the organism colony radius.",
                            "For an active sample, warning color inside the radius means you are too close; success color outside the radius means the next sample is valid.",
                            "Use Track location 1 through 8 shortcuts to toggle reusable surface bookmarks.",
                        ],
                        [
                            GuideIconKind.RadarCommander,
                            GuideIconKind.RadarShip,
                            GuideIconKind.RadarSrv,
                            GuideIconKind.RadarBookmark,
                        ]
                    ),
                    Section(
                        "Codex, Codex Bingo, and prior scans",
                        "Codex tools distinguish personal discoveries, regional discoveries, confirmed entries, predictions, and known scan locations.",
                        [],
                        [
                            "Codex Bingo groups discoveries by region and lets you inspect where and when an entry was found.",
                            "Old journals or the Canonn Codex Challenge can be imported into the Commander Codex without replacing unrelated profile data.",
                            "Prior-scan overlays can show known biological locations near the current body when external Canonn data is enabled.",
                            "A filled discovery flag is a Commander first; an outline flag is a regional first. Highlighting regional firsts is optional.",
                        ]
                    ),
                ]
            ),
            Category(
                "surface-mining",
                "05",
                "Surface mining",
                "Use the Rhino mining radar, rig key chords, vehicle guidance, and cargo display while working on a planetary surface.",
                [
                    Section(
                        "Enable Surface mining",
                        "Surface Mining has its own maps, searches, and Rhino guidance. Mining contains the ship and ring-mining tools.",
                        [
                            "Expand Activities and open Surface Mining for Surface Maps, Survey map, Hotspot List, Surface Hunt, and Search.",
                            "Use the overlay-settings icon beside Surface Mining to enable its overlay and configure Rhino tracking and shortcuts.",
                            "Board a Rhino on a planetary surface. With valid journal and status data, the overlay shows your vehicles, saved rigs, resource bookmarks, and cargo.",
                        ],
                        [
                            "Surface Survey and its mini tracker hide while operating the Rhino or returning to its parked location on foot.",
                            "Use Theme > Overlay Settings > Edit Overlay Positions to arrange previews, including the rig calibration frame.",
                        ]
                    ),
                    Section(
                        "Experimental rig bar detection",
                        "Optional screen detection tracks deployed rigs from the six Rhino HUD bars.",
                        [
                            "In Surface Mining overlay settings, enable Automatically track rigs from the Rhino HUD. Set Deployment bar color if a HUD mod changes the usual bright green.",
                            "On Wayland, also enable Wayland screen capture and Surface Mining Rhino rig tracking in Settings > Application. Select the display containing Elite.",
                            "Calibrate the six circles in Edit Overlay Positions, test the readings, then save and close the editor before automatic tracking can change rig locations.",
                        ],
                        [
                            "Use Rig tracking with key chords when capture is unavailable. Neither method deploys or collects rigs for you.",
                        ]
                    ),
                    Section(
                        "Calibrate the six rig circles",
                        "Align the capture frame and numbered guides with the forward-facing Rhino HUD.",
                        [
                            "Open Edit Overlay Positions and choose Mining. Drag the capture frame around all six circles and their complete deployment bars; resize from the lower-right corner.",
                            "Drag each numbered red dot onto its circle centre: 1–3 across the top row, 4–6 across the bottom. Each centre can be adjusted separately.",
                            "Use Size−/Size+, Height−/Height+, and R−/R+ to match circle diameter, oval shape, and rotation. Gap−/Gap+ align the cyan curves with the middle of the deployed bars.",
                            "Use Search−/Search+ to adjust the movement allowance. Keep the Search bounds inside the capture frame.",
                            "Stationary and looking forward, select Test. Deploy rig 1 first to establish the anchor, then check the other five slot readings. Repeat Test after any alignment change.",
                            "Save and close the editor to use the calibration, or cancel to discard changes. Test is a preview and never changes saved rig locations.",
                        ],
                        [
                            "Calibration scales with the game viewport. Recheck after changing aspect ratio, field of view, HUD geometry, or HUD color. Numbers inside the HUD circles are not read.",
                        ]
                    ),
                    Section(
                        "Understand automatic rig readings",
                        "BAR means present, empty means absent, ? means uncertain, and an ellipsis means a change is awaiting confirmation.",
                        [],
                        [
                            "BAR immediately records a missing tracker at the Rhino deployment position. Repeated BAR readings preserve that original position.",
                            "A tracker clears only after three continuous seconds of reliable empty readings. Uncertain or missing frames, a returning bar, or interrupted capture restart the delay.",
                            "Tracking runs aboard the Rhino with no cockpit panel open. Position, heading, or HUD movement pauses changes until one second of stillness. Stop before deploying so the recorded position is correct.",
                            "Keep the full six-circle layout visible. Free head look is not reported as panel focus; looking away, low brightness, obscuring windows, and partial bars can produce uncertain readings.",
                            "Only calibration and the selected color are saved. Screen images are processed locally in memory and discarded; no audio is captured.",
                        ]
                    ),
                    Section(
                        "Rig tracking with key chords",
                        "Ctrl+Alt+F1 through Ctrl+Alt+F6 toggle the six saved rig locations. Each numbered radar circle is paired with the matching rig chevron below the vehicle row.",
                        [
                            "While aboard the Rhino, hold Ctrl and Alt and press F1 to set Rig 1 at the deployment position. Release the keys after each chord.",
                            "Press Ctrl+Alt+F1 again to unset Rig 1: its radar circle disappears and its slot returns to NOT SET. Ctrl+Alt+F2 through Ctrl+Alt+F6 work the same way for their numbered rigs.",
                            "To move an existing marker, clear it first, drive to the new position, and press its chord again to set it there.",
                        ],
                        [
                            "These shortcuts record or clear locations; they do not deploy or collect rigs in the game.",
                            "Input settings lists Tracker/Mining Rig (1) through Tracker/Mining Rig (6), followed by regular Tracker (7) and Tracker (8) bindings. Defaults are Ctrl+Alt+F1 through Ctrl+Alt+F8. Outside Rhino mining these toggle surface trackers; slots 7 and 8 do not place rigs.",
                            "Edit the first six shared bindings in Settings > Input or in Surface Mining overlay settings, where they retain the Mining rig labels. Both locations edit the same bindings, including any replacement keyboard or controller chords.",
                            "Locations are saved for the current Commander and body separately from biology bookmarks. Placement accounts for the Rhino cockpit and deployment offsets and requires being aboard the Rhino.",
                            "By default, returning to your own ship on foot or docking the Rhino automatically clears all six rig locations because the game destroys deployed rigs then. Turn off Clear rigs automatically when boarding your ship in Surface Mining overlay settings to keep your saved markers; this preference persists between sessions. Re-entering the Rhino on foot keeps the locations; boarding a taxi or another Commander's ship does not clear them.",
                            "Send --- in game chat to clear all rigs and surface bookmarks on the current body, including resource, biology, and regular tracker bookmarks. This works even when automatic rig clearing is disabled and does not erase scan history or bookmarks on other bodies.",
                        ]
                    ),
                    Section(
                        "Rig circles and distance cues",
                        "The radar shows 78-meter-radius rig circles at the legacy mining zoom. Chevrons turn with your heading and show distance to each saved location.",
                        [],
                        [
                            "COLLECT uses cyan when within 5 meters of a saved rig. TOO CLOSE uses red inside the 78-meter deployment exclusion distance. TRACKED means outside that distance; NOT SET means the slot has no saved location.",
                            "Colors follow the selected theme, while the text labels keep their meaning. On foot, distances use your position without the Rhino cockpit offset.",
                        ]
                    ),
                    Section(
                        "Rig range warning",
                        "Aboard the Rhino, the Mining warning appears beyond 4 km from the farthest saved rig and clears when every rig is back within range.",
                        [],
                        [
                            "TOO FAR FROM RIGS uses the high-risk flight warning colors and the reminder: Moving beyond 4.5Km will Destroy Rigs. Only saved rig bookmarks count; resource bookmarks do not.",
                            "Surface Mining overlay settings provides an independent visibility toggle and optional shortcut. Its initial placement copies your saved Flight Warning placement; you can move it separately in the overlay editor.",
                            "The warning is hidden when you leave the Rhino. Flight warnings are also hidden on foot and in ground vehicles, with the Nomad retaining its flight warnings.",
                        ]
                    ),
                    Section(
                        "Ground resource bookmarks",
                        "Named resource locations appear below the rig trackers in two columns, filled left to right. Each saved location has its own material name, bearing chevron, and distance.",
                        [
                            "At a resource location, send +helium or +thortveitite in game chat to save your current position under that name. These are manual bookmarks; the overlay does not automatically detect mineral deposits.",
                            "Repeat the command at another location to track multiple deposits of the same material. Distance and direction update automatically as you move or turn.",
                            "Send -helium to remove the nearest helium bookmark, or --helium to remove all saved helium locations on this body. Use the matching name for other materials.",
                        ],
                        [
                            "Within 150 meters, the name and distance use the theme's near-target color and the chevron becomes a single arrow. Farther targets use the normal accent and double chevron; kilometers are shown for longer distances.",
                            "Resource bookmarks also appear as 70-meter circles on the mining radar. Long lists scroll in the resource area using overlay interaction mode. Biological bookmarks and numbered quick trackers are excluded from these rows.",
                            "Named resources use the existing Commander/body surface bookmarks. Boarding your own ship clears the six rig slots but preserves these resource locations.",
                        ]
                    ),
                    Section(
                        "Surface Mining maps",
                        "Save a surface mining location's center and map its deposits with case-insensitive chat commands.",
                        [
                            "Send .mining survey to start the compact guided workflow. Run it again before center setup is complete to restart at the border, or while following scan waypoints to return to waypoint 1. The fourth row keeps the deposit-marking commands visible while scanning.",
                            "Drive to the orange location border, face the center marker, and send .mining <bearing> <border radius km> <location number>. Example: .mining 120 6.44 4.",
                            "From the true center, send .mining center here to correct the saved center and ring alignment without moving any deposit markers.",
                            "Send .alignment to toggle a red guide at the exact center of the Elite window. In the Rhino driving view, place a distant location circle under the guide before reading its bearing from the visible compass for .mine. Turret mode does not show the required bearing.",
                            "Use a full commodity name or its three-character code in any deposit command. Codes are case insensitive and match the material tags. Examples: .mine mon m/h here and .mine ltd l/h here. Codes also work with bearing-and-distance placement and .mine move <commodity> here.",
                            "From anywhere inside the saved border, face a deposit and send .mine <bearing> <commodity> <distance km> <l|m|h>/<l|m|h>. The bearing and distance start at your live position. Use l, m, or h for Low, Medium, or High; full words also work. A same-commodity marker within 100 m is declined as a likely duplicate. Example: .mine 342 jadeite 1.87 h/m.",
                            "At a deposit, send .mine <commodity> <l|m|h>/<l|m|h> here. Use l, m, or h for Low, Medium, or High; full words also work. Precise here placement may overlap an existing marker. Example: .mine ruby m/l here. Remove the nearest mapped deposit within 0.5 km with .mine delete here.",
                            "While standing near a mapped deposit, send .mine rigs <number> to record its positive rig capacity on the nearest marker. The count remains visible in brackets when marker names are hidden. Example: .mine rigs 4.",
                            "Center the Rhino chassis on a mapped deposit edge, switch to turret mode, keep the mineral scanner active, and send .mine splat. Drive the full visible circuit until SrvSurvey closes the trace, then use the square rig-position suggestions to line up each deployment. The compact radar zooms automatically near the deposit and recommendations. Send .mine splat cancel to discard an unfinished trace.",
                            "To correct a marker, stand at its true position and send .mine move <commodity> here. The nearest marker matching that commodity must be within 200 m. Example: .mine move haematite here.",
                        ],
                        [
                            "Mineral amount and density describe each deposit and accept l, m, or h as shorthand for low, medium, or high. Deposit names must match a commodity in Activities > Surface Mining > Hotspot List; rejected commands explain the problem in Status notifications.",
                            "Surface maps use the shared Navigation > Bookmarks catalog. Edit their system, body, category, notes, signal, body type, arrival distance, or border radius there; deleting the bookmark also removes the map. Contains and Body Type filters populate automatically from those saved maps.",
                            "A bookmarked map loads automatically when your live surface position enters its saved border and unloads when you leave the area.",
                            "The fixed-upright map shows 1 km rings through the whole-kilometer ring that encloses the saved border, mapped deposits, and your live position. Use the mouse wheel, slider, or minus and plus controls to zoom; drag to pan after zooming in.",
                        ]
                    ),
                    Section(
                        "Edit or remove a mapped deposit",
                        "Select deposits directly on Surface Mining > Survey map.",
                        [
                            "Open a saved surface map, then left-click a deposit. Its target reticle highlights it and SELECTED MARKER appears below Marker visibility.",
                            "Edit the mineral or metal, amount, density, rig capacity, latitude, or longitude. Select Save marker to persist changes.",
                            "Use Remove marker to delete the selected deposit, or left-click empty map space to deselect it.",
                        ],
                        [
                            "Rig capacity 0 means not recorded. Changing visibility filters can hide markers; the editor reports save or removal failures in its status.",
                        ]
                    ),
                    Section(
                        "Find profitable surface mining locations",
                        "Surface Mining > Search pairs landable mining bodies with nearby selling stations.",
                        [
                            "Enter a Reference system and Distance. Choose a Mineral / metal, landing pad size, and minimum and maximum demand.",
                            "Set Max. mine–sell distance to limit travel between a mining system and its buyer. Search results controls how many selling-system groups are returned.",
                            "Select Search. Compare Best Sell Station prices and demand with the linked Mining System bodies, arrival distances, and gravity.",
                            "Expand matching bodies for details. Hide Irrelevant Material Tags reduces clutter; Force Include if viable keeps an eligible reference system in the results.",
                        ],
                        [
                            "Prices and demand are reported observations, not guaranteed current quotes or total profit. Check the station before committing to a mining trip.",
                            "Surface Hunt explains which body types and geological clues to investigate; Hotspot List provides material availability and color references.",
                        ]
                    ),
                    Section(
                        "Ship, Rhino, and cargo",
                        "Two vehicle columns sit above the rig chevrons, with cargo capacity below them.",
                        [
                            "Follow Ship for ship guidance. After disembarking, follow the Rhino chevron back to the parked vehicle.",
                            "Re-enter the Rhino: its chevron becomes X to indicate untracked while aboard. Mining remains available during the walk back.",
                        ],
                        ["The cargo row shows occupied capacity out of 72. Rig-setting chords remain disabled on foot."]
                    ),
                ]
            ),
            Category(
                "travel-search",
                "06",
                "Travel and search",
                "Set surface targets, record journeys, follow routes, and search the galaxy by sphere or nearby-system criteria.",
                [
                    Section(
                        "Ground targets",
                        "A ground target stores latitude and longitude for the current body and turns them into bearing, distance, and approach guidance.",
                        [
                            "Enter coordinates in Travel or send .target here through the in-game chat journal to capture the current location.",
                            "Follow the circular bearing display relative to ship heading. The attack-angle line helps judge descent toward the target.",
                            "Send .target off or clear the target in Travel when finished.",
                        ],
                        ["Ground targets are body-specific. A target is not treated as valid on a different body."],
                        [GuideIconKind.GroundTarget]
                    ),
                    Section(
                        "Journeys, routes, and system notes",
                        "Journeys preserve an expedition timeline, while routes provide an ordered destination list and notes preserve local research.",
                        [],
                        [
                            "Create or resume a journey, then let journal events add visited systems and exploration totals.",
                            "Import a named route or create one from supported route data, then advance it as jumps arrive.",
                            "A saved standard route can include body destinations for each system. Route bodies shows their body icon, arrival distance, exploration and biology values, and completion state; arriving at the matching body marks that destination complete.",
                            "Use Show system notes to edit notes for the current system without leaving the game context.",
                        ]
                    ),
                    Section(
                        "Spherical search",
                        "A spherical search finds candidate systems inside a radius around a central coordinate.",
                        [
                            "Choose the center, radius, and search constraints.",
                            "Generate the next candidate and copy or paste it into the Galaxy Map with the configured shortcuts.",
                            "Use the overlay color and warning text to distinguish valid in-radius destinations, out-of-radius systems, low mass codes, and already-surveyed systems.",
                        ],
                        []
                    ),
                    Section(
                        "Nearby-system searches",
                        "Nearby systems resolves biological search results around a known reference system.",
                        [],
                        [
                            "Choose the search mode, enter a system name or id64 for the distance origin, and select an EDSM suggestion before using that origin.",
                            "Use current restores the active Commander system as the distance origin.",
                            "External system resolution is clearly reported when a lookup service is unavailable or disabled.",
                        ]
                    ),
                ]
            ),
            Category(
                "boxel",
                "07",
                "Boxel",
                "Understand procedural boxels, run bounded system surveys, navigate nested search trees, and preserve multiple research projects.",
                [
                    Section(
                        "What a boxel is and why survey one",
                        "A boxel is a cubic subsector of procedurally generated space. Systems in one boxel share the same generated-name prefix and use a numeric suffix as their sequence within that boxel.",
                        [],
                        [
                            "In Leamae UK-D d13-890, Leamae is the sector, UK-D d13 identifies the boxel, and 890 is the system number within that sequence.",
                            "Mass codes a through h describe nested cube sizes from 10 to 1,280 light-years per side. A higher letter means a larger parent cube; each supported level contains eight children one mass code lower.",
                            "Mass code correlates with the system's original mass allocation, but it does not guarantee a particular present-day star class, planet, or biological species.",
                            "Surveying a complete sequence gives an explorer a bounded, repeatable area to study. It is useful for finding regional formation patterns, checking nearby systems after an interesting discovery, and keeping a long project organized.",
                            "The terminology and naming model follow community research documented by Elite Dangerous Astrometrics, Marx's Guide to Boxels, and the IGAU system-identifier notes.",
                        ]
                    ),
                    Section(
                        "Start a boxel search",
                        "Use the Boxel workspace after choosing a procedurally generated system whose surrounding sequence you want to survey.",
                        [
                            "Enter a generated system name, ordinary system name, or id64 in Top boxel or generated system. Select an EDSM suggestion when one appears so SrvSurvey retains the authoritative id64.",
                            "Choose Lowest mass code. Keeping it equal to the top system's mass code searches only that boxel; choosing a lower letter includes every nested child down to that level. Check the displayed boxel count before continuing.",
                            "Set Search start date, then decide whether earlier Commander visits and older Spansh body records should count as already complete.",
                            "Enable Require FSS to report all bodies when entering a new system is not enough and you want completion to wait for a full FSS identification. Enabled earlier-visit and older-Spansh completion rules remain explicit exceptions.",
                            "Select Start search. SrvSurvey opens the top boxel, merges local Commander history with available Spansh observations, and chooses the lowest incomplete suffix.",
                        ],
                        [
                            "Mass-code h searches are intentionally unavailable because reliable empty-boxel tracking is not practical at the full 1,280-light-year sector scale.",
                            "Every lower level multiplies the work: one boxel has eight direct children, and selecting several levels can create a very large search tree.",
                        ]
                    ),
                    Section(
                        "Survey the current boxel",
                        "Current boxel prefix identifies the sequence being worked; Next incomplete system is the next active suffix in the chosen direction that has not met your completion rule.",
                        [
                            "Use Refresh boxel to merge systems from the local Commander profile, the active route, and Spansh. Community databases contain only submitted observations, so an unknown system is not proof that it does not exist.",
                            "If the known data ends too early, enter Expected systems and select Apply. This extends the suffix range SrvSurvey will track without claiming that every generated name exists in the game.",
                            "Use Copy next, or enable Auto-copy next system in Galaxy Map, then paste the name into the Galaxy Map. Boxel, Route Manager, and FC Route auto-copy are mutually exclusive.",
                            "When Require FSS is off, an FSD jump into a matching system completes it. When it is on, new local visits wait for Elite to write FSSAllBodiesFound; enabled earlier-history rules still count matching systems as complete.",
                            "Hover over a row's System actions button to Complete, Reopen, Defer, or Start Here. Deferred systems remain unfinished, move to the end of the table, and are skipped until you return to them.",
                            "Start Here defers every unfinished system before the chosen row in the current sort direction. Use Show Only Deferred when you are ready to return to skipped systems.",
                            "The systems table shows ten rows per page. Use Next Jump Page to return to the page containing the next target, Previous or Next page to browse in order, or Select page to jump directly.",
                            "Use Mark Next Empty only when the Galaxy Map confirms that the next incomplete system does not exist. The marker is retained and the search skips to the following target.",
                        ],
                        [
                            "The systems table keeps active systems in the current sort direction and groups deferred systems last. Show Only Deferred temporarily hides the active group.",
                            "The top Current system is where the Commander actually is; it is not the first system in the sequence. Next incomplete system is the next work item calculated from saved completion state.",
                        ]
                    ),
                    Section(
                        "Navigate the boxel hierarchy",
                        "Hierarchy controls move between nested cubes, not between systems in the current table.",
                        [
                            "Read Location in search as a breadcrumb from the search root to the boxel currently being inspected. Select an ancestor to jump back to it.",
                            "Previous at this level and Next at this level move between sibling boxels that share the same parent. The center card always shows the current boxel and its progress.",
                            "Open a child row to move one mass code lower into a smaller cube. Its progress and state indicate whether it is unknown, in progress, empty, or complete.",
                            "Use Up one level to return to the parent. Navigation preserves the same search and does not discard progress from another branch.",
                        ],
                        [
                            "A parent contains eight child cubes at the next lower mass code. This nested layout explains why systems with different prefixes can be close together and systems sharing a large high-mass boxel can be far apart.",
                        ]
                    ),
                    Section(
                        "Save, resume, and audit a boxel project",
                        "Saved searches let one Commander pause several independent boxel surveys and return to each without replacing its progress.",
                        [
                            "Select Save to Library. The first save asks for a name and notes, preloading the top system as the name. Progress then syncs to that library entry automatically.",
                            "Select Open Library to browse saved searches by favorite, name, creation date, last modified date, completion, and notes. Resume Selected restores its tree, options, and completed systems.",
                            "Use Stop search when you want the live Boxel workflow inactive while retaining both its current profile state and its linked library progress.",
                            "Use Audit all boxels when you need every branch refreshed against local history and Spansh rather than only the current boxel. Large audits require explicit confirmation because they can make more than 1,000 requests.",
                            "Cancel audit safely retains the partial work already applied. Linked library progress and its modified date update automatically as audit results are applied.",
                        ],
                        [
                            "External timestamps describe when a community service last received data, not guaranteed first-discovery or current in-game completeness. Treat skip rules as workflow filters rather than proof that a system has nothing left to discover.",
                        ]
                    ),
                    Section(
                        "Review Boxel statistics",
                        "Boxel Stats summarizes the Commander data SrvSurvey has actually recorded; it does not estimate unvisited systems from a configured search size.",
                        [
                            "Open Boxel Stats from the top of the Boxel workspace, then filter by mass code or choose a recently recorded boxel. Open a row to inspect recorded systems, highest suffix, completeness, helium, body classes, averages, and estimated value.",
                            "Explore recorded children to move down the boxel hierarchy. When statistics are opened from a saved-search entry, switch between the selected boxel and the combined saved-search scope; boxels without recorded statistics add nothing to the rollup.",
                            "Refresh reloads current statistics. Rebuild imports historical journals when older visits are missing; ordinary live journal updates do not require a rebuild.",
                            "Export JSON + CSV writes the detailed snapshot and tabular summary to a folder you choose.",
                        ],
                        [
                            "Count Nav Beacon scans as FSS complete changes only the displayed statistic. Minimum-system settings control when averages appear and which boxels export; they do not change stored survey data, Boxel completion, or the next target.",
                        ]
                    ),
                    Section(
                        "Share a survey with VoxStellar",
                        "VoxStellar is an independent boxel-surveying service. SrvSurvey can send selected new exploration journal events to it only after you opt in from the top of the Boxel workspace.",
                        [
                            "Open the information button beside VoxStellar before opting in. It lists the journal events, the exploration data VoxStellar says it stores, its user-content license, privacy policy, terms, and the MIT-licensed EDMC plugin protocol adapted by SrvSurvey.",
                            "Enable Send Journal to VoxStellar for boxel surveying to upload new live Scan, FSDTarget, FSDJump, FSSDiscoveryScan, SAASignalsFound, ScanOrganic, ScanBaryCentre, and CodexEntry events with your Commander name.",
                            "SrvSurvey does not upload journal history read during startup or replay. Turning the option off invalidates entries that are still waiting in memory, and multiple simultaneous Elite windows pause new VoxStellar publication to avoid attributing data to the wrong Commander.",
                        ],
                        [
                            "VoxStellar maintains its own database and says it does not forward these submissions to EDDN. Use a separate EDDN-capable tool when you also want to contribute discoveries to the wider community database.",
                        ]
                    ),
                ]
            ),
            Category(
                "guardian",
                "08",
                "Guardian sites",
                "Locate Guardian sites, align maps, survey points of interest, track Ram Tah progress, and share non-destructive survey packages.",
                [
                    GuardianSection(
                        "Arriving at a Guardian system",
                        "Guardian summaries identify known sites, beacons, survey status, blueprint type, and extra notes for the current system.",
                        [],
                        [
                            "The system summary can appear automatically when the system has known Guardian sites.",
                            "Near a site, the live map projects the published layout around your current position and heading.",
                            "Map size, automatic zoom, SRV-turret zoom, measurement grid, material dots, notes, aerial grid, and legend are independently configurable.",
                        ]
                    ),
                    GuardianSection(
                        "Aligning and surveying a site",
                        "A correct site type and heading make the projected map line up with the ruins or structure.",
                        [
                            "Type A, B, or G for common ruins layouts, or send .site followed by a supported layout name.",
                            "In a ship or SRV, cycle the fire group to choose Alpha, Beta, Gamma, Present, Absent, or Empty; then toggle the configured Guardian confirmation control twice.",
                            "Face the mapped alignment feature and send .heading; send .heading followed by degrees when entering a heading directly.",
                            "Use .tower to record the nearest relic tower state, .empty for an empty puddle, and .note followed by text to append a site note.",
                            "Use .aerial for origin guidance while taking an aligned aerial screenshot, then .map to return to the live map.",
                        ],
                        [
                            "Present, absent, empty, active, scanned, and target states use different fills, outlines, and colors. The icon glossary shows the underlying shapes.",
                        ],
                        [GuideIconKind.GuardianSiteHeading, GuideIconKind.GuardianPoiStates]
                    ),
                    GuardianSection(
                        "Ram Tah and obelisks",
                        "The Ram Tah workspace tracks mission logs, active obelisks, required combinations, and decoded entries.",
                        [],
                        [
                            "An active obelisk receives an emphasized ring; a scanned obelisk is filled with the success color.",
                            "Filters can show only mission logs still needed for the active Ram Tah task.",
                            "The nearest mapped point and target ring help correlate the in-game site with the survey layout.",
                        ],
                        [GuideIconKind.GuardianObelisk, GuideIconKind.GuardianActiveObelisk]
                    ),
                    GuardianSection(
                        "Inspecting the Survey map",
                        "Reference maps and Commander surveys use the same selectable marker inspector so the preview matches the live editing workflow.",
                        [
                            "Hover a marker to show its segmented highlight ring, then select it to replace the Selected Map summary with the point inspector.",
                            "Review the marker name, type, angle, distance, rotation, status, relic heading, and component materials. Reference values are shown in the same controls but remain disabled.",
                            "Select empty map space to return to the map summary. Zoom as far as 15x and pan at higher zoom when nearby markers overlap.",
                        ],
                        [
                            "The legend identifies each marker and explains active-obelisk wedges: gray is unscanned, orange is scanned, and cyan is still needed for Ram Tah.",
                            "At the active in-game site, the Commander position is mirrored onto the Survey map. It is a passive marker and cannot block selection of an overlapping survey point.",
                        ]
                    ),
                    GuardianSection(
                        "Editing a Commander survey",
                        "A live Commander survey unlocks the same point form used by the reference preview and adds survey-specific controls below the map.",
                        [
                            "Choose the correct Site type to repair an unknown or misidentified survey, and enter both surface latitude and longitude when the surveyed origin is known.",
                            "Select a marker to update its survey status, relic heading when it is a relic tower, and component materials. Commander-specific raw points also expose angle, distance, and rotation controls with 0.1 increments.",
                            "Add, select, edit, scan or unscan, and delete active obelisks with their map-marker name, Ram Tah log code, and required artifact codes.",
                            "Use Add measured point only while the selected survey is the active site and valid surface position, body radius, site heading, and Commander position agree.",
                            "Save survey to persist the Commander record after reviewing notes, groups, obelisks, and survey points.",
                        ],
                        [
                            "Published reference geometry is shared by every site of that type and cannot be changed through an ordinary Commander survey. Use a map draft when the shared template itself needs correction.",
                        ]
                    ),
                    GuardianSection(
                        "Building a map draft",
                        "Start map draft from the Selected Map card to author or correct shared template geometry without leaving the Survey map workspace.",
                        [
                            "Start the draft, then select a point to rename it, change its type, fine-tune angle, distance, or rotation in 0.1 steps, or remove it.",
                            "Choose a local background image and adjust its X, Y, and scale values while reviewing the preview on the same map.",
                            "At the live site, add a measured master point from the Commander position and place or remove obelisk group labels using label, angle, and distance.",
                            "Export verified catalog to persist the draft for review, or Discard draft to abandon every unexported shared-template change.",
                        ],
                        [
                            "Draft changes preview immediately but remain session-only until verified export. Starting a draft naturally unlocks the same selected-point form that was read only in reference mode.",
                        ]
                    ),
                    GuardianSection(
                        "Sharing a Guardian survey",
                        "Share data packages meaningful Commander discoveries for review without changing published reference data.",
                        [
                            "Open Share data from the selected site or Guardian workspace and prepare the survey bundle.",
                            "Review the included sites and output path, then copy the bundle path or file, open its folder, or open the Guardian Science Corps destination.",
                        ],
                        [
                            "The export preserves site identity, visits, headings, surface location, notes, point states, relic headings, obelisk groups and scan state, raw points, and component materials in the expected compact survey format.",
                            "Only meaningful differences are included in a content-addressed ZIP. Packaging never clears the legacy staging folder or modifies the published catalogs.",
                        ]
                    ),
                ]
            ),
            Category(
                "quests",
                "09",
                "Quests",
                "Follow communications, objectives, settlement routes, massacre progress, and optional developer-authored quest chapters.",
                [
                    Section(
                        "Communications and objectives",
                        "Quest communications appear when an enabled chapter emits messages or objective updates.",
                        [],
                        [
                            "Unread message counts are shown in the application and the compact quest indicator.",
                            "Use the quest communications shortcut to show or hide the overlay without stopping the quest.",
                            "Objective history and variables are stored per quest identity so unrelated chapters do not overwrite one another.",
                        ]
                    ),
                    Section(
                        "Settlement and massacre guidance",
                        "Active quest geometry can add routes and target radii to a human-settlement map, while massacre missions track credited kills by mission giver.",
                        [],
                        [
                            "A gold target circle means the Commander is outside the objective radius; the accent color means the Commander is inside it.",
                            "Massacre rows show progress only for compatible active missions and avoid double-crediting one bounty event to the same mission giver.",
                        ]
                    ),
                    Section(
                        "Developer tools",
                        "Quest authors can validate and test Lua chapters without risking unrelated player progress.",
                        [],
                        [
                            "Imports are hash-verified and preserve progress only when the quest identity matches.",
                            "Definitions can be reloaded from disk, edited as JSON, debugged, started, stopped, and removed with explicit guards.",
                            "Publishing to Raven requires a separate overwrite confirmation; local testing does not publish implicitly.",
                        ]
                    ),
                ]
            ),
            Category(
                "colonisation",
                "10",
                "Colonization",
                "Connect Raven Colonial, manage construction projects safely, plan cargo, repair completed build-site records, and reconcile system data.",
                [
                    Section(
                        "Connect Raven Colonial",
                        "Raven access is opt-in. A valid Commander API key is required before private project data or authenticated mutations are used.",
                        [
                            "Open Colonization and check Enable colonization features.",
                            "Expand Fleet Carrier cargo sync, enter the active Commander’s Raven API key, and select Save key. Get API key opens Raven’s key page. Refresh projects after validation succeeds; rejected credentials do not replace the stored profile.",
                            "Use the Raven links when you need to review a project or system in the website.",
                        ],
                        [
                            "Automatic ship cargo publishing, Fleet Carrier publishing, system updates, and Green Gas Giant publication each have their own gates.",
                        ]
                    ),
                    Section(
                        "Focused primary project",
                        "The Commander shopping focus is explicit Raven state and is separate from a system's primary-port order.",
                        [
                            "Choose Make primary on the intended project before focusing the shopping plan.",
                            "Use Clear primary only when you intentionally want aggregate planning across visible projects.",
                        ],
                        [
                            "Making a project primary changes Commander planning focus; it does not reorder the sites stored for the system.",
                        ]
                    ),
                    Section(
                        "Primary port order safety",
                        "Creating a project preserves Raven’s existing primary port.",
                        [],
                        [
                            "SrvSurvey checks the first saved system site before and after project creation. If the new project moves it, SrvSurvey restores the original order without replacing the other site details.",
                            "If the existing primary port cannot be identified or the order cannot be verified, the operation reports the problem. Review Raven before trying again.",
                        ]
                    ),
                    Section(
                        "Create and update projects",
                        "Project creation uses the live docked construction-site context and a shipped build catalog.",
                        [],
                        [
                            "Review the system, body, construction market, build type, layout, architect, and notes before confirming publication.",
                            "Helpers can only link a visible planned Raven site; only the system architect can start a build project without a plan.",
                            "Depot contribution, completion, docking, beacon architect, and market events update the matching project only when the identity is unambiguous.",
                            "Docking an untracked Raven construction site auto-links the commander only when the active commander name matches the project's architect; otherwise the site loads as untracked for shopping.",
                            "Stale docking, SRV, bootstrap, malformed delta, or missing API-key context cannot publish project mutations.",
                        ]
                    ),
                    Section(
                        "Construction shopping overlay",
                        "The shopping plan compares project need with current ship cargo and linked Fleet Carrier cargo.",
                        [],
                        [
                            "Need is the remaining project requirement; FC is the cargo on linked carriers; Ship is the current usable ship inventory.",
                            "A check means that source has enough for the row. A direction marker calls out the next useful item or focused project. Dimmed rows are unavailable or already satisfied.",
                            "The overlay can focus a primary project, the docked build site, a local aggregate, or all visible projects depending on game context and settings.",
                            "Fresh Market and Cargo events reconcile carrier and ship totals. Ambiguous multi-client cargo is excluded until a fresh unambiguous file write arrives.",
                        ]
                    ),
                    Section(
                        "Completed build-site repair",
                        "When docking at a player colony, SrvSurvey can repair a Raven system-site entry that is missing its MarketID or final name.",
                        [],
                        [
                            "The repair compares the live docked market with Raven system sites and sends one targeted authenticated PATCH only when exactly one safe match exists.",
                            "Ambiguous or missing matches are not changed and remain retryable.",
                            "Successful repairs are kept in a persistent 50-location guard, preventing repeat API calls on routine revisits.",
                            "A cache-write failure does not repeat a successful server mutation in the same session.",
                        ]
                    ),
                    Section(
                        "Resolve unconfirmed deliveries",
                        "A lost connection can leave a delivery’s server outcome unknown. Retrying it without checking could credit it twice.",
                        [
                            "When Unconfirmed construction deliveries appears at the top of Colonization, check the listed deliveries on Raven.",
                            "Select only deliveries you are ready to resolve using their checkboxes.",
                            "Use Retry selected deliveries for deliveries Raven has not recorded. Use Dismiss selected deliveries for those already credited; dismissing clears local recovery records and does not undo a Raven delivery.",
                        ],
                        [
                            "Unselected records stay pending and survive an application restart. The panel disappears once no unresolved deliveries remain.",
                        ]
                    ),
                    Section(
                        "Raven system update tool",
                        "The updater merges live discoveries, local edits, and the latest Raven copy before publishing.",
                        [
                            "Acquire the required Raven system-update permission and API key.",
                            "Confirm any body import, review inferred sites and manually edit details as needed.",
                            "Load system only succeeds when the architect is unassigned or matches the active commander. Anyone else is refused with a not-the-architect warning.",
                            "Refresh to perform a three-way reconciliation. Concurrent edits to the same field become blocking conflicts; unrelated remote fields are preserved.",
                            "Resolve conflicts, review the final patch, then confirm publication separately.",
                        ],
                        [
                            "Scanning, approaching, or docking can infer system/site data locally, but none of those actions alone publishes the system record.",
                        ]
                    ),
                ]
            ),
            Category(
                "overlays",
                "11",
                "Overlays and controls",
                "Control when overlays appear, edit their positions safely, customize their independent palette, and configure keyboard or controller actions.",
                [
                    Section(
                        "Context and visibility",
                        "Each overlay has a specific game-state trigger and can also have a manual shortcut.",
                        [],
                        [
                            "FSS overlays follow FSS focus; body and biology overlays follow the current body; maps follow nearby site or surface state; travel overlays follow targets and routes; shopping follows active construction context.",
                            "Individual visibility settings suppress only that overlay. Toggle overlays hides or restores the detached overlay group.",
                            "Manual show shortcuts do not fabricate live data; the full position editor is the intentional offline preview surface.",
                            "Use the overlay-settings icon beside a supported navigation category for that activity’s controls. Theme > Overlay Settings opens the complete overlay controls.",
                            "When overlays compete for the same context, SrvSurvey hides lower-priority panels first and restores them from their current settings and game state when the blocker ends.",
                        ]
                    ),
                    Section(
                        "Overlay Exceptions",
                        "Restrict each overlay category to selected ships or vehicles without changing its normal triggers.",
                        [
                            "Open the category’s overlay settings and select Overlay Exceptions at the top right.",
                            "Use Check All or Uncheck All, then select the allowed entries under Small, Medium, Large and Vessel / Vehicle. Changes save immediately.",
                            "The gate follows the vessel you are aboard: SRV, Scorpion, Rhino and Nomad have separate entries; Fighters covers all fighter variants. On foot is independent of your parked ship.",
                        ],
                        [
                            "All entries start checked. Other / unknown controls unrecognized ship types, unavailable boarded status and taxi/multicrew rides where the current vessel is not identified.",
                            "Firegroups has a dedicated overlay settings window with its own exceptions. The button in global overlay settings controls the other Status & utilities panels. Panels shared by several settings categories must be allowed in each of those categories.",
                            "These filters only hide overlays; enabling a vehicle does not bypass the panel’s normal game-state trigger, visibility switch or focus rules. Editor previews remain available.",
                        ]
                    ),
                    Section(
                        "Edit all overlay positions",
                        "The position editor can display realistic simulated overlays without Elite running.",
                        [
                            "Open Theme > Overlay Settings > Edit Overlay Positions.",
                            "Choose a category from the selector at the top; only that group appears so the desktop is not overwhelmed.",
                            "Drag the bordered previews to the desired monitors and positions. Preview content comes from a false game state shaped like real overlay data.",
                            "Use the check button to save every staged position, or X to close and restore the original layout.",
                        ],
                        [
                            "The editor forces normally contextual overlays to appear, but it does not publish network data or change the player profile.",
                        ]
                    ),
                    Section(
                        "Move existing live overlays",
                        "Live-interaction mode is intentionally separate from the full editor.",
                        [
                            "Press Toggle live overlay interaction (default Alt+Shift+O unless changed) while overlays are already visible.",
                            "The existing live overlays stop being click-through and can be dragged in place.",
                            "Press the shortcut again to restore passive click-through behavior.",
                        ],
                        ["This shortcut does not open simulated previews or change which overlays are visible."]
                    ),
                    Section(
                        "Overlay appearance and saved states",
                        "The in-game palette is independent from the application light/dark theme.",
                        [
                            "Open Theme > In-game overlay appearance and choose colors with the picker beside each overlay control.",
                            "Adjust Header, Title, Value, Body, Detail, and Caption font sizes in half-point increments. These shared roles keep related overlays consistent instead of resizing individual labels independently.",
                            "Use Refresh preview to apply unsaved colors and typography to open overlays and position previews, then Apply to overlays to keep them or Discard changes to restore theme.json.",
                            "Save a named overlay state when its palette and typography are ready; choose saved states from the dropdown or reload the original defaults.",
                        ],
                        [
                            "Changing Blue light, Blue dark, Orange dark, Green light, or Green dark for the application never rewrites theme.json or a named overlay state.",
                            "Imported legacy overlay colors, positions, scale, and opacity remain in the overlay control group.",
                        ]
                    ),
                    Section(
                        "Choose an overlay monitor",
                        "Theme > Overlay Settings controls display placement for live panels and the position editor.",
                        [
                            "Choose Overlay monitor, or leave Automatic to follow Elite’s display and use the primary display when Elite is not detected.",
                            "Enable Keep overlays on the selected monitor to constrain dragging to that display. Reopen the position editor after changing the monitor.",
                        ],
                        [
                            "Desktop scaling changes logical display sizes. A 3840×2160 monitor can be reported as 2560×1440 at 150% scale without reducing the captured image.",
                        ]
                    ),
                    Section(
                        "Bypass Window Management on Linux",
                        "This optional setting lets SrvSurvey place and raise separate X11/XWayland overlay windows directly.",
                        [
                            "Open Theme > Overlay Settings and toggle Bypass Window Management.",
                            "Restart SrvSurvey when **App Restart Required appears. The choice applies to both live overlays and the position editor.",
                        ],
                        [
                            "On KDE or GNOME using X11/XWayland, bypass can keep overlays above fullscreen games without KDE window rules. Desktop placement and snapping are bypassed, and visibility and editor focus depend on the compositor.",
                            "With either choice, overlays use a tool window classification that avoids KDE notification and popup animations. The setting does not apply to native Wayland windows or the combined Gamescope presenter.",
                        ]
                    ),
                    Section(
                        "Linux keyboard input sources",
                        "Settings > Input provides one source choice for all keyboard shortcuts in the active Commander profile.",
                        [
                            "Enable key chords, assign the actions you need, and leave Keyboard input source on Automatic initially.",
                            "Test a shortcut with Elite focused. A working source is remembered for that session; shortcuts used in SrvSurvey or before the game runs do not lock in the game source.",
                            "For troubleshooting, select Desktop keyboard, Game display, or Wayland portal when available. Check the status for the selected source and last configured shortcut received.",
                            "Use Reset input detection to return to Automatic and clear the learned source and held-key state. Bindings and desktop approval are retained; no restart is required.",
                        ],
                        [
                            "Game display can follow a separate nested X11 display, including Gamescope, when that display is accessible. Native Wayland input needs desktop Global Shortcuts portal support.",
                            "A manual source does not silently fall back when unavailable. Switch to Automatic or reset detection if your game or desktop setup changes.",
                            "Automatic selection requires confirmed Elite focus. If the desktop cannot report game focus, approved portal actions can remain global while Elite is running; those actions do not lock in a source.",
                        ]
                    ),
                    Section(
                        "Approve or change desktop shortcuts",
                        "The desktop Global Shortcuts portal has its own approval and configuration screen.",
                        [
                            "In Settings > Input, enable key chords and select Desktop shortcut settings when you want to approve or configure portal bindings. Startup does not open this menu automatically.",
                            "On GNOME versions that open SrvSurvey’s Applications page, choose its Global Shortcuts row. Other supported desktops may show their own shortcut dialog.",
                            "Check the approved keys shown in the status. Application key-code fields request bindings, but changes made in desktop settings do not rewrite those fields.",
                        ],
                        [
                            "Keep application and desktop bindings consistent when switching between portal and raw input. Changed portal requests may need approval through the button.",
                            "Approval can be remembered by the desktop. Availability depends on its portal backend, not just the distribution name. If unsupported, use an available raw input source.",
                        ]
                    ),
                    Section(
                        "Wayland screen capture",
                        "Optional display sharing supports FSS tuning completion, first-footfall detection, and Rhino rig tracking on Linux Wayland/XWayland.",
                        [
                            "Open Settings > Application. Enable Wayland screen capture and enable only the individual trackers you want to test; all start disabled.",
                            "Choose capture source again opens the desktop picker immediately, even without Elite running. Select the display where Elite will run and click Share.",
                            "Prefer Display or Monitor. Capturing a Proton/Gamescope game window can be slower; try Window only if display capture cannot provide the correct game image.",
                            "Allow Remember this selection if offered. SrvSurvey requests reuse on later sessions; the desktop controls whether it can restore without asking. Use Choose capture source again when you want to change it.",
                        ],
                        [
                            "No application restart is needed to choose a new source. Detection pauses when the game is unavailable and resumes when valid game context returns.",
                            "Display and captured-frame sizes can differ because of desktop scaling. SrvSurvey maps the game’s crop into the shared display; select the display containing the game and reselect if you move it to another monitor.",
                            "Rhino detection uses your calibrated circles. FSS tuning checks the upper-right game region; first-footfall detection checks the upper central message area. Those fixed regions scale with the game viewport, but altered HUD geometry or obscured content can reduce accuracy.",
                            "The screen is processed locally, not transmitted. Screen sharing and keyboard shortcut approval are separate permissions.",
                        ]
                    ),
                    Section(
                        "Input bindings",
                        "Keyboard and supported controller bindings are configurable per action and checked for collisions.",
                        [],
                        [
                            "Useful actions include overlay visibility, live interaction, map zoom, jump/FSS/body/station panels, colony shopping, system notes, boxel copy/paste, quest communications, VR adjustment, surface bookmarks, and screenshot data.",
                            "A binding is reported as unavailable when the current operating system cannot provide the required global input capability.",
                        ]
                    ),
                ]
            ),
            Category(
                "virtual-reality",
                "12",
                "VR & headset overlays",
                "Connect the VR runtime used by Elite Dangerous, verify SrvSurvey overlays, and calibrate each panel safely on Windows or Linux.",
                [
                    CreateVrConnectionRouteSection(),
                    CreateVrActivationSection(),
                    CreateVrMetaBridgeSection(),
                    CreateVrPlatformSetupSection(),
                    CreateVrCalibrationSection(),
                    CreateVrInteractionSection(),
                    CreateVrTroubleshootingSection(),
                ]
            ),
            Category(
                "settings-migration",
                "13",
                "Settings and migration",
                "Keep application and overlay appearance separate, import an original profile without corruption, and understand every network/privacy gate.",
                [
                    Section(
                        "Theme workspace: application versus overlay appearance",
                        "The shell and the in-game overlays are two separate appearance systems.",
                        [],
                        [
                            "Application theme changes the main Avalonia windows. Choose Blue light/dark, Orange dark, Green light/dark, or the low-glare Monochrome dark palette.",
                            "In-game overlay appearance changes only detached overlays and retains the original SrvSurvey color roles, named states, and defaults.",
                            "Neither selector writes into the other control group, so a light application can use a dark orange overlay palette or any custom combination.",
                        ]
                    ),
                    Section(
                        "Finding settings",
                        "Settings uses focused categories and a searchable catalog so long configuration pages do not have to be scanned manually.",
                        [
                            "Choose Application, Desktop, Global overlays, Input, Privacy & sharing, Screenshots, or Data & migration from the Settings column.",
                            "Enter one or more terms in Search settings. Use Up and Down to move through grouped results, Enter to open the selected setting, or select a result directly.",
                            "Open Theme workspace from Global overlays when you need overlay colors, typography, opacity, layout, or individual overlay controls.",
                        ],
                        []
                    ),
                    Section(
                        "Desktop placement and focus",
                        "Desktop behavior controls where the main application returns, how large it appears, and whether focus is handed back to Elite Dangerous.",
                        [],
                        [
                            "SrvSurvey restores the last on-screen window position when that display is still available. Otherwise it uses the configured Default monitor and clamps the window to the usable desktop.",
                            "Application window scale changes the complete shell and is reduced only when the selected size would not fit the active monitor.",
                            "Focus-on-start, focus-on-minimize, focus-after-jump, and minimize-to-tray are independent. Passive overlays remain click-through and do not activate the application window.",
                        ]
                    ),
                    Section(
                        "Back up and continue on another computer",
                        "Settings > Data & migration can back up your profile to Google Drive or a local file. Link the same Google account on each computer to synchronize portable preferences and saved work.",
                        [
                            "Choose Link Google Drive and approve access in your browser. Automatic sync checks cloud changes at startup and saves a backup on orderly shutdown.",
                            "Use Backup now before changing computers. Use Sync now to check cloud changes while SrvSurvey is open; downloaded changes apply after restart.",
                            "If both computers changed the same value, choose this machine's or the cloud's conflicting values. Independent changes are kept together.",
                            "Expand the restore history to recover an older backup, or export and restore a backup file without Google Drive.",
                            "Refresh cloud history to see the backup count and storage used. Download the selected cloud backup to keep a separate copy. Delete selected backup or Clear all SrvSurvey cloud backups asks for confirmation before permanently removing cloud history; local data and Google linking stay intact.",
                        ],
                        [
                            "Themes, portable overlay preferences, shortcut bindings, workspace selections, saved searches, routes, bookmarks and survey data can follow you. Display positions, capture calibration, platform options and device choices stay local; their backup can only be restored on the originating machine.",
                            "Credentials, journals, screenshots and queued API reports are excluded. Offline changes stay local and sync can be retried next session. Close all SrvSurvey instances before applying a restore.",
                            "Google linking is available when your build includes the publisher's Desktop OAuth client setup. Development builds can import that setup from Data & migration.",
                            "Cloud backups use Google's hidden app-data folder and count toward your Google storage. Automatic backups on any linked computer can create new files after you clear history.",
                        ]
                    ),
                    Section(
                        "Import an original SrvSurvey profile",
                        "Legacy migration is backup-first, staged, checksum-verified, and designed to leave the source untouched.",
                        [
                            "Close the original SrvSurvey so its files are stable, then choose its profile folder in Settings.",
                            "Review the detected Commander and destination, then choose Back up, verify, and import.",
                            "Wait for the manifest and verification summary. If activation fails, the staged destination is rolled back and the original folder remains unchanged.",
                        ],
                        [
                            "Commander data, journeys, routes, notes, Codex progress, system surveys, Guardian work, quest state, Raven settings, plotters.json layout/opacity, and theme.json overlay colors are migrated when compatible.",
                            "Unknown compatible JSON fields are preserved where the modern store supports lossless merging. Incompatible reference catalogs are ignored safely and reported in logs.",
                            "A SHA-256 manifest records the imported files so partial copies and silent corruption can be detected.",
                        ]
                    ),
                    Section(
                        "Link a Frontier account",
                        "Frontier linking provides Commander, ship, market, and Fleet Carrier information for the active profile.",
                        [
                            "Select the Active Commander card, then use the Frontier connection control to open the official sign-in page.",
                            "Authorize SrvSurvey in the browser and allow the callback link to open SrvSurvey. Return to the app and check the linked Commander.",
                            "For another Commander, launch that profile’s isolated instance from Overview and start linking there.",
                        ],
                        [
                            "Keep the intended Commander instance open during authorization. If the callback reports no available application, retry a fresh link after confirming the current SrvSurvey installation is registered.",
                        ]
                    ),
                    Section(
                        "Reference-data updates",
                        "SrvSurvey still uses small version files and GitHub-hosted JSON catalogs so data corrections can ship without reinstalling the application.",
                        [],
                        [
                            "At startup, SrvSurvey checks the published version index and downloads only catalogs whose version changed.",
                            "Downloaded catalogs are bounded, validated, checksummed, staged, and activated with health confirmation and rollback.",
                            "Guardian sites, biology criteria, human settlements, and other published datasets remain independent from executable releases.",
                            "Diagnostics can refresh the catalogs manually and reports when a restart is required.",
                        ]
                    ),
                    Section(
                        "Privacy and network services",
                        "External reads and every upload path are visible and separately gated.",
                        [],
                        [
                            "System enrichment may use EDSM, Spansh, Canonn, or Raven depending on the enabled feature.",
                            "EDDN publication, Inara publication, direct EDSM synchronization, human-settlement geometry, Green Gas Giant candidates, Raven cargo, Fleet Carrier data, system updates, and quest publication each require the corresponding setting, credential, or explicit confirmation.",
                            "Analysis, previews, imports, and historical reconstruction do not imply network publication.",
                        ]
                    ),
                    Section(
                        "EDDN sharing",
                        "EDDN sharing is installation-wide, disabled by default, and configured from Settings > Privacy & sharing.",
                        [
                            "Select Configure sharing, review the journal and companion-file data SrvSurvey can send, then enable live-session sharing and save.",
                            "Use only one EDDN uploader at a time. Disable sharing in SrvSurvey when EDMC or another application already publishes the same events.",
                            "Return to the same dialog to disable sharing; pending local messages are removed when consent is withdrawn.",
                        ],
                        [
                            "Eligible live events enter a bounded durable retry queue. Startup history and multicrew activity are not uploaded, and delivery pauses when multiple Elite windows make companion-file ownership ambiguous.",
                            "EDDN needs no personal account or API key. This release sends production-schema messages through the Live gateway.",
                        ]
                    ),
                    Section(
                        "Inara publishing",
                        "Inara publishing follows the active Commander and is enabled by saving that Commander's personal API key.",
                        [
                            "Open Settings > Privacy & sharing, confirm the Commander shown in the Inara card, enter the personal API key, and select Save API key.",
                            "Remove API key and confirm to disable Inara uploads for that Commander. Switching Commanders loads that profile's separate key and publication state.",
                            "Enable Inara uploads in only one application at a time to avoid duplicate Commander events.",
                        ],
                        [
                            "Queued events never cross Commander or API-key boundaries. Live/beta eligibility, final session reporting, batching, retries, and shutdown flushes follow the bound Commander session.",
                        ]
                    ),
                    Section(
                        "EDSM synchronization",
                        "Direct EDSM synchronization follows the active Commander and is enabled by saving that profile's personal API key.",
                        [
                            "Open Settings > Privacy & sharing, confirm that the active Commander shown in the EDSM card matches the Commander registered with EDSM, enter its personal API key, then select Save and enable.",
                            "Select Disable and confirm to remove the API key and cancel pending delivery for that Commander. Switching Commanders loads that profile's separate EDSM state.",
                            "Enable direct EDSM synchronization in only one application at a time to avoid duplicate requests.",
                        ],
                        [
                            "Only new attributable Live journal events are considered. SrvSurvey first loads EDSM's current discard policy, then sends bounded ordered batches with EDSM-sanctioned system, station, and ship context when known.",
                            "Startup history, Legacy, alpha/beta, diagnostic replay, multicrew, and sessions with multiple Elite windows are excluded. The pending queue is memory-only and credentials or events never cross Commander sessions.",
                        ]
                    ),
                    Section(
                        "Screenshots, notifications, stream, and VR",
                        "Optional desktop integrations are configured independently so unsupported platforms degrade without disabling core journal processing.",
                        [],
                        [
                            "Screenshot conversion has its own source, destination, naming, banner, and image-embedding controls. High-resolution naming is determined from the Elite game client dimensions rather than whichever desktop happens to be primary.",
                            "Notifications and the dedicated stream overlay can be enabled without changing ordinary overlay positions.",
                            "VR overlay adjustment captures and resets orientation through dedicated actions; capability and status are reported when the runtime is unavailable.",
                        ]
                    ),
                ]
            ),
            Category(
                "diagnostics",
                "14",
                "Diagnostics and troubleshooting",
                "Inspect current inputs, application logs, journal events, updates, caches, crash reports, and safe recovery tools.",
                [
                    Section(
                        "Journal source and inspector",
                        "Start here when the Commander, system, body, cargo, or overlay state does not update.",
                        [
                            "Confirm Selected folder and Current journal refer to the Commander you are playing.",
                            "Refresh the session, then inspect recent journal events and parsed state for the expected event.",
                            "When multiple Commanders share a folder, confirm the preferred Frontier ID and active Elite window.",
                        ],
                        [
                            "Malformed or unknown journal fields are logged without requiring SrvSurvey to rewrite the source file.",
                        ]
                    ),
                    Section(
                        "Application logs and crash reports",
                        "Diagnostics exposes persisted application logs and the non-destructive crash-report workflow.",
                        [],
                        [
                            "Copy the relevant log section when reporting a problem, including the first error and the operation that preceded it.",
                            "Crash packages are staged for review and do not silently upload user data.",
                            "Network response sizes, validation failures, ignored incompatible catalogs, and rollback results are recorded for diagnosis.",
                        ]
                    ),
                    Section(
                        "Updates and reference recovery",
                        "Executable releases and reference catalogs are checked and activated independently.",
                        [],
                        [
                            "Application update checks verify checksum-indexed packages, prevent overlapping installations, and confirm before handing off when other SrvSurvey instances are running.",
                            "Downloads are staged before shutdown; cancelled or failed pre-handoff attempts clean their candidates, stale plans are removed automatically, and failed startup can roll back safely.",
                            "Reference refresh updates only changed catalogs and retains verified backups.",
                            "Visited-star cache swap/restore is blocked while Elite is running, uses a persistent original backup, validates responses, and rolls back failed activation.",
                        ]
                    ),
                    Section(
                        "Common fixes",
                        "Use the smallest targeted recovery before considering a profile import or reset.",
                        [],
                        [
                            "No live data: verify journal folder, Commander preference, and current journal in Diagnostics.",
                            "Overlay missing: verify its individual visibility and trigger, then test it in Edit overlay positions.",
                            "Overlay cannot be dragged: use Toggle live overlay interaction for live overlays or the full position editor for simulated overlays.",
                            "Wrong cargo: close extra Elite clients and wait for a fresh Cargo.json write from the intended Commander.",
                            "Raven mutation rejected: verify the active Commander key, feature permission, dock/system identity, and explicit confirmation state.",
                            "Imported data incomplete: inspect the import manifest and logs; do not modify or delete the untouched legacy source while investigating.",
                        ]
                    ),
                ]
            ),
            Category(
                "chat-commands",
                "15",
                "Chat Commands",
                "Activity-by-activity reference for the chat messages SrvSurvey currently recognizes, including examples, requirements, and clearing behavior.",
                ChatCommandSections.Value
            ),
            Category(
                "icons",
                "16",
                "Overlay icon glossary",
                "A visual reference for route and body artwork, text symbols, biology reward PIPs, surface-radar markers, Guardian points, and human-settlement map icons.",
                [
                    Section(
                        "How to read color",
                        "Shape conveys the object or state; color adds live context and follows the saved in-game overlay palette.",
                        [],
                        [
                            "Primary/secondary colors identify ordinary information and active guidance. Success means confirmed, complete, or safely outside a sample radius.",
                            "Warning calls for attention. Danger marks an invalid, prohibited, failed, dead, or too-close state. Muted/dim means historical, unavailable, inactive, or already satisfied.",
                            "Gold highlights valuable biology, Guardian information, focused targets, or other high-interest rows depending on the overlay.",
                        ]
                    ),
                ],
                CreateIconGlossary()
            ),
            Category(
                "mining-workspace",
                "17",
                "Mining workspace",
                "Plan mining trips, track sessions and missions, and keep shared locations and reports.",
                [
                    Section(
                        "Sessions and cargo",
                        "Activities → Mining combines ship mining tools with the existing cargo and journal feed.",
                        [
                            "Start manually or enable automatic start when a prospector limpet launches. Pause and resume exclude breaks from efficiency. End a session to add it to Reports.",
                            "A recovered session opens paused. Check the system and ring, then resume when ready.",
                            "Session shows prospecting percentages, core discoveries, quality hits, refined tonnage, limpets, engineering materials and cargo. Manual count corrections retain the original observations.",
                        ],
                        [
                            "Cargo transfers and purchases do not count as mining production. Mission cargo is allocated once across active mining missions.",
                            "Refinery bins are not reported by the journal. Enter pending contents manually; reports keep these estimates separate from refined tonnage.",
                            "The optional cargo-full reminder waits until cargo has been full and collection idle for one minute.",
                        ]
                    ),
                    Section(
                        "Find rings, markets and traders",
                        "Mining → Find keeps each search and its results in a dedicated viewport.",
                        [
                            "Find → Rings combines historical reference rings, your scans and Spansh. Choose Local for cached data, Spansh for online results, or Both. Refine by radius, mineral, ring type, count, overlap or RES annotations.",
                            "Right-click a ring to bookmark it, save the online observation locally, copy the system or open a reference site. Historical journal import adds this commander's rings and missions without starting sessions.",
                            "Markets uses Trade objective to find buyers for mined cargo or sellers for supplies. Choose a commodity category and up to five commodities; expand station and freshness filters for pads, carrier exclusions and price age. Traders has its own results for raw, manufactured or encoded engineering materials.",
                            "Use Local ring discoveries / Import earlier journals to review your own scans and import earlier journals. Provider page starts at 0; station searches use 20 results per page.",
                        ],
                        [
                            "Unknown coordinates are not treated as nearby. Bundled observations are historical; multiple hotspots alone do not establish an overlap. Local searches show at most 500 rings.",
                            "Prices are observations and may change. Mining → Settings → Session can enable receive-only EDDN observations, retained for 24 hours for commodities. This does not enable uploads.",
                            "Fleet Carrier reuses the existing Frontier profile. Distance supports two systems, current position, home and your carrier's system.",
                        ]
                    ),
                    Section(
                        "Find Platinum spots",
                        "Mining > Find > Platinum ranks candidate rings for a platinum trip.",
                        [
                            "Choose the reference system, radius, result count, reserve level, minimum hotspots, and data source.",
                            "Select Find Platinum spots. Compare Why it ranks, RES, overlap, reserve, and travel distances.",
                            "Use Find buyers for selected spot to look for selling stations, or bookmark the ring for later.",
                        ],
                        [
                            "Spots++ combines built-in historical rings, Commander discoveries, live EDDN observations, and Spansh. An overlap annotation is evidence from those sources; several reported hotspots alone do not prove an overlap.",
                        ]
                    ),
                    Section(
                        "Plan mining for Powerplay",
                        "Mining > Powerplay finds mining and selling opportunities for Reinforce, Undermine, or Acquire.",
                        [
                            "Enter a Reference system and Distance, then choose Your Power, Power goal, and optionally an Opposing Power.",
                            "Select minerals and Mining type. Traditional ring searches support Core, Laser Surface, Surface Deposit, and Sub Surface Deposit; choose Planetary Mining for landable surface-mining bodies.",
                            "Adjust demand, landing pad, market age, system state, and result count, then select Search.",
                            "Compare the result systems, stations, prices, body or ring details, State, and Power. Expand a system to inspect its available destinations.",
                        ],
                        [
                            "These are planning filters, not a merit calculator. Check current in-game eligibility and reward rules before mining or selling. Ownership, market prices, and demand can change.",
                            "Unknown Power ownership is not treated as an enemy or an acquisition target. Some states depend on local journal/EDDN observations and may be missing from an online provider.",
                        ]
                    ),
                    Section(
                        "Track mining missions",
                        "Mining > Missions compares active mining contracts with cargo and delivery progress.",
                        [
                            "Accept a mining mission in Elite and check its commodity, required amount, delivered amount, onboard allocation, and remaining need.",
                            "Use Find hotspots for this commodity from a mission’s context menu to plan collection.",
                            "Complete, abandon, or fail the mission in Elite; journal events update the list.",
                        ],
                        [
                            "Cargo is allocated in acceptance order so one unit is not counted toward several missions. Mission progress depends on the journal; SrvSurvey does not submit the mission for you.",
                        ]
                    ),
                    Section(
                        "Shared bookmarks",
                        "Navigation → Bookmarks and Mining → Bookmarks use the same catalog.",
                        [
                            "Enter a system, optional body/ring and category; saving a new category makes it available to the category filter.",
                            "Store minerals, ratings, hotspot names, average yields, last-mined dates, overlap/RES notes and screenshots. Right-click to copy or delete; Undo restores the last deletion during this run.",
                            "Import shared bookmark JSON or EliteMining bookmark lists. Imports retain existing locations rather than silently replacing them.",
                        ],
                        []
                    ),
                    Section(
                        "Reports and backups",
                        "Reports preserve the observations behind mining statistics.",
                        [
                            "Select a completed session to edit notes or attach screenshots. Export HTML for graphs, material breakdowns and the prospecting timeline; open it in a browser to print or save as PDF.",
                            "Compare all sessions, export CSV, or import EliteMining/SrvSurvey summary CSVs. Imported summaries keep their original fields without pretending that per-asteroid journal data was supplied.",
                            "Mining → Settings → Backup exports a ZIP with commander mining data, named Firegroups configurations and cached loadouts, shared bookmarks and local screenshot attachments. Restore applies the backed-up contents and keeps previous files for recovery. Older ZIPs without Firegroups leave current named configurations intact.",
                        ],
                        [
                            "Screenshots larger than 20 MB or unsupported formats are omitted from packaged images. Keep originals when using standalone bookmark JSON.",
                        ]
                    ),
                    Section(
                        "Mining notifications and announcements",
                        "Ship mining notifications and optional speech are independent of Rhino guidance and Firegroups.",
                        [
                            "Enable Mining notifications in Mining Overlay Settings and choose whether they are limited to an active session or hidden in supercruise.",
                            "Open Mining > Settings > Announcements to configure collected, refined, and prospecting messages, material thresholds, core filters, and named presets.",
                            "Save mining settings to retain the Commander’s preferences.",
                        ],
                        [
                            "Mining notifications hide on foot or aboard an SRV. Optional speech uses locally installed Windows voices.",
                            "Firegroups has its own category in Guides and its own overlay-settings window.",
                        ]
                    ),
                ]
            ),
            Category(
                "firegroups",
                "18",
                "Firegroups",
                "Create ship-specific primary and secondary firing references and control where the live overlay appears.",
                [
                    Section(
                        "Create a ship configuration",
                        "Firegroups is a reference for your in-game assignments; it does not change Elite’s firing bindings.",
                        [
                            "Open Firegroups below Fleet Carrier in the sidebar. A Loadout for the identified ship supplies its equipped modules.",
                            "Choose A–H with the arrows. Add primary and secondary rows with the circled plus controls, choose modules, and use Add group for another group.",
                            "Enter a configuration name and select Save. The tree previews the saved assignments; saved configurations belong to the Commander and ship identity.",
                        ],
                        [
                            "The D-Scanner, SC-Suite, Data Link Scanner, and Composition Scanner are always available as built-in actions.",
                            "Other supported weapons and scanners, including FSD interdictors, are offered when equipped. Assignments for removed modules remain visible with a warning. Engineering conversions currently retain the base module name.",
                        ]
                    ),
                    Section(
                        "Manage saved firegroups",
                        "Named configurations let you keep different references for the same ship.",
                        [
                            "Select a saved name to edit it, or expand its row to inspect its groups.",
                            "Use Save to retain changes. Remove or a saved row’s trash button requests deletion; confirm Yes to remove it or No to keep it.",
                        ],
                        [
                            "The live overlay uses the saved configuration and Elite’s active fire group, not unsaved editor changes. Mining > Settings > Backup includes Firegroups configurations and cached loadouts.",
                        ]
                    ),
                    Section(
                        "Firegroups cockpit visibility",
                        "Only the main cockpit view is enabled by default.",
                        [
                            "Open the overlay-settings icon beside Firegroups.",
                            "Under Cockpit view visibility, choose Left, Main, and Right. Changes apply immediately and survive restart.",
                            "Use the same window to configure the visibility shortcut and Overlay Exceptions for allowed vessels.",
                        ],
                        [
                            "Left and Right correspond to Elite’s external and internal panels. Main is the normal no-panel cockpit focus; unrestricted head-look direction is not reported by the game.",
                            "The overlay requires the identified boarded vessel, its Loadout, and a saved configuration. It hides on foot and in other panel views such as maps, comms, and FSS. Enabling a view does not bypass those requirements.",
                        ]
                    ),
                ]
            ),
            Category(
                "fleet-carrier",
                "19",
                "Fleet Carrier",
                "View personal and squadron carrier information, link Raven inventories, and plan travel.",
                [
                    Section(
                        "Personal, squadron, and linked carriers",
                        "Fleet Carrier sits below Overview and uses Frontier and Raven data for different purposes.",
                        [
                            "Link the active Commander’s Frontier account to load personal Fleet Carrier and Squadron Carrier information. Use Refresh when needed; cached data and cooldowns prevent repeated requests.",
                            "Open Linked carriers for RavenColonial-linked inventories and use its on-demand refresh.",
                            "Docking at a linked carrier with a squadron bank can identify the squadron carrier. Otherwise select it explicitly.",
                        ],
                        [
                            "Frontier supplies capacity, finances, and services. Raven supplies linked cargo inventory and follows your Raven cargo-sync preference.",
                            "Travel > Distance provides current, home, and carrier shortcuts; FC Routes handles carrier journey planning.",
                        ]
                    ),
                    Section(
                        "Keep linked cargo current",
                        "Automatic Raven Fleet Carrier cargo sync requires an API key and explicit consent.",
                        [
                            "Open Colonization, save the active Commander’s validated Raven API key, and enable Sync linked Fleet Carrier cargo automatically if desired.",
                            "Dock at the intended linked carrier and open its market to provide a fresh inventory snapshot.",
                            "Review the sync status. A failed or ambiguous update needs a fresh valid context; do not use another Commander’s shared cargo data.",
                        ],
                        [
                            "Ship cargo publishing is a separate option. Carrier purchases, sales, and transfers are attributed to the carrier docked at when the event occurred, including events immediately before undocking.",
                        ]
                    ),
                ]
            ),
        ];
    }

    private static GuideSectionViewModel CreateVrConnectionRouteSection()
    {
        return Section(
            "Choose the connection route",
            "SrvSurvey uses SteamVR's OpenVR overlay compositor because it can place a companion panel over another running VR application.",
            [
                "Open Settings > Global overlays and find VR overlays.",
                "Choose SteamVR headset for Index, Vive, Pimax, Bigscreen Beyond, Pico, or another headset already exposed to SteamVR.",
                "For Meta Quest or Rift, choose the exact bridge you use: Link / Air Link, Steam Link, Virtual Desktop, or experimental ALVR.",
                "Choose Windows Mixed Reality only on a Windows installation where its headset software and SteamVR bridge remain available. Check the vendor’s current support before changing Windows versions.",
                "Use Custom OpenVR runtime only when the compositor implements the OpenVR overlay API and you know its process name.",
            ],
            [
                "Every route in this panel feeds the same OpenVR overlay backend. An OpenXR-only Elite session does not provide the external-overlay surface SrvSurvey needs.",
                "Changing connection routes does not erase overlay positions or vehicle-mode calibrations.",
            ]
        );
    }

    private static GuideSectionViewModel CreateVrActivationSection()
    {
        return Section(
            "Pair the headset and activate overlays",
            "The settings panel keeps headset pairing, runtime startup, and SrvSurvey activation in the order they must happen.",
            [
                "Complete the vendor's normal wired or wireless headset connection first.",
                "Start SteamVR and wait until it reports the headset ready before launching Elite Dangerous in VR.",
                "Enable VR overlays in SrvSurvey. The status changes from Waiting to Connecting and then Connected automatically.",
                "Use Check connection after changing runtimes or repairing a headset connection.",
            ],
            [
                "Meta users must launch Elite through SteamVR. A session running directly in the Meta runtime cannot receive SrvSurvey's OpenVR overlays.",
                "A Connected state with zero live overlays means the runtime is ready but no SrvSurvey panel is currently visible in the active game context.",
            ]
        );
    }

    private static GuideSectionViewModel CreateVrMetaBridgeSection()
    {
        return Section(
            "Choose a Meta compatibility bridge",
            "Meta headsets can receive SrvSurvey overlays when a supported bridge presents the headset to SteamVR; the SrvSurvey backend stays OpenVR in every case.",
            [
                "Choose Link / Air Link for Meta's wired or wireless Windows PC-VR connection, then start SteamVR inside it.",
                "Choose Steam Link for Valve's free wireless Quest app and pair it directly with the Windows PC running SteamVR.",
                "Choose Virtual Desktop when its Windows Streamer is configured to launch SteamVR, not its VDXR path.",
                "Choose ALVR only for an advanced or Linux setup; install its SteamVR driver and matching Quest client before pairing.",
            ],
            [
                "Steam Link and Meta Horizon Link are Windows routes. Virtual Desktop's PC-VR streaming also requires Windows.",
                "ALVR supports Windows and Linux, but its Linux setup and SteamVR on Linux are experimental; Elite additionally runs through Proton.",
                "OpenComposite is not a compatible workaround. It translates OpenVR scene applications toward OpenXR, while overlay-application support remains unfinished.",
                "Whichever bridge you choose, SteamVR must show the headset as ready before SrvSurvey can create overlays.",
            ]
        );
    }

    private static GuideSectionViewModel CreateVrPlatformSetupSection()
    {
        return Section(
            "Windows and Linux setup",
            "The same overlay publisher runs on both supported desktop operating systems; only native OpenVR library discovery differs.",
            [],
            [
                "Windows packages include the OpenVR client library used by SrvSurvey.",
                "Linux packages include the OpenVR client library used by SrvSurvey; no custom library location is required.",
                "If packaged OpenVR support is reported unavailable, reinstall SrvSurvey and verify that the package matches the system architecture.",
                "Elite can run through Proton while the native SrvSurvey application publishes to the same SteamVR compositor, but Frontier does not support that Linux game path and SrvSurvey labels it experimental.",
            ]
        );
    }

    private static GuideSectionViewModel CreateVrCalibrationSection()
    {
        return Section(
            "Calibrate panels in the headset",
            "Each live panel keeps its own default placement and can also have ship, SRV, fighter, taxi, or on-foot overrides.",
            [
                "Select Adjust overlays or press the configured Adjust VR overlay shortcut while the runtime is connected.",
                "Choose the live panel and Default or the current vehicle/game mode.",
                "Change scale, position, pitch, yaw, and roll while checking the live preview in the headset.",
                "Save calibration to verify the write and preserve a backup. Cancel restores the saved values; Reset selected restores the shipped placement for that target.",
            ],
            [
                "Reset VR orientation captures the current headset yaw as the overlay origin without changing saved panel placement.",
                "Desktop overlay placement and VR calibration are stored independently.",
            ]
        );
    }

    private static GuideSectionViewModel CreateVrInteractionSection()
    {
        return Section(
            "Interact with panels in the headset",
            "VR overlays stay passive until you explicitly enable SteamVR controller-pointer interaction.",
            [
                "In Settings > Global overlays, assign Toggle VR overlay interaction directly below the Live-overlay interaction shortcut. Global keyboard input must also be enabled.",
                "With VR overlays connected, use that shortcut once to let SteamVR send controller laser-pointer clicks and scrolling to SrvSurvey panels.",
                "Use the shortcut again when finished. SrvSurvey restores passive click-through mode automatically when VR overlays are disabled or the runtime disconnects.",
            ],
            [
                "The desktop live-overlay interaction shortcut remains separate; it controls desktop dragging and does not enable VR input.",
                "Only controls already shown by a live SrvSurvey overlay can be activated. VR calibration still uses Adjust overlays and its dedicated shortcut.",
            ]
        );
    }

    private static GuideSectionViewModel CreateVrTroubleshootingSection()
    {
        return Section(
            "Troubleshoot a missing overlay",
            "Use the connection state to separate headset/runtime problems from ordinary overlay visibility rules.",
            [],
            [
                "Waiting: confirm SteamVR is running and the selected headset route is using that same session.",
                "OpenVR connection failed: restart SteamVR and use Check connection. On Linux, reinstall SrvSurvey if packaged OpenVR support is reported unavailable.",
                "NEEDS ATTENTION — The VR runtime rejected an overlay: use Check connection to retry publishing. If the rejection persists, restart SteamVR, reset the affected panel's VR calibration, and verify that the panel appears normally on the desktop before reconnecting.",
                "Connected with zero panels: make a SrvSurvey overlay visible in Elite or open its supported game context.",
                "Connected but one panel is absent: verify that panel's normal visibility switch, vehicle exception, and game-state trigger.",
                "Meta headset with no SrvSurvey panels: confirm Elite was launched through SteamVR rather than directly through the Meta OpenXR runtime.",
            ]
        );
    }

    private static GuideSectionViewModel[] LoadChatCommandSections()
    {
        using Stream stream =
            typeof(GuideCatalog).Assembly.GetManifestResourceStream(
                "SrvSurvey.Desktop.Resources.chat-commands-guide.json"
            ) ?? throw new InvalidOperationException("The chat command guide resource is missing.");
        return JsonSerializer.Deserialize<GuideSectionViewModel[]>(stream)
            ?? throw new InvalidOperationException("The chat command guide resource is empty.");
    }

    private static IReadOnlyList<GuideIconViewModel> CreateIconGlossary()
    {
        return
        [
            Icon(
                GuideIconKind.Glyph,
                "⚑",
                "Commander first",
                "The organism is a first discovery for the current Commander. The same filled flag on compact FSS rows marks an undiscovered body.",
                "FSS information, system survey, biology"
            ),
            Icon(
                GuideIconKind.Glyph,
                "⚐",
                "Commander regional first",
                "The organism is new to this Commander in the current Codex region. The optional regional-first setting promotes the outline flag and its reward PIP to the highlight color.",
                "Biology system and Codex overlays"
            ),
            Icon(
                GuideIconKind.Glyph,
                "☀",
                "Potential Galactic-region first",
                "The external regional candidate catalog has no reported discovery for this predicted organism in the current Galactic region. This is advisory until an in-game CodexEntry confirms the result.",
                "Biology system and body predictions",
                "global regional first discovery biology"
            ),
            Icon(
                GuideIconKind.Glyph,
                "?",
                "Predicted organism",
                "A trailing question mark means the body criteria predict the colored species, but a DSS or organic scan has not confirmed it. Hover the marker in the overlay for its state description.",
                "Biology body predictions",
                "uncertain predicted species subtype"
            ),
            Icon(
                GuideIconKind.Glyph,
                "►",
                "Next or active direction",
                "Calls out the next action, selected destination, active target, route note, or focused row.",
                "Travel, search, Guardian, colonization, messages"
            ),
            Icon(
                GuideIconKind.Glyph,
                "✓",
                "Complete or sufficient",
                "The scan/task is complete, the condition is valid, or the ship/carrier has enough cargo for the requirement.",
                "FSS, body information, quests, colonization"
            ),
            Icon(
                GuideIconKind.Glyph,
                "⚠",
                "Warning",
                "The route, gravity, search candidate, build state, or other condition needs attention before proceeding.",
                "Flight warning, search, travel, colonization"
            ),
            Icon(
                GuideIconKind.Glyph,
                "◆",
                "Mapped site or Codex item",
                "Identifies a Guardian/site point or a recorded Codex-style item in compact overlay rows.",
                "Guardian, Codex, preview rows"
            ),
            Icon(
                GuideIconKind.Glyph,
                "◇",
                "Objective outside target",
                "A quest objective exists but the Commander is not yet within its required target area.",
                "Quest indicator and settlement objectives"
            ),
            Icon(
                GuideIconKind.DirectionalChevron,
                "",
                "Near and far bearing chevrons",
                "An open chevron points toward a near or standard target. A double chevron marks a target beyond its defined far threshold; markers without one use 1 km.",
                "Prior scans, mini-track, surface survey",
                "relative bearing direction near far distance"
            ),
            Icon(
                GuideIconKind.Glyph,
                "☀",
                "Star, body, or biological signal",
                "Outside the body-prediction discovery markers, this symbol identifies a stellar/body context or an unresolved biological signal according to the row title.",
                "System survey and biology overlays"
            ),
            Icon(
                GuideIconKind.Glyph,
                "T",
                "Terraformable",
                "The body is a terraformable candidate.",
                "FSS information and system survey"
            ),
            Icon(
                GuideIconKind.Glyph,
                "L",
                "Landable",
                "The body can be landed on.",
                "FSS information and system survey"
            ),
            Icon(
                GuideIconKind.Glyph,
                "?",
                "Unknown",
                "A standalone question mark means the signal, organism, site detail, or reward cannot yet be identified reliably from current data.",
                "Biology, Guardian, body and system rows"
            ),
            Icon(
                GuideIconKind.Glyph,
                "■",
                "Construction site",
                "Identifies construction/build context; the exact row color reports whether the item is actionable, satisfied, or unavailable.",
                "Colonization shopping"
            ),
            AssetIcon(
                DesktopAssetUri("Assets/Routes/refuel-star.png"),
                "Fuel-scoop stop",
                "An orange star containing a fuel droplet marks a route waypoint where the ship should refuel by fuel scooping.",
                "Route Workspace and next-jump overlay",
                "fuel scoop refuel star route"
            ),
            AssetIcon(
                DesktopAssetUri("Assets/Routes/neutron-star.png"),
                "Neutron boost stop",
                "A blue neutron-star marker identifies a route waypoint that uses or approaches a neutron-star FSD boost.",
                "Route Workspace and next-jump overlay",
                "neutron boost fsd star route"
            ),
            .. CreateBodyIconGlossary(),
            Icon(
                GuideIconKind.BiologyRewardKnown,
                "",
                "Confirmed reward PIPs",
                "An unhatched PIP means journal, Canonn, or Spansh data confirms the organism or genus. Each filled segment clears a confirmed reward threshold; a darker possible segment may remain when the exact species or First Logged bonus is still uncertain. Black empty slots show thresholds the reward does not reach. The dotted outer frame and solid segment outlines are independently themeable.",
                "Bio signals and biology system overlays",
                "bars pips confirmed solid filled empty border"
            ),
            Icon(
                GuideIconKind.BiologyRewardPredicted,
                "",
                "Predicted reward PIPs",
                "Diagonal hatching marks an organism candidate supplied only by the biology prediction data, without journal, Canonn, or Spansh evidence. Solid prediction segments show the dependable lower band; the darker possible segment shows how high the reward range may extend. Each segment retains its legacy solid outline. The dotted group frame spans the body's reported biological signal count; additional PIPs outside it are alternative genus candidates, not additive rewards.",
                "Bio signals and biology predictions",
                "bars pips estimate range hatched potential alternative genus overflow dotted frame"
            ),
            Icon(
                GuideIconKind.BiologyRewardHighlighted,
                "",
                "Commander-first and regional-first PIPs",
                "The bright yellow or theme-highlight PIP marks an organism that is new to the current Commander, or new to this Commander in the current Codex region when regional highlighting is enabled. Hatching still means the organism and reward are predicted.",
                "Biology system and prediction overlays",
                "bars pips gold yellow highlighted commander first discovery regional"
            ),
            Icon(
                GuideIconKind.BiologyRewardGlobalRegional,
                "",
                "Galactic-region candidate PIPs",
                "The white or separately themed PIP means external candidate data has no reported discovery for this predicted organism in the current Galactic region. It is advisory until an in-game journal event confirms the result; hatching remains because the organism is predicted.",
                "Biology system and prediction overlays",
                "bars pips white galactic region potential candidate first discovery"
            ),
            Icon(
                GuideIconKind.BiologyRewardDimmed,
                "",
                "Analyzed reward PIPs",
                "A dimmed PIP means that organism has already been analyzed for the current body. Its reward band remains visible for reference, but it is no longer an outstanding sample.",
                "Biology system and body-detail overlays",
                "bars pips dim analyzed complete scanned"
            ),
            Icon(
                GuideIconKind.BiologyRewardUnknown,
                "",
                "Unknown reward PIP",
                "A question mark inside the unknown-color frame means there is not enough dependable organism or reward data to calculate a band yet.",
                "Bio signals and unresolved biology",
                "bars pips question mark unknown unresolved"
            ),
            Icon(
                GuideIconKind.CanonnSignals,
                "",
                "Canonn-known signals",
                "The original Canonn Research logo means external Canonn data contains known biological signals for this body. It appears immediately beside the reward PIPs when external data and automatic prior-scan loading are enabled.",
                "System biology overlay",
                "canonn external known signals pips prior scans"
            ),
            Icon(
                GuideIconKind.RadarCommander,
                "",
                "Commander and heading",
                "The ringed arrow at radar center is your current position and heading.",
                "Grounded surface radar"
            ),
            Icon(
                GuideIconKind.RadarShip,
                "",
                "Ship position",
                "A triangle marks the current ship. A dim triangle marks a former ship position.",
                "Grounded surface radar"
            ),
            Icon(
                GuideIconKind.RadarSrv,
                "",
                "SRV position",
                "A rounded rectangle marks the Surface Recon Vehicle.",
                "Grounded surface radar"
            ),
            Icon(
                GuideIconKind.RadarSample,
                "",
                "Biology sample and colony radius",
                "The dot is a scan/sample location and the circle is its colony radius. Warning inside means too close; success outside means valid spacing.",
                "Grounded surface radar and prior scans"
            ),
            Icon(
                GuideIconKind.RadarHistoricalScan,
                "",
                "Historical biology scan",
                "A muted dot is a prior scan location. A danger-colored radius means the Commander is currently too close to reuse that colony area.",
                "Grounded surface radar and prior scans"
            ),
            Icon(
                GuideIconKind.RadarBookmark,
                "",
                "Surface bookmark",
                "A dot and radius mark one of the eight reusable tracked surface locations. Inactive bookmarks are dimmed.",
                "Grounded surface radar and mini-track"
            ),
            Icon(
                GuideIconKind.GroundTarget,
                "",
                "Ground-target guidance",
                "The inner ringed pointer is the ship heading; the radial line points toward the target. The lower angled line shows approach or attack angle.",
                "Ground target overlay"
            ),
            Icon(
                GuideIconKind.JumpRoute,
                "",
                "Jump-route progress",
                "Connected nodes show completed, current, and remaining route positions. The emphasized node is the active jump context.",
                "Next-jump information and route overlays"
            ),
            Icon(
                GuideIconKind.GuardianRelic,
                "",
                "Guardian relic tower",
                "The original blue-filled, cyan-edged triangle is a confirmed relic tower. It rotates to its recorded heading; a translucent blue line through the tower means that tower has an individual heading measurement.",
                GuardianSiteMap
            ),
            Icon(
                GuideIconKind.GuardianArtifact,
                "",
                "Guardian artifact points",
                "Legacy POI colors identify present artifacts: orange Orb, green Casket, pale-blue Tablet, blue-violet Totem, and magenta Urn.",
                GuardianSiteMap,
                "orb casket tablet totem urn colors"
            ),
            Icon(
                GuideIconKind.GuardianEmptyPuddle,
                "",
                "Empty puddle",
                "A gold-filled, yellow-edged circle identifies a surveyed artifact puddle with no object present.",
                GuardianSiteMap
            ),
            Icon(
                GuideIconKind.GuardianObelisk,
                "",
                "Guardian obelisk",
                "The narrow dark-cyan, three-sided legacy glyph identifies an inactive obelisk and rotates with the template geometry. A dotted lime ring identifies the nearest or targeted point.",
                "Guardian site map and Ram Tah"
            ),
            Icon(
                GuideIconKind.GuardianActiveObelisk,
                "",
                "Active Guardian obelisk",
                "A cyan obelisk with a 90-degree radial glow is active. The glow center is cyan when its log is needed for the active Ram Tah mission, orange when scanned, and light gray when active but neither needed nor scanned.",
                "Guardian site map and Ram Tah",
                "active scanned needed glow wedge"
            ),
            Icon(
                GuideIconKind.GuardianBrokenObelisk,
                "",
                "Broken obelisk",
                "The asymmetric narrow three-sided legacy outline identifies a broken obelisk; it is not a generic X.",
                GuardianSiteMap
            ),
            Icon(
                GuideIconKind.GuardianPylon,
                "",
                "Guardian energy pylon",
                "The rotated legacy diamond and its center-to-tip stem identify an energy pylon. Its outline color records unknown, present, absent, or empty survey state.",
                GuardianSiteMap
            ),
            Icon(
                GuideIconKind.GuardianComponent,
                "",
                "Guardian component tower",
                "Nested triangular outlines identify a component tower. The three fixed screen-facing dots are lime Power Cell, cyan Power Conduit, and orange-red Technology Component; a small square uses the same materials for a destructible panel.",
                GuardianSiteMap
            ),
            Icon(
                GuideIconKind.GuardianCommander,
                "",
                "Guardian-map Commander",
                "A ring with a center dot marks the Commander's live position on the Guardian site projection.",
                GuardianSiteMap
            ),
            Icon(
                GuideIconKind.GuardianSiteHeading,
                "",
                "Guardian site heading",
                "A dashed dark-red line through site center is the recorded site alignment heading and rotates with the live Commander view.",
                GuardianSiteMapLegend
            ),
            Icon(
                GuideIconKind.GuardianTowerHeading,
                "",
                "Guardian tower heading",
                "A translucent blue line through site center is the general relic-tower heading. A wider, fainter line through one relic records that tower's individual heading.",
                GuardianSiteMapLegend
            ),
            Icon(
                GuideIconKind.GuardianSurveyNeeded,
                "",
                "Guardian survey needed",
                "A dotted ring marks a point or site state that still needs survey data.",
                GuardianSiteMapLegend
            ),
            Icon(
                GuideIconKind.GuardianPoiStates,
                "",
                "Guardian survey states",
                "Unknown points use the cyan dotted survey treatment, absent points use translucent gray, present points use their POI-specific legacy color, and empty puddles use gold with a yellow edge.",
                GuardianSiteMapLegend,
                "unknown absent present empty colors"
            ),
            Icon(
                GuideIconKind.Glyph,
                "A",
                "Atmospheric regulator",
                "A named atmospheric-control point in a human settlement.",
                HumanSettlementMap,
                "atmos"
            ),
            Icon(
                GuideIconKind.Glyph,
                "!",
                "Settlement alarm",
                "An alarm-control point in a human settlement. Its color reflects access/security context.",
                HumanSettlementMap
            ),
            Icon(
                GuideIconKind.Glyph,
                "K",
                "Authorization point",
                "An authorization or security-clearance point in a human settlement.",
                HumanSettlementMap,
                "auth access"
            ),
            Icon(
                GuideIconKind.Glyph,
                "+",
                "Medkit",
                "A known medical-kit location in a human settlement.",
                HumanSettlementMap
            ),
            Icon(
                GuideIconKind.Glyph,
                "B",
                "Battery",
                "A known battery or energy-cell location in a human settlement.",
                HumanSettlementMap
            ),
            Icon(
                GuideIconKind.Glyph,
                "P",
                "Power control",
                "A named power-control point in a human settlement.",
                HumanSettlementMap
            ),
            Icon(
                GuideIconKind.HumanLandingPad,
                "",
                "Landing pad",
                "A rotated rectangular outline and pad number show a settlement landing pad and its orientation.",
                HumanSettlementMap
            ),
            Icon(
                GuideIconKind.HumanDoor,
                "",
                "Secure door",
                "A short filled bar marks a secure door. Green, cyan, gold, and danger colors correspond to increasing security levels.",
                HumanSettlementMap
            ),
            Icon(
                GuideIconKind.HumanTerminal,
                "",
                "Data terminal",
                "A rounded square with a center line marks a data terminal. A dim/processed color means it has already been handled.",
                HumanSettlementMap
            ),
            Icon(
                GuideIconKind.HumanMaterial,
                "",
                "Collected material",
                "A small outlined dot marks material already collected at that settlement position.",
                HumanSettlementMap
            ),
            Icon(
                GuideIconKind.HumanCommander,
                "",
                "Settlement Commander",
                "A circle with a heading stalk shows the Commander's position and facing on the settlement map.",
                HumanSettlementMap
            ),
            Icon(
                GuideIconKind.HumanShip,
                "",
                "Settlement ship",
                "A large circle labeled SHIP marks the current or departed ship. A dashed boundary can show the dismissal distance.",
                HumanSettlementMap
            ),
            Icon(
                GuideIconKind.HumanSrv,
                "",
                "Settlement SRV",
                "A rounded square labeled SRV marks the vehicle on the settlement map.",
                HumanSettlementMap
            ),
            Icon(
                GuideIconKind.HumanQuestTarget,
                "",
                "Settlement quest target",
                "A target-radius circle marks quest geometry. Gold means outside the target; the active accent means within it.",
                HumanSettlementQuestMap
            ),
            Icon(
                GuideIconKind.HumanFloor,
                "",
                "Upper floor",
                "One upward chevron means floor 2; two chevrons mean floor 3 or higher for a named point or terminal.",
                HumanSettlementMap
            ),
            Icon(
                GuideIconKind.ConflictCheckpoint,
                "",
                "Conflict-zone checkpoint",
                "A labeled circle marks a frontline checkpoint. The local checkpoint uses the configured local/success color.",
                HumanSettlementConflictZoneMap,
                "fcz"
            ),
            Icon(
                GuideIconKind.ConflictPowerPost,
                "",
                "Conflict-zone power post",
                "A circle with a lightning stroke marks a power post.",
                HumanSettlementConflictZoneMap,
                "fcz"
            ),
        ];
    }

    private static IEnumerable<GuideIconViewModel> CreateBodyIconGlossary()
    {
        return RouteBodyAssetResolver.SupportedVisuals.Select(visual =>
            AssetIcon(
                visual.AssetPath,
                visual.AccessibleName,
                GetBodyIconMeaning(visual),
                "Route Workspace and route-bodies overlay",
                $"body planet stellar route {visual.AccessibleName}"
            )
        );
    }

    private static string GetBodyIconMeaning(RouteBodyVisual visual)
    {
        return visual.Kind == RouteBodyVisualKind.Unknown
            ? "The fallback marker used when imported route data does not provide a body subtype that SrvSurvey can identify."
            : $"Identifies an imported route destination classified as {visual.AccessibleName.ToLowerInvariant()}. The marker appears immediately before the body name.";
    }

    private static GuideCategoryViewModel Category(
        string key,
        string number,
        string title,
        string summary,
        IReadOnlyList<GuideSectionViewModel> sections,
        IReadOnlyList<GuideIconViewModel>? icons = null
    )
    {
        return new GuideCategoryViewModel(key, number, title, summary, sections, icons ?? []);
    }

    /// <summary>Builds one task and its optional contextual symbol examples.</summary>
    private static GuideSectionViewModel Section(
        string title,
        string summary,
        IReadOnlyList<string> steps,
        IReadOnlyList<string> details,
        IReadOnlyList<GuideIconKind>? illustrations = null
    )
    {
        return new GuideSectionViewModel(title, summary, steps, details, illustrations);
    }

    private static GuideSectionViewModel IntroSection(
        string title,
        string summary,
        IReadOnlyList<string> steps,
        IReadOnlyList<string> details
    ) => Section(title, summary, steps, details);

    /// <summary>Builds a Guardian task with optional map-symbol examples.</summary>
    private static GuideSectionViewModel GuardianSection(
        string title,
        string summary,
        IReadOnlyList<string> steps,
        IReadOnlyList<string> details,
        IReadOnlyList<GuideIconKind>? illustrations = null
    ) => Section(title, summary, steps, details, illustrations);

    private static GuideIconViewModel Icon(
        GuideIconKind kind,
        string symbol,
        string name,
        string meaning,
        string appearsIn,
        string searchTerms = ""
    )
    {
        return new GuideIconViewModel(kind, symbol, name, meaning, appearsIn, searchTerms);
    }

    private static GuideIconViewModel AssetIcon(
        string assetPath,
        string name,
        string meaning,
        string appearsIn,
        string searchTerms
    )
    {
        return new GuideIconViewModel(
            GuideIconKind.Asset,
            string.Empty,
            name,
            meaning,
            appearsIn,
            searchTerms,
            assetPath
        );
    }

    private static string DesktopAssetUri(string relativePath)
    {
        return $"{AvaloniaResourceScheme}://{DesktopAssemblyName}/{relativePath}";
    }
}
