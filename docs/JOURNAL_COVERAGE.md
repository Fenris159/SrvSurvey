# Journal Coverage Matrix

Last audited: 2026-08-02

The converted application has an explicit inventory of 74 supported Elite
Dangerous journal events. `JournalCoverageInventoryTests` requires every event
to appear exactly once, have a production consumer, and have event-specific
assertion evidence. This keeps coverage reviewable without depending on source
files from another application.

| State group | Supported events | Regression evidence |
|---|---|---|
| Commander/session | `Commander`, `LoadGame`, `Loadout`, `Location`, `Died`, `Resurrect`, `Music`, `Shutdown`, `StartJump`, `FSDJump`, `CarrierJump`, `SupercruiseExit`, `ApproachBody`, `LeaveBody`, `LaunchSRV`, `DockSRV`, `LaunchFighter`, `DockFighter`, `SuitLoadout`, `SwitchSuitLoadout` | `JournalSessionStateTests`, `JournalSnapshotReaderTests`, `DockToDockLogServiceTests`, `GuardianLiveSiteStateTests` |
| System/body exploration | `FSSDiscoveryScan`, `FSSAllBodiesFound`, `FSSSignalDiscovered`, `NavBeaconScan`, `Scan`, `ScanBaryCentre`, `SAAScanComplete`, `SAASignalsFound`, `FSSBodySignals`, `Touchdown`, `Liftoff` | `SystemScanStateTests`, `ExplorationStateTests`, `SurfaceSurveyJournalTrackerTests`, `ColonizationSystemSiteJournalTrackerTests` |
| Exobiology and text commands | `CodexEntry`, `ScanOrganic`, `SellOrganicData`, `Disembark`, `Embark`, `SendText` | `ExobiologyStateTests`, `SurfaceSurveyJournalTrackerTests`, `CommanderCodexJournalTrackerTests`, `SystemScanStateTests` |
| Guardian/human sites | `ApproachSettlement`, `BackpackChange`, `CollectItems`, `SupercruiseEntry`, `DockingRequested`, `DockingCancelled`, `DockingDenied`, `DockingGranted` | `GuardianLiveSiteStateTests`, `HumanSiteLiveStateTests`, `HumanSiteActivityTrackerTests` |
| Missions/combat | `Missions`, `MissionAccepted`, `MissionFailed`, `MissionAbandoned`, `MissionCompleted`, `Bounty`, `FactionKillBond` | `CombatStateTests`, `RamTahStateTests` |
| Cargo/materials | `CollectCargo`, `EjectCargo`, `CargoTransfer`, `CargoDepot`, `Cargo`, `MarketBuy`, `MarketSell`, `Market`, `Materials`, `MaterialCollected`, `MaterialTrade`, `TechnologyBroker` | `CargoInventoryStateTests`, `GuardianArtifactInventoryStateTests`, `JournalDirectoryMonitorTests`, `NotificationViewModelTests` |
| Colonization | `ColonisationConstructionDepot`, `ColonisationContribution`, `ColonisationBeaconDeployed` | `ColonizationConstructionStateTests` |
| Route/docking/travel | `FSDTarget`, `NavRoute`, `NavRouteClear`, `Interdicted`, `Docked`, `Undocked` | `DockToDockLogServiceTests`, `JumpInfoViewModelTests`, `GalaxyMapOverlayViewModelTests` |
| Screenshots/journeys | `Screenshot` | `JourneyJournalProcessorTests`, `ScreenshotProcessingServiceTests` |

The projection tests cover durable commander/system data, disposable live
location and vehicle context, body scan and organic progress, Guardian and human
site state, mission counters, cargo/material balances, colonization construction,
and route/docking history. Malformed payload tests remain fail-closed beside the
corresponding state tests.
