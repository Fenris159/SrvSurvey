# Network and Update Parity Matrix

Last audited: 2026-07-26

The Avalonia port preserves the original runtime delivery model: application
startup checks the small published version index, compares each locally active
catalog version, and downloads a catalog only when its published version has
advanced or a required local file is absent. Reference-data releases therefore
remain independent of application releases. The active files are changed only
after the complete candidate set validates and a verified rollback backup is
available.

This is deliberately separate from the application release updater. Tagged
cross-platform application packages use their own release index, RID, size,
SHA-256, staging, health-confirmation, and rollback contract.

| Legacy runtime surface | Cross-platform implementation | Contract evidence |
|---|---|---|
| `Git.cs`, `CodexRef.cs` | `PublishedDataIndexClient`, `PublishedReferenceUpdateService` | Legacy `data.json` keys and versions; startup check; version-gated downloads; bounded archives; strict catalog validation; transactional activation and rollback |
| `Git.cs` application update | `CrossPlatformReleaseClient`, download/staging/install services | Exact Windows/Linux package metadata, bounded streaming, hashes, manifests, safe paths, same-volume replacement, health token, rollback |
| `EDSM.cs`, `LookupStarSystem.cs`, `NetCache.cs`, `NetSysData.cs`, `spansh.cs` | `SystemBodyDataClient`, `SystemSummaryClient`, `CodexDiscoveryLocationClient`, `SpanshStarSystemResolver` | EDSM and Spansh requests fail independently; identity and shape validation; bounded responses; cancellation and stale-result containment |
| `spansh.cs`, `spansh-route.cs`, `spansh-search.cs`, `spansh-misc.cs` | `SpanshRouteClient`, `SpanshBoxelClient`, `NearestSystemsClient` | Legacy request/response shapes, polling states, page limits, coordinate validation, route-kind mapping, bounded streamed responses |
| `Canonn.cs`, `CanonnStation.cs`, `types.cs` | Canonn Challenge, system-POI, nearest-system, and human-site clients | Commander Codex import, biology/POI enrichment, settlement lookup/publication, privacy and bootstrap gates, duplicate suppression, bounded responses |
| `EDDN.cs` | `EddnPublisher` | All 16 legacy event contracts, environment routing, context hydration, schema field validation, privacy/bootstrap gates, bounded payloads and response details |
| `RavenColonial.cs` plus the later `ravencolonial_edmc` completed-site repair | `RavenColonialClient`, `ColonizationBuildSiteRepair`, `RavenQuestClient`, `GreenGasGiantClient` | Projects, systems, targeted authenticated system-site PATCH, Fleet Carriers/cargo, current ship, quests/chapters/progress, GGG publication, API-key ownership, exact endpoint/payload tests, bounded responses, conservative unique-match repair and persistent repeat suppression |
| `NetCache.cs` and remote image/cache paths | `VisitedStarsCacheService`, `CodexImageCache` | Expected content types, streamed size bounds, atomic replacement, checksum verification, prior-cache preservation |

`LegacyNetworkParityTests` is the inventory gate. It requires all 15 legacy
`SrvSurvey/net` source files to remain assigned to an audited runtime surface,
requires production and assertion evidence for every surface, and requires the
complete modern `HttpClient` owner inventory to use header-only completion plus
an explicit byte limit before response content is parsed or persisted.

The old `Debugger.IsAttached` maintenance dropdown is not an installed-player
network surface. Its source-generation responsibilities are now separated into
the localization generator and the guarded Guardian/human-template export
workflows. Generated JSON and archives remain repository-owned published data;
the installed application continues to check and consume those data releases,
but it does not receive credentials or an automatic path that writes to the
maintainer's repository.
