# Journal and State Parity Matrix

Last audited: 2026-07-26

This matrix covers every concrete `onJournalEntry(T entry)` overload in the
legacy `SrvSurvey/game/Game.cs`. The cross-platform port intentionally compares
stable state projections rather than serialized implementation objects: the
legacy WinForms `Game` object mixed UI handles, timers, and network clients into
its state and therefore has no portable byte-for-byte representation.

`LegacyJournalParityTests` is the enforcement gate. It parses the legacy source,
requires the audited inventory to match all 68 handlers exactly, requires every
event to occur in modern production code and event-specific regression evidence,
and requires each event to occur in the golden projection files assigned below.

| Golden group | Legacy events | Equivalent projection evidence |
|---|---|---|
| Commander/session | `Commander`, `LoadGame`, `Loadout`, `Location`, `Died`, `Resurrect`, `Music`, `Shutdown`, `StartJump`, `FSDJump`, `CarrierJump`, `SupercruiseExit`, `ApproachBody`, `LeaveBody`, `LaunchSRV`, `DockSRV`, `LaunchFighter`, `DockFighter`, `SuitLoadout`, `SwitchSuitLoadout` | `JournalSessionStateTests`, `JournalSnapshotReaderTests`, `DockToDockLogServiceTests`, `GuardianLiveSiteStateTests` |
| System/body exploration | `FSSDiscoveryScan`, `FSSAllBodiesFound`, `Scan`, `SAAScanComplete`, `SAASignalsFound`, `FSSBodySignals`, `Touchdown`, `Liftoff` | `SystemScanStateTests`, `ExplorationStateTests`, `SurfaceSurveyJournalTrackerTests` |
| Exobiology | `CodexEntry`, `ScanOrganic`, `SellOrganicData`, `Disembark`, `Embark` | `ExobiologyStateTests`, `SurfaceSurveyJournalTrackerTests`, `CommanderCodexJournalTrackerTests`, `SystemScanStateTests` |
| Guardian/human sites | `ApproachSettlement`, `BackpackChange`, `CollectItems`, `SupercruiseEntry`, `DockingRequested`, `DockingCancelled`, `DockingDenied`, `DockingGranted` | `GuardianLiveSiteStateTests`, `HumanSiteLiveStateTests`, `HumanSiteActivityTrackerTests` |
| Missions/combat | `Missions`, `MissionAccepted`, `MissionFailed`, `MissionAbandoned`, `MissionCompleted`, `Bounty` | `CombatStateTests`, `RamTahStateTests` |
| Cargo/materials | `CollectCargo`, `EjectCargo`, `CargoTransfer`, `CargoDepot`, `Cargo`, `MarketBuy`, `MarketSell`, `Market`, `Materials`, `MaterialCollected`, `MaterialTrade`, `TechnologyBroker` | `CargoInventoryStateTests`, `GuardianArtifactInventoryStateTests`, `JournalDirectoryMonitorTests`, `NotificationViewModelTests` |
| Colonization | `ColonisationConstructionDepot`, `ColonisationContribution`, `ColonisationBeaconDeployed`, live `Docked`, docked `Location` | `ColonizationConstructionStateTests`, `ColonizationBuildSiteRepairTests`, `ColonizationViewModelTests` |
| Route/docking/travel | `FSDTarget`, `NavRoute`, `NavRouteClear`, `Interdicted`, `Docked`, `Undocked` | `DockToDockLogServiceTests`, `JumpInfoViewModelTests`, `GalaxyMapOverlayViewModelTests` |

The projection tests cover durable commander/system data, disposable live
location and vehicle context, body scan and organic progress, Guardian and human
site state, mission counters, cargo/material balances, colonization construction,
and route/docking history. Malformed payload tests remain fail-closed and are
kept beside their corresponding state tests.
