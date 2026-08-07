using SrvSurvey.Desktop.Presentation;

namespace SrvSurvey.Desktop.ViewModels;

public static class GuideCatalog
{
    private const string AvaloniaResourceScheme = "avares";
    private const string DesktopAssemblyName = "SrvSurvey.Desktop";
    private const string GuardianSiteMap = "Guardian site map";
    private const string GuardianSiteMapLegend = "Guardian site map legend";
    private const string HumanSettlementMap = "Human settlement map";
    private const string HumanSettlementQuestMap = "Human settlement quest map";
    private const string HumanSettlementConflictZoneMap = "Human settlement conflict-zone map";

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
                    Section(
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
                        ]),
                    Section(
                        "How the interface is organized",
                        "The numbered navigation follows the major activities in SrvSurvey. Guides remains available even when Elite is not running.",
                        [],
                        [
                            "Exploration, Exobiology, Travel, Search, Guardian, Quests, and Colonisation contain activity-specific tools.",
                            "Diagnostics explains the live data source and provides repair, update, cache, and journal-inspection tools.",
                            "Settings controls application behavior, overlays, input bindings, privacy, profile migration, screenshots, and appearance.",
                        ]),
                    Section(
                        "What this field manual covers",
                        "The in-app guide reconciles the original SrvSurvey user guide with the current cross-platform implementation.",
                        [],
                        [
                            "Content is derived from the repository README, porting plan, UI/journal/network/data-migration parity matrices, biology-criteria notes, the original project wiki, and the behavior implemented in the current code.",
                            "Legacy Windows-only steps are replaced with their current Avalonia workflow. Features that still require an operating-system capability report that status in the relevant setting.",
                            "Developer file formats are summarized as player tasks here; Diagnostics and repository documentation retain the lower-level evidence.",
                        ]),
                    Section(
                        "Automatic overlays",
                        "Most overlays appear only when their information is useful, then hide when that game context ends.",
                        [
                            "Play normally; entering FSS, approaching a body, landing, opening the Galaxy Map, or docking can activate the relevant overlay.",
                            "Use Settings > Overlay behavior and layout to disable individual overlays or change their triggers.",
                            "Use the global overlay visibility shortcut when you need to hide or restore every detached overlay at once.",
                        ],
                        [
                            "Borderless or windowed Elite modes provide the most predictable desktop overlay stacking. Exclusive fullscreen behavior depends on the operating system and compositor.",
                            "A passive overlay is click-through. The live-interaction shortcut temporarily makes existing live overlays draggable without opening the full position editor.",
                        ]),
                ]),
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
                        ]),
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
                        ]),
                    Section(
                        "Live versus historical processing",
                        "Live journal monitoring never requires rewriting an Elite journal. Historical tools analyze older files separately.",
                        [],
                        [
                            "Diagnostics can analyze older journals by Commander and date without changing profile data.",
                            "Commander Codex merges and Odyssey system/body reconstruction require explicit confirmation, verified backups, and atomic activation.",
                            "Recent active journals are excluded from destructive historical reconstruction paths.",
                        ]),
                ]),
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
                            "Low-value bodies can be dimmed or filtered in Settings while valuable bodies remain highlighted.",
                            "External-data enrichment can add prior discovery, traffic, station, and biological context when its privacy setting is enabled.",
                        ]),
                    Section(
                        "Body information and values",
                        "Body information combines the scan, orbit, atmosphere, gravity, temperature, volcanism, materials, and reward details known so far.",
                        [],
                        [
                            "A check beside value means the detailed surface scan is complete.",
                            "T identifies a terraformable candidate and L identifies a landable body in compact FSS rows.",
                            "A first-discovery flag means the body was not previously discovered according to the journal data.",
                        ]),
                    Section(
                        "Galaxy Map and next jump",
                        "Route overlays keep destination and next-hop information visible while the Galaxy Map or route is active.",
                        [],
                        [
                            "Next-jump information shows route progress, remaining distance, star class, scoopability, neutron routing, and available system context.",
                            "Galaxy Map preview can show destination discovery, biological, traffic, and port data; some fields require external lookup data.",
                            "Use the configurable jump-information shortcut to show or hide the route overlay manually.",
                        ]),
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
                        ]),
                ]),
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
                            "A hatched reward PIP is a prediction. A solid PIP is a confirmed organism. A question mark means no reliable reward band is known yet.",
                            "The four vertical PIP segments use configurable reward thresholds. More filled segments indicate a higher expected reward band, not sample completion.",
                            "A reward range appears when unresolved signals could match organisms with different values.",
                        ]),
                    Section(
                        "Sampling an organism",
                        "The bio-status and surface overlays track the active genus, three-sample sequence, colony distance, and estimated reward.",
                        [
                            "Scan the first specimen with the Genetic Sampler.",
                            "Move outside the displayed colony radius before taking the next sample; the radar ring and distance state show whether the spacing is valid.",
                            "Complete all three samples. The confirmed organism and unclaimed reward are retained in the Commander profile until sale events clear them.",
                        ],
                        [
                            "First-footfall status can change reward presentation because a confirmed first footfall multiplies biological sale value.",
                            "The first-footfall screen detector is optional; its shortcut can override the current body state when automatic inference is unavailable.",
                        ]),
                    Section(
                        "Surface radar and bookmarks",
                        "The grounded radar keeps the Commander, ship, SRV, samples, prior scan locations, and numbered bookmarks in one relative view.",
                        [],
                        [
                            "The center ringed arrow is the Commander and current heading. Triangles represent the ship; a dim triangle is a former ship position; a rounded rectangle is the SRV.",
                            "Dots represent biological samples or bookmarks. Their surrounding circles show the organism colony radius.",
                            "For an active sample, warning color inside the radius means you are too close; success color outside the radius means the next sample is valid.",
                            "Use Track location 1 through 8 shortcuts to toggle reusable surface bookmarks.",
                        ]),
                    Section(
                        "Codex, Codex Bingo, and prior scans",
                        "Codex tools distinguish personal discoveries, regional discoveries, confirmed entries, predictions, and known scan locations.",
                        [],
                        [
                            "Codex Bingo groups discoveries by region and lets you inspect where and when an entry was found.",
                            "Old journals or the Canonn Codex Challenge can be imported into the Commander Codex without replacing unrelated profile data.",
                            "Prior-scan overlays can show known biological locations near the current body when external Canonn data is enabled.",
                            "A filled discovery flag is a Commander first; an outline flag is a regional first. Highlighting regional firsts is optional.",
                        ]),
                ]),
            Category(
                "travel-search",
                "05",
                "Travel and search",
                "Set surface targets, record journeys, follow routes, and search the galaxy by sphere, boxel, or nearby-system criteria.",
                [
                    Section(
                        "Ground targets",
                        "A ground target stores latitude and longitude for the current body and turns them into bearing, distance, and approach guidance.",
                        [
                            "Enter coordinates in Travel or send .target here through the in-game chat journal to capture the current location.",
                            "Follow the circular bearing display relative to ship heading. The attack-angle line helps judge descent toward the target.",
                            "Send .target off or clear the target in Travel when finished.",
                        ],
                        [
                            "Ground targets are body-specific. A target is not treated as valid on a different body.",
                        ]),
                    Section(
                        "Journeys, routes, and system notes",
                        "Journeys preserve an expedition timeline, while routes provide an ordered destination list and notes preserve local research.",
                        [],
                        [
                            "Create or resume a journey, then let journal events add visited systems and exploration totals.",
                            "Import a named route or create one from supported route data, then advance it as jumps arrive.",
                            "Use Show system notes to edit notes for the current system without leaving the game context.",
                        ]),
                    Section(
                        "Spherical search",
                        "A spherical search finds candidate systems inside a radius around a central coordinate.",
                        [
                            "Choose the center, radius, and search constraints.",
                            "Generate the next candidate and copy or paste it into the Galaxy Map with the configured shortcuts.",
                            "Use the overlay color and warning text to distinguish valid in-radius destinations, out-of-radius systems, low mass codes, and already-surveyed systems.",
                        ],
                        []),
                    Section(
                        "Boxel and nearby-system searches",
                        "Boxel search walks procedural-name ranges; Nearby systems resolves close alternatives around a known system.",
                        [],
                        [
                            "Boxel results can skip known or empty ranges and retain progress in the Commander profile.",
                            "Copy next boxel system and Paste Galaxy Map target reduce retyping while the map is open.",
                            "External system resolution is clearly reported when a lookup service is unavailable or disabled.",
                        ]),
                ]),
            Category(
                "guardian",
                "06",
                "Guardian sites",
                "Locate Guardian sites, align maps, survey points of interest, track Ram Tah progress, and share non-destructive survey packages.",
                [
                    Section(
                        "Arriving at a Guardian system",
                        "Guardian summaries identify known sites, beacons, survey status, blueprint type, and extra notes for the current system.",
                        [],
                        [
                            "The system summary can appear automatically when the system has known Guardian sites.",
                            "Near a site, the live map projects the published layout around your current position and heading.",
                            "Map size, automatic zoom, SRV-turret zoom, measurement grid, material dots, notes, aerial grid, and legend are independently configurable.",
                        ]),
                    Section(
                        "Aligning and surveying a site",
                        "A correct site type and heading make the projected map line up with the ruins or structure.",
                        [
                            "Type A, B, or G for common ruins layouts, or send .site followed by a supported layout name.",
                            "Face the mapped alignment feature and send .heading; send .heading followed by degrees when entering a heading directly.",
                            "Use .tower to record the nearest relic tower state, .empty for an empty puddle, and .note followed by text to append a site note.",
                            "Use .aerial for origin guidance while taking an aligned aerial screenshot, then .map to return to the live map.",
                        ],
                        [
                            "Present, absent, empty, active, scanned, and target states use different fills, outlines, and colors. The icon glossary shows the underlying shapes.",
                        ]),
                    Section(
                        "Ram Tah and obelisks",
                        "The Ram Tah workspace tracks mission logs, active obelisks, required combinations, and decoded entries.",
                        [],
                        [
                            "An active obelisk receives an emphasized ring; a scanned obelisk is filled with the success color.",
                            "Filters can show only mission logs still needed for the active Ram Tah task.",
                            "The nearest mapped point and target ring help correlate the in-game site with the survey layout.",
                        ]),
                    Section(
                        "Authoring and sharing",
                        "Advanced tools can measure new layouts, edit points and groups, and package a survey for review.",
                        [],
                        [
                            "Authoring changes remain in a session until explicitly committed or discarded.",
                            "Share data compares the local survey with published data, includes only meaningful differences, and creates a content-addressed ZIP.",
                            "Packaging never clears the legacy staging folder or modifies the published reference catalogs.",
                        ]),
                ]),
            Category(
                "quests",
                "07",
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
                        ]),
                    Section(
                        "Settlement and massacre guidance",
                        "Active quest geometry can add routes and target radii to a human-settlement map, while massacre missions track credited kills by mission giver.",
                        [],
                        [
                            "A gold target circle means the Commander is outside the objective radius; the accent color means the Commander is inside it.",
                            "Massacre rows show progress only for compatible active missions and avoid double-crediting one bounty event to the same mission giver.",
                        ]),
                    Section(
                        "Developer tools",
                        "Quest authors can validate and test Lua chapters without risking unrelated player progress.",
                        [],
                        [
                            "Imports are hash-verified and preserve progress only when the quest identity matches.",
                            "Definitions can be reloaded from disk, edited as JSON, debugged, started, stopped, and removed with explicit guards.",
                            "Publishing to Raven requires a separate overwrite confirmation; local testing does not publish implicitly.",
                        ]),
                ]),
            Category(
                "colonisation",
                "08",
                "Colonisation",
                "Connect Raven Colonial, manage construction projects safely, plan cargo, repair completed build-site records, and reconcile system data.",
                [
                    Section(
                        "Connect Raven Colonial",
                        "Raven access is opt-in. A valid Commander API key is required before private project data or authenticated mutations are used.",
                        [
                            "Enable Raven Colonial in Settings and save the API key for the active Commander.",
                            "Open Colonisation and refresh projects. Rejected or mismatched credentials do not replace the stored Commander profile.",
                            "Use the Raven links when you need to review a project or system in the website.",
                        ],
                        [
                            "Automatic ship cargo publishing, Fleet Carrier publishing, system updates, and Green Gas Giant publication each have their own gates.",
                        ]),
                    Section(
                        "Focused primary project",
                        "The Commander shopping focus is explicit Raven state and is separate from a system's primary-port order.",
                        [
                            "Choose Make primary on the intended project before focusing the shopping plan.",
                            "Use Clear primary only when you intentionally want aggregate planning across visible projects.",
                        ],
                        [
                            "Making a project primary changes Commander planning focus; it does not reorder the sites stored for the system.",
                        ]),
                    Section(
                        "Primary port order safety",
                        "Creating a project protects the existing first system site because Raven's nexus page treats that position as the primary port.",
                        [
                            "Before creation, SrvSurvey reads the Raven system sites and records the persisted ID of the first site.",
                            "If an existing first site has no stable ID, or no API key is available to protect it, creation is refused before any project is posted.",
                            "After creation, SrvSurvey reads the sites again. If the new project displaced the original first site, it sends an order-only correction with the original site first.",
                            "SrvSurvey verifies the corrected order and makes at most two correction attempts. An unverifiable result is reported clearly instead of being treated as safe.",
                        ],
                        [
                            "The correction changes only the ordered site IDs; it does not rebuild or overwrite the site's other Raven fields.",
                        ]),
                    Section(
                        "Create and update projects",
                        "Project creation uses the live docked construction-site context and a shipped build catalog.",
                        [],
                        [
                            "Review the system, body, construction market, build type, layout, architect, and notes before confirming publication.",
                            "Depot contribution, completion, docking, beacon architect, and market events update the matching project only when the identity is unambiguous.",
                            "Stale docking, SRV, bootstrap, malformed delta, or missing API-key context cannot publish project mutations.",
                        ]),
                    Section(
                        "Construction shopping overlay",
                        "The shopping plan compares project need with current ship cargo and linked Fleet Carrier cargo.",
                        [],
                        [
                            "Need is the remaining project requirement; FC is the cargo on linked carriers; Ship is the current usable ship inventory.",
                            "A check means that source has enough for the row. A direction marker calls out the next useful item or focused project. Dimmed rows are unavailable or already satisfied.",
                            "The overlay can focus a primary project, the docked build site, a local aggregate, or all visible projects depending on game context and settings.",
                            "Fresh Market and Cargo events reconcile carrier and ship totals. Ambiguous multi-client cargo is excluded until a fresh unambiguous file write arrives.",
                        ]),
                    Section(
                        "Completed build-site repair",
                        "When docking at a player colony, SrvSurvey can repair a Raven system-site entry that is missing its MarketID or final name.",
                        [],
                        [
                            "The repair compares the live docked market with Raven system sites and sends one targeted authenticated PATCH only when exactly one safe match exists.",
                            "Ambiguous or missing matches are not changed and remain retryable.",
                            "Successful repairs are kept in a persistent 50-location guard, preventing repeat API calls on routine revisits.",
                            "A cache-write failure does not repeat a successful server mutation in the same session.",
                        ]),
                    Section(
                        "Raven system update tool",
                        "The updater merges live discoveries, local edits, and the latest Raven copy before publishing.",
                        [
                            "Acquire the required Raven system-update permission and API key.",
                            "Confirm any body import, review inferred sites and manually edit details as needed.",
                            "Refresh to perform a three-way reconciliation. Concurrent edits to the same field become blocking conflicts; unrelated remote fields are preserved.",
                            "Resolve conflicts, review the final patch, then confirm publication separately.",
                        ],
                        [
                            "Scanning, approaching, or docking can infer system/site data locally, but none of those actions alone publishes the system record.",
                        ]),
                ]),
            Category(
                "overlays",
                "09",
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
                        ]),
                    Section(
                        "Edit all overlay positions",
                        "The position editor can display realistic simulated overlays without Elite running.",
                        [
                            "Open Settings > Overlay behavior and layout > Edit overlay positions.",
                            "Choose a category from the selector at the top; only that group appears so the desktop is not overwhelmed.",
                            "Drag the bordered previews to the desired monitors and positions. Preview content comes from a false game state shaped like real overlay data.",
                            "Use the check button to save every staged position, or X to close and restore the original layout.",
                        ],
                        [
                            "The editor forces normally contextual overlays to appear, but it does not publish network data or change the player profile.",
                        ]),
                    Section(
                        "Move existing live overlays",
                        "Live-interaction mode is intentionally separate from the full editor.",
                        [
                            "Press Toggle live overlay interaction (default Alt+Shift+O unless changed) while overlays are already visible.",
                            "The existing live overlays stop being click-through and can be dragged in place.",
                            "Press the shortcut again to restore passive click-through behavior.",
                        ],
                        [
                            "This shortcut does not open simulated previews or change which overlays are visible.",
                        ]),
                    Section(
                        "Overlay appearance and saved states",
                        "The in-game palette is independent from the application light/dark theme.",
                        [
                            "Open Settings > In-game overlay appearance and choose colors with the picker beside each overlay control.",
                            "Keep the position editor open to see unsaved color edits refresh in its previews.",
                            "Save a named overlay state when the palette is ready; choose saved states from the dropdown or reload the original defaults.",
                        ],
                        [
                            "Changing Blue light, Blue dark, Orange dark, Green light, or Green dark for the application never rewrites theme.json or a named overlay state.",
                            "Imported legacy overlay colors, positions, scale, and opacity remain in the overlay control group.",
                        ]),
                    Section(
                        "Input bindings",
                        "Keyboard and supported controller bindings are configurable per action and checked for collisions.",
                        [],
                        [
                            "Useful actions include overlay visibility, live interaction, map zoom, jump/FSS/body/station panels, colony shopping, system notes, boxel copy/paste, quest communications, VR adjustment, surface bookmarks, and screenshot data.",
                            "A binding is reported as unavailable when the current operating system cannot provide the required global input capability.",
                        ]),
                ]),
            Category(
                "settings-migration",
                "10",
                "Settings and migration",
                "Keep application and overlay appearance separate, import an original profile without corruption, and understand every network/privacy gate.",
                [
                    Section(
                        "Application theme versus overlay theme",
                        "The shell and the in-game overlays are two separate appearance systems.",
                        [],
                        [
                            "Application theme changes the main Avalonia windows and supports the Raven Colonial light and dark palettes.",
                            "In-game overlay appearance changes only detached overlays and retains the original SrvSurvey color roles, named states, and defaults.",
                            "Neither selector writes into the other control group, so a light application can use a dark orange overlay palette or any custom combination.",
                        ]),
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
                        ]),
                    Section(
                        "Reference-data updates",
                        "SrvSurvey still uses small version files and GitHub-hosted JSON catalogs so data corrections can ship without reinstalling the application.",
                        [],
                        [
                            "At startup, SrvSurvey checks the published version index and downloads only catalogs whose version changed.",
                            "Downloaded catalogs are bounded, validated, checksummed, staged, and activated with health confirmation and rollback.",
                            "Guardian sites, biology criteria, human settlements, and other published datasets remain independent from executable releases.",
                            "Diagnostics can refresh the catalogs manually and reports when a restart is required.",
                        ]),
                    Section(
                        "Privacy and network services",
                        "External reads and every upload path are visible and separately gated.",
                        [],
                        [
                            "System enrichment may use EDSM, Spansh, Canonn, or Raven depending on the enabled feature.",
                            "EDDN publication, human-settlement geometry, Green Gas Giant candidates, Raven cargo, Fleet Carrier data, system updates, and quest publication each require the corresponding setting, credential, or explicit confirmation.",
                            "Analysis, previews, imports, and historical reconstruction do not imply network publication.",
                        ]),
                    Section(
                        "Screenshots, notifications, stream, and VR",
                        "Optional desktop integrations are configured independently so unsupported platforms degrade without disabling core journal processing.",
                        [],
                        [
                            "Screenshot conversion has its own source, destination, naming, banner, and image-embedding controls.",
                            "Notifications and the dedicated stream overlay can be enabled without changing ordinary overlay positions.",
                            "VR overlay adjustment captures and resets orientation through dedicated actions; capability and status are reported when the runtime is unavailable.",
                        ]),
                ]),
            Category(
                "diagnostics",
                "11",
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
                        ]),
                    Section(
                        "Application logs and crash reports",
                        "Diagnostics exposes persisted application logs and the non-destructive crash-report workflow.",
                        [],
                        [
                            "Copy the relevant log section when reporting a problem, including the first error and the operation that preceded it.",
                            "Crash packages are staged for review and do not silently upload user data.",
                            "Network response sizes, validation failures, ignored incompatible catalogs, and rollback results are recorded for diagnosis.",
                        ]),
                    Section(
                        "Updates and reference recovery",
                        "Executable releases and reference catalogs are checked and activated independently.",
                        [],
                        [
                            "Application update checks verify checksum-indexed packages and can automatically roll back a failed startup.",
                            "Reference refresh updates only changed catalogs and retains verified backups.",
                            "Visited-star cache swap/restore is blocked while Elite is running, uses a persistent original backup, validates responses, and rolls back failed activation.",
                        ]),
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
                        ]),
                ]),
            Category(
                "icons",
                "12",
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
                        ]),
                ],
                CreateIconGlossary()),
        ];
    }

    private static IReadOnlyList<GuideIconViewModel> CreateIconGlossary()
    {
        return
        [
            Icon(GuideIconKind.Glyph, "⚑", "First discovery", "The body or organism is new to the current Commander/discovery context. On compact FSS rows it marks an undiscovered body.", "FSS information, system survey, biology"),
            Icon(GuideIconKind.Glyph, "⚐", "Regional first", "The organism is new for the current Codex region. Optional highlighting can promote this marker.", "Biology system and Codex overlays"),
            Icon(GuideIconKind.Glyph, "►", "Next or active direction", "Calls out the next action, selected destination, active target, route note, or focused row.", "Travel, search, Guardian, colonisation, messages"),
            Icon(GuideIconKind.Glyph, "✓", "Complete or sufficient", "The scan/task is complete, the condition is valid, or the ship/carrier has enough cargo for the requirement.", "FSS, body information, quests, colonisation"),
            Icon(GuideIconKind.Glyph, "⚠", "Warning", "The route, gravity, search candidate, build state, or other condition needs attention before proceeding.", "Flight warning, search, travel, colonisation"),
            Icon(GuideIconKind.Glyph, "◆", "Mapped site or Codex item", "Identifies a Guardian/site point or a recorded Codex-style item in compact overlay rows.", "Guardian, Codex, preview rows"),
            Icon(GuideIconKind.Glyph, "◇", "Objective outside target", "A quest objective exists but the Commander is not yet within its required target area.", "Quest indicator and settlement objectives"),
            Icon(GuideIconKind.Glyph, "▲", "Relative bearing marker", "Points toward a tracked surface item that is outside the current compact view or emphasizes its bearing.", "Prior scans, mini-track, surface survey"),
            Icon(GuideIconKind.Glyph, "☀", "Star, body, or biological signal", "Marks a stellar/body context or an unresolved biological signal depending on the row title.", "System survey and biology overlays"),
            Icon(GuideIconKind.Glyph, "T", "Terraformable", "The body is a terraformable candidate.", "FSS information and system survey"),
            Icon(GuideIconKind.Glyph, "L", "Landable", "The body can be landed on.", "FSS information and system survey"),
            Icon(GuideIconKind.Glyph, "?", "Unknown", "The signal, organism, site detail, or reward cannot yet be identified reliably from current data.", "Biology, Guardian, body and system rows"),
            Icon(GuideIconKind.Glyph, "■", "Construction site", "Identifies construction/build context; the exact row color reports whether the item is actionable, satisfied, or unavailable.", "Colonisation shopping"),
            AssetIcon(DesktopAssetUri("Assets/Routes/refuel-star.png"), "Fuel-scoop stop", "An orange star containing a fuel droplet marks a route waypoint where the ship should refuel by fuel scooping.", "Route Workspace and next-jump overlay", "fuel scoop refuel star route"),
            AssetIcon(DesktopAssetUri("Assets/Routes/neutron-star.png"), "Neutron boost stop", "A blue neutron-star marker identifies a route waypoint that uses or approaches a neutron-star FSD boost.", "Route Workspace and next-jump overlay", "neutron boost fsd star route"),
            .. CreateBodyIconGlossary(),
            Icon(GuideIconKind.BiologyRewardKnown, "", "Solid reward PIPs", "Four vertical segments classify a confirmed biological reward against the thresholds configured in Settings. More filled segments mean a higher reward band.", "Bio signals and biology system overlays", "bars pips confirmed solid"),
            Icon(GuideIconKind.BiologyRewardPredicted, "", "Hatched reward PIPs", "Diagonal hatching means the organism and reward are predicted. Dark potential segments cover the possible upper end of a reward range.", "Bio signals and biology predictions", "bars pips estimate range"),
            Icon(GuideIconKind.BiologyRewardUnknown, "", "Unknown reward PIP", "A question mark in the PIP frame means no dependable reward band can be calculated yet.", "Bio signals and unresolved biology", "bars pips"),
            Icon(GuideIconKind.CanonnSignals, "", "Canonn-known signals", "The original Canonn Research logo means external Canonn data contains known biological signals for this body. It appears immediately beside the reward PIPs when external data and automatic prior-scan loading are enabled.", "System biology overlay", "canonn external known signals pips prior scans"),
            Icon(GuideIconKind.RadarCommander, "", "Commander and heading", "The ringed arrow at radar center is your current position and heading.", "Grounded surface radar"),
            Icon(GuideIconKind.RadarShip, "", "Ship position", "A triangle marks the current ship. A dim triangle marks a former ship position.", "Grounded surface radar"),
            Icon(GuideIconKind.RadarSrv, "", "SRV position", "A rounded rectangle marks the Surface Recon Vehicle.", "Grounded surface radar"),
            Icon(GuideIconKind.RadarSample, "", "Biology sample and colony radius", "The dot is a scan/sample location and the circle is its colony radius. Warning inside means too close; success outside means valid spacing.", "Grounded surface radar and prior scans"),
            Icon(GuideIconKind.RadarHistoricalScan, "", "Historical biology scan", "A muted dot is a prior scan location. A danger-colored radius means the Commander is currently too close to reuse that colony area.", "Grounded surface radar and prior scans"),
            Icon(GuideIconKind.RadarBookmark, "", "Surface bookmark", "A dot and radius mark one of the eight reusable tracked surface locations. Inactive bookmarks are dimmed.", "Grounded surface radar and mini-track"),
            Icon(GuideIconKind.GroundTarget, "", "Ground-target guidance", "The inner arrow is the ship heading; the radial line points toward the target. The lower angled line shows approach or attack angle.", "Ground target overlay"),
            Icon(GuideIconKind.JumpRoute, "", "Jump-route progress", "Connected nodes show completed, current, and remaining route positions. The emphasized node is the active jump context.", "Next-jump information and route overlays"),
            Icon(GuideIconKind.GuardianRelic, "", "Guardian relic tower", "The original blue-filled, cyan-edged triangle is a confirmed relic tower. It rotates to its recorded heading; a translucent blue line through the tower means that tower has an individual heading measurement.", GuardianSiteMap),
            Icon(GuideIconKind.GuardianArtifact, "", "Guardian artifact points", "Legacy POI colors identify present artifacts: orange Orb, green Casket, pale-blue Tablet, blue-violet Totem, and magenta Urn.", GuardianSiteMap, "orb casket tablet totem urn colors"),
            Icon(GuideIconKind.GuardianEmptyPuddle, "", "Empty puddle", "A gold-filled, yellow-edged circle identifies a surveyed artifact puddle with no object present.", GuardianSiteMap),
            Icon(GuideIconKind.GuardianObelisk, "", "Guardian obelisk", "The narrow dark-cyan, three-sided legacy glyph identifies an inactive obelisk and rotates with the template geometry. A dotted lime ring identifies the nearest or targeted point.", "Guardian site map and Ram Tah"),
            Icon(GuideIconKind.GuardianActiveObelisk, "", "Active Guardian obelisk", "A cyan obelisk with a 90-degree radial glow is active. The glow center is cyan when its log is needed for the active Ram Tah mission, orange when scanned, and light gray when active but neither needed nor scanned.", "Guardian site map and Ram Tah", "active scanned needed glow wedge"),
            Icon(GuideIconKind.GuardianBrokenObelisk, "", "Broken obelisk", "The asymmetric narrow three-sided legacy outline identifies a broken obelisk; it is not a generic X.", GuardianSiteMap),
            Icon(GuideIconKind.GuardianPylon, "", "Guardian energy pylon", "The rotated legacy diamond and its center-to-tip stem identify an energy pylon. Its outline color records unknown, present, absent, or empty survey state.", GuardianSiteMap),
            Icon(GuideIconKind.GuardianComponent, "", "Guardian component tower", "Nested triangular outlines identify a component tower. The three fixed screen-facing dots are lime Power Cell, cyan Power Conduit, and orange-red Technology Component; a small square uses the same materials for a destructible panel.", GuardianSiteMap),
            Icon(GuideIconKind.GuardianCommander, "", "Guardian-map Commander", "A ring with a center dot marks the Commander's live position on the Guardian site projection.", GuardianSiteMap),
            Icon(GuideIconKind.GuardianSiteHeading, "", "Guardian site heading", "A dashed dark-red line through site center is the recorded site alignment heading and rotates with the live Commander view.", GuardianSiteMapLegend),
            Icon(GuideIconKind.GuardianTowerHeading, "", "Guardian tower heading", "A translucent blue line through site center is the general relic-tower heading. A wider, fainter line through one relic records that tower's individual heading.", GuardianSiteMapLegend),
            Icon(GuideIconKind.GuardianSurveyNeeded, "", "Guardian survey needed", "A dotted ring marks a point or site state that still needs survey data.", GuardianSiteMapLegend),
            Icon(GuideIconKind.GuardianPoiStates, "", "Guardian survey states", "Unknown points use the cyan dotted survey treatment, absent points use translucent gray, present points use their POI-specific legacy color, and empty puddles use gold with a yellow edge.", GuardianSiteMapLegend, "unknown absent present empty colors"),
            Icon(GuideIconKind.Glyph, "A", "Atmospheric regulator", "A named atmospheric-control point in a human settlement.", HumanSettlementMap, "atmos"),
            Icon(GuideIconKind.Glyph, "!", "Settlement alarm", "An alarm-control point in a human settlement. Its color reflects access/security context.", HumanSettlementMap),
            Icon(GuideIconKind.Glyph, "K", "Authorization point", "An authorization or security-clearance point in a human settlement.", HumanSettlementMap, "auth access"),
            Icon(GuideIconKind.Glyph, "+", "Medkit", "A known medical-kit location in a human settlement.", HumanSettlementMap),
            Icon(GuideIconKind.Glyph, "B", "Battery", "A known battery or energy-cell location in a human settlement.", HumanSettlementMap),
            Icon(GuideIconKind.Glyph, "P", "Power control", "A named power-control point in a human settlement.", HumanSettlementMap),
            Icon(GuideIconKind.HumanLandingPad, "", "Landing pad", "A rotated rectangular outline and pad number show a settlement landing pad and its orientation.", HumanSettlementMap),
            Icon(GuideIconKind.HumanDoor, "", "Secure door", "A short filled bar marks a secure door. Green, cyan, gold, and danger colors correspond to increasing security levels.", HumanSettlementMap),
            Icon(GuideIconKind.HumanTerminal, "", "Data terminal", "A rounded square with a center line marks a data terminal. A dim/processed color means it has already been handled.", HumanSettlementMap),
            Icon(GuideIconKind.HumanMaterial, "", "Collected material", "A small outlined dot marks material already collected at that settlement position.", HumanSettlementMap),
            Icon(GuideIconKind.HumanCommander, "", "Settlement Commander", "A circle with a heading stalk shows the Commander's position and facing on the settlement map.", HumanSettlementMap),
            Icon(GuideIconKind.HumanShip, "", "Settlement ship", "A large circle labeled SHIP marks the current or departed ship. A dashed boundary can show the dismissal distance.", HumanSettlementMap),
            Icon(GuideIconKind.HumanSrv, "", "Settlement SRV", "A rounded square labeled SRV marks the vehicle on the settlement map.", HumanSettlementMap),
            Icon(GuideIconKind.HumanQuestTarget, "", "Settlement quest target", "A target-radius circle marks quest geometry. Gold means outside the target; the active accent means within it.", HumanSettlementQuestMap),
            Icon(GuideIconKind.HumanFloor, "", "Upper floor", "One upward chevron means floor 2; two chevrons mean floor 3 or higher for a named point or terminal.", HumanSettlementMap),
            Icon(GuideIconKind.ConflictCheckpoint, "", "Conflict-zone checkpoint", "A labeled circle marks a frontline checkpoint. The local checkpoint uses the configured local/success color.", HumanSettlementConflictZoneMap, "fcz"),
            Icon(GuideIconKind.ConflictPowerPost, "", "Conflict-zone power post", "A circle with a lightning stroke marks a power post.", HumanSettlementConflictZoneMap, "fcz"),
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
                $"body planet stellar route {visual.AccessibleName}"));
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
        IReadOnlyList<GuideIconViewModel>? icons = null)
    {
        return new GuideCategoryViewModel(
            key,
            number,
            title,
            summary,
            sections,
            icons ?? []);
    }

    private static GuideSectionViewModel Section(
        string title,
        string summary,
        IReadOnlyList<string> steps,
        IReadOnlyList<string> details)
    {
        return new GuideSectionViewModel(title, summary, steps, details);
    }

    private static GuideIconViewModel Icon(
        GuideIconKind kind,
        string symbol,
        string name,
        string meaning,
        string appearsIn,
        string searchTerms = "")
    {
        return new GuideIconViewModel(
            kind,
            symbol,
            name,
            meaning,
            appearsIn,
            searchTerms);
    }

    private static GuideIconViewModel AssetIcon(
        string assetPath,
        string name,
        string meaning,
        string appearsIn,
        string searchTerms)
    {
        return new GuideIconViewModel(
            GuideIconKind.Asset,
            string.Empty,
            name,
            meaning,
            appearsIn,
            searchTerms,
            assetPath);
    }

    private static string DesktopAssetUri(string relativePath)
    {
        return $"{AvaloniaResourceScheme}://{DesktopAssemblyName}/{relativePath}";
    }
}
