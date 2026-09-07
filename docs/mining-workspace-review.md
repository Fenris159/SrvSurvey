# Mining workspace implementation review

Review base: `e46fb9a3216b7df580f0c9dbe4d5c0a085a8161e`.
Initial implementation: `c8ec3394` (`Implement mining workspace and shared navigation bookmarks`).
The follow-up changes address the findings below. Two independent reviewers rechecked their original findings against those changes.

Spec source: the supplied `srvsurvey-mining-workspace-handoff.md`, with the feature mapping in [the parity checklist](mining-workspace-parity.md).

## Standards

No hard violations of the repository's AGENTS/domain/CONTEXT standards were reported. Three heuristic findings were addressed:

- **Possible duplicated code:** commodity aliases appeared in both HTTP and community-cache searches. Both now call `MiningCommodityName.Normalize`.
- **Possible primitive obsession:** search preferences used reflective string-dictionary persistence with duplicated defaults. They now use `MiningSearchPreferences`, with typed serialization and view-model input normalization.
- **Possible feature envy:** Desktop imported rings and missions by manipulating Core collections. `MiningWorkspaceState.Import` and `CacheRing` now own these operations.

A separate UI correctness finding in this review was also fixed: Bookmarks now exposes the screenshot-attachment and deletion-undo handlers as buttons. The shared workspace test exercises delete and undo through that control.

The reviewer confirmed all four changes by source inspection. The reviewer did not execute tests or validate gameplay.

## Spec

Four functional findings were addressed:

- **Core-only minerals:** summaries now include core material separately when absent from surface yields. Each core counts as a quality hit regardless of the percentage threshold, without inventing a surface-yield percentage.
- **Shared bookmark annotations:** empty overlap or RES fields no longer overwrite known ring metadata, so bookmarking an unannotated location does not remove it from filtered results.
- **Backup restore:** package restore now restores the saved bookmark catalog, including edits to existing locations. It retains `bookmarks.json.before-restore`; ordinary bookmark import still merges without replacing existing locations.
- **Reporting:** HTML reports now include observed mineral-yield timelines, cumulative refining charts and per-material comparisons across sessions. Chart time is elapsed wall time, explicitly including pauses; session efficiency and material comparison use active duration. Summary-only imports do not invent timed observations.

The reviewer confirmed all four fixes by source inspection. No scope creep or Rhino-boundary violation was reported. The reviewer did not execute tests or validate gameplay.

Standards: 0 unresolved findings (3 heuristics and 1 UI finding addressed; no hard violations). Spec: 0 unresolved findings (4 functional findings addressed); no remaining worst issue in either axis.
