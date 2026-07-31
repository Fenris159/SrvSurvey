# Network and Update Coverage Matrix

Last audited: 2026-07-29

The converted application keeps reference-data delivery separate from
application releases. Startup checks the published reference index and downloads
only missing or newer catalogs. Catalog activation is staged, validated, backed
up, and independently recoverable. Application packages have their own RID,
size, SHA-256, staging, health-confirmation, and rollback contract.

| Runtime surface | Implementation | Contract evidence |
|---|---|---|
| Published reference data | `PublishedDataIndexClient`, `PublishedReferenceUpdateService` | Version-gated downloads, bounded archives, strict catalog validation, transactional activation and rollback |
| Application releases | `CrossPlatformReleaseClient`, download/staging/install services | Windows/Linux package metadata, bounded streaming, hashes, manifests, safe paths, same-volume replacement, health token and rollback |
| System lookup and enrichment | `SystemBodyDataClient`, `SystemSummaryClient`, `CodexDiscoveryLocationClient`, `SpanshStarSystemResolver` | EDSM and Spansh isolation, identity and shape validation, bounded responses, cancellation and stale-result containment |
| Spansh search and routes | `SpanshRouteClient`, `SpanshBoxelClient`, `NearestSystemsClient` | Request/response shapes, polling states, page limits, coordinate validation, route-kind mapping and bounded streaming |
| Canonn services | Challenge, system-POI, nearest-system and human-site clients | Commander Codex import, biology/POI enrichment, settlement lookup/publication, privacy gates and duplicate suppression |
| EDDN publication | `EddnPublisher` | Sixteen event contracts, environment routing, context hydration, schema validation, privacy/bootstrap gates and bounded payloads |
| Inara publication | `InaraPublisher` | Personal commander credentials, 35-second batching, bounded requests/responses, startup and live-galaxy gates, multicrew/multi-box suppression, event mapping and retry retention |
| Frontier commander dashboard | `FrontierAccountService` | PKCE authorization, exact callback/state validation, protected per-FID token storage, active-journal identity verification, isolated profile caches, bounded profile/carrier/market/shipyard/community-goal responses, one-minute attempt cooldown, 15-minute carrier cadence and per-section fallback |
| Raven services | `RavenColonialClient`, `ColonizationBuildSiteRepair`, `RavenQuestClient`, `GreenGasGiantClient` | Projects, systems, authenticated repair, Fleet Carriers/cargo, quests, GGG publication, ownership checks and endpoint/payload tests |
| Downloaded caches and images | `VisitedStarsCacheService`, `CodexImageCache` | Content-type checks, streamed size bounds, atomic replacement, checksum verification and prior-cache preservation |

`NetworkSurfaceCoverageTests` requires production and assertion evidence for each
surface. It also inventories every modern `HttpClient` owner and requires
response-header-only completion plus explicit byte limits before content is
parsed or persisted.
