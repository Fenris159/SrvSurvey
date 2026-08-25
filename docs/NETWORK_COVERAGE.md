# Network and Update Coverage Matrix

Last audited: 2026-08-25

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
| Spansh search and routes | `SpanshRouteClient`, `SpanshRouteUrlParser`, `SpanshBoxelClient`, `NearestSystemsClient` | Result-URL mapping for all current planner families, structural detection for bare job IDs, array/system-jump/jump/trade-leg response shapes, bounded exponential polling, page limits, coordinate validation and bounded streaming |
| Canonn services | Challenge, system-POI, nearest-system and human-site clients | Commander Codex import, biology/POI enrichment, settlement lookup/publication, privacy gates and duplicate suppression |
| EDDN publication | `EddnPublisher`, `EddnSessionPublisher`, `EddnOutbox` | Application-owned durable delivery, immutable Commander-session headers and context, fixed test schemas, per-message persistence, fair retry scheduling, companion validation, privacy/bootstrap gates and bounded payloads |
| Inara publication | `InaraPublisher` | Session-bound personal commander credentials, key-presence opt-in, 35-second batching, bounded requests/responses, startup and live-galaxy gates, multicrew/multi-box suppression, event mapping, retry retention, and graceful final flush |
| EDSM publication | `EdsmPublisher` | Current journal Commander name and profile-scoped personal key, Live-only session binding, current discard-policy gate, sanctioned transient context, bounded memory-only batching, rate-aware delayed retry, fatal-credential pause, multicrew/multi-box suppression, and graceful final flush |
| VoxStellar publication | `VoxStellarPublisher` | Explicit opt-in, eight-event allowlist, commander + data envelope, HMAC-SHA256 signature, bootstrap/multi-box gates, ordered memory-only queue, and consent-revocation invalidation |
| Inara Community Goal read | `InaraCommunityGoalClient`, `InaraCommunityGoalEnricher`, `CommunityGoalJournalHistoryReader` | Generic application identity only, no commander fields, 15-minute disk cache, bounded response, stale-cache fallback, conservative title/location/expiry matching, Frontier-first field precedence, and commander-isolated local-journal recovery for personal progress |
| Frontier commander dashboard | `FrontierAccountService` | PKCE authorization, exact callback/state validation, protected per-FID token storage, active-journal identity verification, isolated profile caches, bounded profile/carrier/market/shipyard/community-goal responses, one-minute attempt cooldown, 15-minute carrier cadence and per-section fallback |
| Raven services | `RavenColonialClient`, `ColonizationBuildSiteRepair`, `RavenQuestClient`, `GreenGasGiantClient` | Projects, systems, authenticated repair, Fleet Carriers/cargo, quests, GGG publication, ownership checks and endpoint/payload tests |
| Downloaded caches and images | `VisitedStarsCacheService`, `CodexImageCache` | Content-type checks, streamed size bounds, atomic replacement, checksum verification and prior-cache preservation |

`NetworkSurfaceCoverageTests` requires production and assertion evidence for each
surface. It also inventories every modern `HttpClient` owner and requires
response-header-only completion plus explicit byte limits before content is
parsed or persisted.
