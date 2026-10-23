# Raven Colonial commander system discovery

Researched 2026-10-19 using the live public Raven API and upstream
`njthomson/RavenColonialWeb` commit
`1b65322073bcdc39803776a43dce6bbc873bce87`. The supplied EDMC API reference
was checked at commit `ec1d47b41099d133b744c2f9a6bb1d93c585dfd6`.
Only anonymous public reads were made; no account credentials were accessed.

## Supported answer

**Yes, Raven provides discovery of systems associated with a named commander:**
`GET /api/Cmdr/{url-encoded-cmdr}/map/architect`. Filter its `systems` array
to the **Architect** category to obtain architected systems known to Raven.
The full map also includes **Helped** systems belonging to other architects.
This is not a guaranteed inventory of every system owned in the game.
The upstream help explicitly says architected systems may require searching
once before they appear.
[Map client](https://github.com/njthomson/RavenColonialWeb/blob/1b65322073bcdc39803776a43dce6bbc873bce87/public/map/map_architect.js#L15-L27),
[upstream help](https://github.com/njthomson/RavenColonialWeb/blob/1b65322073bcdc39803776a43dce6bbc873bce87/src/views/About.tsx#L123-L126),
[live public map](https://ravencolonial100-awcbdvabgze4c5cq.canadacentral-01.azurewebsites.net/api/Cmdr/Grinning2001/map/architect).

## Endpoint distinctions

| Endpoint | Verified meaning | Useful treatment |
|---|---|---|
| `GET /api/Cmdr/{cmdr}/map/architect` | Returns `MapData` with `categories`, `systems`, and optional `routes`. The upstream map fetches it without credentials. | Discover the named commander's map; filter Architect nodes before presenting an architect list. |
| `GET /api/v2/system/snapshots/` | Returns `SysSnapshot[]`; upstream's commander summary calls this method and caches by current commander. Its shared API helper sends `rcc-cmdr0` and `rcc-key`. | Saved planning summaries for the selected commander, not a separate authoritative game ownership feed. |
| `GET /api/v2/system/{nameOrNum}/architect` | Takes an already-known system name or ID64 and returns a string. A live public lookup returned its current Raven architect. | Confirm each discovered system's architect; this route cannot discover system names from a commander. |

The declared response shapes come from the
[live official OpenAPI](https://ravencolonial100-awcbdvabgze4c5cq.canadacentral-01.azurewebsites.net/openapi/v1.json).
Snapshot behavior is visible in
[the API client](https://github.com/njthomson/RavenColonialWeb/blob/1b65322073bcdc39803776a43dce6bbc873bce87/src/api/v2-system.ts#L85-L103),
[commander summary](https://github.com/njthomson/RavenColonialWeb/blob/1b65322073bcdc39803776a43dce6bbc873bce87/src/views/SystemView2/ArchitectSummary.tsx#L202-L209),
and [request headers](https://github.com/njthomson/RavenColonialWeb/blob/1b65322073bcdc39803776a43dce6bbc873bce87/src/api/api-util.ts#L19-L24).
The OpenAPI does not declare authentication requirements; the client behavior
and successful anonymous map/architect reads establish only the observations
above.

## Actual map contents and filtering

An anonymous read of the public `Grinning2001` map on the research date
returned **687 nodes: 19 Architect and 668 Helped**. Its category dictionary
mapped `0` to Architect, `1` Favourite, `2` Building, `3` Planning, and
`4` Helped. One Helped node, `Greae Phoea RF-I c25-207`, has ID64
`56991766159322`; its
[architect lookup](https://ravencolonial100-awcbdvabgze4c5cq.canadacentral-01.azurewebsites.net/api/v2/system/56991766159322/architect)
returned `"GFJake"`. Thus the dedicated endpoint itself demonstrably includes
another architect's system. Counts describe this dated sample only.
[Sample response](https://ravencolonial100-awcbdvabgze4c5cq.canadacentral-01.azurewebsites.net/api/Cmdr/Grinning2001/map/architect).

- Keep nodes where `cat` **contains numeric `0`**, not only those whose first
  category is `0`. Architect nodes can also have Favourite/Building/Planning
  categories before `0`; for example, the sample included `[1, 2, 0]`.
  Confirm the category dictionary when consuming future responses.
- Architect nodes in this sample had JSON text in `infos`, containing a
  `SysSnapshot`, including `architect`, `id64`, and canonical `name`.
  All 19 parsed architects matched `grinning2001`. Preserve canonical system
  identity rather than treating a nickname displayed by the map as its name.
- Do not blindly parse every `infos` value as JSON. A Helped value was
  `{ id64: 56991766159322}`, which is not valid JSON; the upstream map skips
  parsing Helped entries and also accepts HTML. Treat this field as untrusted
  display/metadata content, never executable code.

The upstream map performs the same JSON/HTML/Helped distinction and can replace
displayed names with nicknames.
[Map renderer](https://github.com/njthomson/RavenColonialWeb/blob/1b65322073bcdc39803776a43dce6bbc873bce87/public/map/map_architect.js#L30-L67).
Its separately constructed commander-summary map uses the same category names
and appends Helped systems; that corroborates the meaning but is distinct from
the live endpoint evidence above.
[Summary map construction](https://github.com/njthomson/RavenColonialWeb/blob/1b65322073bcdc39803776a43dce6bbc873bce87/src/views/SystemView2/ArchitectSummary.tsx#L139-L196).

## Completeness and implementation implication

Snapshots are persisted derived summaries: upstream creates them from the
current system model, includes one when saving sites, and can refresh missing
or stale snapshots after loading a system. They carry recorded architect
identity, which can lag subsequent changes. The map's Architect metadata is
also snapshot-shaped; no evidence establishes that it discovers more
architected systems than the snapshot list.
[Snapshot generation](https://github.com/njthomson/RavenColonialWeb/blob/1b65322073bcdc39803776a43dce6bbc873bce87/src/system-model2.ts#L842-L864),
[refresh](https://github.com/njthomson/RavenColonialWeb/blob/1b65322073bcdc39803776a43dce6bbc873bce87/src/views/SystemView2/SystemView2.tsx#L298-L325),
[save payload](https://github.com/njthomson/RavenColonialWeb/blob/1b65322073bcdc39803776a43dce6bbc873bce87/src/views/SystemView2/SystemView2.tsx#L442-L454).

For SrvSurvey, the supported discovery approach is the named-commander map,
Architect-category filtering, then a current v2 architect/system lookup with
case-insensitive commander comparison. An empty or partial Raven result does
not prove absence of in-game ownership. Snapshot-only discovery is limited to
recorded planning summaries; changing to the map removes dependence on a key
for this public read but does not establish exhaustive game coverage. No public
backend implementation was located, so exact server selection/storage rules
and completeness remain unverified. No application code was changed.
